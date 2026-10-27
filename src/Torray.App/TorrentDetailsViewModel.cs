using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torray.Application;
using Torray.Domain;

namespace Torray.App;

public sealed record DetailRow(string Label, string Value);

public sealed partial class TorrentDetailsViewModel(string name, Func<CancellationToken, Task<TorrentDetails>> load)
    : ObservableObject
{
    // Hundreds of peers are possible; a short sample is enough for a glance and keeps the window light.
    private const int MaxPeersShown = 50;

    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasDetails { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<DetailRow> Rows { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> Trackers { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> Peers { get; set; } = [];

    [ObservableProperty]
    public partial string PeersHeader { get; set; } = string.Empty;

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Show(await load(cancellationToken));
        }
        catch (TransmissionRpcException ex)
        {
            ErrorText = Strings.Format("RpcError" + ex.Kind, ex.Message);
        }
        catch (InvalidOperationException)
        {
            ErrorText = Strings.Get("FlyoutNotConfigured");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Show(TorrentDetails details)
    {
        List<DetailRow> rows =
        [
            new(Strings.Get("DetailsLocation"), details.DownloadDir),
            new(Strings.Get("DetailsSize"), DisplayFormat.Bytes(details.TotalSize)),
            new(Strings.Get("DetailsDownloaded"), DisplayFormat.Bytes(details.DownloadedEver)),
            new(Strings.Get("DetailsUploaded"), DisplayFormat.Bytes(details.UploadedEver)),
            new(Strings.Get("DetailsRatio"), details.Ratio is { } ratio ? ratio.ToString("0.00") : DisplayFormat.Unknown),
            new(Strings.Get("DetailsAdded"), FormatDate(details.Added)),
            new(Strings.Get("DetailsCompleted"), FormatDate(details.Done)),
            new(Strings.Get("DetailsHash"), details.HashString),
        ];
        if (details.ErrorMessage is { } error)
        {
            rows.Add(new(Strings.Get("DetailsError"), error));
        }

        Rows = rows;
        Trackers = [.. details.Trackers.Select(t => Strings.Format(
            "DetailsTracker", t.Host, OrUnknown(t.LastAnnounceResult), Count(t.Seeders), Count(t.Leechers)))];
        var peers = details.Peers.Take(MaxPeersShown).Select(p => Strings.Format(
            "DetailsPeer",
            p.Address,
            OrUnknown(p.Client),
            p.Progress * 100,
            DisplayFormat.Rate(p.DownloadRate),
            DisplayFormat.Rate(p.UploadRate)));
        Peers = details.Peers.Count > MaxPeersShown
            ? [.. peers, Strings.Format("DetailsMorePeers", details.Peers.Count - MaxPeersShown)]
            : [.. peers];
        PeersHeader = Strings.Format("DetailsPeers", details.Peers.Count);
        HasDetails = true;
    }

    private static string FormatDate(DateTimeOffset? date) =>
        date is { } value ? value.ToLocalTime().ToString("g") : DisplayFormat.Unknown;

    // Transmission reports -1 when a tracker has not announced counts yet.
    private static string Count(int value) => value >= 0 ? value.ToString() : DisplayFormat.Unknown;

    private static string OrUnknown(string value) => value.Length > 0 ? value : DisplayFormat.Unknown;
}
