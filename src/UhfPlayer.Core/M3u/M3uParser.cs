using UhfPlayer.Core.Models;

namespace UhfPlayer.Core.M3u;

/// <summary>
/// Streaming parser for extended M3U playlists (#EXTM3U / #EXTINF with tvg-* attributes).
/// Tolerates the usual provider quirks: missing header, CRLF, #EXTGRP, #EXTVLCOPT and blank lines.
/// </summary>
public static class M3uParser
{
    public static async Task<PlaylistResult> ParseAsync(TextReader reader, Guid sourceId = default, CancellationToken ct = default)
    {
        var channels = new List<Channel>();
        var epgUrls = new List<string>();

        PendingEntry? pending = null;
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            var span = line.AsSpan().Trim();
            if (span.IsEmpty)
                continue;

            if (span[0] == '#')
            {
                if (span.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                {
                    var attrs = ParseAttributes(span[7..]);
                    foreach (var key in (ReadOnlySpan<string>)["url-tvg", "x-tvg-url", "tvg-url"])
                    {
                        if (attrs.TryGetValue(key, out var urls))
                            epgUrls.AddRange(urls.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    }
                }
                else if (span.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                {
                    pending = ParseExtInf(span[8..]);
                }
                else if (pending is not null && span.StartsWith("#EXTGRP:", StringComparison.OrdinalIgnoreCase))
                {
                    pending.Group ??= span[8..].Trim().ToString();
                }
                else if (pending is not null && span.StartsWith("#EXTVLCOPT:", StringComparison.OrdinalIgnoreCase))
                {
                    var opt = span[11..];
                    var eq = opt.IndexOf('=');
                    if (eq > 0)
                    {
                        var key = opt[..eq].Trim();
                        var value = opt[(eq + 1)..].Trim().ToString();
                        if (key.Equals("http-user-agent", StringComparison.OrdinalIgnoreCase))
                            pending.UserAgent = value;
                        else if (key.Equals("http-referrer", StringComparison.OrdinalIgnoreCase))
                            pending.Referrer = value;
                    }
                }
                continue;
            }

            // A URL line terminates the current entry. Bare URLs without #EXTINF are still accepted.
            var url = span.ToString();
            var entry = pending ?? new PendingEntry { Name = url };
            pending = null;

            channels.Add(new Channel
            {
                Id = $"{sourceId:N}:{channels.Count}",
                Name = string.IsNullOrWhiteSpace(entry.Name) ? url : entry.Name,
                StreamUrl = url,
                SourceId = sourceId,
                Group = entry.Group,
                LogoUrl = entry.Logo,
                EpgId = entry.TvgId,
                UserAgent = entry.UserAgent,
                Referrer = entry.Referrer,
            });
        }

        return new PlaylistResult(channels, epgUrls.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static Task<PlaylistResult> ParseAsync(string content, Guid sourceId = default, CancellationToken ct = default)
        => ParseAsync(new StringReader(content), sourceId, ct);

    private static PendingEntry ParseExtInf(ReadOnlySpan<char> body)
    {
        // Format: <duration> key="value" key2="value2",Display Name
        // The name starts after the first comma that is not inside quotes.
        var inQuotes = false;
        var comma = -1;
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '"')
                inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes)
            {
                comma = i;
                break;
            }
        }

        var head = comma >= 0 ? body[..comma] : body;
        var name = comma >= 0 ? body[(comma + 1)..].Trim().ToString() : string.Empty;
        var attrs = ParseAttributes(head);

        return new PendingEntry
        {
            Name = name.Length > 0 ? name : attrs.GetValueOrDefault("tvg-name") ?? string.Empty,
            TvgId = NullIfEmpty(attrs.GetValueOrDefault("tvg-id")),
            Logo = NullIfEmpty(attrs.GetValueOrDefault("tvg-logo")),
            Group = NullIfEmpty(attrs.GetValueOrDefault("group-title")),
            UserAgent = NullIfEmpty(attrs.GetValueOrDefault("user-agent") ?? attrs.GetValueOrDefault("http-user-agent")),
            Referrer = NullIfEmpty(attrs.GetValueOrDefault("http-referrer")),
        };
    }

    /// <summary>Parses key="value" pairs; also accepts unquoted values terminated by whitespace.</summary>
    internal static Dictionary<string, string> ParseAttributes(ReadOnlySpan<char> s)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
            var keyStart = i;
            while (i < s.Length && s[i] != '=' && !char.IsWhiteSpace(s[i]))
                i++;
            if (i >= s.Length || s[i] != '=')
            {
                // Bare token such as the duration "-1"; skip it.
                continue;
            }

            var key = s[keyStart..i].ToString();
            i++; // '='

            string value;
            if (i < s.Length && s[i] == '"')
            {
                var end = s[(i + 1)..].IndexOf('"');
                if (end < 0)
                {
                    value = s[(i + 1)..].ToString();
                    i = s.Length;
                }
                else
                {
                    value = s.Slice(i + 1, end).ToString();
                    i += end + 2;
                }
            }
            else
            {
                var start = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]))
                    i++;
                value = s[start..i].ToString();
            }

            if (key.Length > 0)
                result[key] = value;
        }

        return result;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed class PendingEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? TvgId { get; set; }
        public string? Logo { get; set; }
        public string? Group { get; set; }
        public string? UserAgent { get; set; }
        public string? Referrer { get; set; }
    }
}
