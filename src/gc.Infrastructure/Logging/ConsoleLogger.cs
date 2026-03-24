using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;

namespace gc.Infrastructure.Logging;

public sealed class ConsoleLogger : ILogger
{
    private bool _includeTimestamps;
    public LogLevel Level { get; set; }

    public ConsoleLogger(LoggingConfiguration? config = null)
    {
        Level = config?.Level switch
        {
            "debug" => LogLevel.Debug,
            "verbose" => LogLevel.Info,
            "info" => LogLevel.Info,
            "normal" => LogLevel.Warning,
            _ => LogLevel.Warning
        };
        _includeTimestamps = config?.IncludeTimestamps ?? false;
    }

    public void Log(LogLevel level, string message, Exception? ex = null)
    {
        if (level < Level) return;

        var prefix = _includeTimestamps ? $"[{DateTime.Now:HH:mm:ss.fff}] " : "";
        var levelPrefix = level switch
        {
            LogLevel.Debug => "[DEBUG] ",
            LogLevel.Warning => "[WARNING] ",
            LogLevel.Error => "[ERROR] ",
            _ => ""
        };

        var output = $"{prefix}{levelPrefix}{message}";
        if (ex != null)
        {
            output += $": {ex.Message}";
            if (Level == LogLevel.Debug)
            {
                output += $"\n{ex.StackTrace}";
            }
        }

        // Write to stderr for errors, stdout for everything else
        if (level == LogLevel.Error)
        {
            Console.Error.WriteLine(output);
        }
        else
        {
            Console.WriteLine(output);
        }
    }
}