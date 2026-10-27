using System.Text;
using Torray.Infrastructure;

namespace Torray.UnitTests;

public sealed class DpapiSecretStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Torray.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Round_trips_without_storing_plaintext()
    {
        var store = new DpapiSecretStore(_directory);
        var id = Guid.NewGuid();

        await store.SetAsync(id, "p@ss-wörd", TestContext.Current.CancellationToken);

        Assert.Equal("p@ss-wörd", await store.GetAsync(id, TestContext.Current.CancellationToken));
        var raw = await File.ReadAllBytesAsync(Directory.GetFiles(_directory).Single(), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("p@ss", Encoding.UTF8.GetString(raw));
    }

    [Fact]
    public async Task Missing_or_deleted_secret_returns_null()
    {
        var store = new DpapiSecretStore(_directory);
        var id = Guid.NewGuid();
        await store.SetAsync(id, "secret", TestContext.Current.CancellationToken);

        store.Delete(id);

        Assert.Null(await store.GetAsync(id, TestContext.Current.CancellationToken));
        Assert.Null(await store.GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }
}
