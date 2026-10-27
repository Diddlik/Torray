using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Torray.Domain;
using Torray.Infrastructure;

namespace Torray.UnitTests;

public sealed class TransmissionRpcClientTests
{
    private const string LegacySession40 =
        """{"result":"success","arguments":{"version":"4.0.6 (38c164933e)","rpc-version-semver":"5.3.0"}}""";

    private static readonly Uri Endpoint = new("http://nas:9091/transmission/rpc");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Retries_once_with_session_id_from_409()
    {
        var handler = new FakeHandler(
            _ => Conflict("abc"),
            _ => Json(LegacySession40));
        var client = CreateClient(handler);

        var info = await client.GetSessionAsync(Token);

        Assert.Equal("4.0.6 (38c164933e)", info.Version);
        Assert.Equal(RpcDialect.Legacy, info.Dialect);
        Assert.Equal(2, handler.Requests.Count);
        Assert.False(handler.Requests[0].Headers.ContainsKey("X-Transmission-Session-Id"));
        Assert.Equal("abc", handler.Requests[1].Headers["X-Transmission-Session-Id"]);
        Assert.Equal("session-get", handler.Requests[1].Body.GetProperty("method").GetString());
    }

    [Fact]
    public async Task Repeated_409_is_reported_without_endless_retry()
    {
        var handler = new FakeHandler(_ => Conflict("a"), _ => Conflict("b"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<TransmissionRpcException>(() => client.GetSessionAsync(Token));

        Assert.Equal(RpcErrorKind.InvalidResponse, ex.Kind);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Switches_to_json_rpc_when_409_announces_rpc_6()
    {
        var handler = new FakeHandler(
            _ => Conflict("abc", rpcVersion: "6.0.1"),
            _ => Json("""{"jsonrpc":"2.0","result":{"version":"4.1.1 (abc)","rpc_version_semver":"6.0.1"},"id":1}"""));
        var client = CreateClient(handler);

        var info = await client.GetSessionAsync(Token);

        Assert.Equal(RpcDialect.JsonRpc, info.Dialect);
        Assert.Equal("6.0.1", info.RpcVersionSemver);
        var body = handler.Requests[1].Body;
        Assert.Equal("2.0", body.GetProperty("jsonrpc").GetString());
        Assert.Equal("session_get", body.GetProperty("method").GetString());
    }

    [Fact]
    public async Task Legacy_probe_detects_json_rpc_server_for_later_calls()
    {
        var handler = new FakeHandler(
            _ => Json("""{"result":"success","arguments":{"version":"4.1.0","rpc-version-semver":"6.0.0"}}"""),
            _ => Json("""{"jsonrpc":"2.0","result":{"version":"4.1.0","rpc_version_semver":"6.0.0"},"id":1}"""));
        var client = CreateClient(handler);

        Assert.Equal(RpcDialect.JsonRpc, (await client.GetSessionAsync(Token)).Dialect);
        await client.GetSessionAsync(Token);

        Assert.Equal("session_get", handler.Requests[1].Body.GetProperty("method").GetString());
    }

    [Fact]
    public async Task Server_older_than_4_0_is_unsupported()
    {
        var handler = new FakeHandler(_ => Json("""{"result":"success","arguments":{"version":"3.00","rpc-version":16}}"""));

        var ex = await Assert.ThrowsAsync<TransmissionRpcException>(() => CreateClient(handler).GetSessionAsync(Token));

        Assert.Equal(RpcErrorKind.UnsupportedVersion, ex.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, RpcErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, RpcErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, RpcErrorKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, RpcErrorKind.ServerError)]
    public async Task Http_errors_map_to_error_kinds(HttpStatusCode status, RpcErrorKind expected)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status));

        var ex = await Assert.ThrowsAsync<TransmissionRpcException>(() => CreateClient(handler).GetSessionAsync(Token));

        Assert.Equal(expected, ex.Kind);
    }

    [Theory]
    [InlineData("<html>proxy login</html>")]
    [InlineData("""{"unexpected":true}""")]
    public async Task Malformed_response_is_invalid(string content)
    {
        var handler = new FakeHandler(_ => Json(content));

        var ex = await Assert.ThrowsAsync<TransmissionRpcException>(() => CreateClient(handler).GetSessionAsync(Token));

        Assert.Equal(RpcErrorKind.InvalidResponse, ex.Kind);
    }

    [Fact]
    public async Task Legacy_failure_result_is_server_error()
    {
        var handler = new FakeHandler(_ => Json("""{"result":"method name not recognized","arguments":{}}"""));

        var ex = await Assert.ThrowsAsync<TransmissionRpcException>(() => CreateClient(handler).GetSessionAsync(Token));

        Assert.Equal(RpcErrorKind.ServerError, ex.Kind);
        Assert.Equal("method name not recognized", ex.Message);
    }

    [Fact]
    public async Task Json_rpc_error_object_is_server_error()
    {
        var handler = new FakeHandler(
            _ => Conflict("abc", rpcVersion: "6.0.0"),
            _ => Json("""{"jsonrpc":"2.0","error":{"code":-32601,"message":"Method not found"},"id":1}"""));

        var ex = await Assert.ThrowsAsync<TransmissionRpcException>(() => CreateClient(handler).GetSessionAsync(Token));

        Assert.Equal(RpcErrorKind.ServerError, ex.Kind);
        Assert.Equal("Method not found", ex.Message);
    }

