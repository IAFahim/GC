using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.System;

namespace gc.Infrastructure.Logging;

public sealed class ConsoleLogger : ILogger
{
    private readonly IConsole _console;
    private bool _includeTimestamps;

    public ConsoleLogger(LoggingConfiguration? config = null, IConsole? console = null)
    {
        _console = console ?? new SystemConsole();
        Level = LogLevel.Success;
        ApplyConfiguration(config);
    }

    public void ApplyConfiguration(LoggingConfiguration? config)
    {
        if (config == null)
        {
            Level = LogLevel.Success;
            return;
        }
        Level = config.Level switch
        {
            "debug" => LogLevel.Debug,
            "verbose" => LogLevel.Info,
            "info" => LogLevel.Info,
            "normal" => LogLevel.Success,
            _ => LogLevel.Success
        };
        _includeTimestamps = config.IncludeTimestamps ?? false;
    }

    public LogLevel Level { get; set; }

    public void Log(LogLevel level, string message, Exception? ex = null)
    {
        if (level < Level) return;

        var prefix = _includeTimestamps ? $"[{DateTime.Now:HH:mm:ss.fff}] " : "";
        var levelPrefix = level switch
        {
            LogLevel.Debug => "[DEBUG] ",
            LogLevel.Success => "",
            LogLevel.Warning => "[WARNING] ",
            LogLevel.Error => "[ERROR] ",
            _ => ""
        };

        var output = $"{prefix}{levelPrefix}{message}";
        if (ex != null)
        {
            output += $": {ex.Message}";
            if (Level == LogLevel.Debug) output += $"\n{ex.StackTrace}";
        }

        if (level == LogLevel.Error)
            _console.WriteErrorLine(output);
        else
            _console.WriteLine(output);
    }
}