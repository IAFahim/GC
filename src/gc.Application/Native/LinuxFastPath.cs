using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace gc.Application.Native;

public static class LinuxFastPath
{
    // Constants for Linux syscalls
    public const int O_RDONLY = 0x0000;
    public const int O_NOATIME = 0x40000;
    public const int POSIX_FADV_SEQUENTIAL = 2;
    public const int POSIX_FADV_WILLNEED = 3;

    // Magic numbers
    public const int DefaultPrewarmFileLimit = 50;
    public const int DefaultPrewarmBytesPerFile = 10 * 1024 * 1024; // 10MB

    [DllImport("libc", SetLastError = true)]
    public static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern int posix_fadvise(int fd, long offset, long len, int advice);

    [DllImport("libc", SetLastError = true)]
    public static extern IntPtr readahead(int fd, long offset, long count);

    /// <summary>
    /// Prefetches file contents for faster subsequent reads.
    /// Returns a Task so callers can observe faults.
    /// </summary>
    public static Task PrewarmAsync(
        IEnumerable<string> filePaths,
        int maxFiles = DefaultPrewarmFileLimit,
        int bytesToRead = DefaultPrewarmBytesPerFile,
        CancellationToken ct = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Task.CompletedTask;

        return Task.Run(async () =>
        {
            try
            {
                int count = 0;
                foreach (var path in filePaths)
                {
                    if (ct.IsCancellationRequested) break;
                    if (count++ >= maxFiles) break;

                    try
                    {
                        int fd = open(path, O_RDONLY | O_NOATIME);
                        if (fd >= 0)
                        {
                            readahead(fd, 0, bytesToRead);
                            posix_fadvise(fd, 0, bytesToRead, POSIX_FADV_WILLNEED);
                            close(fd);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Console.Error.WriteLine($"[gc] Prewarm failed for {path}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"[gc] Prewarm task failed: {ex.Message}");
            }
        }, ct);
    }

    /// <summary>
    /// Synchronous prewarm for backwards compatibility.
    /// Prefer PrewarmAsync for proper task observation.
    /// </summary>
    [Obsolete("Use PrewarmAsync for proper fault observation")]
    public static void Prewarm(IEnumerable<string> filePaths, int maxFiles = DefaultPrewarmFileLimit)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        foreach (var path in filePaths)
        {
            if (maxFiles-- <= 0) break;

            try
            {
                int fd = open(path, O_RDONLY | O_NOATIME);
                if (fd >= 0)
                {
                    readahead(fd, 0, DefaultPrewarmBytesPerFile);
                    posix_fadvise(fd, 0, DefaultPrewarmBytesPerFile, POSIX_FADV_WILLNEED);
                    close(fd);
                }
            }
            catch
            {
                // Suppress for legacy compatibility
            }
        }
    }
}