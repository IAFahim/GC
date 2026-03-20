using System.Diagnostics;

namespace gc.Utilities;

public enum LogLevel
{
    Normal,
    Verbose,
    Debug
}

public static class Logger
{
    private static readonly object _lock = new();

    public static LogLevel CurrentLevel { get; set; } = LogLevel.Normal;

    public static bool IncludeTimestamps { get; set; }

    public static void SetLevel(LogLevel level)
    {
        lock (_lock)
        {
            CurrentLevel = level;
        }
    }

    private static string GetPrefix()
    {
        return IncludeTimestamps ? $"[{DateTime.Now:HH:mm:ss.fff}] " : "";
    }

    public static void LogInfo(string message)
    {
        if (CurrentLevel >= LogLevel.Normal) Console.Error.WriteLine($"{GetPrefix()}{message}");
    }

    public static void LogVerbose(string message)
    {
        if (CurrentLevel >= LogLevel.Verbose) Console.Error.WriteLine($"{GetPrefix()}[VERBOSE] {message}");
    }

    public static void LogDebug(string message)
    {
        if (CurrentLevel >= LogLevel.Debug) Console.Error.WriteLine($"{GetPrefix()}[DEBUG] {message}");
    }

    public static void LogError(string message, Exception? ex = null)
    {
        Console.Error.Write($"{GetPrefix()}[ERROR] {message}");
        if (ex != null)
        {
            Console.Error.WriteLine($": {ex.Message}");
            if (CurrentLevel >= LogLevel.Debug)
                Console.Error.WriteLine($"{GetPrefix()}[DEBUG] Stack trace: {ex.StackTrace}");
        }
        else
        {
            Console.Error.WriteLine();
        }
    }

    public static IDisposable? TimeOperation(string operationName)
    {
        if (CurrentLevel < LogLevel.Debug) return null;

        return new OperationTimer(operationName);
    }

    private class OperationTimer : IDisposable
    {
        private readonly string _operationName;
        private readonly Stopwatch _stopwatch;
        private bool _disposed;

        public OperationTimer(string operationName)
        {
            _operationName = operationName;
            _stopwatch = Stopwatch.StartNew();
            LogDebug($"Starting: {_operationName}");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _stopwatch.Stop();
            LogDebug($"Completed: {_operationName} in {_stopwatch.ElapsedMilliseconds}ms");
            _disposed = true;
        }
    }
}