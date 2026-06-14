using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Linq;

namespace gc.Application.Native;

/// <summary>
/// High-performance native file operations with platform-specific optimizations.
/// Uses source-generated interop via LibraryImport for NativeAOT compatibility.
/// </summary>
public static partial class NativeFileOps
{
    // Constants for Linux syscalls
    private const int O_RDONLY = 0x0000;
    private const int O_NOATIME = 0x40000;
    private const int POSIX_FADV_SEQUENTIAL = 2;
    private const int POSIX_FADV_WILLNEED = 3;

    // Magic numbers
    public const int DefaultPrewarmFileLimit = 50;
    public const int DefaultPrewarmBytesPerFile = 10 * 1024 * 1024; // 10MB

    #region Linux Native Methods (LibraryImport - Source Generated)

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [UnsupportedOSPlatform("windows")]
    [UnsupportedOSPlatform("osx")]
    private static partial int open_linux(string pathname, int flags);

    [LibraryImport("libc", SetLastError = true)]
    [UnsupportedOSPlatform("windows")]
    [UnsupportedOSPlatform("osx")]
    private static partial int close_linux(int fd);

    [LibraryImport("libc", SetLastError = true)]
    [UnsupportedOSPlatform("windows")]
    [UnsupportedOSPlatform("osx")]
    private static partial int posix_fadvise_linux(int fd, long offset, long len, int advice);

    [LibraryImport("libc", SetLastError = true)]
    [UnsupportedOSPlatform("windows")]
    [UnsupportedOSPlatform("osx")]
    private static partial nint readahead_linux(int fd, long offset, long count);

    #endregion

    #region macOS Native Methods

