using System.IO.Compression;
using System.Text;
using UhfPlayer.Core.Epg;
using UhfPlayer.Core.Models;

namespace UhfPlayer.Core.Tests;

public class XmlTvParserTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE tv SYSTEM "xmltv.dtd">
        <tv>
          <channel id="zdf.de"><display-name>ZDF</display-name></channel>
          <programme start="20261021190000 +0200" stop="20261021191500 +0200" channel="zdf.de">
            <title lang="de">heute</title><desc>Nachrichten</desc>
          </programme>
          <programme start="20261021180000 +0200" channel="zdf.de"><title>Vorher</title></programme>
          <programme start="20261021191500 +0200" stop="20261021201500 +0200" channel="zdf.de"><title>Wetter</title></programme>
        </tv>
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParsesPlainAndGzip(bool gzip)
    {
        var guide = await XmlTvParser.ParseAsync(ToStream(Xml, gzip));
        var channel = new Channel { Id = "1", Name = "ZDF", StreamUrl = "x", EpgId = "zdf.de" };

        var list = guide.For(channel);
        Assert.Equal(["Vorher", "heute", "Wetter"], list.Select(p => p.Title));
        Assert.Equal(list[1].Start, list[0].Stop); // missing stop filled from the next start
        Assert.Equal("Nachrichten", list[1].Description);
    }

    [Fact]
    public async Task NowNextAndNameFallback()
    {
        var guide = await XmlTvParser.ParseAsync(ToStream(Xml, gzip: false));
        var byName = new Channel { Id = "1", Name = "zdf", StreamUrl = "x" };

        var (now, next) = guide.NowNext(byName, new DateTimeOffset(2026, 10, 21, 17, 5, 0, TimeSpan.Zero));

        Assert.Equal("heute", now?.Title);
        Assert.Equal("Wetter", next?.Title);
    }

    [Theory]
    [InlineData("20261021190000 +0200", "2026-10-21T17:00:00Z")]
    [InlineData("20261021190000 -0130", "2026-10-21T20:30:00Z")]
    [InlineData("20261021190000", "2026-10-21T19:00:00Z")]
    [InlineData("202610211900", "2026-10-21T19:00:00Z")]
    public void ParsesTimes(string input, string expectedUtc)
        => Assert.Equal(DateTimeOffset.Parse(expectedUtc), XmlTvParser.ParseTime(input));

    private static Stream ToStream(string text, bool gzip)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (!gzip)
            return new MemoryStream(bytes);
        var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(bytes);
        ms.Position = 0;
        return ms;
    }
}
