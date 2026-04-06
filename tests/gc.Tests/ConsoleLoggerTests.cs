using gc.Infrastructure.Logging;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;

namespace gc.Tests;

public class ConsoleLoggerTests
{
    // ─── Constructor ─────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullConfig_UsesDefaults()
    {
        var logger = new ConsoleLogger(null);
        Assert.Equal(LogLevel.Success, logger.Level);
    }

    [Fact]
    public void Constructor_DebugLevel_SetsDebug()
    {
        var config = new LoggingConfiguration { Level = "debug" };
        var logger = new ConsoleLogger(config);
        Assert.Equal(LogLevel.Debug, logger.Level);
    }

    [Fact]
    public void Constructor_VerboseLevel_SetsInfo()
    {
        var config = new LoggingConfiguration { Level = "verbose" };
        var logger = new ConsoleLogger(config);
        Assert.Equal(LogLevel.Info, logger.Level);
    }

    [Fact]
    public void Constructor_NormalLevel_SetsSuccess()
    {
        var config = new LoggingConfiguration { Level = "normal" };
        var logger = new ConsoleLogger(config);
        Assert.Equal(LogLevel.Success, logger.Level);
    }

    [Fact]
    public void Constructor_InfoLevel_SetsInfo()
    {
        var config = new LoggingConfiguration { Level = "info" };
        var logger = new ConsoleLogger(config);
        Assert.Equal(LogLevel.Info, logger.Level);
    }

    [Fact]
    public void Constructor_IncludeTimestamps_True()
    {
        var config = new LoggingConfiguration { Level = "debug", IncludeTimestamps = true };
        var logger = new ConsoleLogger(config);
        Assert.Equal(LogLevel.Debug, logger.Level);
    }

    // ─── Log filtering ───────────────────────────────────────────────────

