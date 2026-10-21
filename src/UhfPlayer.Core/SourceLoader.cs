using UhfPlayer.Core.Epg;
using UhfPlayer.Core.M3u;
using UhfPlayer.Core.Models;
using UhfPlayer.Core.Xtream;

namespace UhfPlayer.Core;

/// <summary>Loads channels and the EPG for any configured source.</summary>
public sealed class SourceLoader(HttpClient http)
{
    public async Task<PlaylistResult> LoadChannelsAsync(SourceConfig source, CancellationToken ct = default)
    {
        if (source.Kind == SourceKind.Xtream)
            return await new XtreamClient(http).LoadLiveAsync(source, ct).ConfigureAwait(false);

        var location = source.M3uLocation ?? throw new ArgumentException("M3U source needs a URL or file path.");
        using var reader = new StreamReader(await OpenAsync(location, ct).ConfigureAwait(false));
        return await M3uParser.ParseAsync(reader, source.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Uses the source's own EPG URL if set, otherwise the first one the playlist advertised.</summary>
    public async Task<EpgGuide> LoadEpgAsync(SourceConfig source, PlaylistResult playlist, CancellationToken ct = default)
    {
        var url = source.EpgUrl ?? playlist.EpgUrls.FirstOrDefault();
        if (url is null)
            return EpgGuide.Empty;
        await using var stream = await OpenAsync(url, ct).ConfigureAwait(false);
        return await XmlTvParser.ParseAsync(stream, ct).ConfigureAwait(false);
    }

    private async Task<Stream> OpenAsync(string location, CancellationToken ct)
        => Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? await http.GetStreamAsync(uri, ct).ConfigureAwait(false)
            : File.OpenRead(location);
}
