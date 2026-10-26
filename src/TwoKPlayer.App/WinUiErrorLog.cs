using System.Runtime.InteropServices;
using System.Text;

namespace TwoKPlayer.App;

/// <summary>
/// Records the errors WinUI reports internally to winui-errors.log. When WinUI ends the process itself (c000027b),
/// neither crash.log nor the event log says why; the last error recorded here does. Windows only raises these
/// errors as exceptions under a debugger, unless RO_ERROR_REPORTING_FORCEEXCEPTIONS is set, as Install does.
/// </summary>
internal static unsafe partial class WinUiErrorLog
{
    private const uint RO_ERROR_REPORTING_FORCEEXCEPTIONS = 0x2;
    private const long MaxSize = 1 << 20;

    private static string? s_path;
    [ThreadStatic] private static bool t_writing;

    public static void Install()
    {
        try
        {
            s_path = Path.Combine(AppPaths.Root, "winui-errors.log");
            if (File.Exists(s_path) && new FileInfo(s_path).Length > MaxSize)
                File.Delete(s_path);
            if (RoGetErrorReportingFlags(out var flags) >= 0)
                RoSetErrorReportingFlags(flags | RO_ERROR_REPORTING_FORCEEXCEPTIONS);
            if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                AddVectoredExceptionHandler(1, NativeFilter((nint)(delegate* unmanaged<ExceptionPointers*, int>)&OnException));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>
    /// Every exception passes a vectored handler, also .NET's own while the runtime must not be re-entered (a null
    /// reference is an access violation in managed code). So a few machine instructions pass on only WinUI's errors,
    /// which come from native code, and return EXCEPTION_CONTINUE_SEARCH for everything else.
    /// </summary>
    private static nint NativeFilter(nint handler)
    {
        byte[] code =
        [
            0x48, 0x8B, 0x01,                               // mov rax, [rcx]           ; EXCEPTION_POINTERS.ExceptionRecord
            0x81, 0x38, 0x01, 0x02, 0x08, 0x40,             // cmp dword [rax], 0x40080201 ; EXCEPTION_RO_ORIGINATEERROR
            0x75, 0x0C,                                     // jne other
            0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0,             // mov rax, handler
            0xFF, 0xE0,                                     // jmp rax
            0x31, 0xC0,                                     // other: xor eax, eax
            0xC3,                                           // ret
        ];
        BitConverter.TryWriteBytes(code.AsSpan(13), handler);
        var memory = VirtualAlloc(0, (nuint)code.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        Marshal.Copy(code, 0, memory, code.Length);
        VirtualProtect(memory, (nuint)code.Length, PAGE_EXECUTE_READ, out _);
        FlushInstructionCache(-1, memory, (nuint)code.Length);
        return memory;
    }

    [UnmanagedCallersOnly]
    private static int OnException(ExceptionPointers* pointers)
    {
        var record = pointers->Record;
        if (t_writing || s_path is null)
            return 0; // EXCEPTION_CONTINUE_SEARCH: Windows handles it as before
        t_writing = true;
        try
        {
            var hr = (int)record->Information[0];
            var message = record->NumberParameters >= 3 && record->Information[2] != 0 && record->Information[1] < 4096
                ? new string((char*)record->Information[2], 0, (int)record->Information[1])
                : "";
            var text = new StringBuilder()
                .AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} thread {Environment.CurrentManagedThreadId} 0x{hr:x8} {message.TrimEnd('\0', '\r', '\n')}")
                .AppendLine(NativeStack())
                .AppendLine(Environment.StackTrace)
                .AppendLine();
            File.AppendAllText(s_path, text.ToString());
        }
        catch
        {
            // Never let logging change what happens to the error.
        }
        finally
        {
            t_writing = false;
        }
        return 0;
    }

    /// <summary>Module and offset of each native frame, which the managed stack trace leaves out.</summary>
    private static string NativeStack()
    {
        var frames = stackalloc nint[48];
        var count = RtlCaptureStackBackTrace(2, 48, frames, null);
        var text = new StringBuilder();
        var name = stackalloc char[260];
        for (var i = 0; i < count; i++)
        {
            if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, frames[i], out var module)
                && GetModuleFileNameW(module, name, 260) is var length and > 0)
                text.Append($"  {Path.GetFileName(new string(name, 0, (int)length))}+0x{frames[i] - module:x}\n");
            else
                text.Append($"  0x{frames[i]:x}\n");
        }
        return text.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExceptionRecord
    {
        public uint Code;
        public uint Flags;
        public nint Inner;
        public nint Address;
        public uint NumberParameters;
        public fixed ulong Information[15];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExceptionPointers
    {
        public ExceptionRecord* Record;
        public nint Context;
    }

    private const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, PAGE_READWRITE = 0x04, PAGE_EXECUTE_READ = 0x20;

    [LibraryImport("kernel32.dll")]
    private static partial nint VirtualAlloc(nint address, nuint size, uint type, uint protect);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VirtualProtect(nint address, nuint size, uint protect, out uint old);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushInstructionCache(nint process, nint address, nuint size);

    private const uint GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT = 0x2, GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS = 0x4;

    [LibraryImport("combase.dll")]
    private static partial int RoGetErrorReportingFlags(out uint flags);

    [LibraryImport("combase.dll")]
    private static partial int RoSetErrorReportingFlags(uint flags);

    [LibraryImport("kernel32.dll")]
    private static partial nint AddVectoredExceptionHandler(uint first, nint handler);

    [LibraryImport("kernel32.dll")]
    private static partial ushort RtlCaptureStackBackTrace(uint skip, uint count, nint* frames, uint* hash);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetModuleHandleExW(uint flags, nint address, out nint module);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetModuleFileNameW(nint module, char* name, uint size);
}