    [Fact]
    public void Log_DebugLevel_FiltersInfoMessages()
    {
        // When Level is Success (default), Info messages (lower priority) should be filtered out
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outMs = new MemoryStream();
            using var outWriter = new StreamWriter(outMs) { AutoFlush = true };
            using var errMs = new MemoryStream();
            using var errWriter = new StreamWriter(errMs) { AutoFlush = true };
            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            var logger = new ConsoleLogger(); // default level = Success
            logger.Log(LogLevel.Info, "should be filtered");

            outMs.Position = 0;
            var output = new StreamReader(outMs).ReadToEnd();
            Assert.Empty(output.Trim());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Log_SuccessLevel_ShowsErrors()
    {
        // Errors should always pass through even at Success level
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outMs = new MemoryStream();
            using var outWriter = new StreamWriter(outMs) { AutoFlush = true };
            using var errMs = new MemoryStream();
            using var errWriter = new StreamWriter(errMs) { AutoFlush = true };
            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            var logger = new ConsoleLogger(); // default level = Success
            logger.Log(LogLevel.Error, "something broke");

            errMs.Position = 0;
            var errorOutput = new StreamReader(errMs).ReadToEnd();
            Assert.Contains("something broke", errorOutput);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Log_InfoLevel_ShowsInfoMessages()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outMs = new MemoryStream();
            using var outWriter = new StreamWriter(outMs) { AutoFlush = true };
            using var errMs = new MemoryStream();
            using var errWriter = new StreamWriter(errMs) { AutoFlush = true };
            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            var config = new LoggingConfiguration { Level = "info" };
            var logger = new ConsoleLogger(config);
            logger.Log(LogLevel.Info, "info message");

            outMs.Position = 0;
            var output = new StreamReader(outMs).ReadToEnd();
            Assert.Contains("info message", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Log_ErrorLevel_WritesToStderr()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outMs = new MemoryStream();
            using var outWriter = new StreamWriter(outMs) { AutoFlush = true };
            using var errMs = new MemoryStream();
            using var errWriter = new StreamWriter(errMs) { AutoFlush = true };
            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            var logger = new ConsoleLogger();
            logger.Log(LogLevel.Error, "error message");

            outMs.Position = 0;
            errMs.Position = 0;
            var stdout = new StreamReader(outMs).ReadToEnd();
            var stderr = new StreamReader(errMs).ReadToEnd();

            Assert.Empty(stdout.Trim());
            Assert.Contains("error message", stderr);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    // ─── Log formatting ──────────────────────────────────────────────────

    [Fact]
    public void Log_WithException_IncludesMessage()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outMs = new MemoryStream();
            using var outWriter = new StreamWriter(outMs) { AutoFlush = true };
            using var errMs = new MemoryStream();
            using var errWriter = new StreamWriter(errMs) { AutoFlush = true };
            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            var logger = new ConsoleLogger();
            var exception = new InvalidOperationException("inner failure");
            logger.Log(LogLevel.Error, "outer message", exception);

            errMs.Position = 0;
            var output = new StreamReader(errMs).ReadToEnd();
            Assert.Contains("outer message", output);
            Assert.Contains("inner failure", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Log_WithException_IncludesStackTrace_WhenDebug()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outMs = new MemoryStream();
            using var outWriter = new StreamWriter(outMs) { AutoFlush = true };
            using var errMs = new MemoryStream();
            using var errWriter = new StreamWriter(errMs) { AutoFlush = true };
            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            var config = new LoggingConfiguration { Level = "debug" };
            var logger = new ConsoleLogger(config);

            // Create an exception with a real stack trace
            Exception capturedEx;
            try { throw new InvalidOperationException("stack test"); }
            catch (Exception ex) { capturedEx = ex; }

            logger.Log(LogLevel.Error, "debug error", capturedEx);

            errMs.Position = 0;
            var output = new StreamReader(errMs).ReadToEnd();
            Assert.Contains("stack test", output);
            // Stack trace should be present when in debug mode
            Assert.Contains("at gc.Tests", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Log_WithTimestamp_IncludesTimestamp()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outMs = new MemoryStream();
            using var outWriter = new StreamWriter(outMs) { AutoFlush = true };
            using var errMs = new MemoryStream();
            using var errWriter = new StreamWriter(errMs) { AutoFlush = true };
            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            var config = new LoggingConfiguration { Level = "debug", IncludeTimestamps = true };
            var logger = new ConsoleLogger(config);
            logger.Log(LogLevel.Success, "timed message");

            outMs.Position = 0;
            var output = new StreamReader(outMs).ReadToEnd();
            // Timestamp format: [HH:mm:ss.fff]
            Assert.Matches(@"\[\d{2}:\d{2}:\d{2}\.\d{3}\]", output);
            Assert.Contains("timed message", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Log_WarningPrefix_ContainsWARNING()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outMs = new MemoryStream();
            using var outWriter = new StreamWriter(outMs) { AutoFlush = true };
            using var errMs = new MemoryStream();
            using var errWriter = new StreamWriter(errMs) { AutoFlush = true };
            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            var logger = new ConsoleLogger();
            logger.Log(LogLevel.Warning, "careful now");

            outMs.Position = 0;
            var output = new StreamReader(outMs).ReadToEnd();
            Assert.Contains("[WARNING]", output);
            Assert.Contains("careful now", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Log_DebugPrefix_ContainsDEBUG()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outMs = new MemoryStream();
            using var outWriter = new StreamWriter(outMs) { AutoFlush = true };
            using var errMs = new MemoryStream();
            using var errWriter = new StreamWriter(errMs) { AutoFlush = true };
            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            var config = new LoggingConfiguration { Level = "debug" };
            var logger = new ConsoleLogger(config);
            logger.Log(LogLevel.Debug, "trace info");

            outMs.Position = 0;
            var output = new StreamReader(outMs).ReadToEnd();
            Assert.Contains("[DEBUG]", output);
            Assert.Contains("trace info", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    // ─── Additional edge cases ───────────────────────────────────────────

    [Fact]
    public void Log_LevelProperty_IsSettable()
    {
        var logger = new ConsoleLogger();
        Assert.Equal(LogLevel.Success, logger.Level);

        logger.Level = LogLevel.Debug;
        Assert.Equal(LogLevel.Debug, logger.Level);
    }

    [Fact]
    public void Log_UnknownConfigLevel_DefaultsToSuccess()
    {
        var config = new LoggingConfiguration { Level = "unknown_level" };
        var logger = new ConsoleLogger(config);
        Assert.Equal(LogLevel.Success, logger.Level);
    }

    [Fact]
    public void Log_SuccessMessage_NoPrefix()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outMs = new MemoryStream();
            using var outWriter = new StreamWriter(outMs) { AutoFlush = true };
            using var errMs = new MemoryStream();
            using var errWriter = new StreamWriter(errMs) { AutoFlush = true };
            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            var logger = new ConsoleLogger();
            logger.Log(LogLevel.Success, "all good");

            outMs.Position = 0;
            var output = new StreamReader(outMs).ReadToEnd();
            Assert.Equal("all good" + Environment.NewLine, output);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Log_WithoutTimestamp_NoBracketPrefix()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var outMs = new MemoryStream();
            using var outWriter = new StreamWriter(outMs) { AutoFlush = true };
            using var errMs = new MemoryStream();
            using var errWriter = new StreamWriter(errMs) { AutoFlush = true };
            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            var config = new LoggingConfiguration { Level = "debug", IncludeTimestamps = false };
            var logger = new ConsoleLogger(config);
            logger.Log(LogLevel.Debug, "no timestamp");

            outMs.Position = 0;
            var output = new StreamReader(outMs).ReadToEnd();
            // Should have [DEBUG] prefix but NOT a timestamp like [HH:mm:ss.fff]
            Assert.DoesNotMatch(@"^\[\d{2}:\d{2}:\d{2}", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
