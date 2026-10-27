using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Torray.Domain;

namespace Torray.Infrastructure;

// Spec: https://github.com/transmission/transmission/blob/main/docs/rpc-spec.md
public sealed class TransmissionRpcClient(HttpClient httpClient, Uri endpoint, AuthenticationHeaderValue? authorization)
    : ITransmissionRpcClient
{
    private const string SessionIdHeader = "X-Transmission-Session-Id";
    private const string RpcVersionHeader = "X-Transmission-Rpc-Version";
    private static readonly Version FirstJsonRpcVersion = new(6, 0, 0);

    private string? _sessionId;

    // Null until detected. Detection starts with the legacy protocol because every supported server accepts it.
    private RpcDialect? _dialect;

    public async Task<ServerInfo> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        var (result, dialect) = await SendAsync("session-get", "session_get", cancellationToken);
        var semverKey = dialect == RpcDialect.JsonRpc ? "rpc_version_semver" : "rpc-version-semver";

        // rpc-version-semver exists since Transmission 4.0, the oldest supported release.
        if (GetString(result, semverKey) is not { } semver)
        {
            throw new TransmissionRpcException(RpcErrorKind.UnsupportedVersion, "Server is older than Transmission 4.0.");
        }

        _dialect = IsJsonRpc(semver) ? RpcDialect.JsonRpc : RpcDialect.Legacy;
        return new ServerInfo(GetString(result, "version") ?? "—", semver, _dialect.Value);
    }

    public async Task<IReadOnlyList<Torrent>> GetTorrentsAsync(CancellationToken cancellationToken = default)
    {
        var (result, dialect) = await SendAsync(
            "torrent-get",
            "torrent_get",
            cancellationToken,
            d => new JsonObject { ["fields"] = new JsonArray([.. TorrentFields.Select(f => (JsonNode)FieldName(f, d))]) });

        try
        {
            return [.. result.GetProperty("torrents").EnumerateArray().Select(t => ParseTorrent(t, dialect))];
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new TransmissionRpcException(RpcErrorKind.InvalidResponse, "Unexpected torrent-get response.", ex);
        }
    }

    // Legacy (camelCase) names; JSON-RPC uses the snake_case form of the same fields.
    private static readonly string[] TorrentFields =
    [
        "id", "name", "status", "percentDone", "rateDownload", "rateUpload",
        "sizeWhenDone", "leftUntilDone", "eta", "queuePosition", "error", "errorString",
    ];

    private static string FieldName(string legacyName, RpcDialect dialect) =>
        dialect == RpcDialect.JsonRpc ? JsonNamingPolicy.SnakeCaseLower.ConvertName(legacyName) : legacyName;

    private static Torrent ParseTorrent(JsonElement t, RpcDialect dialect)
    {
        JsonElement Get(string legacyName) => t.GetProperty(FieldName(legacyName, dialect));

        var status = Get("status").GetInt32();
        var eta = Get("eta").GetInt64();
        return new Torrent(
            Id: Get("id").GetInt32(),
            Name: Get("name").GetString() ?? string.Empty,
            Status: Enum.IsDefined((TorrentStatus)status) ? (TorrentStatus)status : TorrentStatus.Unknown,
            PercentDone: Get("percentDone").GetDouble(),
            DownloadRate: Get("rateDownload").GetInt64(),
            UploadRate: Get("rateUpload").GetInt64(),
            SizeWhenDone: Get("sizeWhenDone").GetInt64(),
            LeftUntilDone: Get("leftUntilDone").GetInt64(),
            // Transmission reports -1 (not available) or -2 (unknown) instead of a duration.
            Eta: eta >= 0 ? TimeSpan.FromSeconds(eta) : null,
            QueuePosition: Get("queuePosition").GetInt32(),
            ErrorMessage: Get("error").GetInt32() != 0 ? Get("errorString").GetString() : null);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private async Task<(JsonElement Result, RpcDialect Dialect)> SendAsync(
        string legacyMethod,
        string jsonRpcMethod,
        CancellationToken cancellationToken,
        Func<RpcDialect, JsonObject>? buildArguments = null)
    {
        // One retry covers the CSRF handshake: the first request learns the session id from the 409 response.
        for (var attempt = 0; ; attempt++)
        {
            // Arguments are rebuilt per attempt because the handshake may switch the dialect and thus the field names.
            var dialect = _dialect ?? RpcDialect.Legacy;
            var body = dialect == RpcDialect.JsonRpc
                ? new JsonObject { ["jsonrpc"] = "2.0", ["method"] = jsonRpcMethod, ["id"] = 1 }
                : new JsonObject { ["method"] = legacyMethod };
            if (buildArguments is not null)
            {
                body[dialect == RpcDialect.JsonRpc ? "params" : "arguments"] = buildArguments(dialect);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = authorization;
            if (_sessionId is not null)
            {
                request.Headers.Add(SessionIdHeader, _sessionId);
            }

            using var response = await SendHttpAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Conflict && attempt == 0)
            {
                _sessionId = response.Headers.TryGetValues(SessionIdHeader, out var ids) ? ids.FirstOrDefault() : null;
                if (_sessionId is null)
                {
                    throw new TransmissionRpcException(RpcErrorKind.InvalidResponse, "409 response without session id.");
                }

                // Transmission 4.1+ announces its RPC version here, which lets us skip the legacy probe.
                if (response.Headers.TryGetValues(RpcVersionHeader, out var versions) && IsJsonRpc(versions.First()))
                {
                    _dialect = RpcDialect.JsonRpc;
                }

                continue;
            }

            EnsureSuccessStatus(response.StatusCode);
            return (await ReadResultAsync(response, dialect, cancellationToken), dialect);
        }
    }

    private async Task<HttpResponseMessage> SendHttpAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            throw new TransmissionRpcException(RpcErrorKind.CertificateInvalid, "TLS handshake failed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new TransmissionRpcException(RpcErrorKind.Unreachable, "Server is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TransmissionRpcException(RpcErrorKind.Timeout, "Request timed out.", ex);
        }
    }

    private static void EnsureSuccessStatus(HttpStatusCode status)
    {
        var kind = status switch
        {
            HttpStatusCode.Unauthorized => RpcErrorKind.Unauthorized,
            HttpStatusCode.Forbidden => RpcErrorKind.Forbidden,
            HttpStatusCode.NotFound => RpcErrorKind.NotFound,
            HttpStatusCode.Conflict => RpcErrorKind.InvalidResponse,
            >= HttpStatusCode.BadRequest => RpcErrorKind.ServerError,
            _ => (RpcErrorKind?)null,
        };

        if (kind is not null)
        {
            throw new TransmissionRpcException(kind.Value, $"HTTP {(int)status}.");
        }
    }

    private static async Task<JsonElement> ReadResultAsync(
        HttpResponseMessage response, RpcDialect dialect, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (dialect == RpcDialect.JsonRpc)
            {
                if (root.TryGetProperty("error", out var error))
                {
                    var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                    throw new TransmissionRpcException(RpcErrorKind.ServerError, message ?? "RPC error.");
                }

                return root.GetProperty("result").Clone();
            }

            var result = root.GetProperty("result").GetString();
            if (result != "success")
            {
                throw new TransmissionRpcException(RpcErrorKind.ServerError, result ?? "RPC error.");
            }

            return root.GetProperty("arguments").Clone();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new TransmissionRpcException(RpcErrorKind.InvalidResponse, "Response is not Transmission RPC.", ex);
        }
    }

    private static bool IsJsonRpc(string semver) =>
        Version.TryParse(semver.Split('-', '+')[0], out var version) && version >= FirstJsonRpcVersion;
}
