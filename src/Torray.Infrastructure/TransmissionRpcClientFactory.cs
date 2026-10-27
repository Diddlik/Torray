using System.Net.Http.Headers;
using System.Text;
using Torray.Domain;

namespace Torray.Infrastructure;

public sealed class TransmissionRpcClientFactory : ITransmissionRpcClientFactory, IDisposable
{
    // One shared HttpClient keeps connection pooling; credentials go on each request, never on the shared client.
    private readonly HttpClient _httpClient = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public ITransmissionRpcClient Create(ConnectionSettings connection, string? password)
    {
        var authorization = string.IsNullOrEmpty(connection.Username)
            ? null
            : new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.Username}:{password}")));
        return new TransmissionRpcClient(_httpClient, connection.GetEndpoint(), authorization);
    }

    public void Dispose() => _httpClient.Dispose();
}
