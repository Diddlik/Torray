using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Torray.Application;
using Torray.Domain;
using Forms = System.Windows.Forms;

namespace Torray.App;

public partial class App(
    ISettingsStore settingsStore,
    ISecretStore secretStore,
    ITransmissionRpcClientFactory rpcClientFactory,
    TorrentService refresh,
    WatchFolderService watchFolders,
    UpdateService updates,
    ILogger<App> logger) : System.Windows.Application
{
    private readonly TorrentTransitionTracker _transitions = new();
    private AppSettings _settings = new();
    private FlyoutViewModel? _flyoutViewModel;
    private FlyoutWindow? _flyout;
    private Forms.NotifyIcon? _trayIcon;
    private GlobalHotkey? _hotkey;
    private bool _settingsDialogOpen;

    public IEnumerable<(ConnectionSettings Connection, bool IsActive)> Servers
    {
        get
        {
            var activeId = _settings.GetActiveConnection()?.Id;
            return _settings.Connections.Select(c => (c, c.Id == activeId));
        }
    }

    // Settings dialogs may hold unsaved input, and a flyout dialog may be about to act on a torrent of the
    // current server; switching servers or opening another dialog waits until they are closed.
    private bool IsBusy => _settingsDialogOpen || _flyout?.IsDialogOpen == true;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _settings = await LoadSettingsAsync();
        var viewModel = new FlyoutViewModel(refresh);
        _flyoutViewModel = viewModel;
        refresh.Updated += snapshot => Dispatcher.InvokeAsync(() =>
        {
            viewModel.Apply(snapshot);
            Notify(_transitions.Track(snapshot));
        });
        watchFolders.Updated += snapshot => Dispatcher.InvokeAsync(() => viewModel.ApplyWatch(snapshot));
        watchFolders.Configure(_settings.WatchFolders);
        updates.UpdateReady += version => Dispatcher.InvokeAsync(() => viewModel.SetUpdateReady(version));
        viewModel.RestartToUpdate = RestartToUpdate;
        _ = RunUpdateChecksAsync();
        _flyout = new FlyoutWindow(viewModel, _settings.HideFlyoutOnDeactivate);
        _flyout.IsVisibleChanged += (_, _) => refresh.SetVisible(_flyout.IsVisible);
        _trayIcon = CreateTrayIcon();
        _hotkey = new GlobalHotkey(OnHotkey);
        if (!RegisterHotkey())
        {
            viewModel.SetNotice(Strings.Format("HotkeyUnavailable", _settings.Hotkey), isError: true);
        }

        await ApplyActiveConnectionAsync();
    }

    private async Task ApplyActiveConnectionAsync()
    {
        var connection = _settings.GetActiveConnection();
        ApplyConnection(connection, connection is null ? null : await LoadPasswordAsync(connection.Id));
    }

    private void ApplyConnection(ConnectionSettings? connection, string? password)
    {
        _flyoutViewModel!.ServerName = connection?.GetDisplayName() ?? Strings.Get("AppName");
        _flyoutViewModel.ServerHost = string.IsNullOrWhiteSpace(connection?.Name) ? string.Empty : connection.Host;
        refresh.SetClient(connection is null ? null : rpcClientFactory.Create(connection, password));
    }

    public async void SwitchServer(Guid id)
    {
        if (IsBusy || id == _settings.GetActiveConnection()?.Id)
        {
            return;
        }

        var settings = _settings with { ActiveConnectionId = id };
        if (await TrySaveSettingsAsync(settings, "SettingsSaveFailed"))
        {
            logger.LogInformation("Switched server");
            await ApplyActiveConnectionAsync();
        }
    }

    private bool RegisterHotkey()
    {
        if (_hotkey!.Register(_settings.HotkeyEnabled ? _settings.Hotkey : null))
        {
            return true;
        }

        logger.LogWarning("Hotkey {Hotkey} could not be registered", _settings.Hotkey);
        return false;
    }

    private void OnHotkey()
    {
        if (!IsBusy)
        {
            _flyout?.Toggle();
        }
    }

    private void Notify(IReadOnlyList<TorrentEvent> events)
    {
        if (!_settings.ShowNotifications || events.Count == 0 || _trayIcon is null)
        {
            return;
        }

        var completed = events.Where(e => e.Kind == TorrentEventKind.Completed).ToList();
        var failed = events.Where(e => e.Kind == TorrentEventKind.Failed).ToList();
        var lines = new List<string>();
        if (completed.Count > 0)
        {
            lines.Add(completed.Count == 1
                ? Strings.Format("NotifyCompleted", completed[0].Name)
                : Strings.Format("NotifyCompletedMany", completed.Count));
        }

        if (failed.Count > 0)
        {
            lines.Add(failed.Count == 1
                ? Strings.Format("NotifyFailed", failed[0].Name)
                : Strings.Format("NotifyFailedMany", failed.Count));
        }

        _trayIcon.ShowBalloonTip(
            5000,
            _flyoutViewModel!.ServerName,
            string.Join(Environment.NewLine, lines),
            failed.Count > 0 ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _trayIcon?.Dispose();
        updates.ApplyOnExit();
        base.OnExit(e);
    }

    private async Task RunUpdateChecksAsync()
    {
        // Give startup and the first torrent refresh priority over the network check.
        await Task.Delay(TimeSpan.FromMinutes(1));
        while (true)
        {
            if (_settings.CheckForUpdates && updates.ReadyVersion is null)
            {
                await updates.CheckAndDownloadAsync();
            }

            await Task.Delay(TimeSpan.FromHours(24));
        }
    }

    private void RestartToUpdate()
    {
        // Settings dialogs may hold unsaved input; the update waits until they are closed.
        if (_settingsDialogOpen)
        {
            return;
        }

        logger.LogInformation("Restarting to install update {Version}", updates.ReadyVersion);
        Serilog.Log.CloseAndFlush();
        updates.ApplyAndRestart();
    }

    public void ShowSettings()
    {
        if (IsBusy)
        {
            return;
        }

        _settingsDialogOpen = true;
        try
        {
            var viewModel = new SettingsViewModel(_settings, Autostart.IsEnabled, updates);
            if (new SettingsWindow(viewModel).ShowDialog() == true)
            {
                SaveGeneralSettings(viewModel);
            }
        }
        finally
        {
            _settingsDialogOpen = false;
        }
    }

    private async void SaveGeneralSettings(SettingsViewModel viewModel)
    {
        try
        {
            Autostart.Set(viewModel.StartWithWindows);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            logger.LogError(ex, "Autostart could not be changed");
            ShowError("SettingsSaveFailed");
            return;
        }

        var settings = _settings with
        {
            HideFlyoutOnDeactivate = viewModel.HideFlyoutOnDeactivate,
            CheckForUpdates = viewModel.CheckForUpdates,
            ShowNotifications = viewModel.ShowNotifications,
            HotkeyEnabled = viewModel.HotkeyEnabled,
            Hotkey = viewModel.Hotkey,
        };
        if (!await TrySaveSettingsAsync(settings, "SettingsSaveFailed"))
        {
            return;
        }

        _flyout!.HideOnDeactivate = settings.HideFlyoutOnDeactivate;
        logger.LogInformation("Settings saved");
        if (!RegisterHotkey())
        {
            MessageBox.Show(
                Strings.Format("HotkeyUnavailable", settings.Hotkey), Strings.Get("AppName"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task<bool> TrySaveSettingsAsync(AppSettings settings, string failureKey)
    {
        try
        {
            await settingsStore.SaveAsync(settings);
            _settings = settings;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Settings could not be saved");
            ShowError(failureKey);
            return false;
        }
    }

    private static void ShowError(string key) =>
        MessageBox.Show(Strings.Get(key), Strings.Get("AppName"), MessageBoxButton.OK, MessageBoxImage.Error);

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        ExitApplication();
        base.OnSessionEnding(e);
    }

    public static void ShowAbout() => new AboutWindow().ShowDialog();

    // Edits the active server, or creates a new one that becomes active.
    public async void ShowConnection(bool addNew = false)
    {
        // The tray menu stays usable while a modal dialog runs, so guard against a second dialog.
        if (IsBusy)
        {
            return;
        }

        _settingsDialogOpen = true;
        try
        {
            var existing = addNew ? null : _settings.GetActiveConnection();
            var password = existing is null ? null : await LoadPasswordAsync(existing.Id);
            var viewModel = new ConnectionViewModel(existing, password, rpcClientFactory);
            if (new ConnectionWindow(viewModel).ShowDialog() != true)
            {
                return;
            }

            if (viewModel.RemoveRequested && existing is not null)
            {
                await RemoveServerAsync(existing);
            }
            else if (viewModel.Result is var (connection, newPassword))
            {
                await SaveConnectionAsync(connection, newPassword);
            }
        }
        finally
        {
            _settingsDialogOpen = false;
        }
    }

    public async void ShowWatchFolders()
    {
        if (IsBusy)
        {
            return;
        }

        _settingsDialogOpen = true;
        WatchFoldersWindow? window = null;
        var viewModel = new WatchFoldersViewModel(_settings.WatchFolders, watchFolders, () => window?.PickFolder());
        void OnUpdated(WatchSnapshot snapshot) => Dispatcher.InvokeAsync(() => viewModel.ApplySnapshot(snapshot));
        watchFolders.Updated += OnUpdated;
        try
        {
            window = new WatchFoldersWindow(viewModel);
            if (window.ShowDialog() == true && viewModel.Result is { } folders)
            {
                await SaveWatchFoldersAsync(folders);
            }
        }
        finally
        {
            watchFolders.Updated -= OnUpdated;
            _settingsDialogOpen = false;
        }
    }

    private async Task SaveWatchFoldersAsync(IReadOnlyList<WatchFolder> folders)
    {
        try
        {
            var settings = _settings with { WatchFolders = folders };
            await settingsStore.SaveAsync(settings);
            _settings = settings;
            watchFolders.Configure(folders);
            logger.LogInformation("Watch folders saved ({Count})", folders.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Watch folders could not be saved");
            MessageBox.Show(Strings.Get("WatchSaveFailed"), Strings.Get("AppName"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveConnectionAsync(ConnectionSettings connection, string? password)
    {
        try
        {
            if (password is null)
            {
                secretStore.Delete(connection.Id);
            }
            else
            {
                await secretStore.SetAsync(connection.Id, password);
            }

            var connections = _settings.Connections.Any(c => c.Id == connection.Id)
                ? _settings.Connections.Select(c => c.Id == connection.Id ? connection : c).ToList()
                : [.. _settings.Connections, connection];
            var settings = _settings with { Connections = connections, ActiveConnectionId = connection.Id };
            await settingsStore.SaveAsync(settings);
            _settings = settings;
            ApplyConnection(connection, password);
            logger.LogInformation("Connection settings saved ({Count} servers)", connections.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            logger.LogError(ex, "Connection settings could not be saved");
            MessageBox.Show(Strings.Get("ConnectionSaveFailed"), Strings.Get("AppName"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Only Torray's profile is removed; torrents and files on the server are not touched.
    private async Task RemoveServerAsync(ConnectionSettings connection)
    {
        var answer = MessageBox.Show(
            Strings.Format("ConnectionRemoveQuestion", connection.GetDisplayName()),
            Strings.Get("AppName"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        var remaining = _settings.Connections.Where(c => c.Id != connection.Id).ToList();
        var settings = _settings with { Connections = remaining, ActiveConnectionId = remaining.FirstOrDefault()?.Id };
        if (!await TrySaveSettingsAsync(settings, "ConnectionSaveFailed"))
        {
            return;
        }

        try
        {
            secretStore.Delete(connection.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The profile is gone; an orphaned encrypted password file is harmless and only readable by this user.
            logger.LogWarning(ex, "Password of a removed server could not be deleted");
        }

        logger.LogInformation("Server removed ({Count} left)", remaining.Count);
        await ApplyActiveConnectionAsync();
    }

    private async Task<string?> LoadPasswordAsync(Guid id)
    {
        try
        {
            return await secretStore.GetAsync(id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Happens when the profile moved to another user or machine; DPAPI data cannot be decrypted there.
            logger.LogWarning(ex, "Stored password could not be read");
            return null;
        }
    }

    private async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            return await settingsStore.LoadAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Settings could not be loaded; using defaults");
            return new AppSettings();
        }
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Strings.Get("TrayOpen"), null, (_, _) => _flyout?.ShowNearTray());
        var servers = new Forms.ToolStripMenuItem(Strings.Get("TrayServers"));
        servers.DropDownItems.Add(new Forms.ToolStripMenuItem());
        servers.DropDownOpening += (_, _) => FillServerMenu(servers);
        menu.Items.Add(servers);
        menu.Items.Add(Strings.Get("TrayConnection"), null, (_, _) => ShowConnection());
        menu.Items.Add(Strings.Get("TrayWatchFolders"), null, (_, _) => ShowWatchFolders());
        menu.Items.Add(Strings.Get("TraySettings"), null, (_, _) => ShowSettings());
        menu.Items.Add(Strings.Get("TrayAbout"), null, (_, _) => ShowAbout());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Strings.Get("TrayExit"), null, (_, _) => ExitApplication());

        using var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/Torray.ico")).Stream;
        var icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize),
            Text = Strings.Get("AppName"),
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                _flyout?.Toggle();
            }
        };
        return icon;
    }

    // Rebuilt on every opening so it always reflects the saved profiles.
    private void FillServerMenu(Forms.ToolStripMenuItem servers)
    {
        servers.DropDownItems.Clear();
        foreach (var (connection, isActive) in Servers)
        {
            var id = connection.Id;
            servers.DropDownItems.Add(new Forms.ToolStripMenuItem(connection.GetDisplayName(), null, (_, _) => SwitchServer(id))
            {
                Checked = isActive,
            });
        }

        if (servers.DropDownItems.Count > 0)
        {
            servers.DropDownItems.Add(new Forms.ToolStripSeparator());
        }

        servers.DropDownItems.Add(Strings.Get("TrayAddServer"), null, (_, _) => ShowConnection(addNew: true));
    }

    private void ExitApplication()
    {
        _flyout?.AllowClose();
        Shutdown();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the tray app alive; a single failed UI action should not take down background work.
        logger.LogError(e.Exception, "Unhandled UI exception");
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        logger.LogError(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
