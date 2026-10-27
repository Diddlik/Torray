using Torray.Domain;

namespace Torray.Application;

public enum TorrentEventKind
{
    Completed,
    Failed,
}

public sealed record TorrentEvent(TorrentEventKind Kind, string Name);

// Compares consecutive connected snapshots of one server, so only real transitions produce events: nothing fires
// for the first list after start or a server switch, for torrents added already complete, or while offline.
public sealed class TorrentTransitionTracker
{
    private Dictionary<int, Torrent>? _previous;

    public IReadOnlyList<TorrentEvent> Track(TorrentSnapshot snapshot)
    {
        switch (snapshot.State)
        {
            case ConnectionState.NotConfigured or ConnectionState.Connecting:
                _previous = null;
                return [];
            case ConnectionState.Disconnected:
                return [];
        }

        var previous = _previous;
        _previous = snapshot.Torrents.ToDictionary(t => t.Id);
        if (previous is null)
        {
            return [];
        }

        var events = new List<TorrentEvent>();
        foreach (var torrent in snapshot.Torrents)
        {
            if (!previous.TryGetValue(torrent.Id, out var before))
            {
                continue;
            }

            if (before.ErrorMessage is null && torrent.ErrorMessage is not null)
            {
                events.Add(new TorrentEvent(TorrentEventKind.Failed, torrent.Name));
            }
            else if (IsComplete(torrent) && !IsComplete(before))
            {
                events.Add(new TorrentEvent(TorrentEventKind.Completed, torrent.Name));
            }
        }

        return events;
    }

    // Magnets report size 0 until their metadata arrives; verifying re-counts data that was already there.
    private static bool IsComplete(Torrent torrent) =>
        torrent.SizeWhenDone > 0
        && torrent.LeftUntilDone == 0
        && torrent.Status is not (TorrentStatus.Verifying or TorrentStatus.QueuedToVerify);
}
