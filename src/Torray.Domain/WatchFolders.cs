namespace Torray.Domain;

public sealed record WatchFolder(string Path, bool Enabled = true, bool MoveToArchive = false);

public enum WatchJobState
{
    Pending,
    Done,
    Failed,
}

// One .torrent file seen in a watch folder. Hash identifies the content, so a replaced file is uploaded again.
public sealed record WatchJob(
    string Path,
    string Hash,
    WatchJobState State,
    int Attempts,
    DateTimeOffset? NextAttempt,
    string? ErrorKey);

public interface IWatchJobStore
{
    Task<IReadOnlyList<WatchJob>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<WatchJob> jobs, CancellationToken cancellationToken = default);
}
