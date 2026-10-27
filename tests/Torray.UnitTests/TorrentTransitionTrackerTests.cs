using Torray.Application;
using Torray.Domain;

namespace Torray.UnitTests;

public sealed class TorrentTransitionTrackerTests
{
    [Fact]
    public void First_list_seeds_state_without_events()
    {
        var tracker = new TorrentTransitionTracker();

        Assert.Empty(tracker.Track(Connected(Make(1, left: 0), Make(2, left: 50, error: "Tracker gone"))));
    }

    [Fact]
    public void Reports_completion_and_new_error_once()
    {
        var tracker = new TorrentTransitionTracker();
        tracker.Track(Connected(Make(1, left: 50), Make(2, left: 50)));

        var events = tracker.Track(Connected(Make(1, left: 0), Make(2, left: 50, error: "No data found")));

        Assert.Equal(
            [new TorrentEvent(TorrentEventKind.Completed, "t1"), new TorrentEvent(TorrentEventKind.Failed, "t2")],
            events);
        Assert.Empty(tracker.Track(Connected(Make(1, left: 0), Make(2, left: 50, error: "No data found"))));
    }

    [Fact]
    public void Ignores_added_torrents_metadata_and_verification()
    {
        var tracker = new TorrentTransitionTracker();
        tracker.Track(Connected(Make(1, left: 0, size: 0), Make(2, left: 50)));

        // Magnet metadata arriving and a re-check passing over existing data are not completions.
        var events = tracker.Track(Connected(
            Make(1, left: 100, size: 100),
            Make(2, left: 0, status: TorrentStatus.Verifying),
            Make(3, left: 0)));

        Assert.Empty(events);
    }

    [Fact]
    public void Server_switch_resets_and_offline_keeps_state()
    {
        var tracker = new TorrentTransitionTracker();
        tracker.Track(Connected(Make(1, left: 50)));

        tracker.Track(new TorrentSnapshot(ConnectionState.Disconnected, [], null, RpcErrorKind.Unreachable));
        Assert.Single(tracker.Track(Connected(Make(1, left: 0))));

        tracker.Track(Connected(Make(1, left: 50)));
        tracker.Track(TorrentSnapshot.Connecting);
        Assert.Empty(tracker.Track(Connected(Make(1, left: 0))));
    }

    private static TorrentSnapshot Connected(params Torrent[] torrents) =>
        new(ConnectionState.Connected, torrents, DateTimeOffset.Now, null);

    private static Torrent Make(
        int id, long left, long size = 100, string? error = null, TorrentStatus status = TorrentStatus.Downloading) =>
        new(id, $"t{id}", status, (size - left) / 100.0, 0, 0, size, left, null, id, error);
}
