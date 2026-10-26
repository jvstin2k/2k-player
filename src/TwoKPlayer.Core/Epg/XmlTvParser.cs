using System.Globalization;
using System.IO.Compression;
using System.Xml;
using TwoKPlayer.Core.Models;

namespace TwoKPlayer.Core.Epg;

public enum EpgFormatError
{
    /// <summary>A web page, e.g. a download page or a login, instead of the file.</summary>
    Html,
    Zip,
    Xz,

    /// <summary>XML, but without the XMLTV root element &lt;tv&gt;.</summary>
    NotXmlTv,
}

/// <summary>The EPG link delivers something other than (gzip-compressed) XMLTV.</summary>
public sealed class EpgFormatException(EpgFormatError error)
    : FormatException($"The EPG is not an XMLTV file ({error}).")
{
    public EpgFormatError Error { get; } = error;
}

/// <summary>Streaming XMLTV reader. Handles plain and gzip-compressed files (detected by magic bytes).</summary>
public static class XmlTvParser
{
    public static async Task<EpgGuide> ParseAsync(Stream stream, CancellationToken ct = default)
    {
        var buffered = new BufferedStream(stream);
        var magic = new byte[64];
        var read = await buffered.ReadAtLeastAsync(magic, magic.Length, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        // shortcut: rewinding needs a seekable stream, so we prepend the sniffed bytes instead.
        Stream source = new PrefixStream(magic.AsMemory(0, read), buffered);
        if (read >= 2 && magic[0] == 0x1f && magic[1] == 0x8b)
            source = new GZipStream(source, CompressionMode.Decompress);
        else if (Sniff(magic.AsSpan(0, read)) is { } error)
            throw new EpgFormatException(error);

        var settings = new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true, IgnoreComments = true };
        using var reader = XmlReader.Create(source, settings);

        var programmes = new Dictionary<string, List<EpgProgramme>>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var root = true;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (root)
            {
                if (reader.Name != "tv")
                    throw new EpgFormatException(reader.Name.Equals("html", StringComparison.OrdinalIgnoreCase) ? EpgFormatError.Html : EpgFormatError.NotXmlTv);
                root = false;
            }
            else if (reader.Name == "channel")
            {
                var id = reader.GetAttribute("id");
                var channelNames = await ReadChildTextsAsync(reader, "display-name").ConfigureAwait(false);
                if (id is not null && channelNames.Count > 0 && !names.ContainsKey(id))
                    names[id] = channelNames;
            }
            else if (reader.Name == "programme")
            {
                var channel = reader.GetAttribute("channel");
                var start = ParseTime(reader.GetAttribute("start"));
                var stop = ParseTime(reader.GetAttribute("stop"));
                string? title = null, desc = null;

                using (var sub = reader.ReadSubtree())
                {
                    // ReadElementContent already advances to the next node, so only Read() when nothing was consumed.
                    await sub.ReadAsync().ConfigureAwait(false);
                    while (!sub.EOF)
                    {
                        if (sub.NodeType == XmlNodeType.Element && sub.Name == "title" && title is null)
                            title = await sub.ReadElementContentAsStringAsync().ConfigureAwait(false);
                        else if (sub.NodeType == XmlNodeType.Element && sub.Name == "desc" && desc is null)
                            desc = await sub.ReadElementContentAsStringAsync().ConfigureAwait(false);
                        else
                            await sub.ReadAsync().ConfigureAwait(false);
                    }
                }

                if (channel is null || start is null || title is null)
                    continue;

                if (!programmes.TryGetValue(channel, out var list))
                    programmes[channel] = list = [];
                // Some feeds omit stop; it is fixed up from the next start below.
                list.Add(new EpgProgramme(channel, start.Value, stop ?? start.Value, title, desc));
            }
        }

        foreach (var list in programmes.Values)
        {
            list.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (var i = 0; i < list.Count - 1; i++)
            {
                if (list[i].Stop <= list[i].Start)
                    list[i] = list[i] with { Stop = list[i + 1].Start };
            }
        }

        return new EpgGuide(programmes, names);
    }

    /// <summary>XMLTV time: "yyyyMMddHHmmss +zzzz", offset optional (then UTC), seconds optional.</summary>
    internal static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var s = value.AsSpan().Trim();
        var space = s.IndexOf(' ');
        var datePart = space < 0 ? s : s[..space];
        var offsetPart = space < 0 ? [] : s[(space + 1)..].Trim();

        var format = datePart.Length switch { 14 => "yyyyMMddHHmmss", 12 => "yyyyMMddHHmm", _ => null };
        if (format is null || !DateTime.TryParseExact(datePart, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return null;

        var offset = TimeSpan.Zero;
        if (offsetPart.Length == 5 && (offsetPart[0] == '+' || offsetPart[0] == '-')
            && int.TryParse(offsetPart[1..3], out var h) && int.TryParse(offsetPart[3..5], out var m))
        {
            offset = new TimeSpan(h, m, 0);
            if (offsetPart[0] == '-')
                offset = -offset;
        }

        return new DateTimeOffset(dt, offset);
    }

    /// <summary>Recognises what is clearly not XMLTV from the first bytes, so the user learns why the guide is empty.</summary>
    private static EpgFormatError? Sniff(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("PK\u0003\u0004"u8))
            return EpgFormatError.Zip;
        if (head.StartsWith((ReadOnlySpan<byte>)[0xfd, 0x37, 0x7a, 0x58, 0x5a]))
            return EpgFormatError.Xz;
        var text = System.Text.Encoding.UTF8.GetString(head).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return text.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            ? EpgFormatError.Html
            : null;
    }

    private static async Task<List<string>> ReadChildTextsAsync(XmlReader reader, string child)
    {
        var result = new List<string>();
        using var sub = reader.ReadSubtree();
        await sub.ReadAsync().ConfigureAwait(false);
        while (!sub.EOF)
        {
            if (sub.NodeType == XmlNodeType.Element && sub.Name == child)
            {
                var text = (await sub.ReadElementContentAsStringAsync().ConfigureAwait(false)).Trim();
                if (text.Length > 0)
                    result.Add(text);
            }
            else
                await sub.ReadAsync().ConfigureAwait(false);
        }
        return result;
    }

    private sealed class PrefixStream(ReadOnlyMemory<byte> prefix, Stream inner) : Stream
    {
        private ReadOnlyMemory<byte> _prefix = prefix;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_prefix.IsEmpty)
                return inner.Read(buffer);
            var n = Math.Min(buffer.Length, _prefix.Length);
            _prefix.Span[..n].CopyTo(buffer);
            _prefix = _prefix[n..];
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_prefix.IsEmpty)
                return inner.ReadAsync(buffer, ct);
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
