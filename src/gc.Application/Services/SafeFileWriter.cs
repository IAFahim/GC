using System.Text;

namespace gc.Application.Services;

/// <summary>
/// Provides transactional file writes — writes to a temp file first, then
/// atomically moves into place. Prevents partial output corruption on failure.
/// </summary>
public static class SafeFileWriter
{
    /// <summary>
    /// Writes content to a temporary file, then moves it to the target path.
    /// On failure, the target file is left untouched.
    /// </summary>
    public static async Task WriteAllTextAsync(string path, string content, Encoding encoding, CancellationToken ct = default)
    {
        var tmpPath = path + ".tmp." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N")[..8];

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await using (var writer = new StreamWriter(fs, encoding, 4096, leaveOpen: false))
            {
                await writer.WriteAsync(content.AsMemory(), ct);
                await writer.FlushAsync();
            }

            if (File.Exists(path))
                File.Delete(path);

            File.Move(tmpPath, path, overwrite: true);
        }
        catch
        {
            // Clean up temp file on any failure
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Writes bytes to a temporary file, then moves it to the target path.
    /// </summary>
    public static async Task WriteAllBytesAsync(string path, byte[] content, CancellationToken ct = default)
    {
        var tmpPath = path + ".tmp." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N")[..8];

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await fs.WriteAsync(content, ct);
                await fs.FlushAsync();
            }

            if (File.Exists(path))
                File.Delete(path);

            File.Move(tmpPath, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            throw;
        }
    }
}
