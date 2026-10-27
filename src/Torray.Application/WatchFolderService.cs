using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Torray.Domain;

namespace Torray.Application;

public sealed record WatchSnapshot(IReadOnlyList<WatchJob> Issues, IReadOnlyList<string> MissingFolders)
{
    public static WatchSnapshot Empty { get; } = new([], []);
}

// Uploads .torrent files that appear in local watch folders. File system events only wake the loop; the
// periodic scan is the single place that decides, so missed events and files present at startup are handled
// the same way. Jobs are persisted, so a restart neither loses pending uploads nor repeats finished ones.
public sealed class WatchFolderService(
    TorrentService torrents,
    IWatchJobStore store,
    ILogger<WatchFolderService> logger) : BackgroundService
{
    public const string ArchiveFolderName = "Added";
    public const int MaxAttempts = 10;
    public static readonly TimeSpan StableAge = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryBase = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryMax = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly Lock _watchersLock = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Dictionary<string, (long Length, DateTime WriteTime, string Hash)> _hashCache =
        new(StringComparer.OrdinalIgnoreCase);

    private volatile IReadOnlyList<WatchFolder> _folders = [];
    private Dictionary<string, WatchJob>? _jobs;
    private bool _recheckSoon;
    private int _retryPending;
    private int _retryFailed;
    private ConnectionState _lastConnectionState;

    // Raised on a background thread; UI subscribers must marshal to their dispatcher.
    public event Action<WatchSnapshot>? Updated;

    public WatchSnapshot Snapshot { get; private set; } = WatchSnapshot.Empty;

    public void Configure(IReadOnlyList<WatchFolder> folders)
    {
        _folders = folders;
        lock (_watchersLock)
        {
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
            foreach (var folder in folders.Where(f => f.Enabled && Directory.Exists(f.Path)))
            {
                var watcher = new FileSystemWatcher(folder.Path, "*.torrent")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                watcher.Created += (_, _) => Wake();
                watcher.Changed += (_, _) => Wake();
                watcher.Renamed += (_, _) => Wake();
                watcher.Error += (_, _) => Wake();
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
        }

        Wake();
    }

    // Retries waiting uploads immediately; failed ones too, because the user asked explicitly.
    public void RetryNow()
    {
        Interlocked.Exchange(ref _retryPending, 1);
        Interlocked.Exchange(ref _retryFailed, 1);
        Wake();
    }

    public static TimeSpan GetRetryDelay(int attempts)
    {
        var delay = RetryBase * Math.Pow(2, Math.Min(attempts - 1, 16));
        return delay < RetryMax ? delay : RetryMax;
    }

    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        await _scanLock.WaitAsync(cancellationToken);
        try
        {
            _jobs ??= (await store.LoadAsync(cancellationToken))
                .ToDictionary(j => j.Path, StringComparer.OrdinalIgnoreCase);
            var changed = ApplyRetryRequests();
            var now = DateTimeOffset.UtcNow;
            var missing = new List<string>();
            _recheckSoon = false;

            foreach (var folder in _folders.Where(f => f.Enabled))
            {
                if (!TryListTorrentFiles(folder.Path, out var files))
                {
                    missing.Add(folder.Path);
                    continue;
                }

                foreach (var file in files)
                {
                    changed |= await ScanFileAsync(folder, file, now, cancellationToken);
                }
            }

            foreach (var gone in _jobs.Keys.Where(path => !File.Exists(path)).ToList())
            {
                _jobs.Remove(gone);
                _hashCache.Remove(gone);
                changed = true;
            }

            if (changed)
            {
                await SaveJobsAsync(cancellationToken);
            }

            Snapshot = new WatchSnapshot([.. _jobs.Values.Where(j => j.State != WatchJobState.Done)], missing);
            Updated?.Invoke(Snapshot);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        torrents.Updated += OnTorrentsUpdated;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ScanAsync(stoppingToken);
                    if (await _wake.WaitAsync(GetNextDelay(), stoppingToken))
                    {
                        // Let a burst of file system events (copy, rename, write) settle before scanning.
                        await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // The watcher must survive unexpected file system or store failures; the next scan retries.
                    logger.LogError(ex, "Watch folder scan failed");
                    await Task.Delay(ScanInterval, stoppingToken);
                }
            }
        }
        finally
        {
            torrents.Updated -= OnTorrentsUpdated;
            Configure([]);
        }
    }

    private async Task<bool> ScanFileAsync(WatchFolder folder, FileInfo file, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // A file still being written changes size or time; wait until it has been quiet for a moment.
        if (now - file.LastWriteTimeUtc < StableAge || file.Length == 0)
        {
            _recheckSoon = true;
            return false;
        }

        var hash = await TryHashAsync(file, cancellationToken);
        if (hash is null)
        {
            _recheckSoon = true;
            return false;
        }

        var changed = false;
        if (!_jobs!.TryGetValue(file.FullName, out var job) || job.Hash != hash)
        {
            job = new WatchJob(file.FullName, hash, WatchJobState.Pending, 0, null, null);
            _jobs[file.FullName] = job;
            changed = true;
        }

        if (job.State != WatchJobState.Pending || job.NextAttempt > now)
        {
            return changed;
        }

        var result = await ProcessAsync(folder, job, now, cancellationToken);
        _jobs[file.FullName] = result;
        return changed || result != job;
    }

    private async Task<WatchJob> ProcessAsync(WatchFolder folder, WatchJob job, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(job.Path);
        try
        {
            var metainfo = await TorrentInput.ReadTorrentFileAsync(job.Path, cancellationToken);
            var added = await torrents.AddFileAsync(metainfo, cancellationToken);
            logger.LogInformation(
                "Watch folder uploaded {File} as torrent {Id} (duplicate: {Duplicate})", fileName, added.Id, added.IsDuplicate);

            // Archive only after the server accepted the torrent or recognized it as a duplicate.
            if (folder.MoveToArchive)
            {
                TryArchive(job.Path);
            }

            return job with { State = WatchJobState.Done, NextAttempt = null, ErrorKey = null };
        }
        catch (TorrentInputException ex) when (ex.Error != TorrentInputError.FileUnreadable)
        {
            logger.LogWarning("Watch folder file {File} is not a valid torrent: {Error}", fileName, ex.Error);
            return job with { State = WatchJobState.Failed, ErrorKey = "TorrentInput" + ex.Error };
        }
        catch (TorrentInputException ex)
        {
            return Retry(job, now, "TorrentInput" + ex.Error);
        }
        catch (TransmissionRpcException ex)
        {
            logger.LogWarning("Watch folder upload of {File} failed: {Kind}", fileName, ex.Kind);
            return Retry(job, now, "RpcError" + ex.Kind);
        }
        catch (InvalidOperationException)
        {
            // No connection configured yet: wait without using up attempts.
            return job;
        }
    }

    private static WatchJob Retry(WatchJob job, DateTimeOffset now, string errorKey)
    {
        var attempts = job.Attempts + 1;
        if (attempts >= MaxAttempts)
        {
            return job with { State = WatchJobState.Failed, Attempts = attempts, NextAttempt = null, ErrorKey = errorKey };
        }

        // Jitter keeps several waiting files from hitting a recovering server at the same moment.
        var delay = GetRetryDelay(attempts) * (1 + Random.Shared.NextDouble() * 0.2);
        return job with { Attempts = attempts, NextAttempt = now + delay, ErrorKey = errorKey };
    }

    private void TryArchive(string path)
    {
        try
        {
            var directory = Path.Combine(Path.GetDirectoryName(path)!, ArchiveFolderName);
            Directory.CreateDirectory(directory);
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            var target = Path.Combine(directory, name + extension);
            for (var i = 2; File.Exists(target); i++)
            {
                target = Path.Combine(directory, $"{name} ({i}){extension}");
            }

            // overwrite: false, so a file that appears at the target in the meantime is never replaced.
            File.Move(path, target, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The upload succeeded; the persisted Done job keeps the file from being uploaded again.
            logger.LogWarning(ex, "Could not move {File} to the archive folder", Path.GetFileName(path));
        }
    }

    private bool ApplyRetryRequests()
    {
        var retryPending = Interlocked.Exchange(ref _retryPending, 0) == 1;
        var retryFailed = Interlocked.Exchange(ref _retryFailed, 0) == 1;
        if (!retryPending && !retryFailed)
        {
            return false;
        }

        var jobs = _jobs!;
        foreach (var (path, job) in jobs.ToList())
        {
            if (job.State == WatchJobState.Pending)
            {
                jobs[path] = job with { NextAttempt = null };
            }
            else if (retryFailed && job.State == WatchJobState.Failed)
            {
                jobs[path] = job with { State = WatchJobState.Pending, Attempts = 0, NextAttempt = null };
            }
        }

        return true;
    }

    private bool TryListTorrentFiles(string folder, out List<FileInfo> files)
    {
        try
        {
            files = [.. new DirectoryInfo(folder)
                .EnumerateFiles("*.torrent", SearchOption.TopDirectoryOnly)
                .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)];
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = [];
            return false;
        }
    }

    private async Task<string?> TryHashAsync(FileInfo file, CancellationToken cancellationToken)
    {
        if (_hashCache.TryGetValue(file.FullName, out var cached)
            && cached.Length == file.Length
            && cached.WriteTime == file.LastWriteTimeUtc)
        {
            return cached.Hash;
        }

        try
        {
            // FileShare.None fails while another process still has the file open for writing.
            await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            _hashCache[file.FullName] = (file.Length, file.LastWriteTimeUtc, hash);
            return hash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task SaveJobsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.SaveAsync([.. _jobs!.Values], cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Watch job state could not be saved");
        }
    }

    private TimeSpan GetNextDelay()
    {
        if (_recheckSoon)
        {
            return StableAge;
        }

        var nextAttempt = _jobs?.Values
            .Where(j => j.State == WatchJobState.Pending && j.NextAttempt is not null)
            .Min(j => j.NextAttempt);
        if (nextAttempt is null)
        {
            return ScanInterval;
        }

        var untilRetry = nextAttempt.Value - DateTimeOffset.UtcNow;
        return untilRetry < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1)
            : untilRetry < ScanInterval ? untilRetry
            : ScanInterval;
    }

    private void OnTorrentsUpdated(TorrentSnapshot snapshot)
    {
        // A restored connection should not wait out the backoff of uploads that failed while it was down.
        if (snapshot.State == ConnectionState.Connected && _lastConnectionState != ConnectionState.Connected)
        {
            Interlocked.Exchange(ref _retryPending, 1);
            Wake();
        }

        _lastConnectionState = snapshot.State;
    }

    private void Wake()
    {
        if (_wake.CurrentCount == 0)
        {
            try
            {
                _wake.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another caller woke the loop first; one pending wake is enough.
            }
        }
    }
}
