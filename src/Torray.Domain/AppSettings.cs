namespace Torray.Domain;

public sealed record AppSettings
{
    public bool HideFlyoutOnDeactivate { get; init; } = true;
    public ConnectionSettings? Connection { get; init; }
    public IReadOnlyList<WatchFolder> WatchFolders { get; init; } = [];
}
