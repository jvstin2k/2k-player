namespace TwoKPlayer.Core.Models;

/// <param name="EpgUrls">XMLTV URLs advertised by the source (url-tvg, xmltv.php, ...).</param>
public sealed record PlaylistResult(IReadOnlyList<Channel> Channels, IReadOnlyList<string> EpgUrls);
