namespace Torray.Domain;

public sealed record ConnectionSettings
{
    // Also the key of the password in ISecretStore, so settings never contain the secret itself.
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Name { get; init; }
    public string Scheme { get; init; } = "http";
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 9091;
    public string RpcPath { get; init; } = "/transmission/rpc";
    public string? Username { get; init; }

    public Uri GetEndpoint() => new UriBuilder(Scheme, Host, Port, RpcPath).Uri;

    public string GetDisplayName() => string.IsNullOrWhiteSpace(Name) ? Host : Name;
}
