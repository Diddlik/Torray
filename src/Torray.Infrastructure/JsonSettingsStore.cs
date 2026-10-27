using Torray.Domain;

namespace Torray.Infrastructure;

public sealed class JsonSettingsStore(string filePath) : ISettingsStore
{
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        await JsonFile.ReadAsync<AppSettings>(filePath, cancellationToken) ?? new AppSettings();

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        JsonFile.WriteAsync(filePath, settings, cancellationToken);
}
