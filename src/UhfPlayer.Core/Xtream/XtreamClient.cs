using System.Text.Json;
using UhfPlayer.Core.Models;

namespace UhfPlayer.Core.Xtream;

/// <summary>Live TV, movies and series over the Xtream Codes player_api.php.</summary>
public sealed class XtreamClient(HttpClient http)
{
    public async Task<PlaylistResult> LoadAsync(SourceConfig source, CancellationToken ct = default)
    {
        var (server, user, pass, api) = Endpoints(source);

        // All six calls are independent; run them in parallel.
        var live = LoadSectionAsync(api, "get_live_categories", "get_live_streams", ct);
        var movies = LoadSectionAsync(api, "get_vod_categories", "get_vod_streams", ct);
        var series = LoadSectionAsync(api, "get_series_categories", "get_series", ct);

        var (liveCategories, liveItems) = await live.ConfigureAwait(false);
        if (liveItems.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Xtream login failed or server returned no live streams.");

        var channels = new List<Channel>();
        foreach (var s in liveItems.EnumerateArray())
        {
            if (Str(s, "stream_id") is not { } id)
                continue;
            channels.Add(new Channel
            {
                Id = $"{source.Id:N}:{id}",
                Name = Str(s, "name") ?? id,
                StreamUrl = $"{server}/live/{user}/{pass}/{id}.ts",
                SourceId = source.Id,
                Group = Group(s, liveCategories),
                LogoUrl = NullIfEmpty(Str(s, "stream_icon")),
                EpgId = NullIfEmpty(Str(s, "epg_channel_id")),
            });
        }

        // Movies and series are extras: a panel without them must not break live TV.
        var (movieCategories, movieItems) = await OrEmpty(movies).ConfigureAwait(false);
        foreach (var m in Items(movieItems))
        {
            if (Str(m, "stream_id") is not { } id)
                continue;
            channels.Add(new Channel
            {
                Id = $"{source.Id:N}:movie:{id}",
                Name = Str(m, "name") ?? id,
                StreamUrl = $"{server}/movie/{user}/{pass}/{id}.{Str(m, "container_extension") ?? "mp4"}",
                Kind = MediaKind.Movie,
                SourceId = source.Id,
                Group = Group(m, movieCategories),
                LogoUrl = NullIfEmpty(Str(m, "stream_icon")),
            });
        }

        var (seriesCategories, seriesItems) = await OrEmpty(series).ConfigureAwait(false);
        foreach (var s in Items(seriesItems))
        {
            if (Str(s, "series_id") is not { } id)
                continue;
            channels.Add(new Channel
            {
                Id = $"{source.Id:N}:series:{id}",
                Name = Str(s, "name") ?? id,
                StreamUrl = "",
                Kind = MediaKind.Series,
                SeriesId = id,
                SourceId = source.Id,
                Group = Group(s, seriesCategories),
                LogoUrl = NullIfEmpty(Str(s, "cover")),
            });
        }

        return new PlaylistResult(channels, [$"{server}/xmltv.php?username={user}&password={pass}"]);
    }

    /// <summary>Episodes of a series, ordered by season and episode number.</summary>
    public async Task<IReadOnlyList<Channel>> LoadEpisodesAsync(SourceConfig source, string seriesId, CancellationToken ct = default)
    {
        var (server, user, pass, api) = Endpoints(source);
        using var doc = await GetJsonAsync($"{api}&action=get_series_info&series_id={Uri.EscapeDataString(seriesId)}", ct).ConfigureAwait(false);

        var episodes = new List<(int Season, int Number, Channel Channel)>();
        // "episodes" is an object keyed by season number, each holding an array of episodes.
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("episodes", out var seasons) && seasons.ValueKind == JsonValueKind.Object)
        {
            foreach (var season in seasons.EnumerateObject())
            {
                foreach (var e in season.Value.EnumerateArray())
                {
                    if (Str(e, "id") is not { } id)
                        continue;
                    var s = int.TryParse(Str(e, "season") ?? season.Name, out var sn) ? sn : 0;
                    var n = int.TryParse(Str(e, "episode_num"), out var en) ? en : 0;
                    var logo = e.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object ? Str(info, "movie_image") : null;
                    episodes.Add((s, n, new Channel
                    {
                        Id = $"{source.Id:N}:episode:{id}",
                        Name = $"S{s:00}E{n:00} {Str(e, "title")}".TrimEnd(),
                        StreamUrl = $"{server}/series/{user}/{pass}/{id}.{Str(e, "container_extension") ?? "mp4"}",
                        Kind = MediaKind.Episode,
                        SourceId = source.Id,
                        Group = $"Staffel {s}",
                        LogoUrl = NullIfEmpty(logo),
                    }));
                }
            }
        }

        return episodes.OrderBy(e => e.Season).ThenBy(e => e.Number).Select(e => e.Channel).ToList();
    }

    private static (string Server, string User, string Pass, string Api) Endpoints(SourceConfig source)
    {
        var server = (source.Server ?? throw new ArgumentException("Xtream source needs a server URL.")).TrimEnd('/');
        var user = Uri.EscapeDataString(source.Username ?? "");
        var pass = Uri.EscapeDataString(source.Password ?? "");
        return (server, user, pass, $"{server}/player_api.php?username={user}&password={pass}");
    }

    private async Task<(Dictionary<string, string> Categories, JsonElement Items)> LoadSectionAsync(
        string api, string categoriesAction, string itemsAction, CancellationToken ct)
    {
        var categoriesTask = GetJsonAsync($"{api}&action={categoriesAction}", ct);
        var itemsTask = GetJsonAsync($"{api}&action={itemsAction}", ct);
        using var categoriesDoc = await categoriesTask.ConfigureAwait(false);
        var itemsDoc = await itemsTask.ConfigureAwait(false);

        var categories = new Dictionary<string, string>();
        if (categoriesDoc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in categoriesDoc.RootElement.EnumerateArray())
            {
                if (Str(c, "category_id") is { } id && Str(c, "category_name") is { } name)
                    categories[id] = name;
            }
        }

        // Clone detaches the items from the pooled document so it can be disposed here.
        var items = itemsDoc.RootElement.Clone();
        itemsDoc.Dispose();
        return (categories, items);
    }

    private static async Task<(Dictionary<string, string>, JsonElement)> OrEmpty(Task<(Dictionary<string, string>, JsonElement)> section)
    {
        try
        {
            return await section.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return ([], default);
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        await using var stream = await http.GetStreamAsync(url, ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private static IEnumerable<JsonElement> Items(JsonElement e)
        => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : [];

    private static string? Group(JsonElement item, Dictionary<string, string> categories)
        => Str(item, "category_id") is { } cat ? categories.GetValueOrDefault(cat) : null;

    // Panels are inconsistent: ids arrive as numbers on one server and strings on the next.
    private static string? Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
