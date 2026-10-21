using System.Net;
using System.Text;
using UhfPlayer.Core.Models;
using UhfPlayer.Core.Xtream;

namespace UhfPlayer.Core.Tests;

public class XtreamClientTests
{
    [Fact]
    public async Task MapsLiveStreamsWithMixedIdTypes()
    {
        var http = new HttpClient(new FakeHandler(url => url.Contains("get_live_categories")
            ? """[{"category_id":"5","category_name":"Sport"}]"""
            : """[{"stream_id":101,"name":"Sky Sport","category_id":5,"stream_icon":"","epg_channel_id":"sky.de"}]"""));
        var source = new SourceConfig { Name = "x", Kind = SourceKind.Xtream, Server = "http://host:8080/", Username = "u", Password = "p@ss" };

        var result = await new XtreamClient(http).LoadLiveAsync(source);

        var ch = Assert.Single(result.Channels);
        Assert.Equal("Sky Sport", ch.Name);
        Assert.Equal("Sport", ch.Group);
        Assert.Equal("http://host:8080/live/u/p%40ss/101.ts", ch.StreamUrl);
        Assert.Null(ch.LogoUrl);
        Assert.Equal("sky.de", ch.EpgId);
        Assert.Equal("http://host:8080/xmltv.php?username=u&password=p%40ss", Assert.Single(result.EpgUrls));
    }

    private sealed class FakeHandler(Func<string, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request.RequestUri!.ToString()), Encoding.UTF8, "application/json"),
            });
    }
}
