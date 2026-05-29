using gc.Application.Services;
using gc.Application.Validators;
using gc.CLI.Models;
using gc.CLI.Services;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;

namespace gc.Tests;

// ─── Recording logger mock ───────────────────────────────────────────────────
internal sealed class RecordingLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Messages { get; } = new();

    public void Log(LogLevel level, string message, Exception? ex = null)
    {
        Messages.Add((level, message));
    }
}

// ─── CliParser tests ─────────────────────────────────────────────────────────
public sealed class CliParserFlagTests
{
    private static readonly GcConfiguration DefaultConfig = new();
    private readonly CliParser _parser = new();

    private CliArguments Parse(params string[] args)
    {
        return _parser.Parse(args, DefaultConfig).Value!;
    }

    // 1
    [Fact]
    public void Parse_PresetFlag_CollectsPresetNames()
    {
        var result = Parse("--preset", "web");
        Assert.Contains("web", result.Presets);
    }

    // 2
    [Fact]
    public void Parse_PresetFlag_MultiplePresets()
    {
        var result = Parse("--preset", "web", "backend");
        Assert.Contains("web", result.Presets);
        Assert.Contains("backend", result.Presets);
    }

    // 3
    [Fact]
    public void Parse_ShortHFlag()
    {
        var result = Parse("-h");
        Assert.True(result.ShowHelp);
    }

    // 4
    [Fact]
    public void Parse_ShortVFlag()
    {
        var result = Parse("-v");
        Assert.True(result.Verbose);
    }

    // 5
    [Fact]
    public void Parse_TestFlag()
    {
        var result = Parse("--test");
        Assert.True(result.RunTests);
    }

    // 6
    [Fact]
    public void Parse_BenchmarkFlag()
    {
        var result = Parse("--benchmark");
        Assert.True(result.RunRealBenchmark);
    }

    // 7
    [Fact]
    public void Parse_InitConfigFlag()
    {
        var result = Parse("--init-config");
        Assert.True(result.InitConfig);
    }

    // 8
    [Fact]
    public void Parse_ValidateConfigFlag()
    {
        var result = Parse("--validate-config");
        Assert.True(result.ValidateConfig);
    }

    // 9
    [Fact]
    public void Parse_DumpConfigFlag()
    {
        var result = Parse("--dump-config");
        Assert.True(result.DumpConfig);
    }

    // 10
    [Fact]
    public void Parse_NoAppendFlag()
    {
        var result = Parse("--no-append");
        Assert.False(result.Append);
    }

    // 11
    [Fact]
    public void Parse_FunKeywordsGrabAndBrain()
    {
        var result = Parse("grab", "src", "brain");
        Assert.Contains("src", result.Paths);
        Assert.True(result.BrainMode);
    }

    // 12
    [Fact]
    public void Parse_HordeKeyword()
    {
        var result = Parse("horde");
        Assert.True(result.Cluster);
    }

    // 13 — Pascal-case variants for state-changing flags
    [Fact]
    public void Parse_AllPascalCaseVariants()
    {
        var r1 = Parse("--Paths", "a");
        Assert.Contains("a", r1.Paths);

        var r2 = Parse("--Extension", "cs");
        Assert.Contains("cs", r2.Extensions);

        var r3 = Parse("--Exclude", "bin");
        Assert.Contains("bin", r3.Excludes);

        var r4 = Parse("--Output", "out.md");
        Assert.Equal("out.md", r4.OutputFile);

        var r5 = Parse("--Depth", "5");
        Assert.Equal(5, r5.Depth);
    }

    // 14
    [Fact]
    public void Parse_BackslashNormalization()
    {
        var result = Parse("grab", "src\\folder");
        Assert.Contains("src/folder", result.Paths);
    }

    // 15
    [Fact]
    public void Parse_HistoryNegativeIndex()
    {
        // --history expects a positive index; "-1" should not crash.
        // Since the parser only accepts idx > 0, -1 is treated as a default arg (path).
        var result = Parse("--history", "-1");
        Assert.True(result.ShowHistory);
        // HistoryIndex stays null because -1 fails the > 0 check
        Assert.Null(result.HistoryIndex);
    }
}

