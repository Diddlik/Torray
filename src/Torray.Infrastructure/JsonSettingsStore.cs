using Torray.Domain;

namespace Torray.Infrastructure;

public sealed class JsonSettingsStore(string filePath) : ISettingsStore
{
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        Migrate(await JsonFile.ReadAsync<AppSettings>(filePath, cancellationToken) ?? new AppSettings());

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        JsonFile.WriteAsync(filePath, settings, cancellationToken);

    // The profile keeps its Id, so its stored password still matches.
    private static AppSettings Migrate(AppSettings settings) =>
        settings.Connection is { } legacy && settings.Connections.Count == 0
            ? settings with { Connection = null, Connections = [legacy], ActiveConnectionId = legacy.Id }
            : settings;
}
