using System.Windows;

namespace Torray.App;

public partial class WatchFoldersWindow : Window
{
    public WatchFoldersWindow(WatchFoldersViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += result => DialogResult = result;
    }

    public string? PickFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }
}
