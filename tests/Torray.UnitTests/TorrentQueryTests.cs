using System.Globalization;
using Torray.Application;
using Torray.Domain;

namespace Torray.UnitTests;

public sealed class TorrentQueryTests
{
    [Fact]
    public void Counts_group_statuses()
    {
        TorrentStatus[] statuses =
        [
            TorrentStatus.Downloading, TorrentStatus.Verifying, TorrentStatus.QueuedToDownload,
            TorrentStatus.QueuedToSeed, TorrentStatus.Stopped, TorrentStatus.Seeding, TorrentStatus.Seeding,
        ];

        var counts = TorrentCounts.From(statuses.Select((s, i) => new Torrent(i, "t", s, 0, 0, 0, 0, 0, null, i, null)));

        Assert.Equal(new TorrentCounts(Active: 2, Queued: 2, Paused: 1, Seeding: 2), counts);
    }

    [Theory]
    [InlineData(TorrentFilter.All, TorrentStatus.Verifying, true)]
    [InlineData(TorrentFilter.Downloading, TorrentStatus.QueuedToDownload, true)]
    [InlineData(TorrentFilter.Downloading, TorrentStatus.Seeding, false)]
    [InlineData(TorrentFilter.Seeding, TorrentStatus.QueuedToSeed, true)]
    [InlineData(TorrentFilter.Paused, TorrentStatus.Stopped, true)]
    [InlineData(TorrentFilter.Paused, TorrentStatus.Downloading, false)]
    public void Filter_matches_statuses(TorrentFilter filter, TorrentStatus status, bool expected)
    {
        Assert.Equal(expected, filter.Matches(status));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5.0 GB")]
    public void Bytes_are_formatted_with_binary_units(long bytes, string expected)
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        Assert.Equal(expected, DisplayFormat.Bytes(bytes));
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(45, "45s")]
    [InlineData(125, "2m 5s")]
    [InlineData(7260, "2h 1m")]
    [InlineData(90000, "1d 1h")]
    public void Eta_is_compact_and_unknown_is_a_dash(int? seconds, string expected)
    {
        Assert.Equal(expected, DisplayFormat.Eta(seconds is null ? null : TimeSpan.FromSeconds(seconds.Value)));
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(CultureInfo culture) => CultureInfo.CurrentCulture = culture;

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
