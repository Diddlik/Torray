using Microsoft.Extensions.Logging.Abstractions;
using Torray.Application;
using Torray.Domain;
using Torray.Infrastructure;

namespace Torray.UnitTests;

public sealed class WatchFolderServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("Torray.Tests").FullName;
    private readonly FakeServer _server = new();

    private string WatchPath => Path.Combine(_root, "watch");

    private string StorePath => Path.Combine(_root, "watch-jobs.json");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public WatchFolderServiceTests() => Directory.CreateDirectory(WatchPath);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Stable_file_is_uploaded_once_across_scans_and_restarts()
    {
        var file = WriteTorrent("a.torrent");
        var service = CreateService(new WatchFolder(WatchPath));

        await service.ScanAsync(Token);
        await service.ScanAsync(Token);
        await CreateService(new WatchFolder(WatchPath)).ScanAsync(Token);

        Assert.Equal(1, _server.Uploads);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task File_still_being_written_is_skipped()
    {
        WriteTorrent("fresh.torrent", age: TimeSpan.Zero);

        await CreateService(new WatchFolder(WatchPath)).ScanAsync(Token);

        Assert.Equal(0, _server.Uploads);
    }

    [Fact]
    public async Task Archive_moves_file_without_overwriting_existing_archive_files()
    {
        var archive = Directory.CreateDirectory(Path.Combine(WatchPath, WatchFolderService.ArchiveFolderName)).FullName;
        File.WriteAllText(Path.Combine(archive, "a.torrent"), "older file");
        WriteTorrent("a.torrent");

        await CreateService(new WatchFolder(WatchPath, MoveToArchive: true)).ScanAsync(Token);

        Assert.False(File.Exists(Path.Combine(WatchPath, "a.torrent")));
        Assert.Equal("older file", File.ReadAllText(Path.Combine(archive, "a.torrent")));
        Assert.True(File.Exists(Path.Combine(archive, "a (2).torrent")));
    }

    [Fact]
    public async Task Duplicate_counts_as_success_and_is_archived()
    {
        _server.Duplicate = true;
        WriteTorrent("dup.torrent");

        await CreateService(new WatchFolder(WatchPath, MoveToArchive: true)).ScanAsync(Token);

        Assert.True(File.Exists(Path.Combine(WatchPath, WatchFolderService.ArchiveFolderName, "dup.torrent")));
    }

    [Fact]
    public async Task Invalid_file_fails_once_and_is_kept()
    {
        var file = Path.Combine(WatchPath, "broken.torrent");
        File.WriteAllText(file, "<html>not a torrent</html>");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-1));
        var service = CreateService(new WatchFolder(WatchPath, MoveToArchive: true));
        WatchSnapshot? snapshot = null;
        service.Updated += s => snapshot = s;

        await service.ScanAsync(Token);
        await service.ScanAsync(Token);

        Assert.Equal(0, _server.Uploads);
        Assert.True(File.Exists(file));
        var issue = Assert.Single(snapshot!.Issues);
        Assert.Equal(WatchJobState.Failed, issue.State);
        Assert.Equal("TorrentInputNotTorrentFile", issue.ErrorKey);
    }

    [Fact]
    public async Task Network_failure_backs_off_and_retry_now_resumes()
    {
        _server.Failure = new TransmissionRpcException(RpcErrorKind.Unreachable, "down");
        var file = WriteTorrent("a.torrent");
        var service = CreateService(new WatchFolder(WatchPath, MoveToArchive: true));
        WatchSnapshot? snapshot = null;
        service.Updated += s => snapshot = s;

        await service.ScanAsync(Token);
        await service.ScanAsync(Token);
        Assert.Equal(1, _server.Attempts);
        var pending = Assert.Single(snapshot!.Issues);
        Assert.Equal((WatchJobState.Pending, 1, "RpcErrorUnreachable"), (pending.State, pending.Attempts, pending.ErrorKey));
        Assert.True(File.Exists(file));

        _server.Failure = null;
        service.RetryNow();
        await service.ScanAsync(Token);

        Assert.Equal(1, _server.Uploads);
        Assert.Empty(snapshot.Issues);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Pending_job_survives_restart()
    {
        _server.Failure = new TransmissionRpcException(RpcErrorKind.Timeout, "slow");
        WriteTorrent("a.torrent");
        await CreateService(new WatchFolder(WatchPath)).ScanAsync(Token);

        _server.Failure = null;
        var restarted = CreateService(new WatchFolder(WatchPath));
        restarted.RetryNow();
        await restarted.ScanAsync(Token);

        Assert.Equal(1, _server.Uploads);
    }

    [Fact]
    public async Task Missing_and_disabled_folders_are_handled()
    {
        WriteTorrent("a.torrent");
        var service = CreateService(
            new WatchFolder(Path.Combine(_root, "does-not-exist")),
            new WatchFolder(WatchPath, Enabled: false));
        WatchSnapshot? snapshot = null;
        service.Updated += s => snapshot = s;

        await service.ScanAsync(Token);

        Assert.Equal(0, _server.Uploads);
        Assert.Equal([Path.Combine(_root, "does-not-exist")], snapshot!.MissingFolders);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(5, 480)]
    [InlineData(9, 3600)]
    public void Retry_delay_grows_exponentially_up_to_an_hour(int attempts, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), WatchFolderService.GetRetryDelay(attempts));
    }

    private WatchFolderService CreateService(params WatchFolder[] folders)
    {
        var torrents = new TorrentService(NullLogger<TorrentService>.Instance);
        torrents.SetClient(_server);
        var service = new WatchFolderService(torrents, new JsonWatchJobStore(StorePath), NullLogger<WatchFolderService>.Instance);
        service.Configure(folders);
        return service;
    }

    private string WriteTorrent(string name, TimeSpan? age = null)
    {
        var path = Path.Combine(WatchPath, name);
        File.WriteAllBytes(path, "d8:announce3:urle"u8.ToArray());
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - (age ?? TimeSpan.FromMinutes(1)));
        return path;
    }

    private sealed class FakeServer : ITransmissionRpcClient
    {
        public int Attempts { get; private set; }

        public int Uploads { get; private set; }

        public bool Duplicate { get; set; }

        public Exception? Failure { get; set; }

        public Task<AddedTorrent> AddMetainfoAsync(
            byte[] metainfo, AddTorrentOptions options, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Failure is not null)
            {
                throw Failure;
            }

            Uploads++;
            return Task.FromResult(new AddedTorrent(Uploads, "t", Duplicate));
        }

        public Task<ServerInfo> GetSessionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Torrent>> GetTorrentsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Torrent>>([]);

        public Task RemoveAsync(int id, bool deleteLocalData, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StartAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task StopAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AddedTorrent> AddMagnetAsync(
            string magnetUri, AddTorrentOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TorrentDetails> GetDetailsAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
