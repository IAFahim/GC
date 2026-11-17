using System.Text;
using gc.Application.Services;
using gc.Domain.Constants;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.IO;
using gc.Infrastructure.Logging;

namespace gc.Tests;

/// <summary>Regression tests for issue #14: fence length and the file-block layout shared by every path.</summary>
public class FileBlockLayoutTests : IDisposable
{
    private readonly GcConfiguration _config = BuiltInPresets.GetDefaultConfiguration();
    private readonly ConsoleLogger _logger = new();
    private readonly string _root = Directory.CreateTempSubdirectory("gc-layout").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private async Task<string> RenderOnDiskAsync(string name, string content, string[]? dropLines = null,
        bool brain = false)
    {
        File.WriteAllText(Path.Combine(_root, name), content);
        var entry = new FileEntry(_root, name, Path.GetExtension(name).TrimStart('.'), "cs", content.Length);
        using var ms = new MemoryStream();
        var result = await new MarkdownGenerator(_logger).GenerateMarkdownStreamingAsync(
            [new FileContent(entry, null, content.Length)], ms, _config, dropLines,
            brain ? new BrainCrusher() : null);
        Assert.True(result.IsSuccess, result.Error);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private async Task<string> RenderInMemoryAsync(string name, string content)
    {
        var entry = new FileEntry(_root, name, "cs", "cs", content.Length);
        using var ms = new MemoryStream();
        var result = await new MarkdownGenerator(_logger).GenerateMarkdownStreamingAsync(
            [new FileContent(entry, content, content.Length)], ms, _config);
        Assert.True(result.IsSuccess, result.Error);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string Block(string output)
    {
        var start = output.IndexOf("## File:", StringComparison.Ordinal);
        var end = output.IndexOf("_Project Structure:_", StringComparison.Ordinal);
        return output[start..end];
    }

    [Fact]
    public async Task Fence_WithoutBackticks_IsThreeBackticks()
    {
        var output = await RenderOnDiskAsync("a.cs", "class A {}\n");
        Assert.Contains("```cs", output);
        Assert.DoesNotContain("````", Block(output));
    }

    [Fact]
    public async Task Fence_ContentWithThreeBackticks_UsesFour()
    {
        var output = await RenderOnDiskAsync("a.cs", "// ```\nclass A {}\n");
        Assert.Contains("````cs", output);
        Assert.DoesNotContain("`````", Block(output));
    }

    [Fact]
    public async Task InMemoryAndStreamedPaths_ProduceIdenticalBlocks()
    {
        const string text = "class A {}\n";
        Assert.Equal(Block(await RenderInMemoryAsync("a.cs", text)), Block(await RenderOnDiskAsync("a.cs", text)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DropLinesAndBrain_DoNotAddBlankLineBeforeClosingFence(bool brain)
    {
        var output = await RenderOnDiskAsync("a.cs", "// note\nclass A {}\n",
            brain ? null : ["//"], brain);
        var nl = Environment.NewLine;
        Assert.DoesNotContain(nl + nl + "```" + nl, Block(output));
        Assert.Contains("}" + nl + "```" + nl, Block(output));
    }

    [Fact]
    public async Task ContentFilterSkip_WritesNoPlaceholderLine()
    {
        File.WriteAllText(Path.Combine(_root, "a.cs"), "class A {}\n");
        var entry = new FileEntry(_root, "a.cs", "cs", "cs", 11);
        var compiled = new ContentFilter(_logger).CompilePatterns(["class"], []);
        using var ms = new MemoryStream();
        var result = await new MarkdownGenerator(_logger).GenerateMarkdownStreamingAsync(
            [new FileContent(entry, null, 11)], ms, _config, compiled);
        Assert.True(result.IsSuccess, result.Error);
        Assert.DoesNotContain("Skipped by content filter", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task FileReader_ResolvesRelativeEntryAgainstRoot()
    {
        File.WriteAllText(Path.Combine(_root, "r.cs"), "class R {}");
        var entry = new FileEntry(_root, "r.cs", "cs", "cs", 10);
        var read = await new FileReader(_logger).ReadAsync(entry);
        Assert.True(read.IsSuccess, read.Error);
        Assert.Equal("class R {}", read.Value.Content);
    }

    [Fact]
    public void LlmHeader_BrainOnly_DoesNotMentionSqzMarkers()
    {
        var brainOnly = gc.Application.UseCases.GenerateContextUseCase.BuildLlmContextHeader(true, false);
        Assert.DoesNotContain("sqz", brainOnly);
        Assert.Contains("GC_DICT", brainOnly);
        Assert.Contains("sqz", gc.Application.UseCases.GenerateContextUseCase.BuildLlmContextHeader(false, true));
    }
}
