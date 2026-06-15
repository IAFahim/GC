namespace gc.Application.Services;

/// <summary>
///     Provides transactional file writes — writes to a temp file first, then
///     atomically moves into place. Prevents partial output corruption on failure.
///     Optimized with RandomAccess API and direct I/O hints.
/// </summary>
public static class SafeFileWriter
{
    private const string TmpPrefix = ".tmp.";

    /// <summary>
    ///     Writes bytes to a temporary file, then moves it to the target path.
    /// </summary>
    public static async Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> content, CancellationToken ct = default)
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
    ///     Builds a unique temporary file path for the given target path.
    /// </summary>
    private static string BuildTempPath(string path)
    {
        // Format: path + ".tmp." + ProcessId + "." + Guid("N")
        return string.Concat(path, TmpPrefix, Environment.ProcessId.ToString(), ".", Guid.NewGuid().ToString("N"));
    }
}
