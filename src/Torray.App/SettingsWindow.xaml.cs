using System.Windows;

namespace Torray.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += result => DialogResult = result;
        Closed += (_, _) => viewModel.CheckNowCommand.Cancel();
    }
}
