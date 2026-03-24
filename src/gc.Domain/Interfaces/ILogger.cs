namespace gc.Domain.Interfaces;

public enum LogLevel
{
    Debug,
    Info,
    Success,
    Warning,
    Error
}

public interface ILogger
{
    void Log(LogLevel level, string message, Exception? ex = null);
}

public static class LoggerExtensions
{
    public static void Info(this ILogger logger, string message) => logger.Log(LogLevel.Info, message);
    public static void Success(this ILogger logger, string message) => logger.Log(LogLevel.Success, message);
    public static void Error(this ILogger logger, string message, Exception? ex = null) => logger.Log(LogLevel.Error, message, ex);
    public static void Warning(this ILogger logger, string message) => logger.Log(LogLevel.Warning, message);
    public static void Debug(this ILogger logger, string message) => logger.Log(LogLevel.Debug, message);
}
