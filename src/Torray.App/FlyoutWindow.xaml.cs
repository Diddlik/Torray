using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Torray.App;

public partial class FlyoutWindow : Window, IFlyoutDialogs
{
    private const double DefaultHeight = 600;
    private const double EdgeMargin = 12;
    private const long ReopenGuardMilliseconds = 300;

    private readonly FlyoutViewModel _viewModel;
    private readonly bool _hideOnDeactivate;
    private bool _allowClose;
    private bool _dialogOpen;
    private long _hiddenAt;

    public FlyoutWindow(FlyoutViewModel viewModel, bool hideOnDeactivate)
    {
        InitializeComponent();
        _viewModel = viewModel;
        viewModel.Dialogs = this;
        DataContext = viewModel;
        _hideOnDeactivate = hideOnDeactivate;
    }

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
        if (_hideOnDeactivate && !_dialogOpen)
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

    // The taskbar edge is wherever the working area is inset from the screen bounds; the tray sits at the
    // far end of a horizontal taskbar and at the bottom of a vertical one.
    private void PositionNearTray()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        var dpi = VisualTreeHelper.GetDpi(this);

        Height = Math.Min(DefaultHeight, work.Height / dpi.DpiScaleY - 2 * EdgeMargin);

        Left = work.Left > bounds.Left
            ? work.Left / dpi.DpiScaleX + EdgeMargin
            : work.Right / dpi.DpiScaleX - Width - EdgeMargin;
        Top = work.Top > bounds.Top
            ? work.Top / dpi.DpiScaleY + EdgeMargin
            : work.Bottom / dpi.DpiScaleY - Height - EdgeMargin;
    }

    private void OnAboutClick(object sender, RoutedEventArgs e) => App.ShowAbout();

    private void OnConnectionClick(object sender, RoutedEventArgs e) => ((App)System.Windows.Application.Current).ShowConnection();
}
