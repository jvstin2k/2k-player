using System.Text.Json;
using UhfPlayer.Core.Models;

namespace UhfPlayer.Core.Xtream;

/// <summary>Live TV over the Xtream Codes player_api.php. VOD and series are not covered yet.</summary>
public sealed class XtreamClient(HttpClient http)
{
    public async Task<PlaylistResult> LoadLiveAsync(SourceConfig source, CancellationToken ct = default)
    {
        var server = (source.Server ?? throw new ArgumentException("Xtream source needs a server URL.")).TrimEnd('/');
        var user = Uri.EscapeDataString(source.Username ?? "");
        var pass = Uri.EscapeDataString(source.Password ?? "");
        var api = $"{server}/player_api.php?username={user}&password={pass}";

        // Fetch categories and streams in parallel; they are independent calls.
        var categoriesTask = GetJsonAsync($"{api}&action=get_live_categories", ct);
        var streamsTask = GetJsonAsync($"{api}&action=get_live_streams", ct);
        using var categoriesDoc = await categoriesTask.ConfigureAwait(false);
        using var streamsDoc = await streamsTask.ConfigureAwait(false);

        var categories = new Dictionary<string, string>();
        if (categoriesDoc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in categoriesDoc.RootElement.EnumerateArray())
            {
                if (Str(c, "category_id") is { } id && Str(c, "category_name") is { } name)
                    categories[id] = name;
            }
        }

        if (streamsDoc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Xtream login failed or server returned no live streams.");

        var channels = new List<Channel>();
        foreach (var s in streamsDoc.RootElement.EnumerateArray())
        {
            var streamId = Str(s, "stream_id");
            if (streamId is null)
                continue;

            channels.Add(new Channel
            {
                Id = $"{source.Id:N}:{streamId}",
                Name = Str(s, "name") ?? streamId,
                StreamUrl = $"{server}/live/{user}/{pass}/{streamId}.ts",
                SourceId = source.Id,
                Group = Str(s, "category_id") is { } cat ? categories.GetValueOrDefault(cat) : null,
                LogoUrl = NullIfEmpty(Str(s, "stream_icon")),
                EpgId = NullIfEmpty(Str(s, "epg_channel_id")),
            });
        }

        return new PlaylistResult(channels, [$"{server}/xmltv.php?username={user}&password={pass}"]);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        await using var stream = await http.GetStreamAsync(url, ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

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
