namespace Torray.Domain;

public interface ISecretStore
{
    Task<string?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetAsync(Guid id, string secret, CancellationToken cancellationToken = default);

    void Delete(Guid id);
}
