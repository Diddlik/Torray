using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Torray.App;

public partial class FlyoutWindow : Window
{
    private const double DefaultHeight = 600;
    private const double EdgeMargin = 12;
    private const long ReopenGuardMilliseconds = 300;

    private readonly bool _hideOnDeactivate;
    private bool _allowClose;
    private long _hiddenAt;

    public FlyoutWindow(bool hideOnDeactivate)
    {
        InitializeComponent();
        _hideOnDeactivate = hideOnDeactivate;
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
        if (_hideOnDeactivate)
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

    public void SetServer(string? host) => ServerText.Text = host ?? Strings.Get("FlyoutNotConfigured");

    private void OnAboutClick(object sender, RoutedEventArgs e) => App.ShowAbout();

    private void OnConnectionClick(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowConnection();
}
