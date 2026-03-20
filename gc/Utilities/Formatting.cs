using System.Globalization;

namespace gc.Utilities;

public static class Formatting
{
    /// <summary>
    ///     Formats a byte count into a human-readable string (B, KB, or MB).
    /// </summary>
    /// <param name="bytes">The number of bytes to format.</param>
    /// <returns>A formatted string representation of the size.</returns>
    public static string FormatSize(long bytes)
    {
        return bytes < 1024 ? $"{bytes} B" :
            bytes < 1048576 ? $"{(bytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} KB" :
            $"{(bytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture)} MB";
    }
}