    [LibraryImport("libSystem", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [UnsupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    private static partial int open_macos(string pathname, int flags);

    [LibraryImport("libSystem", SetLastError = true)]
    [UnsupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    private static partial int close_macos(int fd);

    // macOS uses fcntl instead of posix_fadvise for hints
    [LibraryImport("libSystem", SetLastError = true)]
    [UnsupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    private static partial int fcntl_macos(int fd, int cmd, int arg);

    private const int F_RDADVISE = 53; // macOS fcntl for read advice
    private const int F_RDAHEAD = 28;  // Enable read-ahead

    #endregion

    #region Windows Native Methods

    [LibraryImport("kernel32", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [SupportedOSPlatform("windows")]
    private static partial nint CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [LibraryImport("kernel32", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadFile(
        nint hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    // Windows constants
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 1;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
    private const nint INVALID_HANDLE_VALUE = -1;

    #endregion

    #region Public API

    /// <summary>
    /// Prefetches file contents for faster subsequent reads using platform-specific optimizations.
    /// Returns a Task so callers can observe faults.
    /// </summary>
    public static Task PrewarmAsync(
        IEnumerable<string> filePaths,
        int maxFiles = DefaultPrewarmFileLimit,
        int bytesToRead = DefaultPrewarmBytesPerFile,
        CancellationToken ct = default)
    {
        if (OperatingSystem.IsWindows())
            return PrewarmWindowsAsync(filePaths, maxFiles, bytesToRead, ct);
        else if (OperatingSystem.IsLinux())
            return PrewarmLinuxAsync(filePaths, maxFiles, bytesToRead, ct);
        else if (OperatingSystem.IsMacOS())
            return PrewarmMacOSAsync(filePaths, maxFiles, bytesToRead, ct);
        else
            return Task.CompletedTask;
    }

    /// <summary>
    /// Synchronous prewarm for backwards compatibility.
    /// Prefer PrewarmAsync for proper task observation.
    /// </summary>
    [Obsolete("Use PrewarmAsync for proper fault observation")]
    public static void Prewarm(IEnumerable<string> filePaths, int maxFiles = DefaultPrewarmFileLimit)
    {
        if (OperatingSystem.IsWindows())
            PrewarmWindows(filePaths, maxFiles);
        else if (OperatingSystem.IsLinux())
            PrewarmLinux(filePaths, maxFiles);
        else if (OperatingSystem.IsMacOS())
            PrewarmMacOS(filePaths, maxFiles);
    }

    #endregion

    #region Linux Implementation

    [SupportedOSPlatform("linux")]
    private static Task PrewarmLinuxAsync(
        IEnumerable<string> filePaths,
        int maxFiles,
        int bytesToRead,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var count = 0;
            foreach (var path in filePaths)
            {
                if (ct.IsCancellationRequested) break;
                if (count++ >= maxFiles) break;

                try
                {
                    var fd = open_linux(path, O_RDONLY | O_NOATIME);
                    if (fd >= 0)
                    {
                        readahead_linux(fd, 0, bytesToRead);
                        posix_fadvise_linux(fd, 0, bytesToRead, POSIX_FADV_WILLNEED);
                        close_linux(fd);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.Error.WriteLine($"[NativeFileOps/Linux] Prewarm failed for {path}: {ex.Message}");
                }
            }
        }, ct);
    }

    [SupportedOSPlatform("linux")]
    private static void PrewarmLinux(IEnumerable<string> filePaths, int maxFiles)
    {
        foreach (var path in filePaths.Take(maxFiles))
        {
            try
            {
                var fd = open_linux(path, O_RDONLY | O_NOATIME);
                if (fd >= 0)
                {
                    readahead_linux(fd, 0, DefaultPrewarmBytesPerFile);
                    posix_fadvise_linux(fd, 0, DefaultPrewarmBytesPerFile, POSIX_FADV_WILLNEED);
                    close_linux(fd);
                }
            }
            catch
            {
                // Suppress for legacy compatibility
            }
        }
    }

    #endregion

    #region macOS Implementation

    [SupportedOSPlatform("macos")]
    private static Task PrewarmMacOSAsync(
        IEnumerable<string> filePaths,
        int maxFiles,
        int bytesToRead,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var count = 0;
            foreach (var path in filePaths)
            {
                if (ct.IsCancellationRequested) break;
                if (count++ >= maxFiles) break;

                try
                {
                    var fd = open_macos(path, O_RDONLY);
                    if (fd >= 0)
                    {
                        // Enable read-ahead on macOS
                        fcntl_macos(fd, F_RDAHEAD, 1);
                        close_macos(fd);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.Error.WriteLine($"[NativeFileOps/macOS] Prewarm failed for {path}: {ex.Message}");
                }
            }
        }, ct);
    }

    [SupportedOSPlatform("macos")]
    private static void PrewarmMacOS(IEnumerable<string> filePaths, int maxFiles)
    {
        foreach (var path in filePaths.Take(maxFiles))
        {
            try
            {
                var fd = open_macos(path, O_RDONLY);
                if (fd >= 0)
                {
                    fcntl_macos(fd, F_RDAHEAD, 1);
                    close_macos(fd);
                }
            }
            catch
            {
                // Suppress for legacy compatibility
            }
        }
    }

    #endregion

    #region Windows Implementation

    [SupportedOSPlatform("windows")]
    private static Task PrewarmWindowsAsync(
        IEnumerable<string> filePaths,
        int maxFiles,
        int bytesToRead,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var count = 0;
            var buffer = Marshal.AllocHGlobal(Math.Min(bytesToRead, 64 * 1024)); // 64KB chunk

            try
            {
                foreach (var path in filePaths)
                {
                    if (ct.IsCancellationRequested) break;
                    if (count++ >= maxFiles) break;

                    nint handle = INVALID_HANDLE_VALUE;
                    try
                    {
                        handle = CreateFileW(
                            path,
                            GENERIC_READ,
                            FILE_SHARE_READ,
                            IntPtr.Zero,
                            OPEN_EXISTING,
                            FILE_FLAG_SEQUENTIAL_SCAN,
                            IntPtr.Zero);

                        if (handle != INVALID_HANDLE_VALUE)
                        {
                            // Read a small chunk to trigger Windows caching
                            ReadFile(handle, buffer, 4096, out _, IntPtr.Zero);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Console.Error.WriteLine($"[NativeFileOps/Windows] Prewarm failed for {path}: {ex.Message}");
                    }
                    finally
                    {
                        if (handle != INVALID_HANDLE_VALUE)
                            CloseHandle(handle);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }, ct);
    }

    [SupportedOSPlatform("windows")]
    private static void PrewarmWindows(IEnumerable<string> filePaths, int maxFiles)
    {
        var buffer = Marshal.AllocHGlobal(4096);

        try
        {
            foreach (var path in filePaths.Take(maxFiles))
            {
                nint handle = INVALID_HANDLE_VALUE;
                try
                {
                    handle = CreateFileW(
                        path,
                        GENERIC_READ,
                        FILE_SHARE_READ,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        FILE_FLAG_SEQUENTIAL_SCAN,
                        IntPtr.Zero);

                    if (handle != INVALID_HANDLE_VALUE)
                    {
                        ReadFile(handle, buffer, 4096, out _, IntPtr.Zero);
                    }
                }
                catch
                {
                    // Suppress for legacy compatibility
                }
                finally
                {
                    if (handle != INVALID_HANDLE_VALUE)
                        CloseHandle(handle);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    #endregion
}
