using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Torray.App;

public partial class FlyoutWindow : Window, IFlyoutDialogs
{
    private const double DefaultHeight = 600;
    private const double EdgeMargin = 12;
    private const long ReopenGuardMilliseconds = 300;

    private readonly FlyoutViewModel _viewModel;
    private bool _allowClose;
    private bool _dialogOpen;
    private long _hiddenAt;

    public FlyoutWindow(FlyoutViewModel viewModel, bool hideOnDeactivate)
    {
        InitializeComponent();
        _viewModel = viewModel;
        viewModel.Dialogs = this;
        DataContext = viewModel;
        HideOnDeactivate = hideOnDeactivate;
    }

    public bool HideOnDeactivate { get; set; }

    // Connection changes wait while a flyout dialog is open, so a pending remove cannot hit a different server.
    public bool IsDialogOpen => _dialogOpen;

    public RemoveChoice ConfirmRemove(string torrentName)
    {
        var dialog = new RemoveTorrentWindow(torrentName) { Owner = this };
        return ShowOwnedDialog(() => dialog.ShowDialog()) == true ? dialog.Choice : RemoveChoice.Cancel;
    }

    public IReadOnlyList<string> PickTorrentFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Strings.Get("TorrentFileFilter"), Multiselect = true };
        return ShowOwnedDialog(() => dialog.ShowDialog(this)) == true ? dialog.FileNames : [];
    }

    public void ShowDetails(TorrentDetailsViewModel details)
    {
        var dialog = new TorrentDetailsWindow(details) { Owner = this };
        ShowOwnedDialog(() => dialog.ShowDialog());
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            HideFlyout();
            return;
        }

        // Clicking the tray icon deactivates the flyout before the click arrives; without this guard
        // that same click would immediately reopen it.
        if (Environment.TickCount64 - _hiddenAt < ReopenGuardMilliseconds)
        {
            return;
        }

        ShowNearTray();
    }

    public void ShowNearTray()
    {
        PositionNearTray();
        Show();
        Activate();
    }

    public void AllowClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (HideOnDeactivate && !_dialogOpen && !_viewModel.IsPinned)
        {
            HideFlyout();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideFlyout();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 must only hide the flyout; a closed WPF window cannot be shown again.
        if (!_allowClose)
        {
            e.Cancel = true;
            HideFlyout();
        }

        base.OnClosing(e);
    }

    private void HideFlyout()
    {
        _viewModel.IsPinned = false;
        Hide();
        _hiddenAt = Environment.TickCount64;
    }

    // A dialog owned by the flyout deactivates it; without this the flyout would vanish behind its own dialog.
    private bool? ShowOwnedDialog(Func<bool?> show)
    {
        _dialogOpen = true;
        try
        {
            return show();
        }
        finally
        {
            _dialogOpen = false;
            Activate();
        }
    }

    private static string[] GetTorrentFiles(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] files
            ? [.. files.Where(f => f.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))]
            : [];

    private static string? GetDroppedText(IDataObject data) =>
        data.GetDataPresent(DataFormats.UnicodeText) ? data.GetData(DataFormats.UnicodeText) as string : null;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var supported = GetTorrentFiles(e.Data).Length > 0
            || GetDroppedText(e.Data)?.TrimStart().StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) == true;
        e.Effects = supported && _viewModel.IsConfigured ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = e.Effects == DragDropEffects.Copy ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!_viewModel.IsConfigured)
        {
            return;
        }

        var files = GetTorrentFiles(e.Data);
        if (files.Length > 0)
        {
            await _viewModel.AddFilesAsync(files);
        }
        else if (GetDroppedText(e.Data) is { } text)
        {
            await _viewModel.AddMagnetFromTextAsync(text);
        }
    }

    // Placement works in physical pixels of the monitor under the cursor (where the tray icon was clicked), so the
    // flyout lands correctly on any monitor regardless of each monitor's scaling. The taskbar edge is wherever the
    // working area is inset from the screen bounds; the tray sits at the far end of a horizontal taskbar and at the
    // bottom of a vertical one.
    private void PositionNearTray() => PositionNear(Forms.Cursor.Position);

    private void PositionNear(System.Drawing.Point cursor)
    {
        var screen = Forms.Screen.FromPoint(cursor);
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        Native.GetDpiForMonitor(Native.MonitorFromPoint(new Native.Point(cursor.X, cursor.Y), 2), 0, out var dpi, out _);
        var scale = dpi / 96.0;

        var margin = (int)Math.Round(EdgeMargin * scale);
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Min(Math.Round(DefaultHeight * scale), work.Height - 2 * margin);
        var x = work.Left > bounds.Left ? work.Left + margin : work.Right - width - margin;
        var y = work.Top > bounds.Top ? work.Top + margin : work.Bottom - height - margin;

        var handle = new WindowInteropHelper(this).EnsureHandle();
        Native.SetWindowPos(handle, 0, x, y, width, height, Native.NoZOrder | Native.NoActivate);
    }

    private static class Native
    {
        public const uint NoZOrder = 0x0004;
        public const uint NoActivate = 0x0010;

        public record struct Point(int X, int Y);

        [DllImport("user32.dll")]
        public static extern nint MonitorFromPoint(Point point, uint flags);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    }

    private void OnAboutClick(object sender, RoutedEventArgs e) => App.ShowAbout();

    private void OnConnectionClick(object sender, RoutedEventArgs e) => ((App)System.Windows.Application.Current).ShowConnection();

    private void OnWatchFoldersClick(object sender, RoutedEventArgs e) => ((App)System.Windows.Application.Current).ShowWatchFolders();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ((App)System.Windows.Application.Current).ShowSettings();

    private void OnServerMenuClick(object sender, RoutedEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        var menu = new ContextMenu { PlacementTarget = ServerMenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (connection, isActive) in app.Servers)
        {
            var item = new MenuItem { Header = connection.GetDisplayName(), IsCheckable = true, IsChecked = isActive };
            item.Click += (_, _) => app.SwitchServer(connection.Id);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var add = new MenuItem { Header = Strings.Get("TrayAddServer") };
        add.Click += (_, _) => app.ShowConnection(addNew: true);
        menu.Items.Add(add);
        menu.IsOpen = true;
    }

    // Row actions without a mouse: Space or Enter pauses/resumes, Delete removes (still asking first).
    private void OnTorrentListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is not ListBoxItem { DataContext: TorrentItemViewModel item })
        {
            return;
        }

        ICommand? command = e.Key switch
        {
            Key.Space or Key.Enter => _viewModel.ToggleStartCommand,
            Key.Delete => _viewModel.RemoveCommand,
            _ => null,
        };
        if (command is not null)
        {
            command.Execute(item);
            e.Handled = true;
        }
    }
}
