namespace Torray.Domain;

public sealed record TorrentDetails(
    int Id,
    string Name,
    string DownloadDir,
    string HashString,
    long TotalSize,
    long DownloadedEver,
    long UploadedEver,
    // Null while Transmission reports the ratio as unavailable (-1) or infinite (-2).
    double? Ratio,
    DateTimeOffset? Added,
    DateTimeOffset? Done,
    string? ErrorMessage,
    IReadOnlyList<TrackerInfo> Trackers,
    IReadOnlyList<PeerInfo> Peers);

public sealed record TrackerInfo(string Host, string LastAnnounceResult, int Seeders, int Leechers);

public sealed record PeerInfo(string Address, string Client, double Progress, long DownloadRate, long UploadRate);

// Null DownloadDir keeps the server's default download folder.
public sealed record AddTorrentOptions(string? DownloadDir = null, bool Paused = false)
{
    public static AddTorrentOptions Default { get; } = new();
}
