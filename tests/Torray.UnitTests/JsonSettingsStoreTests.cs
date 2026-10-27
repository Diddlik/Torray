using System.Text.Json;
using Torray.Domain;
using Torray.Infrastructure;

namespace Torray.UnitTests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Torray.Tests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "nested", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_returns_defaults_when_file_is_missing()
    {
        var store = new JsonSettingsStore(SettingsPath);

        var settings = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new AppSettings(), settings);
    }

    [Fact]
    public async Task SaveAsync_then_LoadAsync_round_trips_and_leaves_no_temp_file()
    {
        var store = new JsonSettingsStore(SettingsPath);
        var saved = new AppSettings
        {
            HideFlyoutOnDeactivate = false,
            WatchFolders = [new WatchFolder(@"C:\Torrents", Enabled: false, MoveToArchive: true)],
        };

        await store.SaveAsync(saved, TestContext.Current.CancellationToken);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        // Records compare lists by reference, so compare the list contents separately.
        Assert.Equal(saved with { WatchFolders = [] }, loaded with { WatchFolders = [] });
        Assert.Equal(saved.WatchFolders, loaded.WatchFolders);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public async Task LoadAsync_throws_on_corrupt_file()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await File.WriteAllTextAsync(SettingsPath, "{ not json", TestContext.Current.CancellationToken);
        var store = new JsonSettingsStore(SettingsPath);

        await Assert.ThrowsAnyAsync<JsonException>(() => store.LoadAsync(TestContext.Current.CancellationToken));
    }
}
