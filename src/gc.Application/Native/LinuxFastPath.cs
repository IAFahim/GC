using System.Runtime.InteropServices;

namespace gc.Application.Native;

public static class LinuxFastPath
{
    // open(2) flags (octal in the kernel headers; values are stable across x86-64/arm64 Linux).
    public const int O_RDONLY = 0x0;
    public const int O_NONBLOCK = 0x800; // 04000 — never block on open(); a FIFO with no writer would hang forever
    public const int O_NOATIME = 0x40000; // 01000000 — skip atime updates (needs ownership; falls back on EPERM)
    public const int O_CLOEXEC = 0x80000; // 02000000 — close on exec; gc shells out to git/sqz/dotnet
    public const int POSIX_FADV_SEQUENTIAL = 2;

    // errno
    public const int ENOENT = 2;

    [DllImport("libc", SetLastError = true)]
    public static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int posix_fadvise(int fd, long offset, long len, int advice);
}
