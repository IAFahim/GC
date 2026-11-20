using System.Runtime.InteropServices;

namespace gc.Application.Native;

/// <summary>
/// Darwin/macOS POSIX open(2) P/Invoke declarations for non-blocking special-file handling.
/// </summary>
public static class MacOsFastPath
{
    // open(2) flags from Darwin <sys/fcntl.h> (values differ from Linux).
    public const int O_RDONLY = 0x0;
    public const int O_NONBLOCK = 0x4;      // never block on open(); a FIFO with no writer would hang forever
    public const int O_CLOEXEC = 0x1000000; // close on exec; gc shells out to git/sqz/dotnet

    // errno from Darwin <sys/errno.h>
    public const int ENOENT = 2;

    [DllImport("libSystem.dylib", SetLastError = true)]
    public static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags);
}
