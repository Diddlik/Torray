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
    public partial string PercentText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial string MetaText { get; set; }

    [ObservableProperty]
    public partial string SizeText { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; }

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
        ErrorMessage = torrent.ErrorMessage ?? string.Empty;
        StatusText = Strings.Get(HasError ? "StatusError" : "Status" + torrent.Status);

        // Magnet links report size 0 until their metadata arrives; show unknown rather than "0 B".
        var sizeKnown = torrent.SizeWhenDone > 0;
        var size = DisplayFormat.Bytes(torrent.SizeWhenDone);
        var done = DisplayFormat.Bytes(torrent.SizeWhenDone - torrent.LeftUntilDone);
        SizeText = sizeKnown ? size : Strings.Get("TorrentSizeUnknown");
        PercentText = sizeKnown && !HasError ? Strings.Format("TorrentPercent", Math.Floor(Percent * 10) / 10) : DisplayFormat.Unknown;
        MetaText = (sizeKnown, HasError, torrent.Status) switch
        {
            (false, false, _) => Strings.Get("TorrentGettingInfo"),
            (_, true, _) => SizeText,
            (_, _, TorrentStatus.Downloading) => Strings.Format(
                "TorrentMetaDownloading", done, size, DisplayFormat.Rate(torrent.DownloadRate), DisplayFormat.Eta(torrent.Eta)),
            (_, _, TorrentStatus.Seeding) => Strings.Format("TorrentMetaSeeding", size, DisplayFormat.Rate(torrent.UploadRate)),
            _ when torrent.LeftUntilDone == 0 => Strings.Format("TorrentMetaComplete", size),
            _ => Strings.Format("TorrentMetaProgress", done, size),
        };
    }
}
