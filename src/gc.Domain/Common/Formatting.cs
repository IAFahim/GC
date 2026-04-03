using System.Globalization;

namespace gc.Domain.Common;

public static class Formatting
{
    public static string FormatSize(long bytes)
    {
        return bytes < 1024 ? $"{bytes} B" :
            bytes < 1048576 ? $"{(bytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} KB" :
            $"{(bytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture)} MB";
    }

    public static string FormatRelativeTime(DateTime dateTime)
    {
        var span = DateTime.UtcNow - dateTime.ToUniversalTime();
        return span switch
        {
            { TotalSeconds: < 60 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)span.TotalMinutes} min ago",
            { TotalHours: < 24 } => $"{(int)span.TotalHours} hour{(span.TotalHours < 2 ? "" : "s")} ago",
            { TotalDays: 1 } => "yesterday",
            { TotalDays: < 30 } => $"{(int)span.TotalDays} days ago",
            { TotalDays: < 365 } => $"{(int)(span.TotalDays / 30)} month{(span.TotalDays / 30 < 2 ? "" : "s")} ago",
            _ => $"{(int)(span.TotalDays / 365)} year{(span.TotalDays / 365 < 2 ? "" : "s")} ago"
        };
    }
}