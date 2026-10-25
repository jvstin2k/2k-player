using System.Net;
using System.Text;
using UhfPlayer.Core.Models;
using UhfPlayer.Core.Xtream;

namespace UhfPlayer.Core.Tests;

public class XtreamClientTests
{
    private static readonly SourceConfig Source = new() { Name = "x", Kind = SourceKind.Xtream, Server = "http://host:8080/", Username = "u", Password = "p@ss" };

    [Fact]
    public async Task MapsLiveMoviesAndSeriesWithMixedIdTypes()
    {
        var http = Fake(new()
        {
            ["get_live_categories"] = """[{"category_id":"5","category_name":"Sport"}]""",
            ["get_live_streams"] = """[{"stream_id":101,"name":"Sky Sport","category_id":5,"stream_icon":"","epg_channel_id":"sky.de"}]""",
            ["get_vod_categories"] = """[{"category_id":7,"category_name":"Action"}]""",
            ["get_vod_streams"] = """[{"stream_id":"55","name":"Heat","category_id":"7","stream_icon":"http://img/heat.jpg","container_extension":"mkv"}]""",
            ["get_series_categories"] = """[{"category_id":"9","category_name":"Drama"}]""",
            ["get_series"] = """[{"series_id":12,"name":"Dark","category_id":"9","cover":"http://img/dark.jpg"}]""",
        });

        var result = await new XtreamClient(http).LoadAsync(Source);

        var live = Assert.Single(result.Channels, c => c.Kind == MediaKind.Live);
        Assert.Equal("Sky Sport", live.Name);
        Assert.Equal("Sport", live.Group);
        Assert.Equal("http://host:8080/live/u/p%40ss/101.ts", live.StreamUrl);
        Assert.Null(live.LogoUrl);
        Assert.Equal("sky.de", live.EpgId);
        Assert.Equal("http://host:8080/xmltv.php?username=u&password=p%40ss", Assert.Single(result.EpgUrls));

        var movie = Assert.Single(result.Channels, c => c.Kind == MediaKind.Movie);
        Assert.Equal("Action", movie.Group);
        Assert.Equal("http://host:8080/movie/u/p%40ss/55.mkv", movie.StreamUrl);

        var series = Assert.Single(result.Channels, c => c.Kind == MediaKind.Series);
        Assert.Equal("Drama", series.Group);
        Assert.Equal("12", series.SeriesId);
        Assert.Equal("http://img/dark.jpg", series.LogoUrl);
    }

    [Fact]
    public async Task BrokenVodEndpointDoesNotBreakLiveTv()
    {
        var http = Fake(new() { ["get_live_streams"] = """[{"stream_id":1,"name":"ZDF"}]""", ["get_vod_streams"] = null });

        var result = await new XtreamClient(http).LoadAsync(Source);

        Assert.Equal("ZDF", Assert.Single(result.Channels).Name);
    }

    [Fact]
    public async Task LoadsEpisodesSortedBySeasonAndNumber()
    {
        var http = Fake(new()
        {
            ["get_series_info"] = """
                {"episodes":{
                  "2":[{"id":"202","episode_num":1,"title":"Neu","container_extension":"mp4","season":2}],
                  "1":[{"id":"102","episode_num":"2","title":"Lügen","container_extension":"mkv","season":1},
                       {"id":"101","episode_num":1,"title":"Geheimnisse","container_extension":"mkv","season":1,"info":{"movie_image":"http://img/e1.jpg"}}]
                }}
                """,
        });

        var episodes = await new XtreamClient(http).LoadEpisodesAsync(Source, "12");

        Assert.Equal(["S01E01 Geheimnisse", "S01E02 Lügen", "S02E01 Neu"], episodes.Select(e => e.Name));
        Assert.Equal("http://host:8080/series/u/p%40ss/101.mkv", episodes[0].StreamUrl);
        Assert.Equal("http://img/e1.jpg", episodes[0].LogoUrl);
        Assert.Equal("Staffel 2", episodes[2].Group);
    }

    /// <summary>Answers by the "action" query parameter; unknown actions get [], null values get HTTP 500.</summary>
    private static HttpClient Fake(Dictionary<string, string?> byAction) => new(new FakeHandler(byAction));

    private sealed class FakeHandler(Dictionary<string, string?> byAction) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var action = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["action"] ?? "";
            var body = byAction.TryGetValue(action, out var b) ? b : "[]";
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
