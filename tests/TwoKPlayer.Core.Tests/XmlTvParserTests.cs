using System.IO.Compression;
using System.Text;
using TwoKPlayer.Core.Epg;
using TwoKPlayer.Core.Models;

namespace TwoKPlayer.Core.Tests;

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

    [Fact]
    public async Task FindUpcomingSkipsWhatHasEnded()
    {
        var guide = await XmlTvParser.ParseAsync(ToStream(Xml, gzip: false));
        var channel = new Channel { Id = "1", Name = "ZDF", StreamUrl = "x", EpgId = "zdf.de" };
        var at = new DateTimeOffset(2026, 10, 21, 17, 5, 0, TimeSpan.Zero); // during "heute"

        Assert.Equal("heute", guide.FindUpcoming(channel, "HEUTE", at)?.Title);
        Assert.Null(guide.FindUpcoming(channel, "vorher", at));
        Assert.Equal("Wetter", guide.FindUpcoming(channel, "wett", at)?.Title);
    }

    [Theory]
    [InlineData("DE: ZDF HD", null)]
    [InlineData("ZDF", "zdf")]
    [InlineData("|DE| ZDF FHD", "ZDF.DE")]
    public async Task MatchesLooseNamesAndIds(string name, string? tvgId)
    {
        var guide = await XmlTvParser.ParseAsync(ToStream(Xml, gzip: false));

        Assert.Equal(3, guide.For(new Channel { Id = "1", Name = name, StreamUrl = "x", EpgId = tvgId }).Count);
    }

    [Theory]
    [InlineData("DE: Das Erste HD", "daserste")]
    [InlineData("DasErste.de", "daserste")]
    [InlineData("CNN.us", "cnn")]
    [InlineData("[US] CNN 4K", "cnn")]
    [InlineData("ProSieben", "prosieben")]
    public void KeyDropsCountryAndQuality(string text, string key)
        => Assert.Equal(key, EpgGuide.Key(text));

    [Fact]
    public async Task DiagnosesWhyTheGuideIsEmpty()
    {
        var guide = await XmlTvParser.ParseAsync(ToStream(Xml, gzip: false));
        var zdf = new Channel { Id = "1", Name = "ZDF", StreamUrl = "x", EpgId = "zdf.de" };
        var cnn = new Channel { Id = "2", Name = "CNN", StreamUrl = "x", EpgId = "CNN.us" };
        var during = new DateTimeOffset(2026, 10, 21, 17, 5, 0, TimeSpan.Zero);

        Assert.Equal(EpgProblem.None, guide.Diagnose([zdf, cnn], during).Problem);
        var foreign = guide.Diagnose([cnn], during);
        Assert.Equal(EpgProblem.NoMatchingChannels, foreign.Problem);
        Assert.Equal(["CNN.us"], foreign.ChannelIdSamples);
        Assert.Equal(["zdf.de"], foreign.EpgIdSamples);
        Assert.Equal(EpgProblem.OutOfRange, guide.Diagnose([zdf], during.AddDays(3)).Problem);
        Assert.Equal(EpgProblem.NoProgrammes, (await XmlTvParser.ParseAsync(ToStream("<tv></tv>", gzip: false))).Diagnose([zdf], during).Problem);
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><body>Download</body></html>", EpgFormatError.Html)]
    [InlineData("<html><head></head></html>", EpgFormatError.Html)]
    [InlineData("<?xml version=\"1.0\"?><rss><channel /></rss>", EpgFormatError.NotXmlTv)]
    public async Task RejectsWhatIsNotXmlTv(string content, EpgFormatError error)
    {
        var ex = await Assert.ThrowsAsync<EpgFormatException>(() => XmlTvParser.ParseAsync(ToStream(content, gzip: true)));
        Assert.Equal(error, ex.Error);
        if (error != EpgFormatError.NotXmlTv)
            Assert.Equal(error, (await Assert.ThrowsAsync<EpgFormatException>(() => XmlTvParser.ParseAsync(ToStream(content, gzip: false)))).Error);
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
