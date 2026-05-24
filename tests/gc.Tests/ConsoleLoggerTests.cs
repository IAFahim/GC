using gc.Infrastructure.Logging;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;
using System.Text;

namespace gc.Tests;

public class ConsoleLoggerTests
{
    private class MockConsole : IConsole
    {
        public StringBuilder StdOut { get; } = new StringBuilder();
        public StringBuilder StdErr { get; } = new StringBuilder();

        public void WriteLine(string? value = null)
        {
            if (value == null) StdOut.AppendLine();
            else StdOut.AppendLine(value);
        }
        public void Write(string? value) => StdOut.Append(value);
        public void WriteErrorLine(string? value) => StdErr.AppendLine(value);
        public string? ReadLine() => null;
    }

    // ─── Constructor ─────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullConfig_UsesDefaults()
    {
        var logger = new ConsoleLogger(null, new MockConsole());
        Assert.Equal(LogLevel.Success, logger.Level);
    }

    [Fact]
    public void Constructor_DebugLevel_SetsDebug()
    {
        var config = new LoggingConfiguration { Level = "debug" };
        var logger = new ConsoleLogger(config, new MockConsole());
        Assert.Equal(LogLevel.Debug, logger.Level);
    }

    [Fact]
    public void Constructor_VerboseLevel_SetsInfo()
    {
        var config = new LoggingConfiguration { Level = "verbose" };
        var logger = new ConsoleLogger(config, new MockConsole());
        Assert.Equal(LogLevel.Info, logger.Level);
    }

    [Fact]
    public void Constructor_NormalLevel_SetsSuccess()
    {
        var config = new LoggingConfiguration { Level = "normal" };
        var logger = new ConsoleLogger(config, new MockConsole());
        Assert.Equal(LogLevel.Success, logger.Level);
    }

    [Fact]
    public void Constructor_InfoLevel_SetsInfo()
    {
        var config = new LoggingConfiguration { Level = "info" };
        var logger = new ConsoleLogger(config, new MockConsole());
        Assert.Equal(LogLevel.Info, logger.Level);
    }

    [Fact]
    public void Constructor_IncludeTimestamps_True()
    {
        var config = new LoggingConfiguration { Level = "debug", IncludeTimestamps = true };
        var logger = new ConsoleLogger(config, new MockConsole());
        Assert.Equal(LogLevel.Debug, logger.Level);
    }

    // ─── Log filtering ───────────────────────────────────────────────────

    [Fact]
    public void Log_DebugLevel_FiltersInfoMessages()
    {
        var console = new MockConsole();
        var logger = new ConsoleLogger(null, console); // default level = Success
        logger.Log(LogLevel.Info, "should be filtered");

        Assert.Empty(console.StdOut.ToString().Trim());
    }

    [Fact]
    public void Log_SuccessLevel_ShowsErrors()
    {
        var console = new MockConsole();
        var logger = new ConsoleLogger(null, console); // default level = Success
        logger.Log(LogLevel.Error, "something broke");

        Assert.Contains("something broke", console.StdErr.ToString());
    }

    [Fact]
    public void Log_InfoLevel_ShowsInfoMessages()
    {
        var console = new MockConsole();
        var config = new LoggingConfiguration { Level = "info" };
        var logger = new ConsoleLogger(config, console);
        logger.Log(LogLevel.Info, "info message");

        Assert.Contains("info message", console.StdOut.ToString());
    }

    [Fact]
    public void Log_ErrorLevel_WritesToStderr()
    {
        var console = new MockConsole();
        var logger = new ConsoleLogger(null, console);
        logger.Log(LogLevel.Error, "error message");

        Assert.Empty(console.StdOut.ToString().Trim());
        Assert.Contains("error message", console.StdErr.ToString());
    }

    // ─── Log formatting ──────────────────────────────────────────────────

