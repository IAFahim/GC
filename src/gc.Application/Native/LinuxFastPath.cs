using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace gc.Application.Native;

public static class LinuxFastPath
{
    public const int O_RDONLY = 0x0000;
    public const int O_NOATIME = 0x40000;
    
    public const int POSIX_FADV_SEQUENTIAL = 2;
    public const int POSIX_FADV_WILLNEED = 3;
    public const int POSIX_FADV_DONTNEED = 4;

    [DllImport("libc", SetLastError = true)]
    public static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern int posix_fadvise(int fd, long offset, long len, int advice);

    [DllImport("libc", SetLastError = true)]
    public static extern IntPtr readahead(int fd, long offset, long count);

    public static void Prewarm(IEnumerable<string> filePaths, int maxFiles = 50)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        Task.Run(() =>
        {
            try
            {
                int count = 0;
                foreach (var path in filePaths)
                {
                    if (count++ >= maxFiles) break;

                    try
                    {
                        int fd = open(path, O_RDONLY | O_NOATIME);
                        if (fd >= 0)
                        {
                            readahead(fd, 0, 10 * 1024 * 1024);
                            posix_fadvise(fd, 0, 10 * 1024 * 1024, POSIX_FADV_WILLNEED);
                            close(fd);
                        }
                    }
                    catch
                    {
                        // Ignore individual file errors
                    }
                }
            }
            catch
            {
                // Ignore catastrophic errors
            }
        });
    }
}
