using UhfPlayer.Core.Models;

namespace UhfPlayer.Core.Tests;

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
}
