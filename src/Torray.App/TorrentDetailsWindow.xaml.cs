using System.Windows;

namespace Torray.App;

public partial class TorrentDetailsWindow : Window
{
    public TorrentDetailsWindow(TorrentDetailsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.LoadCommand.Execute(null);
        Closed += (_, _) => viewModel.LoadCommand.Cancel();
    }
}
