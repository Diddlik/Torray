namespace Torray.Domain;

public sealed record AppSettings
{
    public const string DefaultHotkey = "Ctrl+Alt+T";

    public bool HideFlyoutOnDeactivate { get; init; } = true;
    public bool CheckForUpdates { get; init; } = true;
    public bool ShowNotifications { get; init; } = true;
    public bool HotkeyEnabled { get; init; } = true;
    public string Hotkey { get; init; } = DefaultHotkey;

    // Single-server settings written before 0.6.0; JsonSettingsStore moves it into Connections on load.
    public ConnectionSettings? Connection { get; init; }
    public IReadOnlyList<ConnectionSettings> Connections { get; init; } = [];
    public Guid? ActiveConnectionId { get; init; }
    public IReadOnlyList<WatchFolder> WatchFolders { get; init; } = [];

    public ConnectionSettings? GetActiveConnection() =>
        Connections.FirstOrDefault(c => c.Id == ActiveConnectionId) ?? Connections.FirstOrDefault();
}
