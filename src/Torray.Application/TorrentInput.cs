namespace Torray.Application;

public enum TorrentInputError
{
    NotMagnet,
    FileNotFound,
    FileTooLarge,
    NotTorrentFile,
    FileUnreadable,
    InvalidDownloadDir,
}

public sealed class TorrentInputException(TorrentInputError error) : Exception(error.ToString())
{
    public TorrentInputError Error { get; } = error;
}

// Validates user input before anything is sent to the server.
public static class TorrentInput
{
    // Real .torrent files are kilobytes to a few megabytes; anything far larger is not a torrent.
    public const long MaxTorrentFileBytes = 10 * 1024 * 1024;

    public static string ParseMagnet(string text)
    {
        var candidate = text.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && uri.Scheme == "magnet"
            && (uri.Query.Contains("xt=urn:btih:", StringComparison.OrdinalIgnoreCase)
                || uri.Query.Contains("xt=urn:btmh:", StringComparison.OrdinalIgnoreCase)))
        {
            return candidate;
        }

        throw new TorrentInputException(TorrentInputError.NotMagnet);
    }

    // Returns null for an empty entry, which keeps the server's default folder. The path is interpreted by the
    // daemon, so only its form can be checked here: absolute POSIX (/downloads) or Windows (D:\Downloads).
    public static string? ParseDownloadDir(string text)
    {
        var candidate = text.Trim();
        if (candidate.Length == 0)
        {
            return null;
        }

        var absolute = candidate.StartsWith('/')
            || (candidate.Length >= 3 && char.IsAsciiLetter(candidate[0]) && candidate[1] == ':' && candidate[2] is '\\' or '/');
        if (!absolute || candidate.Any(char.IsControl))
        {
            throw new TorrentInputException(TorrentInputError.InvalidDownloadDir);
        }

        return candidate;
    }

    public static async Task<byte[]> ReadTorrentFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Path.GetExtension(path), ".torrent", StringComparison.OrdinalIgnoreCase))
        {
            throw new TorrentInputException(TorrentInputError.NotTorrentFile);
        }

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new TorrentInputException(TorrentInputError.FileNotFound);
        }

        if (file.Length > MaxTorrentFileBytes)
        {
            throw new TorrentInputException(TorrentInputError.FileTooLarge);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TorrentInputException(TorrentInputError.FileUnreadable);
        }

        // Every .torrent file is a bencoded dictionary, which starts with 'd'.
        if (bytes.Length == 0 || bytes[0] != (byte)'d')
        {
            throw new TorrentInputException(TorrentInputError.NotTorrentFile);
        }

        return bytes;
    }
}
