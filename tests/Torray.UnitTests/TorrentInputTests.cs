using Torray.Application;

namespace Torray.UnitTests;

public sealed class TorrentInputTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("Torray.Tests").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=x")]
    [InlineData("  magnet:?dn=x&xt=urn:btmh:1220abcd  ")]
    public void Valid_magnets_are_accepted_and_trimmed(string text)
    {
        Assert.Equal(text.Trim(), TorrentInput.ParseMagnet(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com/file.torrent")]
    [InlineData("magnet:?dn=no-hash")]
    [InlineData("just some text")]
    public void Invalid_magnets_are_rejected(string text)
    {
        var ex = Assert.Throws<TorrentInputException>(() => TorrentInput.ParseMagnet(text));
        Assert.Equal(TorrentInputError.NotMagnet, ex.Error);
    }

    [Fact]
    public async Task Valid_torrent_file_is_read()
    {
        var path = Write("a.torrent", "d8:announce3:urle"u8.ToArray());

        Assert.Equal((byte)'d', (await TorrentInput.ReadTorrentFileAsync(path, Token))[0]);
    }

    [Fact]
    public async Task Invalid_torrent_files_are_rejected()
    {
        await AssertRejected(Path.Combine(_directory, "missing.torrent"), TorrentInputError.FileNotFound);
        await AssertRejected(Write("a.txt", "d"u8.ToArray()), TorrentInputError.NotTorrentFile);
        await AssertRejected(Write("b.torrent", "<html>"u8.ToArray()), TorrentInputError.NotTorrentFile);
        await AssertRejected(Write("c.torrent", []), TorrentInputError.NotTorrentFile);
        await AssertRejected(
            Write("d.torrent", new byte[TorrentInput.MaxTorrentFileBytes + 1]), TorrentInputError.FileTooLarge);
    }

    private static async Task AssertRejected(string path, TorrentInputError expected)
    {
        var ex = await Assert.ThrowsAsync<TorrentInputException>(() => TorrentInput.ReadTorrentFileAsync(path, Token));
        Assert.Equal(expected, ex.Error);
    }

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }
}
