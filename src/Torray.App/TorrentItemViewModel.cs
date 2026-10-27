using CommunityToolkit.Mvvm.ComponentModel;
using Torray.Application;
using Torray.Domain;

namespace Torray.App;

public sealed partial class TorrentItemViewModel : ObservableObject
{
    public TorrentItemViewModel(Torrent torrent)
    {
        Id = torrent.Id;
        Update(torrent);
    }

    public int Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial int QueuePosition { get; set; }

    [ObservableProperty]
    public partial double Percent { get; set; }

    [ObservableProperty]
    public partial string DetailText { get; set; }

    [ObservableProperty]
    public partial string RatesText { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStopped))]
    public partial TorrentStatus Status { get; set; }

    public bool IsStopped => Status == TorrentStatus.Stopped;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    // Observable setters only notify on change, so updating every refresh does not churn the UI.
    public void Update(Torrent torrent)
    {
        Name = torrent.Name;
        Status = torrent.Status;
        QueuePosition = torrent.QueuePosition;
        Percent = torrent.PercentDone * 100;
        HasError = torrent.ErrorMessage is not null;

        // Magnet links report size 0 until their metadata arrives; show unknown rather than "0 B".
        var sizeKnown = torrent.SizeWhenDone > 0;
        DetailText = Strings.Format(
            "TorrentDetail",
            torrent.ErrorMessage ?? Strings.Get("Status" + torrent.Status),
            Percent,
            sizeKnown ? DisplayFormat.Bytes(torrent.SizeWhenDone - torrent.LeftUntilDone) : DisplayFormat.Unknown,
            sizeKnown ? DisplayFormat.Bytes(torrent.SizeWhenDone) : DisplayFormat.Unknown,
            DisplayFormat.Eta(torrent.Eta));
        RatesText = Strings.Format(
            "TorrentRates", DisplayFormat.Rate(torrent.DownloadRate), DisplayFormat.Rate(torrent.UploadRate));
    }
}
