using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Torray.Domain;
using Forms = System.Windows.Forms;

namespace Torray.App;

public partial class App(
    ISettingsStore settingsStore,
    ISecretStore secretStore,
    ITransmissionRpcClientFactory rpcClientFactory,
    ILogger<App> logger) : Application
{
    private AppSettings _settings = new();
    private FlyoutWindow? _flyout;
    private Forms.NotifyIcon? _trayIcon;
    private bool _connectionDialogOpen;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _settings = await LoadSettingsAsync();
        _flyout = new FlyoutWindow(_settings.HideFlyoutOnDeactivate);
        _flyout.SetServer(_settings.Connection?.Host);
        _trayIcon = CreateTrayIcon();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
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
        if (_connectionDialogOpen)
        {
            return;
        }

        _connectionDialogOpen = true;
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
            _connectionDialogOpen = false;
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
            _flyout?.SetServer(connection.Host);
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