// ─── ConfigurationService tests ───────────────────────────────────────────────
public sealed class ConfigurationServiceTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupDir(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    // 16
    [Fact]
    public async Task InitializeConfigAsync_CreatesDirectoryAndFile()
    {
        var tmp = CreateTempDir();
        var configDir = Path.Combine(tmp, ".gc");
        try
        {
            var logger = new RecordingLogger();
            var sut = new ConfigurationService(logger, new ConfigurationValidator());
            var result = await sut.InitializeConfigAsync(configDir);

            Assert.True(result.IsSuccess, result.Error ?? "Init failed");
            Assert.True(Directory.Exists(configDir), ".gc directory should exist");
            Assert.True(File.Exists(Path.Combine(configDir, "config.json")), "config.json should exist");
        }
        finally
        {
            CleanupDir(tmp);
        }
    }

    // 17
    [Fact]
    public async Task InitializeConfigAsync_ConfigExists_ReturnsFailure()
    {
        var tmp = CreateTempDir();
        var configDir = Path.Combine(tmp, ".gc");
        try
        {
            var logger = new RecordingLogger();
            var sut = new ConfigurationService(logger, new ConfigurationValidator());

            // First call succeeds
            var first = await sut.InitializeConfigAsync(configDir);
            Assert.True(first.IsSuccess);

            // Second call should fail
            var second = await sut.InitializeConfigAsync(configDir);
            Assert.False(second.IsSuccess);
            Assert.NotNull(second.Error);
        }
        finally
        {
            CleanupDir(tmp);
        }
    }

    // 18
    [Fact]
    public async Task InitializeConfigAsync_CustomConfigDir()
    {
        var tmp = CreateTempDir();
        var customDir = Path.Combine(tmp, "my-custom-config");
        try
        {
            var logger = new RecordingLogger();
            var sut = new ConfigurationService(logger, new ConfigurationValidator());
            var result = await sut.InitializeConfigAsync(customDir);

            Assert.True(result.IsSuccess, result.Error ?? "Init failed");
            Assert.True(Directory.Exists(customDir));
            Assert.True(File.Exists(Path.Combine(customDir, "config.json")));
        }
        finally
        {
            CleanupDir(tmp);
        }
    }

    // 19
    [Fact]
    public void ValidateConfig_ValidConfig_ReturnsValid()
    {
        var logger = new RecordingLogger();
        var sut = new ConfigurationService(logger, new ConfigurationValidator());
        var config = new GcConfiguration(); // defaults should be valid

        var result = sut.ValidateConfig(config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsValid);
    }

    // 20
    [Fact]
    public void ValidateConfig_InvalidConfig_ReturnsInvalid()
    {
        var logger = new RecordingLogger();
        var sut = new ConfigurationService(logger, new ConfigurationValidator());

        // Craft an invalid config — bad memory format, MaxFiles < 1, empty markdown fence
        var config = new GcConfiguration
        {
            Limits = new LimitsConfiguration
            {
                MaxFileSize = "not-a-size",
                MaxMemoryBytes = "bad",
                MaxClipboardSize = "also-bad",
                MaxFiles = 0
            },
            Markdown = new MarkdownConfiguration { Fence = "" }
        };

        var result = sut.ValidateConfig(config);
        Assert.True(result.IsSuccess); // the Result itself is success (validation ran)
        Assert.False(result.Value!.IsValid); // but the config is invalid
        Assert.NotEmpty(result.Value.Errors);
    }

    // 21
    [Fact]
    public void DumpConfig_ValidConfig_ReturnsSerialized()
    {
        var logger = new RecordingLogger();
        var sut = new ConfigurationService(logger, new ConfigurationValidator());
        var config = new GcConfiguration();

        var result = sut.DumpConfig(config);
        Assert.True(result.IsSuccess, result.Error ?? "Dump failed");

        // The JSON is logged, not returned; verify it was logged
        Assert.Contains(logger.Messages, m =>
            m.Level == LogLevel.Info && m.Message.Contains("version"));
    }
}