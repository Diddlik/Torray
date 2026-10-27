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
    private AppSettings _settings = new();
    private FlyoutViewModel? _flyoutViewModel;
    private FlyoutWindow? _flyout;
    private Forms.NotifyIcon? _trayIcon;
    private bool _settingsDialogOpen;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _settings = await LoadSettingsAsync();
        var viewModel = new FlyoutViewModel(refresh);
        _flyoutViewModel = viewModel;
        refresh.Updated += snapshot => Dispatcher.InvokeAsync(() => viewModel.Apply(snapshot));
        watchFolders.Updated += snapshot => Dispatcher.InvokeAsync(() => viewModel.ApplyWatch(snapshot));
        watchFolders.Configure(_settings.WatchFolders);
        updates.UpdateReady += version => Dispatcher.InvokeAsync(() => viewModel.SetUpdateReady(version));
        viewModel.RestartToUpdate = RestartToUpdate;
        _ = RunUpdateChecksAsync();
        _flyout = new FlyoutWindow(viewModel, _settings.HideFlyoutOnDeactivate);
        _flyout.IsVisibleChanged += (_, _) => refresh.SetVisible(_flyout.IsVisible);
        _trayIcon = CreateTrayIcon();

        var connection = _settings.Connection;
        ApplyConnection(connection, connection is null ? null : await LoadPasswordAsync(connection.Id));
    }

    private void ApplyConnection(ConnectionSettings? connection, string? password)
    {
        _flyoutViewModel!.ServerName = connection?.Host ?? Strings.Get("AppName");
        refresh.SetClient(connection is null ? null : rpcClientFactory.Create(connection, password));
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
        if (_settingsDialogOpen)
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
            var settings = _settings with
            {
                HideFlyoutOnDeactivate = viewModel.HideFlyoutOnDeactivate,
                CheckForUpdates = viewModel.CheckForUpdates,
            };
            await settingsStore.SaveAsync(settings);
            _settings = settings;
            _flyout!.HideOnDeactivate = settings.HideFlyoutOnDeactivate;
            logger.LogInformation("Settings saved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            logger.LogError(ex, "Settings could not be saved");
            MessageBox.Show(Strings.Get("SettingsSaveFailed"), Strings.Get("AppName"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        ExitApplication();
        base.OnSessionEnding(e);
    }

    public static void ShowAbout() => new AboutWindow().ShowDialog();

    public async void ShowConnection()
    {
        // The tray menu stays usable while a modal dialog runs, so guard against a second dialog.
        if (_settingsDialogOpen)
        {
            return;
        }

        _settingsDialogOpen = true;
        try
        {
            var existing = _settings.Connection;
            var password = existing is null ? null : await LoadPasswordAsync(existing.Id);
            var viewModel = new ConnectionViewModel(existing, password, rpcClientFactory);
            if (new ConnectionWindow(viewModel).ShowDialog() == true && viewModel.Result is var (connection, newPassword))
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
        if (_settingsDialogOpen)
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

            var settings = _settings with { Connection = connection };
            await settingsStore.SaveAsync(settings);
            _settings = settings;
            ApplyConnection(connection, password);
            logger.LogInformation("Connection settings saved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            logger.LogError(ex, "Connection settings could not be saved");
            MessageBox.Show(Strings.Get("ConnectionSaveFailed"), Strings.Get("AppName"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
