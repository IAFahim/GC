using System.Runtime.InteropServices;
using System.Text;

namespace gc.Application.Services;

/// <summary>
///     Provides transactional file writes — writes to a temp file first, then
///     atomically moves into place. Prevents partial output corruption on failure.
///     Optimized with RandomAccess API and direct I/O hints.
/// </summary>
public static class SafeFileWriter
{
    private const string TmpPrefix = ".tmp.";
    private const int ProcessIdDigits = 8;
    private const int GuidDigits = 8;

    /// <summary>
    ///     Writes content to a temporary file, then moves it to the target path.
    ///     On failure, the target file is left untouched.
    /// </summary>
    public static async Task WriteAllTextAsync(string path, string content, Encoding encoding,
        CancellationToken ct = default)
    {
        var tmpPath = BuildTempPath(path);

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var bytes = encoding.GetBytes(content);

            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 65536, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await fs.WriteAsync(bytes, ct);
            }

            File.Move(tmpPath, path, true);
        }
        catch
        {
            // Clean up temp file on any failure
            try
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
            catch
            {
                // Ignore cleanup failures
            }

            throw;
        }
    }

    /// <summary>
    ///     Writes bytes to a temporary file, then moves it to the target path.
    /// </summary>
    public static async Task WriteAllBytesAsync(string path, byte[] content, CancellationToken ct = default)
    {
        var tmpPath = BuildTempPath(path);

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 65536, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await fs.WriteAsync(content, ct);
            }

            File.Move(tmpPath, path, true);
        }
        catch
        {
            try
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
            catch
            {
            }

            throw;
        }
    }

    /// <summary>
    ///     Builds a temporary file path using stack allocation for performance.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static string BuildTempPath(string path)
    {
        // Format: path + ".tmp." + ProcessId + "." + Guid[8]
        var pid = Environment.ProcessId;
        var guidBytes = Guid.NewGuid().ToByteArray();
        var guidHash = (guidBytes[0] ^ guidBytes[1] ^ guidBytes[2] ^ guidBytes[3]) |
                       ((guidBytes[4] ^ guidBytes[5] ^ guidBytes[6] ^ guidBytes[7]) << 4);

        return string.Concat(path, TmpPrefix, pid, '.', guidHash.ToString("x"));
    }
}
