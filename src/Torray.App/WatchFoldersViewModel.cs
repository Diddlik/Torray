using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torray.Application;
using Torray.Domain;

namespace Torray.App;

public sealed partial class WatchFolderItem : ObservableObject
{
    public WatchFolderItem(WatchFolder folder)
    {
        Path = folder.Path;
        Enabled = folder.Enabled;
        MoveToArchive = folder.MoveToArchive;
    }

    public string Path { get; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial bool MoveToArchive { get; set; }

    public WatchFolder ToSettings() => new(Path, Enabled, MoveToArchive);
}

public sealed record WatchIssueItem(string Title, string Detail, bool IsFailed);

public sealed partial class WatchFoldersViewModel : ObservableObject
{
    private readonly WatchFolderService _service;
    private readonly Func<string?> _pickFolder;

    public WatchFoldersViewModel(IReadOnlyList<WatchFolder> folders, WatchFolderService service, Func<string?> pickFolder)
    {
        _service = service;
        _pickFolder = pickFolder;
        Folders = [.. folders.Select(f => new WatchFolderItem(f))];
        ApplySnapshot(service.Snapshot);
    }

    public event Action<bool>? CloseRequested;

    public ObservableCollection<WatchFolderItem> Folders { get; }

    public ObservableCollection<WatchIssueItem> Issues { get; } = [];

    [ObservableProperty]
    public partial bool HasIssues { get; set; }

    public IReadOnlyList<WatchFolder>? Result { get; private set; }

    public void ApplySnapshot(WatchSnapshot snapshot)
    {
        Issues.Clear();
        foreach (var folder in snapshot.MissingFolders)
        {
            Issues.Add(new WatchIssueItem(folder, Strings.Get("WatchFolderMissing"), IsFailed: true));
        }

        foreach (var job in snapshot.Issues)
        {
            var error = job.ErrorKey is null ? Strings.Get("WatchWaitingForConnection") : Strings.Format(job.ErrorKey, string.Empty);
            var detail = job.State == WatchJobState.Failed
                ? Strings.Format("WatchJobFailed", error)
                : Strings.Format("WatchJobPending", job.Attempts, error);
            Issues.Add(new WatchIssueItem(System.IO.Path.GetFileName(job.Path), detail, job.State == WatchJobState.Failed));
        }

        HasIssues = Issues.Count > 0;
    }

    [RelayCommand]
    private void AddFolder()
    {
        if (_pickFolder() is { } path
            && !Folders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            Folders.Add(new WatchFolderItem(new WatchFolder(path)));
        }
    }

    [RelayCommand]
    private void RemoveFolder(WatchFolderItem item) => Folders.Remove(item);

    [RelayCommand]
    private void Retry() => _service.RetryNow();

    [RelayCommand]
    private void Save()
    {
        Result = [.. Folders.Select(f => f.ToSettings())];
        CloseRequested?.Invoke(true);
    }
}
