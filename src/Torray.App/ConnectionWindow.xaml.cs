using System.Windows;

namespace Torray.App;

public partial class ConnectionWindow : Window
{
    private readonly ConnectionViewModel _viewModel;

    public ConnectionWindow(ConnectionViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        PasswordBox.Password = viewModel.Password;
        viewModel.CloseRequested += result => DialogResult = result;
        Closed += (_, _) => viewModel.TestCommand.Cancel();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => _viewModel.Password = PasswordBox.Password;
}