    [Fact]
    public async Task Connection_failure_is_unreachable()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("No such host"));

        var ex = await Assert.ThrowsAsync<TransmissionRpcException>(() => CreateClient(handler).GetSessionAsync(Token));

        Assert.Equal(RpcErrorKind.Unreachable, ex.Kind);
    }

    [Fact]
    public async Task Http_client_timeout_is_reported_as_timeout()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException());

        var ex = await Assert.ThrowsAsync<TransmissionRpcException>(() => CreateClient(handler).GetSessionAsync(Token));

        Assert.Equal(RpcErrorKind.Timeout, ex.Kind);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_timeout()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new FakeHandler(_ => Json(LegacySession40));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(handler).GetSessionAsync(cts.Token));
    }

    [Fact]
    public async Task Sends_authorization_header()
    {
        var connection = new ConnectionSettings { Host = "nas", Username = "user" };
        var withAuth = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("user:secret")));
        var handler = new FakeHandler(_ => Json(LegacySession40));

        await new TransmissionRpcClient(new HttpClient(handler), connection.GetEndpoint(), withAuth).GetSessionAsync(Token);

        Assert.Equal($"Basic {withAuth.Parameter}", handler.Requests[0].Headers["Authorization"]);
    }

    [Fact]
    public async Task GetTorrentsAsync_parses_legacy_fields()
    {
        var handler = new FakeHandler(_ => Json("""
            {"result":"success","arguments":{"torrents":[{"id":7,"name":"debian.iso","status":4,"percentDone":0.25,
            "rateDownload":1000,"rateUpload":20,"sizeWhenDone":4000,"leftUntilDone":3000,"eta":-1,"queuePosition":2,
            "error":0,"errorString":""}]}}
            """));

        var torrents = await CreateClient(handler).GetTorrentsAsync(Token);

        Assert.Equal(
            new Torrent(7, "debian.iso", TorrentStatus.Downloading, 0.25, 1000, 20, 4000, 3000, null, 2, null),
            Assert.Single(torrents));
        var arguments = handler.Requests[0].Body.GetProperty("arguments");
        Assert.Contains("percentDone", arguments.GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task GetTorrentsAsync_uses_snake_case_params_for_json_rpc()
    {
        var handler = new FakeHandler(
            _ => Conflict("abc", rpcVersion: "6.0.1"),
            _ => Json("""
                {"jsonrpc":"2.0","id":1,"result":{"torrents":[{"id":1,"name":"a","status":6,"percent_done":1,
                "rate_download":0,"rate_upload":5,"size_when_done":10,"left_until_done":0,"eta":90,"queue_position":0,
                "error":3,"error_string":"No data found"}]}}
                """));

        var torrent = Assert.Single(await CreateClient(handler).GetTorrentsAsync(Token));

        Assert.Equal(TorrentStatus.Seeding, torrent.Status);
        Assert.Equal(TimeSpan.FromSeconds(90), torrent.Eta);
        Assert.Equal("No data found", torrent.ErrorMessage);
        var fields = handler.Requests[1].Body.GetProperty("params").GetProperty("fields").EnumerateArray()
            .Select(f => f.GetString());
        Assert.Contains("percent_done", fields);
    }

    [Fact]
    public async Task GetTorrentsAsync_maps_unknown_status_and_rejects_missing_fields()
    {
        var unknownStatus = new FakeHandler(_ => Json("""
            {"result":"success","arguments":{"torrents":[{"id":1,"name":"a","status":42,"percentDone":0,
            "rateDownload":0,"rateUpload":0,"sizeWhenDone":0,"leftUntilDone":0,"eta":-2,"queuePosition":0,
            "error":0,"errorString":""}]}}
            """));
        var missingField = new FakeHandler(_ => Json("""{"result":"success","arguments":{"torrents":[{"id":1}]}}"""));

        Assert.Equal(TorrentStatus.Unknown, Assert.Single(await CreateClient(unknownStatus).GetTorrentsAsync(Token)).Status);
        var ex = await Assert.ThrowsAsync<TransmissionRpcException>(() => CreateClient(missingField).GetTorrentsAsync(Token));
        Assert.Equal(RpcErrorKind.InvalidResponse, ex.Kind);
    }

    private static TransmissionRpcClient CreateClient(FakeHandler handler) => new(new HttpClient(handler), Endpoint, null);

    private static HttpResponseMessage Conflict(string sessionId, string? rpcVersion = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Conflict);
        response.Headers.Add("X-Transmission-Session-Id", sessionId);
        if (rpcVersion is not null)
        {
            response.Headers.Add("X-Transmission-Rpc-Version", rpcVersion);
        }

        return response;
    }

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(Dictionary<string, string> Headers, JsonElement Body);

    private sealed class FakeHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            Requests.Add(new CapturedRequest(headers, body));
            return responses[Math.Min(Requests.Count - 1, responses.Length - 1)](request);
        }
    }
}