    [Fact]
    public void Log_WithException_IncludesMessage()
    {
        var console = new MockConsole();
        var logger = new ConsoleLogger(null, console);
        var exception = new InvalidOperationException("inner failure");
        logger.Log(LogLevel.Error, "outer message", exception);

        var output = console.StdErr.ToString();
        Assert.Contains("outer message", output);
        Assert.Contains("inner failure", output);
    }

    [Fact]
    public void Log_WithException_IncludesStackTrace_WhenDebug()
    {
        var console = new MockConsole();
        var config = new LoggingConfiguration { Level = "debug" };
        var logger = new ConsoleLogger(config, console);

        // Create an exception with a real stack trace
        Exception capturedEx;
        try { throw new InvalidOperationException("stack test"); }
        catch (Exception ex) { capturedEx = ex; }

        logger.Log(LogLevel.Error, "debug error", capturedEx);

        var output = console.StdErr.ToString();
        Assert.Contains("stack test", output);
        // Stack trace should be present when in debug mode
        Assert.Contains("at gc.Tests", output);
    }

    [Fact]
    public void Log_WithTimestamp_IncludesTimestamp()
    {
        var console = new MockConsole();
        var config = new LoggingConfiguration { Level = "debug", IncludeTimestamps = true };
        var logger = new ConsoleLogger(config, console);
        logger.Log(LogLevel.Success, "timed message");

        var output = console.StdOut.ToString();
        // Timestamp format: [HH:mm:ss.fff]
        Assert.Matches(@"\[\d{2}:\d{2}:\d{2}\.\d{3}\]", output);
        Assert.Contains("timed message", output);
    }

    [Fact]
    public void Log_WarningPrefix_ContainsWARNING()
    {
        var console = new MockConsole();
        var logger = new ConsoleLogger(null, console);
        logger.Log(LogLevel.Warning, "careful now");

        var output = console.StdOut.ToString();
        Assert.Contains("[WARNING]", output);
        Assert.Contains("careful now", output);
    }

    [Fact]
    public void Log_DebugPrefix_ContainsDEBUG()
    {
        var console = new MockConsole();
        var config = new LoggingConfiguration { Level = "debug" };
        var logger = new ConsoleLogger(config, console);
        logger.Log(LogLevel.Debug, "trace info");

        var output = console.StdOut.ToString();
        Assert.Contains("[DEBUG]", output);
        Assert.Contains("trace info", output);
    }

    // ─── Additional edge cases ───────────────────────────────────────────

    [Fact]
    public void Log_LevelProperty_IsSettable()
    {
        var logger = new ConsoleLogger(null, new MockConsole());
        Assert.Equal(LogLevel.Success, logger.Level);

        logger.Level = LogLevel.Debug;
        Assert.Equal(LogLevel.Debug, logger.Level);
    }

    [Fact]
    public void Log_UnknownConfigLevel_DefaultsToSuccess()
    {
        var config = new LoggingConfiguration { Level = "unknown_level" };
        var logger = new ConsoleLogger(config, new MockConsole());
        Assert.Equal(LogLevel.Success, logger.Level);
    }

    [Fact]
    public void Log_SuccessMessage_NoPrefix()
    {
        var console = new MockConsole();
        var logger = new ConsoleLogger(null, console);
        logger.Log(LogLevel.Success, "all good");

        var output = console.StdOut.ToString();
        Assert.Equal("all good" + Environment.NewLine, output);
    }

    [Fact]
    public void Log_WithoutTimestamp_NoBracketPrefix()
    {
        var console = new MockConsole();
        var config = new LoggingConfiguration { Level = "debug", IncludeTimestamps = false };
        var logger = new ConsoleLogger(config, console);
        logger.Log(LogLevel.Debug, "no timestamp");

        var output = console.StdOut.ToString();
        // Should have [DEBUG] prefix but NOT a timestamp like [HH:mm:ss.fff]
        Assert.DoesNotMatch(@"^\[\d{2}:\d{2}:\d{2}", output);
    }
}
