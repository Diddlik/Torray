namespace Torray.Application;

public static class DisplayFormat
{
    public const string Unknown = "—";

    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{Math.Max(bytes, 0)} B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.0} {Units[unit]}";
    }

    public static string Rate(long bytesPerSecond) => Bytes(bytesPerSecond) + "/s";

    public static string Eta(TimeSpan? eta) => eta switch
    {
        null => Unknown,
        { TotalDays: >= 1 } e => $"{(int)e.TotalDays}d {e.Hours}h",
        { TotalHours: >= 1 } e => $"{e.Hours}h {e.Minutes}m",
        { TotalMinutes: >= 1 } e => $"{e.Minutes}m {e.Seconds}s",
        var e => $"{e.Value.Seconds}s",
    };
}
