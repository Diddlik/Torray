namespace Torray.Domain;

// Values match Transmission's tr_torrent_activity numbering.
public enum TorrentStatus
{
    Unknown = -1,
    Stopped = 0,
    QueuedToVerify = 1,
    Verifying = 2,
    QueuedToDownload = 3,
    Downloading = 4,
    QueuedToSeed = 5,
    Seeding = 6,
}

public sealed record Torrent(
    int Id,
    string Name,
    TorrentStatus Status,
    double PercentDone,
    long DownloadRate,
    long UploadRate,
    long SizeWhenDone,
    long LeftUntilDone,
    TimeSpan? Eta,
    int QueuePosition,
    string? ErrorMessage);

public enum TorrentFilter
{
    All,
    Downloading,
    Seeding,
    Paused,
}

public sealed record TorrentCounts(int Active, int Queued, int Paused, int Seeding)
{
    public static TorrentCounts From(IEnumerable<Torrent> torrents)
    {
        int active = 0, queued = 0, paused = 0, seeding = 0;
        foreach (var torrent in torrents)
        {
            switch (torrent.Status)
            {
                case TorrentStatus.Downloading or TorrentStatus.Verifying:
                    active++;
                    break;
                case TorrentStatus.QueuedToDownload or TorrentStatus.QueuedToSeed or TorrentStatus.QueuedToVerify:
                    queued++;
                    break;
                case TorrentStatus.Stopped:
                    paused++;
                    break;
                case TorrentStatus.Seeding:
                    seeding++;
                    break;
            }
        }

        return new TorrentCounts(active, queued, paused, seeding);
    }
}

public static class TorrentFilterExtensions
{
    public static bool Matches(this TorrentFilter filter, TorrentStatus status) => filter switch
    {
        TorrentFilter.Downloading => status is TorrentStatus.Downloading or TorrentStatus.QueuedToDownload,
        TorrentFilter.Seeding => status is TorrentStatus.Seeding or TorrentStatus.QueuedToSeed,
        TorrentFilter.Paused => status is TorrentStatus.Stopped,
        _ => true,
    };
}
