using System.Windows;
using System.Windows.Input;

namespace Torray.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += result => DialogResult = result;
        Closed += (_, _) => viewModel.CheckNowCommand.Cancel();
    }

    // Records the pressed combination instead of letting it type; keys without Ctrl, Alt, or Win (Tab, Esc)
    // keep their normal meaning so the dialog stays keyboard navigable.
    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        if (GlobalHotkey.Format(Keyboard.Modifiers, key) is { } gesture)
        {
            _viewModel.Hotkey = gesture;
            e.Handled = true;
        }
    }
}
