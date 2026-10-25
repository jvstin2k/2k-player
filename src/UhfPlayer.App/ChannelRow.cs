using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using UhfPlayer.Core.Epg;
using UhfPlayer.Core.Models;

namespace UhfPlayer.App;

/// <summary>One line in the channel list: channel plus what is on right now.</summary>
public sealed class ChannelRow
{
    public ChannelRow(Channel channel, EpgGuide guide, DateTimeOffset now)
    {
        Channel = channel;
        (Now, Next) = guide.NowNext(channel, now);
        Progress = Now?.Progress(now) ?? 0;
    }

    public Channel Channel { get; }
    public EpgProgramme? Now { get; }
    public EpgProgramme? Next { get; }
    // Created on first access, i.e. only for rows the list actually shows; VOD lists run into tens of thousands.
    public ImageSource? Logo => _logo ??= Uri.TryCreate(Channel.LogoUrl, UriKind.Absolute, out var uri) ? new BitmapImage(uri) { DecodePixelWidth = 96 } : null;
    private ImageSource? _logo;
    public double Progress { get; }
    public string NowTitle => Now?.Title ?? "";
    public Visibility ProgressVisibility => Now is null ? Visibility.Collapsed : Visibility.Visible;
}
