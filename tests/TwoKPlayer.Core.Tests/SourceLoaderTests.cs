using System.Net;
using System.Text;
using TwoKPlayer.Core.Models;

namespace TwoKPlayer.Core.Tests;

public class SourceLoaderTests
{
    [Theory]
    [InlineData("https://server.org/get.php?username=u&password=p%40ss&type=m3u_plus&output=ts", "https://server.org")]
    [InlineData("http://server.org:8080/iptv/get.php?username=u&amp;password=p%40ss&amp;type=m3u_plus", "http://server.org:8080/iptv")]
    public void TurnsGetPhpLinkIntoXtreamSource(string link, string server)
    {
        var source = new SourceConfig { Name = "TV", M3uLocation = link };

        var xtream = SourceLoader.FromGetPhpLink(source);

        Assert.NotNull(xtream);
        Assert.Equal(SourceKind.Xtream, xtream.Kind);
        Assert.Equal(server, xtream.Server);
        Assert.Equal("u", xtream.Username);
        Assert.Equal("p@ss", xtream.Password);
        Assert.Equal(source.Id, xtream.Id);
    }

    [Fact]
    public void LeavesPlainPlaylistsAlone()
        => Assert.Null(SourceLoader.FromGetPhpLink(new SourceConfig { Name = "x", M3uLocation = "https://example.com/list.m3u" }));

    [Fact]
    public async Task EpgCacheDownloadsOnlyWhenMissingOrStale()
    {
        var downloads = 0;
        var loader = new SourceLoader(new HttpClient(new CountingHandler(() => downloads++)));
        var source = new SourceConfig { Name = "x", EpgUrl = "http://epg.example/guide.xml" };
        var playlist = new PlaylistResult([], []);
        var file = Path.Combine(Path.GetTempPath(), $"2k-epg-{Guid.NewGuid():N}.xml");
        try
        {
            Assert.True(await loader.UpdateEpgCacheAsync(source, playlist, file, TimeSpan.FromHours(12)));
            Assert.False(await loader.UpdateEpgCacheAsync(source, playlist, file, TimeSpan.FromHours(12)));
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-1));
            Assert.True(await loader.UpdateEpgCacheAsync(source, playlist, file, TimeSpan.FromHours(12)));
            Assert.Equal(2, downloads);

            var guide = await SourceLoader.LoadEpgFileAsync(file);
            Assert.Single(guide.For(new Channel { Id = "1", Name = "ZDF", StreamUrl = "x", EpgId = "zdf.de" }));
        }
        finally
        {
            File.Delete(file);
            File.Delete(file + ".url");
        }
    }

    [Fact]
    public async Task EpgCacheDownloadsAgainWhenTheLinkChanges()
    {
        var downloads = 0;
        var loader = new SourceLoader(new HttpClient(new CountingHandler(() => downloads++)));
        var playlist = new PlaylistResult([], ["http://provider.example/xmltv.php"]);
        var file = Path.Combine(Path.GetTempPath(), $"2k-epg-{Guid.NewGuid():N}.xml");
        try
        {
            Assert.True(await loader.UpdateEpgCacheAsync(new SourceConfig { Name = "x" }, playlist, file, TimeSpan.FromHours(12)));
            var own = new SourceConfig { Name = "x", EpgUrl = "http://epg.example/guide.xml.gz" };
            Assert.True(await loader.UpdateEpgCacheAsync(own, playlist, file, TimeSpan.FromHours(12)));
            Assert.False(await loader.UpdateEpgCacheAsync(own, playlist, file, TimeSpan.FromHours(12)));
            Assert.Equal(2, downloads);
        }
        finally
        {
            File.Delete(file);
            File.Delete(file + ".url");
        }
    }

    [Fact]
    public async Task EpgCacheSkipsSourcesWithoutEpg()
        => Assert.False(await new SourceLoader(new HttpClient()).UpdateEpgCacheAsync(
            new SourceConfig { Name = "x" }, new PlaylistResult([], []), Path.Combine(Path.GetTempPath(), $"2k-{Guid.NewGuid():N}"), TimeSpan.FromHours(1)));

    private sealed class CountingHandler(Action onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            onRequest();
            const string xml = """<tv><programme start="20261021190000 +0000" stop="20261021200000 +0000" channel="zdf.de"><title>heute</title></programme></tv>""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") });
        }
    }
}
