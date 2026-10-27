namespace Torray.Domain;

public interface ITransmissionRpcClient
{
    Task<ServerInfo> GetSessionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Torrent>> GetTorrentsAsync(CancellationToken cancellationToken = default);
}

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
