using System.Runtime.InteropServices;

namespace gc.Application.Native;

public static class LinuxFastPath
{
    public const int POSIX_FADV_SEQUENTIAL = 2;

    [DllImport("libc", SetLastError = true)]
    public static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int posix_fadvise(int fd, long offset, long len, int advice);
}
