using System.Globalization;

namespace gc.Utilities;

public static class Formatting
{
    public static string FormatSize(long bytes)
    {
        return bytes < 1024 ? $"{bytes} B" :
            bytes < 1048576 ? $"{(bytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} KB" :
            $"{(bytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture)} MB";
    }
}