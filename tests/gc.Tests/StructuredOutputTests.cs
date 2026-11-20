using System.Text.Json;
using gc.Application.Services;
using gc.Application.UseCases;
using gc.CLI.Services;
using gc.Domain.Common;
using gc.Domain.Constants;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Discovery;

namespace gc.Tests;

/// <summary>output.defaultFormat / --format: markdown, plain and json (issue #14).</summary>
public sealed class StructuredOutputTests : IDisposable
{
    private readonly NullLogger _logger = new();
    private readonly string _root = Directory.CreateTempSubdirectory("gc-format").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private sealed class NullLogger : ILogger
    {
        public void Log(LogLevel level, string message, Exception? ex = null)
        {
        }
    }

    private sealed class NoClipboard : IClipboardService
    {
        public Task<Result> CopyToClipboardAsync(Stream stream, CancellationToken ct) => Task.FromResult(Result.Success());

        public Task<Result> CopyToClipboardAsync(Stream stream, LimitsConfiguration limits, bool append,
            CancellationToken ct) => Task.FromResult(Result.Success());

        public Task<Result> CopyToClipboardAsync(string content, CancellationToken ct) => Task.FromResult(Result.Success());

        public Task<Result> CopyToClipboardAsync(string content, LimitsConfiguration limits, bool append,
            CancellationToken ct) => Task.FromResult(Result.Success());
    }

    private async Task<(Result Result, string Output)> RunAsync(string format, RunOptions? options = null)
    {
        File.WriteAllText(Path.Combine(_root, "a.cs"), "class A { }\n");
        File.WriteAllText(Path.Combine(_root, "b.md"), "# Title with ``` fence\n\ntext\n");
        var output = Path.Combine(_root, "result.out");
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with
        {
            Output = config.Output with { DefaultFormat = format },
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };
        var useCase = new GenerateContextUseCase(new FileDiscovery(_logger), new FileFilter(_logger),
            new ContentFilter(_logger), new MarkdownGenerator(_logger), new NoClipboard(), _logger);

        var result = await useCase.ExecuteAsync(_root, config,
            (options ?? new RunOptions()) with { OutputFile = output, Excludes = ["result.out"] });
        return (result, File.Exists(output) ? File.ReadAllText(output) : "");
    }

    [Fact]
    public async Task Json_HoldsEachFilesExactContent()
    {
        var (result, text) = await RunAsync("json");

        Assert.True(result.IsSuccess, result.Error);
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(2, doc.RootElement.GetProperty("fileCount").GetInt32());
        var files = doc.RootElement.GetProperty("files").EnumerateArray()
            .ToDictionary(f => f.GetProperty("path").GetString()!, f => f.GetProperty("content").GetString()!);
        Assert.Equal("class A { }", files["a.cs"].Trim());
        Assert.Contains("# Title with ``` fence", files["b.md"]);
        Assert.DoesNotContain("## File:", text);
    }

    [Fact]
    public async Task Plain_UsesPathMarkersAndNoFences()
    {
        var (result, text) = await RunAsync("plain");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("==> a.cs <==", text);
        Assert.Contains("==> b.md <==", text);
        Assert.Contains("class A { }", text);
        Assert.DoesNotContain("_Project Structure:_", text);
        Assert.DoesNotContain("## File:", text);
        Assert.DoesNotContain("```cs", text);
    }

    [Fact]
    public async Task Markdown_IsUnchanged()
    {
        var (result, text) = await RunAsync("markdown");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("## File: a.cs", text);
        Assert.Contains("_Project Structure:_", text);
    }

    [Fact]
    public async Task UnknownFormat_FallsBackToMarkdown()
    {
        var (result, text) = await RunAsync("xml");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("## File: a.cs", text);
    }

    [Fact]
    public async Task Json_WorksWithBrainMode()
    {
        File.WriteAllText(Path.Combine(_root, "commented.cs"), "// remove comment\nclass Minified { }\n");
        var (result, text) = await RunAsync("json", new RunOptions { Brain = true });

        Assert.True(result.IsSuccess, result.Error);
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(3, doc.RootElement.GetProperty("fileCount").GetInt32());
        var files = doc.RootElement.GetProperty("files").EnumerateArray()
            .ToDictionary(f => f.GetProperty("path").GetString()!, f => f.GetProperty("content").GetString()!);
        Assert.DoesNotContain("remove comment", files["commented.cs"]);
        Assert.Contains("class Minified", files["commented.cs"]);
    }

    [Fact]
    public async Task Plain_WorksWithBrainMode()
    {
        File.WriteAllText(Path.Combine(_root, "commented.cs"), "// remove comment\nclass Minified { }\n");
        var (result, text) = await RunAsync("plain", new RunOptions { Brain = true });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("==> commented.cs <==", text);
        Assert.DoesNotContain("remove comment", text);
        Assert.DoesNotContain("GC_DICT", text);
        Assert.Contains("class Minified", text);
    }

    [Fact]
    public async Task Json_RejectsCompress()
    {
        var (result, _) = await RunAsync("json", new RunOptions { Compress = true });

        Assert.False(result.IsSuccess);
        Assert.Contains("--compress", result.Error);
    }

    [Fact]
    public async Task SplitOutput_IsMarkdownOnly()
    {
        var (result, _) = await RunAsync("json", new RunOptions { Parts = new ChunkOutputOptions(50000, Path.Combine(_root, "parts")) });

        Assert.False(result.IsSuccess);
        Assert.Contains("Markdown only", result.Error);
    }

    [Theory]
    [InlineData("plain", OutputFormat.Plain)]
    [InlineData("JSON", OutputFormat.Json)]
    [InlineData("markdown", OutputFormat.Markdown)]
    [InlineData(null, OutputFormat.Markdown)]
    [InlineData("nope", OutputFormat.Markdown)]
    public void ParseFormat_MapsNames(string? value, OutputFormat expected)
    {
        Assert.Equal(expected, GenerateContextUseCase.ParseFormat(value));
    }

    [Fact]
    public void FormatFlag_IsParsed()
    {
        var parsed = new CliParser().Parse(["--format", "json"], new GcConfiguration());

        Assert.True(parsed.IsSuccess, parsed.Error);
        Assert.Equal("json", parsed.Value!.Format);
    }

    [Theory]
    [InlineData("markdown", true)]
    [InlineData("plain", true)]
    [InlineData("json", true)]
    [InlineData("xml", false)]
    public void Validator_AcceptsTheThreeFormats(string format, bool valid)
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Output = config.Output with { DefaultFormat = format } };

        var result = new gc.Application.Validators.ConfigurationValidator().Validate(config).Value!;

        Assert.Equal(valid, !result.Warnings.Any(w => w.Contains("DefaultFormat")));
    }
}
