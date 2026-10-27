using System.Windows;

namespace Torray.App;

public partial class RemoveTorrentWindow : Window
{
    public RemoveTorrentWindow(string torrentName)
    {
        InitializeComponent();
        QuestionText.Text = Strings.Format("RemoveQuestion", torrentName);
    }

    public RemoveChoice Choice { get; private set; } = RemoveChoice.Cancel;

    private void OnKeepData(object sender, RoutedEventArgs e) => Close(RemoveChoice.KeepData);

    private void OnDeleteData(object sender, RoutedEventArgs e) => Close(RemoveChoice.DeleteData);

    private void Close(RemoveChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
