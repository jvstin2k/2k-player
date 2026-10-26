using System.Text;
using TwoKPlayer.Core.Models;

namespace TwoKPlayer.Core.Epg;

/// <summary>Why a guide shows nothing for a set of channels.</summary>
public enum EpgProblem
{
    None,

    /// <summary>The XMLTV file holds no programmes at all.</summary>
    NoProgrammes,

    /// <summary>Programmes exist, but no channel id or name matches the playlist (often an EPG for another country).</summary>
    NoMatchingChannels,

    /// <summary>Channels match, but none of their programmes covers the given time (outdated file).</summary>
    OutOfRange,
}

/// <param name="Matched">Channels with at least one programme.</param>
/// <param name="ChannelIdSamples">A few tvg-ids of the playlist, to compare with <paramref name="EpgIdSamples"/>.</param>
public sealed record EpgDiagnosis(
    EpgProblem Problem,
    int EpgChannels,
    int Programmes,
    int Matched,
    int Total,
    DateTimeOffset? FirstStart,
    DateTimeOffset? LastStop,
    IReadOnlyList<string> ChannelIdSamples,
    IReadOnlyList<string> EpgIdSamples);

/// <summary>Programmes per XMLTV channel id, each list sorted by start time.</summary>
public sealed class EpgGuide
{
    private readonly IReadOnlyDictionary<string, List<EpgProgramme>> _programmes;
    private readonly Dictionary<string, string> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _byKey = new(StringComparer.Ordinal);

    /// <param name="channelNames">Display names per XMLTV channel id.</param>
    public EpgGuide(IReadOnlyDictionary<string, List<EpgProgramme>> programmes, IReadOnlyDictionary<string, List<string>> channelNames)
    {
        _programmes = programmes;
        foreach (var (id, names) in channelNames)
        {
            if (!programmes.ContainsKey(id))
                continue;
            foreach (var name in names)
            {
                _byName.TryAdd(name, id);
                if (Key(name) is { Length: > 0 } key)
                    _byKey.TryAdd(key, id);
            }
        }
        // Ids like "ZDF.de" or "CNN.us" also work as a name.
        foreach (var id in programmes.Keys)
        {
            if (Key(id) is { Length: > 0 } key)
                _byKey.TryAdd(key, id);
        }

        ProgrammeCount = programmes.Values.Sum(l => l.Count);
        foreach (var list in programmes.Values)
        {
            if (list.Count == 0)
                continue;
            if (FirstStart is null || list[0].Start < FirstStart)
                FirstStart = list[0].Start;
            var stop = list.Max(p => p.Stop);
            if (LastStop is null || stop > LastStop)
                LastStop = stop;
        }
    }

    public static EpgGuide Empty { get; } = new(new Dictionary<string, List<EpgProgramme>>(), new Dictionary<string, List<string>>());

    public int ChannelCount => _programmes.Count;
    public int ProgrammeCount { get; }
    public DateTimeOffset? FirstStart { get; }
    public DateTimeOffset? LastStop { get; }

    /// <summary>
    /// Programmes for a channel: by tvg-id, then by display name, then both again ignoring case, spacing,
    /// country prefixes ("DE: ", "|US|") and quality tags ("HD", "FHD", "4K").
    /// </summary>
    public IReadOnlyList<EpgProgramme> For(Channel channel)
    {
        if (channel.EpgId is { } id && _programmes.TryGetValue(id, out var list))
            return list;
        if (_byName.TryGetValue(channel.Name, out var epgId)
            || (channel.EpgId is { } tvgId && _byKey.TryGetValue(Key(tvgId), out epgId))
            || _byKey.TryGetValue(Key(channel.Name), out epgId))
            return _programmes[epgId];
        return [];
    }

    /// <summary>Explains an empty guide for <paramref name="channels"/> at <paramref name="at"/>.</summary>
    public EpgDiagnosis Diagnose(IReadOnlyCollection<Channel> channels, DateTimeOffset at)
    {
        int matched = 0, current = 0;
        foreach (var channel in channels)
        {
            var list = For(channel);
            if (list.Count == 0)
                continue;
            matched++;
            if (list[^1].Stop > at && list[0].Start <= at.AddHours(6))
                current++;
        }

        var problem = ProgrammeCount == 0 ? EpgProblem.NoProgrammes
            : channels.Count == 0 ? EpgProblem.None
            : matched == 0 ? EpgProblem.NoMatchingChannels
            : current == 0 ? EpgProblem.OutOfRange
            : EpgProblem.None;
        return new EpgDiagnosis(problem, ChannelCount, ProgrammeCount, matched, channels.Count, FirstStart, LastStop,
            channels.Select(c => c.EpgId ?? c.Name).Where(s => s.Length > 0).Distinct().Take(3).ToList(),
            _programmes.Keys.Take(3).ToList());
    }

    /// <summary>The first programme airing at or after <paramref name="from"/> whose title contains <paramref name="query"/>.</summary>
    public EpgProgramme? FindUpcoming(Channel channel, string query, DateTimeOffset from)
    {
        foreach (var p in For(channel))
        {
            if (p.Stop > from && p.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                return p;
        }
        return null;
    }

    /// <summary>The programme airing at <paramref name="at"/> and the one after it.</summary>
    public (EpgProgramme? Now, EpgProgramme? Next) NowNext(Channel channel, DateTimeOffset at)
    {
        var list = For(channel);
        // Binary search for the last programme starting at or before 'at'.
        int lo = 0, hi = list.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (list[mid].Start <= at)
            {
                found = mid;
                lo = mid + 1;
            }
            else
                hi = mid - 1;
        }

        var now = found >= 0 && list[found].IsAiring(at) ? list[found] : null;
        var nextIndex = found + 1;
        return (now, nextIndex < list.Count ? list[nextIndex] : null);
    }

    private static readonly string[] QualityTags = ["uhd", "fhd", "hd", "sd", "4k", "hevc", "h265"];

    /// <summary>"DE: Das Erste HD" and "DasErste.de" both become "daserste"; "|US| CNN" and "CNN.us" become "cnn".</summary>
    internal static string Key(string text)
    {
        var s = text.Trim().ToLowerInvariant();
        // Country prefix: "de:", "de |", "|de|", "[de]".
        var prefixEnd = s.IndexOfAny([':', '|', ']']);
        if (prefixEnd is > 0 and <= 4 && s[..prefixEnd].Trim(' ', '|', '[').Length is 2 or 3)
            s = s[(prefixEnd + 1)..];
        // Country suffix of XMLTV ids: ".de", ".us", ".uk".
        if (s.Length > 3 && s[^3] == '.' && char.IsAsciiLetter(s[^2]) && char.IsAsciiLetter(s[^1]))
            s = s[..^3];

        var words = s.Split([' ', '.', '-', '_', '(', ')', '[', ']', '|', ':'], StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && QualityTags.Contains(words[^1]))
            words.RemoveAt(words.Count - 1);

        var key = new StringBuilder();
        foreach (var c in string.Concat(words))
        {
            if (char.IsLetterOrDigit(c))
                key.Append(c);
        }
        return key.ToString();
    }
}
