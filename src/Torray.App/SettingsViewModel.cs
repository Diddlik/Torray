using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torray.Domain;

namespace Torray.App;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly UpdateService _updates;

    public SettingsViewModel(AppSettings settings, bool startWithWindows, UpdateService updates)
    {
        _updates = updates;
        StartWithWindows = startWithWindows;
        HideFlyoutOnDeactivate = settings.HideFlyoutOnDeactivate;
        CheckForUpdates = settings.CheckForUpdates;
        if (updates.ReadyVersion is { } version)
        {
            UpdateStatus = Strings.Format("UpdateReady", version);
        }
    }

    public event Action<bool>? CloseRequested;

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool HideFlyoutOnDeactivate { get; set; }

    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    [ObservableProperty]
    public partial string UpdateStatus { get; set; } = string.Empty;

    [RelayCommand]
    private async Task CheckNowAsync(CancellationToken cancellationToken)
    {
        UpdateStatus = Strings.Get("UpdateChecking");
        var result = await _updates.CheckAndDownloadAsync(cancellationToken);
        UpdateStatus = result == UpdateCheckResult.Ready
            ? Strings.Format("UpdateReady", _updates.ReadyVersion)
            : Strings.Get("Update" + result);
    }

    [RelayCommand]
    private void Save() => CloseRequested?.Invoke(true);
}
