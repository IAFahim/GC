using gc.Domain.Common;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Domain.Interfaces;
using gc.CLI.Models;
using System.Text.Json;

namespace gc.Tests;

public class DomainModelTests
{
    // ─── Result<T> ────────────────────────────────────────────────────

    [Fact]
    public void ResultT_Success_HasValue_NoError_IsSuccessTrue()
    {
        var result = Result<int>.Success(42);
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ResultT_Failure_HasError_NoValue_IsSuccessFalse()
    {
        var result = Result<int>.Failure("something broke");
        Assert.False(result.IsSuccess);
        Assert.Equal("something broke", result.Error);
        Assert.Equal(default, result.Value);
    }

    // ─── Result (non-generic) ─────────────────────────────────────────

    [Fact]
    public void Result_Success_StaticMethod()
    {
        var result = Result.Success();
        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Result_Failure_StaticMethod()
    {
        var result = Result.Failure("fail");
        Assert.False(result.IsSuccess);
        Assert.Equal("fail", result.Error);
    }

    // ─── Formatting ───────────────────────────────────────────────────

    [Fact]
    public void FormatSize_Bytes()
    {
        Assert.Equal("100 B", Formatting.FormatSize(100));
    }

    [Fact]
    public void FormatSize_KB()
    {
        Assert.Equal("2.00 KB", Formatting.FormatSize(2048));
    }

    [Fact]
    public void FormatSize_MB()
    {
        Assert.Equal("2.00 MB", Formatting.FormatSize(2097152));
    }

    [Fact]
    public void FormatRelativeTime_JustNow()
    {
        var result = Formatting.FormatRelativeTime(DateTime.UtcNow);
        Assert.Equal("just now", result);
    }

    [Fact]
    public void FormatRelativeTime_MinutesAgo()
    {
        var result = Formatting.FormatRelativeTime(DateTime.UtcNow - TimeSpan.FromMinutes(5));
        Assert.Equal("5 min ago", result);
    }

    [Fact]
    public void FormatRelativeTime_HoursAgo()
    {
        var result = Formatting.FormatRelativeTime(DateTime.UtcNow - TimeSpan.FromHours(2));
        Assert.Equal("2 hours ago", result);
    }

    [Fact]
    public void FormatRelativeTime_DaysAgo()
    {
        var result = Formatting.FormatRelativeTime(DateTime.UtcNow - TimeSpan.FromDays(3));
        Assert.Equal("3 days ago", result);
    }

    // ─── MemorySizeParser ─────────────────────────────────────────────

    [Fact]
    public void Parse_NullOrEmpty_ReturnsDefault()
    {
        const long expected = 104857600; // 100MB
        Assert.Equal(expected, MemorySizeParser.Parse(null!));
        Assert.Equal(expected, MemorySizeParser.Parse(""));
        Assert.Equal(expected, MemorySizeParser.Parse("   "));
    }

    [Fact]
    public void Parse_Bytes()
    {
        Assert.Equal(100, MemorySizeParser.Parse("100B"));
    }

    [Fact]
    public void Parse_KB()
    {
        Assert.Equal(100 * 1024, MemorySizeParser.Parse("100KB"));
    }

    [Fact]
    public void Parse_MB()
    {
        Assert.Equal(100L * 1024 * 1024, MemorySizeParser.Parse("100MB"));
    }

    [Fact]
    public void Parse_GB()
    {
        Assert.Equal(1L * 1024 * 1024 * 1024, MemorySizeParser.Parse("1GB"));
    }

    [Fact]
    public void Parse_DecimalValues()
    {
        Assert.Equal((long)(1.5 * 1024 * 1024 * 1024), MemorySizeParser.Parse("1.5GB"));
    }

    [Fact]
    public void Parse_CaseInsensitive()
    {
        Assert.Equal(100L * 1024 * 1024, MemorySizeParser.Parse("100mb"));
    }

    [Fact]
    public void Parse_InvalidSuffix_ReturnsDefault()
    {
        const long expected = 104857600;
        Assert.Equal(expected, MemorySizeParser.Parse("100XX"));
    }

    [Fact]
    public void Parse_Whitespace_Handled()
    {
        Assert.Equal(100, MemorySizeParser.Parse("  100B  "));
    }

    // ─── FileEntry ────────────────────────────────────────────────────

    [Fact]
    public void FileEntry_DefaultDisplayPath_IsNull()
    {
        var entry = new FileEntry("/some/path.cs", "cs", "csharp", 100);
        Assert.Null(entry.DisplayPath);
    }

    [Fact]
    public void FileEntry_WithDisplayPath_SetCorrectly()
    {
        var entry = new FileEntry("/some/path.cs", "cs", "csharp", 100, "custom/path.cs");
        Assert.Equal("custom/path.cs", entry.DisplayPath);
    }

    [Fact]
    public void FileEntry_Properties_Set()
    {
        var entry = new FileEntry("/a/b.cs", "cs", "csharp", 42, "b.cs");
        Assert.Equal("/a/b.cs", entry.Path);
        Assert.Equal("cs", entry.Extension);
        Assert.Equal("csharp", entry.Language);
        Assert.Equal(42, entry.Size);
    }

    // ─── FileContent ──────────────────────────────────────────────────

    [Fact]
    public void FileContent_IsStreaming_WhenContentNull()
    {
        var entry = new FileEntry("/x.cs", "cs", "csharp", 10);
        var content = new FileContent(entry, null, 10);
        Assert.True(content.IsStreaming);
    }

    [Fact]
    public void FileContent_IsNotStreaming_WhenContentSet()
    {
        var entry = new FileEntry("/x.cs", "cs", "csharp", 10);
        var content = new FileContent(entry, "hello", 10);
        Assert.False(content.IsStreaming);
    }

    // ─── RepoInfo ─────────────────────────────────────────────────────

    [Fact]
    public void RepoInfo_Defaults_AreSet()
    {
        var info = new RepoInfo();
        Assert.Equal(string.Empty, info.RootPath);
        Assert.Equal(string.Empty, info.RelativePath);
        Assert.Equal(string.Empty, info.Name);
        Assert.False(info.IsValid);
        Assert.Null(info.Error);
    }

    [Fact]
    public void RepoInfo_WithValues()
    {
        var info = new RepoInfo
        {
            RootPath = "/repos/api",
            RelativePath = "services/api",
            Name = "api",
            IsValid = true
        };
        Assert.Equal("/repos/api", info.RootPath);
        Assert.Equal("services/api", info.RelativePath);
        Assert.Equal("api", info.Name);
        Assert.True(info.IsValid);
    }

    // ─── HistoryEntry ─────────────────────────────────────────────────

    [Fact]
    public void HistoryEntry_DefaultConstructor()
    {
        var entry = new HistoryEntry();
        Assert.Equal(string.Empty, entry.Directory);
        Assert.Empty(entry.Arguments);
        Assert.Equal(default, entry.LastRun);
    }

    [Fact]
    public void HistoryEntry_ParameterizedConstructor()
    {
        var args = new[] { "--ext", "cs" };
        var dt = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var entry = new HistoryEntry("/home/project", args, dt);
        Assert.Equal("/home/project", entry.Directory);
        Assert.Equal(args, entry.Arguments);
        Assert.Equal(dt, entry.LastRun);
    }

    // ─── ValidationResult ─────────────────────────────────────────────

    [Fact]
    public void ValidationResult_IsValid_NoErrors()
    {
        var vr = new ValidationResult { IsValid = true };
        Assert.True(vr.IsValid);
        Assert.Empty(vr.Errors);
        Assert.Empty(vr.Warnings);
    }

    [Fact]
    public void ValidationResult_ToString_Valid()
    {
        var vr = new ValidationResult { IsValid = true };
        var s = vr.ToString();
        Assert.Contains("valid", s);
    }

    [Fact]
    public void ValidationResult_ToString_Invalid_WithErrors()
    {
        var vr = new ValidationResult
        {
            IsValid = false,
            Errors = new[] { "bad thing" }
        };
        var s = vr.ToString();
        Assert.Contains("invalid", s);
        Assert.Contains("bad thing", s);
    }

    [Fact]
    public void ValidationResult_ToString_WithWarnings()
    {
        var vr = new ValidationResult
        {
            IsValid = true,
            Warnings = new[] { "be careful" }
        };
        var s = vr.ToString();
        Assert.Contains("be careful", s);
    }

    // ─── Configuration record defaults ────────────────────────────────

    [Fact]
    public void ClusterConfiguration_Defaults()
    {
        var cfg = new ClusterConfiguration();
        Assert.False(cfg.Enabled);
        Assert.Equal(2, cfg.MaxDepth);
        Assert.Equal("---", cfg.RepoSeparator);
        Assert.True(cfg.IncludeRepoHeader);
        Assert.Equal(0, cfg.MaxParallelRepos);
        Assert.Empty(cfg.SkipDirectories);
        Assert.False(cfg.IncludeRootFiles);
        Assert.False(cfg.FailFast);
    }

    [Fact]
    public void DiscoveryConfiguration_Defaults()
    {
        var cfg = new DiscoveryConfiguration();
        Assert.Equal("auto", cfg.Mode);
        Assert.True(cfg.UseGit);
        Assert.False(cfg.FollowSymlinks);
        Assert.Null(cfg.MaxDepth);
        Assert.Null(cfg.Cluster);
    }

    [Fact]
    public void GcConfiguration_Defaults()
    {
        var cfg = new GcConfiguration();
        Assert.Equal("1.0.0", cfg.Version);
        Assert.NotNull(cfg.Limits);
        Assert.NotNull(cfg.Discovery);
        Assert.NotNull(cfg.Filters);
        Assert.NotNull(cfg.Presets);
        Assert.NotNull(cfg.LanguageMappings);
        Assert.NotNull(cfg.Markdown);
        Assert.NotNull(cfg.Output);
        Assert.NotNull(cfg.Logging);
    }

    [Fact]
    public void LimitsConfiguration_Defaults()
    {
        var cfg = new LimitsConfiguration();
        Assert.Equal("1MB", cfg.MaxFileSize);
        Assert.Equal("10MB", cfg.MaxClipboardSize);
        Assert.Equal("100MB", cfg.MaxMemoryBytes);
        Assert.Equal(100000, cfg.MaxFiles);
        // Verify the Get* methods work
        Assert.Equal(1048576, cfg.GetMaxFileSizeBytes());
        Assert.Equal(10 * 1048576, cfg.GetMaxClipboardSizeBytes());
        Assert.Equal(100L * 1048576, cfg.GetMaxMemoryBytesValue());
    }

    [Fact]
    public void FiltersConfiguration_Defaults()
    {
        var cfg = new FiltersConfiguration();
        Assert.Empty(cfg.SystemIgnoredPatterns);
        Assert.Empty(cfg.AdditionalExtensions);
    }

    [Fact]
    public void MarkdownConfiguration_Defaults()
    {
        var cfg = new MarkdownConfiguration();
        Assert.Equal("```", cfg.Fence);
        Assert.Equal("_Project Structure:_", cfg.ProjectStructureHeader);
        Assert.Equal("{path}", cfg.FileHeaderTemplate);
        Assert.Equal("extension", cfg.LanguageDetection);
    }

    [Fact]
    public void OutputConfiguration_Defaults()
    {
        var cfg = new OutputConfiguration();
        Assert.Equal("markdown", cfg.DefaultFormat);
        Assert.True(cfg.IncludeStats);
        Assert.True(cfg.SortByPath);
    }

    [Fact]
    public void LoggingConfiguration_Defaults()
    {
        var cfg = new LoggingConfiguration();
        Assert.Equal("normal", cfg.Level);
        Assert.False(cfg.IncludeTimestamps);
    }

    // ─── LoggerExtensions ─────────────────────────────────────────────

    [Fact]
    public void LoggerExtensions_AllMethods_Work()
    {
        var logEntries = new List<(LogLevel Level, string Message)>();
        var mock = new TestLogger((level, msg) => logEntries.Add((level, msg)));

        mock.Info("info");
        mock.Success("ok");
        mock.Error("err");
        mock.Warning("warn");
        mock.Debug("dbg");

        Assert.Equal(5, logEntries.Count);
        Assert.Equal(LogLevel.Info, logEntries[0].Level);
        Assert.Equal("info", logEntries[0].Message);
        Assert.Equal(LogLevel.Success, logEntries[1].Level);
        Assert.Equal("ok", logEntries[1].Message);
        Assert.Equal(LogLevel.Error, logEntries[2].Level);
        Assert.Equal("err", logEntries[2].Message);
        Assert.Equal(LogLevel.Warning, logEntries[3].Level);
        Assert.Equal("warn", logEntries[3].Message);
        Assert.Equal(LogLevel.Debug, logEntries[4].Level);
        Assert.Equal("dbg", logEntries[4].Message);
    }

    private sealed class TestLogger : ILogger
    {
        private readonly Action<LogLevel, string> _onLog;
        public TestLogger(Action<LogLevel, string> onLog) => _onLog = onLog;
        public void Log(LogLevel level, string message, Exception? ex = null) => _onLog(level, message);
    }

    // ─── GcJsonSerializerContext ──────────────────────────────────────

    [Fact]
    public void GcJsonSerializerContext_Serialize_Deserialize_Roundtrip()
    {
        var original = new GcConfiguration
        {
            Version = "2.0.0",
            Limits = new LimitsConfiguration { MaxFileSize = "5MB" },
            Discovery = new DiscoveryConfiguration { Mode = "git" },
            Filters = new FiltersConfiguration { AdditionalExtensions = new[] { "razor" } },
            Markdown = new MarkdownConfiguration { Fence = "~~" },
            Output = new OutputConfiguration { IncludeStats = false },
            Logging = new LoggingConfiguration { Level = "debug" }
        };

        var json = JsonSerializer.Serialize(original, GcJsonSerializerContext.Default.GcConfiguration);
        var deserialized = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.GcConfiguration);

        Assert.NotNull(deserialized);
        Assert.Equal("2.0.0", deserialized.Version);
        Assert.Equal("5MB", deserialized.Limits.MaxFileSize);
        Assert.Equal("git", deserialized.Discovery.Mode);
        Assert.Contains("razor", deserialized.Filters.AdditionalExtensions);
        Assert.Equal("~~", deserialized.Markdown.Fence);
        Assert.False(deserialized.Output.IncludeStats);
        Assert.Equal("debug", deserialized.Logging.Level);
    }

    // ─── CliArguments ────────────────────────────────────────────────

    [Fact]
    public void CliArguments_DefaultValues()
    {
        var args = new CliArguments();
        Assert.Empty(args.Paths);
        Assert.Empty(args.Extensions);
        Assert.Empty(args.Excludes);
        Assert.Empty(args.Presets);
        Assert.Empty(args.ExcludeLineIfStart);
        Assert.Equal(string.Empty, args.OutputFile);
        Assert.False(args.ShowHelp);
        Assert.False(args.ShowVersion);
        Assert.False(args.RunTests);
        Assert.False(args.RunRealBenchmark);
        Assert.Equal(0, args.MaxMemoryBytes);
        Assert.False(args.Verbose);
        Assert.False(args.Debug);
        Assert.False(args.InitConfig);
        Assert.False(args.ValidateConfig);
        Assert.False(args.DumpConfig);
        Assert.False(args.Append);
        Assert.False(args.NoSort);
        Assert.False(args.Force);
        Assert.Null(args.Depth);
        Assert.False(args.ShowHistory);
        Assert.Null(args.HistoryIndex);
        Assert.Null(args.Configuration);
        Assert.False(args.Cluster);
        Assert.Equal(string.Empty, args.ClusterDir);
        Assert.Null(args.ClusterDepth);
    }
}
