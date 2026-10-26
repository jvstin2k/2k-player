using System.Net;
using System.Web;
using TwoKPlayer.Core.Epg;
using TwoKPlayer.Core.M3u;
using TwoKPlayer.Core.Models;
using TwoKPlayer.Core.Xtream;

namespace TwoKPlayer.Core;

/// <summary>Loads channels and the EPG for any configured source.</summary>
public sealed class SourceLoader(HttpClient http)
{
    /// <summary>Many IPTV panels reject requests without a User-Agent (.NET sends none) or with an unknown one.</summary>
    public const string DefaultUserAgent = "VLC/3.0.21 LibVLC/3.0.21";

    public async Task<PlaylistResult> LoadChannelsAsync(SourceConfig source, CancellationToken ct = default)
    {
        if (source.Kind == SourceKind.M3u && FromGetPhpLink(source) is { } xtream)
            source = xtream;
        if (source.Kind == SourceKind.Xtream)
            return await new XtreamClient(http).LoadAsync(source, ct).ConfigureAwait(false);

        var location = WebUtility.HtmlDecode(source.M3uLocation ?? throw new ArgumentException("M3U source needs a URL or file path."));
        using var reader = new StreamReader(await OpenAsync(location, ct).ConfigureAwait(false));
        return await M3uParser.ParseAsync(reader, source.Id, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<Channel>> LoadEpisodesAsync(SourceConfig source, string seriesId, CancellationToken ct = default)
        => new XtreamClient(http).LoadEpisodesAsync(FromGetPhpLink(source) ?? source, seriesId, ct);

    /// <summary>
    /// Xtream panels hand out M3U links like .../get.php?username=u&amp;password=p&amp;type=m3u_plus.
    /// Loading those through player_api.php gives proper categories and the panel's EPG.
    /// </summary>
    internal static SourceConfig? FromGetPhpLink(SourceConfig source)
    {
        // Links copied out of HTML or e-mails often carry "&amp;" instead of "&".
        if (!Uri.TryCreate(WebUtility.HtmlDecode(source.M3uLocation), UriKind.Absolute, out var uri)
            || !uri.AbsolutePath.EndsWith("/get.php", StringComparison.OrdinalIgnoreCase))
            return null;

        var query = HttpUtility.ParseQueryString(uri.Query);
        if (query["username"] is not { } user || query["password"] is not { } pass)
            return null;

        var basePath = uri.AbsolutePath[..^"/get.php".Length];
        return source with
        {
            Kind = SourceKind.Xtream,
            Server = uri.GetLeftPart(UriPartial.Authority) + basePath,
            Username = user,
            Password = pass,
            M3uLocation = null,
        };
    }

    /// <summary>
    /// Downloads the XMLTV file (the source's own EPG URL if set, otherwise the first one the playlist advertised)
    /// into <paramref name="cacheFile"/>, unless the cached copy came from the same URL and is younger than <paramref name="maxAge"/>.
    /// Returns true when a new file was written.
    /// </summary>
    public async Task<bool> UpdateEpgCacheAsync(SourceConfig source, PlaylistResult playlist, string cacheFile, TimeSpan maxAge, CancellationToken ct = default)
    {
        var url = EpgUrlFor(source, playlist);
        if (url is null)
            return false;
        // The URL the cache came from sits next to it, so a changed EPG link is fetched right away instead of after maxAge.
        var urlFile = cacheFile + ".url";
        if (File.Exists(cacheFile) && File.GetLastWriteTimeUtc(cacheFile) > DateTime.UtcNow - maxAge
            && File.Exists(urlFile) && await File.ReadAllTextAsync(urlFile, ct).ConfigureAwait(false) == url)
            return false;

        // Download next to the cache and swap, so a broken download never replaces a good file.
        var temp = cacheFile + ".download";
        await using (var download = await OpenAsync(url, ct).ConfigureAwait(false))
        await using (var target = File.Create(temp))
            await download.CopyToAsync(target, ct).ConfigureAwait(false);
        File.Move(temp, cacheFile, overwrite: true);
        await File.WriteAllTextAsync(urlFile, url, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>The source's own EPG URL if set, otherwise the first one the playlist advertised; null when there is none.</summary>
    public static string? EpgUrlFor(SourceConfig source, PlaylistResult playlist)
        => source.EpgUrl is { } own ? WebUtility.HtmlDecode(own) : playlist.EpgUrls.FirstOrDefault();

    public static async Task<EpgGuide> LoadEpgFileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        return await XmlTvParser.ParseAsync(stream, ct).ConfigureAwait(false);
    }

    private async Task<Stream> OpenAsync(string location, CancellationToken ct)
        => Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? await http.GetStreamAsync(uri, ct).ConfigureAwait(false)
            : File.OpenRead(location);
}
