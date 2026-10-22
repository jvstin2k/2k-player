using System.Runtime.InteropServices;

namespace UhfPlayer.App;

/// <summary>
/// libmpv rendering into a Win32 child window that sits on top of the XAML content.
/// shortcut: a child HWND covers any XAML drawn over it (airspace), so controls live beside the video,
/// not on it; switch to a transparent overlay window when UHF-style on-video controls are needed.
/// </summary>
internal sealed partial class MpvPlayer : IDisposable
{
    private const string Mpv = "libmpv-2.dll";
    private readonly nint _ctx;
    private readonly nint _hwnd;

    public MpvPlayer(nint parentHwnd)
    {
        _hwnd = CreateWindowExW(0, "Static", null, WS_CHILD | WS_CLIPSIBLINGS, 0, 0, 0, 0, parentHwnd, 0, 0, 0);
        if (_hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");

        _ctx = mpv_create();
        if (_ctx == 0)
            throw new InvalidOperationException("mpv_create failed.");

        Option("wid", _hwnd.ToString());
        Option("vo", "gpu-next");
        Option("hwdec", "auto-safe");
        Option("osc", "no");
        Option("input-default-bindings", "no");
        Option("input-vo-keyboard", "no");
        Option("ytdl", "no");
        Option("network-timeout", "10");
        // Faster channel switching: probe less before the first frame and do not wait to fill the cache.
        Option("demuxer-lavf-analyzeduration", "1");
        Option("cache-pause-initial", "no");
        Option("demuxer-max-bytes", "64MiB");
        Check(mpv_initialize(_ctx));
    }

    public void Play(string url, string? userAgent, string? referrer)
    {
        Check(mpv_set_property_string(_ctx, "user-agent", userAgent ?? Core.SourceLoader.DefaultUserAgent));
        Check(mpv_set_property_string(_ctx, "referrer", referrer ?? ""));
        Command("loadfile", url, "replace");
        ShowWindow(_hwnd, SW_SHOW);
    }

    public void TogglePause() => Command("cycle", "pause");

    public void ToggleMute() => Command("cycle", "mute");

    /// <summary>Places the video window over the given client-area rectangle, in physical pixels.</summary>
    public void SetBounds(int x, int y, int width, int height)
        => SetWindowPos(_hwnd, HWND_TOP, x, y, width, height, SWP_NOACTIVATE);

    public void Dispose()
    {
        mpv_terminate_destroy(_ctx);
        DestroyWindow(_hwnd);
    }

    private void Option(string name, string value) => Check(mpv_set_option_string(_ctx, name, value));

    private unsafe void Command(params string[] args)
    {
        var ptrs = new nint[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++)
                ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            fixed (nint* p = ptrs)
                Check(mpv_command(_ctx, p));
        }
        finally
        {
            foreach (var p in ptrs)
                Marshal.FreeCoTaskMem(p);
        }
    }

    private static void Check(int error)
    {
        if (error < 0)
            throw new InvalidOperationException($"mpv: {Marshal.PtrToStringUTF8(mpv_error_string(error))}");
    }

    [LibraryImport(Mpv)] private static partial nint mpv_create();
    [LibraryImport(Mpv)] private static partial int mpv_initialize(nint ctx);
    [LibraryImport(Mpv)] private static partial void mpv_terminate_destroy(nint ctx);
    [LibraryImport(Mpv)] private static partial nint mpv_error_string(int error);
    [LibraryImport(Mpv)] private static unsafe partial int mpv_command(nint ctx, nint* args);
    [LibraryImport(Mpv, StringMarshalling = StringMarshalling.Utf8)] private static partial int mpv_set_option_string(nint ctx, string name, string data);
    [LibraryImport(Mpv, StringMarshalling = StringMarshalling.Utf8)] private static partial int mpv_set_property_string(nint ctx, string name, string data);

    private const uint WS_CHILD = 0x40000000, WS_CLIPSIBLINGS = 0x04000000, SWP_NOACTIVATE = 0x0010;
    private const int SW_SHOW = 5;
    private static readonly nint HWND_TOP = 0;

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);
}
