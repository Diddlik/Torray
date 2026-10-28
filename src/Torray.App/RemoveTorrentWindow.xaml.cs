using System.Windows;

namespace Torray.App;

public partial class RemoveTorrentWindow : Window
{
    public RemoveTorrentWindow(string torrentName, string size)
    {
        InitializeComponent();
        NameText.Text = torrentName;
        SizeText.Text = size;
    }

    private void OnDeleteData(object sender, RoutedEventArgs e) => DialogResult = true;
}
