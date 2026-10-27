using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Torray.Domain;

namespace Torray.Application;

public enum ConnectionState
{
    NotConfigured,
    Connecting,
    Connected,
    Disconnected,
}

public sealed record TorrentSnapshot(
    ConnectionState State,
    IReadOnlyList<Torrent> Torrents,
    DateTimeOffset? LastSuccess,
    RpcErrorKind? Error)
{
    public static TorrentSnapshot NotConfigured { get; } = new(ConnectionState.NotConfigured, [], null, null);

    public static TorrentSnapshot Connecting { get; } = new(ConnectionState.Connecting, [], null, null);
}

// Polls the torrent list in a single loop, so refreshes never overlap: fast while the flyout is visible,
// slow while hidden, and with exponential backoff while the server is unreachable.
public sealed class TorrentRefreshService(ILogger<TorrentRefreshService> logger) : BackgroundService
{
    public static readonly TimeSpan VisibleInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan HiddenInterval = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _wake = new(0, 1);
    private volatile ITransmissionRpcClient? _client;
    private volatile bool _visible;
    private int _failures;
    private TorrentSnapshot _snapshot = TorrentSnapshot.NotConfigured;

    // Raised on a background thread; UI subscribers must marshal to their dispatcher.
    public event Action<TorrentSnapshot>? Updated;

    public void SetClient(ITransmissionRpcClient? client)
    {
        _client = client;
        _failures = 0;
        Publish(client is null ? TorrentSnapshot.NotConfigured : TorrentSnapshot.Connecting);
        Wake();
    }

    public void SetVisible(bool visible)
    {
        var becameVisible = visible && !_visible;
        _visible = visible;
        if (becameVisible)
        {
            Wake();
        }
    }

    public void RefreshNow() => Wake();

    public static TimeSpan GetDelay(bool visible, int failures)
    {
        var interval = visible ? VisibleInterval : HiddenInterval;
        var backoff = interval * Math.Pow(2, Math.Min(failures, 8));
        return backoff < MaxBackoff ? backoff : MaxBackoff;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var client = _client;
        if (client is null)
        {
            return;
        }

        try
        {
            var torrents = await client.GetTorrentsAsync(cancellationToken);
            if (client != _client)
            {
                return;
            }

            if (_failures > 0)
            {
                logger.LogInformation("Connection restored");
            }

            _failures = 0;
            Publish(new TorrentSnapshot(ConnectionState.Connected, torrents, DateTimeOffset.Now, null));
        }
        catch (TransmissionRpcException ex)
        {
            // A result for a connection the user has replaced in the meantime must not leak into the new one.
            if (client != _client)
            {
                return;
            }

            if (_failures++ == 0)
            {
                logger.LogWarning("Refresh failed: {Kind} ({Message})", ex.Kind, ex.Message);
            }

            Publish(_snapshot with { State = ConnectionState.Disconnected, Error = ex.Kind });
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
                await _wake.WaitAsync(GetDelay(_visible, _failures), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void Publish(TorrentSnapshot snapshot)
    {
        _snapshot = snapshot;
        Updated?.Invoke(snapshot);
    }

    private void Wake()
    {
        if (_wake.CurrentCount == 0)
        {
            try
            {
                _wake.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another caller woke the loop first; one pending wake is enough.
            }
        }
    }
}
