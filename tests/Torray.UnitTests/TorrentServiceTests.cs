using Microsoft.Extensions.Logging.Abstractions;
using Torray.Application;
using Torray.Domain;

namespace Torray.UnitTests;

public sealed class TorrentServiceTests
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
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), TorrentService.GetDelay(visible, failures));
    }

    [Fact]
    public async Task Successful_refresh_publishes_connected_snapshot()
    {
        var service = new TorrentService(NullLogger<TorrentService>.Instance);
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
        var service = new TorrentService(NullLogger<TorrentService>.Instance);
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
        var service = new TorrentService(NullLogger<TorrentService>.Instance);
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

    [Fact]
    public async Task Commands_use_current_connection_and_fail_without_one()
    {
        var service = new TorrentService(NullLogger<TorrentService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StopAsync(1, Token));

        var client = new FakeClient(() => []);
        service.SetClient(client);
        await service.RemoveAsync(5, deleteLocalData: false, Token);
        var added = await service.AddMagnetAsync("magnet:?xt=urn:btih:abc", AddTorrentOptions.Default, Token);

        Assert.Equal(["remove 5 False", "magnet"], client.Calls);
        Assert.True(added.IsDuplicate);
    }

    private sealed class FakeClient(Func<IReadOnlyList<Torrent>> torrents, Task? gate = null) : ITransmissionRpcClient
    {
        public List<string> Calls { get; } = [];

        public Task<ServerInfo> GetSessionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(int id, bool deleteLocalData, CancellationToken cancellationToken = default)
        {
            Calls.Add($"remove {id} {deleteLocalData}");
            return Task.CompletedTask;
        }

        public Task StartAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task StopAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AddedTorrent> AddMetainfoAsync(
            byte[] metainfo, AddTorrentOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TorrentDetails> GetDetailsAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AddedTorrent> AddMagnetAsync(
            string magnetUri, AddTorrentOptions options, CancellationToken cancellationToken = default)
        {
            Calls.Add("magnet");
            return Task.FromResult(new AddedTorrent(1, "a", IsDuplicate: true));
        }

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
