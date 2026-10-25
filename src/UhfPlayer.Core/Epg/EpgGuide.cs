using UhfPlayer.Core.Models;

namespace UhfPlayer.Core.Epg;

/// <summary>Programmes per XMLTV channel id, each list sorted by start time.</summary>
public sealed class EpgGuide(
    IReadOnlyDictionary<string, List<EpgProgramme>> programmes,
    IReadOnlyDictionary<string, string> channelNames)
{
    public static EpgGuide Empty { get; } = new(new Dictionary<string, List<EpgProgramme>>(), new Dictionary<string, string>());

    public int ChannelCount => programmes.Count;

    /// <summary>Programmes for a channel, matched by tvg-id first, then by display name.</summary>
    public IReadOnlyList<EpgProgramme> For(Channel channel)
    {
        if (channel.EpgId is { } id && programmes.TryGetValue(id, out var list))
            return list;
        // shortcut: linear name scan; build a name index if guides with many channels feel slow.
        foreach (var (epgId, name) in channelNames)
        {
            if (string.Equals(name, channel.Name, StringComparison.OrdinalIgnoreCase) && programmes.TryGetValue(epgId, out list))
                return list;
        }
        return [];
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
}
