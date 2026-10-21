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
        if (Uri.TryCreate(channel.LogoUrl, UriKind.Absolute, out var logo))
            Logo = new BitmapImage(logo) { DecodePixelWidth = 96 };
    }

    public Channel Channel { get; }
    public EpgProgramme? Now { get; }
    public EpgProgramme? Next { get; }
    public ImageSource? Logo { get; }
    public double Progress { get; }
    public string NowTitle => Now?.Title ?? "";
    public Visibility ProgressVisibility => Now is null ? Visibility.Collapsed : Visibility.Visible;
}
