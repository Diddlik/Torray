using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Torray.Domain;
using Forms = System.Windows.Forms;

namespace Torray.App;

public partial class App(ISettingsStore settingsStore, ILogger<App> logger) : Application
{
    private FlyoutWindow? _flyout;
    private Forms.NotifyIcon? _trayIcon;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var settings = await LoadSettingsAsync();
        _flyout = new FlyoutWindow(settings.HideFlyoutOnDeactivate);
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
        menu.Items.Add(Text("TrayOpen"), null, (_, _) => _flyout?.ShowNearTray());
        menu.Items.Add(Text("TrayAbout"), null, (_, _) => ShowAbout());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Text("TrayExit"), null, (_, _) => ExitApplication());

        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = Text("AppName"),
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

    public static void ShowAbout() => new AboutWindow().ShowDialog();

    private void ExitApplication()
    {
        _flyout?.AllowClose();
        Shutdown();
    }

    private string Text(string key) => (string)FindResource(key);

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
