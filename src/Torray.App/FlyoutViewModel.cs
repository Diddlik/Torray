using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torray.Application;
using Torray.Domain;

namespace Torray.App;

public sealed record FilterOption(TorrentFilter Filter, string Label);

public sealed partial class FlyoutViewModel : ObservableObject
{
    private readonly TorrentRefreshService _refresh;
    private readonly ObservableCollection<TorrentItemViewModel> _torrents = [];
    private readonly Dictionary<int, TorrentItemViewModel> _torrentsById = [];
    private ConnectionState _state = ConnectionState.NotConfigured;

    public FlyoutViewModel(TorrentRefreshService refresh)
    {
        _refresh = refresh;
        Filters = [.. Enum.GetValues<TorrentFilter>().Select(f => new FilterOption(f, Strings.Get("Filter" + f)))];

        // Live shaping re-filters and re-sorts only the rows whose status or queue position changed.
        Torrents = new ListCollectionView(_torrents)
        {
            IsLiveFiltering = true,
            IsLiveSorting = true,
        };
        Torrents.LiveFilteringProperties.Add(nameof(TorrentItemViewModel.Status));
        Torrents.SortDescriptions.Add(new SortDescription(nameof(TorrentItemViewModel.QueuePosition), ListSortDirection.Ascending));
        Torrents.LiveSortingProperties.Add(nameof(TorrentItemViewModel.QueuePosition));

        SelectedFilter = Filters[0];
        Torrents.Filter = item => SelectedFilter.Filter.Matches(((TorrentItemViewModel)item).Status);
        Apply(TorrentSnapshot.NotConfigured);
    }

    public IReadOnlyList<FilterOption> Filters { get; }

    public ListCollectionView Torrents { get; }

    [ObservableProperty]
    public partial FilterOption SelectedFilter { get; set; }

    [ObservableProperty]
    public partial string ServerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsNotConfigured { get; set; }

    [ObservableProperty]
    public partial bool IsConfigured { get; set; }

    [ObservableProperty]
    public partial bool IsOffline { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastRefreshText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DownloadRate { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UploadRate { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CountsText { get; set; } = string.Empty;

    public void Apply(TorrentSnapshot snapshot)
    {
        _state = snapshot.State;
        MergeTorrents(snapshot.Torrents);

        IsConnected = snapshot.State == ConnectionState.Connected;
        IsNotConfigured = snapshot.State == ConnectionState.NotConfigured;
        IsConfigured = !IsNotConfigured;
        IsOffline = snapshot.State == ConnectionState.Disconnected;
        StatusText = Strings.Get(snapshot.State switch
        {
            ConnectionState.NotConfigured => "FlyoutNotConfigured",
            ConnectionState.Connecting => "FlyoutConnecting",
            ConnectionState.Connected => "FlyoutConnected",
            _ => "FlyoutCannotConnect",
        });
        ErrorText = snapshot.Error is { } error ? Strings.Format("RpcError" + error, string.Empty) : string.Empty;
        LastRefreshText = snapshot.LastSuccess is { } time
            ? Strings.Format("FlyoutLastRefresh", time)
            : Strings.Get("FlyoutNeverRefreshed");

        DownloadRate = Strings.Format("FlyoutDownloadRate", DisplayFormat.Rate(snapshot.Torrents.Sum(t => t.DownloadRate)));
        UploadRate = Strings.Format("FlyoutUploadRate", DisplayFormat.Rate(snapshot.Torrents.Sum(t => t.UploadRate)));
        var counts = TorrentCounts.From(snapshot.Torrents);
        CountsText = Strings.Format("FlyoutCounts", counts.Active, counts.Queued, counts.Paused, counts.Seeding);
        UpdateEmptyState();
    }

    [RelayCommand]
    private void Retry() => _refresh.RefreshNow();

    partial void OnSelectedFilterChanged(FilterOption value)
    {
        Torrents.Refresh();
        UpdateEmptyState();
    }

    private void MergeTorrents(IReadOnlyList<Torrent> torrents)
    {
        var seen = new HashSet<int>();
        foreach (var torrent in torrents)
        {
            seen.Add(torrent.Id);
            if (_torrentsById.TryGetValue(torrent.Id, out var item))
            {
                item.Update(torrent);
            }
            else
            {
                item = new TorrentItemViewModel(torrent);
                _torrentsById.Add(torrent.Id, item);
                _torrents.Add(item);
            }
        }

        foreach (var removed in _torrentsById.Values.Where(item => !seen.Contains(item.Id)).ToList())
        {
            _torrentsById.Remove(removed.Id);
            _torrents.Remove(removed);
        }
    }

    private void UpdateEmptyState() => IsEmpty = _state == ConnectionState.Connected && Torrents.IsEmpty;
}
