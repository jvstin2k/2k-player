using UhfPlayer.Core.M3u;
using UhfPlayer.Core.Models;

namespace UhfPlayer.Core.Tests;

public class M3uParserTests
{
    [Fact]
    public async Task ParsesAttributesNameGroupAndEpgUrls()
    {
        const string playlist = """
            #EXTM3U url-tvg="http://epg.example/a.xml.gz,http://epg.example/b.xml"
            #EXTINF:-1 tvg-id="das-erste.de" tvg-logo="http://logo/1.png" group-title="Deutschland, HD",Das Erste HD
            #EXTVLCOPT:http-user-agent=UHF/1.0
            http://stream.example/1.m3u8

            #EXTINF:0,ZDF
            #EXTGRP:Deutschland
            http://stream.example/2.ts
            http://stream.example/bare.ts
            #EXTINF:-1 group-title="Filme",Heat
            http://host/movie/u/p/55.mkv
            """;

        var result = await M3uParser.ParseAsync(playlist);

        Assert.Equal(["http://epg.example/a.xml.gz", "http://epg.example/b.xml"], result.EpgUrls);
        Assert.Equal(4, result.Channels.Count);

        var first = result.Channels[0];
        Assert.Equal("Das Erste HD", first.Name);
        Assert.Equal("das-erste.de", first.EpgId);
        Assert.Equal("http://logo/1.png", first.LogoUrl);
        Assert.Equal("Deutschland, HD", first.Group); // comma inside quotes must not split the name
        Assert.Equal("UHF/1.0", first.UserAgent);

        Assert.Equal("ZDF", result.Channels[1].Name);
        Assert.Equal("Deutschland", result.Channels[1].Group);
        Assert.Equal("http://stream.example/bare.ts", result.Channels[2].Name);
        Assert.Equal(MediaKind.Live, result.Channels[2].Kind);
        Assert.Equal(MediaKind.Movie, result.Channels[3].Kind);
    }
}
