namespace Torray.Domain;

public interface ITransmissionRpcClient
{
    Task<ServerInfo> GetSessionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Torrent>> GetTorrentsAsync(CancellationToken cancellationToken = default);

    // Transmission always discards its copy of the .torrent file; deleteLocalData also deletes the downloaded payload.
    Task RemoveAsync(int id, bool deleteLocalData, CancellationToken cancellationToken = default);

    Task StartAsync(int id, CancellationToken cancellationToken = default);

    Task StopAsync(int id, CancellationToken cancellationToken = default);

    Task<AddedTorrent> AddMetainfoAsync(byte[] metainfo, AddTorrentOptions options, CancellationToken cancellationToken = default);

    Task<AddedTorrent> AddMagnetAsync(string magnetUri, AddTorrentOptions options, CancellationToken cancellationToken = default);

    Task<TorrentDetails> GetDetailsAsync(int id, CancellationToken cancellationToken = default);
}

public sealed record AddedTorrent(int Id, string Name, bool IsDuplicate);

public interface ITransmissionRpcClientFactory
{
    ITransmissionRpcClient Create(ConnectionSettings connection, string? password);
}

public enum RpcDialect
{
    // Transmission 4.0: dash/camelCase names, "arguments" and "result": "success".
    Legacy,

    // Transmission 4.1+ (rpc_version_semver 6.0.0+): JSON-RPC 2.0 with snake_case names.
    JsonRpc,
}

public sealed record ServerInfo(string Version, string RpcVersionSemver, RpcDialect Dialect);

public enum RpcErrorKind
{
    Unreachable,
    Timeout,
    CertificateInvalid,
    Unauthorized,
    Forbidden,
    NotFound,
    InvalidResponse,
    ServerError,
    UnsupportedVersion,
}

public sealed class TransmissionRpcException(RpcErrorKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public RpcErrorKind Kind { get; } = kind;
}
