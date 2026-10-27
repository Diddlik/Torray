using Microsoft.Extensions.Logging.Abstractions;
using Torray.Application;
using Torray.Domain;

namespace Torray.UnitTests;

public sealed class TorrentRefreshServiceTests
{
    private static readonly Torrent Sample =
        new(1, "a", TorrentStatus.Downloading, 0.5, 10, 0, 100, 50, null, 0, null);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true, 0, 2)]
    [InlineData(false, 0, 20)]
    [InlineData(true, 1, 4)]
    [InlineData(true, 3, 16)]
    [InlineData(false, 2, 60)]
    [InlineData(true, 50, 60)]
    public void Delay_adapts_to_visibility_and_backs_off_up_to_a_limit(bool visible, int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), TorrentRefreshService.GetDelay(visible, failures));
    }

    [Fact]
    public async Task Successful_refresh_publishes_connected_snapshot()
    {
        var service = new TorrentRefreshService(NullLogger<TorrentRefreshService>.Instance);
        var snapshots = new List<TorrentSnapshot>();
        service.Updated += snapshots.Add;

        service.SetClient(new FakeClient(() => [Sample]));
        await service.RefreshAsync(Token);

        Assert.Equal(ConnectionState.Connecting, snapshots[0].State);
        Assert.Equal(ConnectionState.Connected, snapshots[1].State);
        Assert.Equal(Sample, Assert.Single(snapshots[1].Torrents));
        Assert.NotNull(snapshots[1].LastSuccess);
    }

    [Fact]
    public async Task Failure_keeps_last_known_torrents_and_reports_error()
    {
        var service = new TorrentRefreshService(NullLogger<TorrentRefreshService>.Instance);
        var fail = false;
        service.SetClient(new FakeClient(() => fail
            ? throw new TransmissionRpcException(RpcErrorKind.Unreachable, "down")
            : [Sample]));
        await service.RefreshAsync(Token);
        TorrentSnapshot? last = null;
        service.Updated += s => last = s;

        fail = true;
        await service.RefreshAsync(Token);

        Assert.Equal(ConnectionState.Disconnected, last!.State);
        Assert.Equal(RpcErrorKind.Unreachable, last.Error);
        Assert.Equal(Sample, Assert.Single(last.Torrents));
        Assert.NotNull(last.LastSuccess);
    }

    [Fact]
    public async Task Result_from_replaced_connection_is_discarded()
    {
        var service = new TorrentRefreshService(NullLogger<TorrentRefreshService>.Instance);
        var release = new TaskCompletionSource();
        service.SetClient(new FakeClient(() => [Sample], release.Task));
        var pending = service.RefreshAsync(Token);
        TorrentSnapshot? last = null;
        service.Updated += s => last = s;

        service.SetClient(null);
        release.SetResult();
        await pending;

        Assert.Equal(ConnectionState.NotConfigured, last!.State);
        Assert.Empty(last.Torrents);
    }

    private sealed class FakeClient(Func<IReadOnlyList<Torrent>> torrents, Task? gate = null) : ITransmissionRpcClient
    {
        public Task<ServerInfo> GetSessionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<IReadOnlyList<Torrent>> GetTorrentsAsync(CancellationToken cancellationToken = default)
        {
            if (gate is not null)
            {
                await gate;
            }

            return torrents();
        }
    }
}
