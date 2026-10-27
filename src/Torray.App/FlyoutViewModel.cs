using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torray.Application;
using Torray.Domain;

namespace Torray.App;

public sealed record FilterOption(TorrentFilter Filter, string Label);

public enum RemoveChoice
{
    Cancel,
    KeepData,
    DeleteData,
}

// Implemented by the view so the view model stays free of window handling.
public interface IFlyoutDialogs
{
    RemoveChoice ConfirmRemove(string torrentName);

    IReadOnlyList<string> PickTorrentFiles();
}

public sealed partial class FlyoutViewModel : ObservableObject
{
    private readonly TorrentService _service;
    private readonly ObservableCollection<TorrentItemViewModel> _torrents = [];
    private readonly Dictionary<int, TorrentItemViewModel> _torrentsById = [];
    private ConnectionState _state = ConnectionState.NotConfigured;

    public FlyoutViewModel(TorrentService service)
    {
        _service = service;
        Filters = [.. Enum.GetValues<TorrentFilter>().Select(f => new FilterOption(f, Strings.Get("Filter" + f)))];

        // Live shaping re-filters and re-sorts only the rows whose status, error, or queue position changed.
        Torrents = new ListCollectionView(_torrents)
        {
            IsLiveFiltering = true,
            IsLiveSorting = true,
        };
        Torrents.LiveFilteringProperties.Add(nameof(TorrentItemViewModel.Status));
        Torrents.LiveFilteringProperties.Add(nameof(TorrentItemViewModel.HasError));
        Torrents.SortDescriptions.Add(new SortDescription(nameof(TorrentItemViewModel.QueuePosition), ListSortDirection.Ascending));
        Torrents.LiveSortingProperties.Add(nameof(TorrentItemViewModel.QueuePosition));

        SelectedFilter = Filters[0];
        Torrents.Filter = item => item is TorrentItemViewModel torrent
            && SelectedFilter.Filter.Matches(torrent.Status, torrent.HasError);
        Apply(TorrentSnapshot.NotConfigured);
    }

    public IFlyoutDialogs? Dialogs { get; set; }

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

    [ObservableProperty]
    public partial string MagnetText { get; set; } = string.Empty;

    // Outcome of the last add/start/stop/remove, so failures are never silent.
    [ObservableProperty]
    public partial string NoticeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool NoticeIsError { get; set; }

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
        CountsText = Strings.Format(
            "FlyoutCounts", counts.Active, counts.Queued, counts.Paused, counts.Seeding, counts.Errors);
        UpdateEmptyState();
    }

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            await RunAsync(null, async () =>
            {
                var metainfo = await TorrentInput.ReadTorrentFileAsync(path);
                ReportAdded(await _service.AddFileAsync(metainfo));
            });
        }
    }

    public Task AddMagnetFromTextAsync(string text) => RunAsync(null, async () =>
    {
        var magnet = TorrentInput.ParseMagnet(text);
        ReportAdded(await _service.AddMagnetAsync(magnet));
        MagnetText = string.Empty;
    });

    [RelayCommand]
    private void Retry() => _service.RefreshNow();

    [RelayCommand]
    private Task AddMagnetAsync() => AddMagnetFromTextAsync(MagnetText);

    [RelayCommand]
    private Task OpenTorrentFilesAsync() => AddFilesAsync(Dialogs?.PickTorrentFiles() ?? []);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ToggleStartAsync(TorrentItemViewModel item) => RunAsync(item, () => item.IsStopped
        ? _service.StartAsync(item.Id)
        : _service.StopAsync(item.Id));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RemoveAsync(TorrentItemViewModel item)
    {
        var choice = Dialogs?.ConfirmRemove(item.Name) ?? RemoveChoice.Cancel;
        if (choice == RemoveChoice.Cancel)
        {
            return Task.CompletedTask;
        }

        var deleteData = choice == RemoveChoice.DeleteData;
        return RunAsync(item, async () =>
        {
            await _service.RemoveAsync(item.Id, deleteData);
            SetNotice(Strings.Format(deleteData ? "NoticeRemovedWithData" : "NoticeRemoved", item.Name), isError: false);
        });
    }

    partial void OnSelectedFilterChanged(FilterOption value)
    {
        Torrents.Refresh();
        UpdateEmptyState();
    }

    // Marks the row busy so a second click cannot send the same command while the first is in flight.
    private async Task RunAsync(TorrentItemViewModel? item, Func<Task> command)
    {
        if (item is { IsBusy: true })
        {
            return;
        }

        if (item is not null)
        {
            item.IsBusy = true;
        }

        try
        {
            await command();
        }
        catch (TransmissionRpcException ex)
        {
            SetNotice(Strings.Format("RpcError" + ex.Kind, ex.Message), isError: true);
        }
        catch (TorrentInputException ex)
        {
            SetNotice(Strings.Get("TorrentInput" + ex.Error), isError: true);
        }
        finally
        {
            if (item is not null)
            {
                item.IsBusy = false;
            }
        }
    }

    private void ReportAdded(AddedTorrent added) =>
        SetNotice(Strings.Format(added.IsDuplicate ? "NoticeDuplicate" : "NoticeAdded", added.Name), isError: false);

    private void SetNotice(string text, bool isError)
    {
        NoticeText = text;
        NoticeIsError = isError;
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
