using Torray.Domain;

namespace Torray.Infrastructure;

public sealed class JsonWatchJobStore(string filePath) : IWatchJobStore
{
    public async Task<IReadOnlyList<WatchJob>> LoadAsync(CancellationToken cancellationToken = default) =>
        await JsonFile.ReadAsync<List<WatchJob>>(filePath, cancellationToken) ?? [];

    public Task SaveAsync(IReadOnlyList<WatchJob> jobs, CancellationToken cancellationToken = default) =>
        JsonFile.WriteAsync(filePath, jobs, cancellationToken);
}
