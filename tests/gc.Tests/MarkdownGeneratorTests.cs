using System.Text;
using gc.Application.Services;
using gc.Domain.Constants;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.IO;
using gc.Infrastructure.Logging;

namespace gc.Tests;

public class MarkdownGeneratorTests
{
    private readonly GcConfiguration _config = BuiltInPresets.GetDefaultConfiguration();
    private readonly ConsoleLogger _logger = new();
    private readonly FileReader _reader;

    public MarkdownGeneratorTests()
    {
        _reader = new FileReader(_logger);
    }

    [Fact]
    public async Task ExcludeLineIfStart_InMemory_FiltersCorrectly()
    {
        var generator = new MarkdownGenerator(_logger);
        var entry = new FileEntry("", "test.cs", "cs", "cs", 100);
        var content = "using System;\n// This is a comment\npublic class Test {\n  // Another comment\n}\n\n";

        var fileContents = new List<FileContent> { new(entry, content, content.Length) };
        using var output = new MemoryStream();

        var excludes = new[] { "//", "\n" };

        var result = await generator.GenerateMarkdownStreamingAsync(
            fileContents,
            output,
            _config,
            excludes, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var str = Encoding.UTF8.GetString(output.ToArray());

        Assert.Contains("using System;", str);
        Assert.Contains("public class Test {", str);
        Assert.Contains("}", str);

        // Excluded
        Assert.DoesNotContain("// This is a comment", str);
        Assert.DoesNotContain("// Another comment", str);
    }

    [Fact]
    public async Task GenerateMarkdownStreamingAsync_DirectoryEntry_EmitsFileNotFound()
    {
        // A git submodule / gitlink appears in `git ls-files` as a directory path. It cannot be read
        // as a file; the output must say "File not found" (byte-identical to historical behavior), not
        // leak the OS "Is a directory" error from open()+read() on a directory.
        var generator = new MarkdownGenerator(_logger);
        var tempDir = Directory.CreateTempSubdirectory("gc-dirtest-");
        try
        {
            var entry = new FileEntry(tempDir.Parent!.FullName, tempDir.Name, "", "", 0);
            var fileContents = new List<FileContent> { new(entry, null, 0) };
            using var output = new MemoryStream();

            var result = await generator.GenerateMarkdownStreamingAsync(fileContents, output, _config);

            Assert.True(result.IsSuccess);
            var str = Encoding.UTF8.GetString(output.ToArray());
            Assert.Contains($"[Error reading file: File not found: {tempDir.Name}]", str);
            Assert.DoesNotContain("Is a directory", str);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public async Task ExcludeLineIfStart_Streaming_FiltersCorrectly()
    {
        var generator = new MarkdownGenerator(_logger);
        var tempFile = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(tempFile,
                "using System;\r\n// A comment\npublic class Test {\n  // Another comment\r\n}");
            var entry = new FileEntry("", tempFile, "cs", "cs", 100);

            // Content is null so it falls back to streaming
            var fileContents = new List<FileContent> { new(entry, null, 100) };
            using var output = new MemoryStream();

            var excludes = new[] { "//", "\n" };

            var result = await generator.GenerateMarkdownStreamingAsync(
                fileContents,
                output,
                _config,
                excludes, null, CancellationToken.None);

            Assert.True(result.IsSuccess);
            var str = Encoding.UTF8.GetString(output.ToArray());

            Assert.Contains("using System;", str);
            Assert.Contains("public class Test {", str);
            Assert.Contains("}", str);

            // Excluded
            Assert.DoesNotContain("// A comment", str);
            Assert.DoesNotContain("// Another comment", str);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ProcessLineSequence_HandlesEmptyLine_WithNewlineExclude()
    {
        var generator = new MarkdownGenerator(_logger);
        var entry = new FileEntry("", "test.cs", "cs", "cs", 100);
        var content = "using System;\n\npublic class Test {}";

        var fileContents = new List<FileContent> { new(entry, content, content.Length) };
        using var output = new MemoryStream();

        var excludes = new[] { "\n" };

        var result = await generator.GenerateMarkdownStreamingAsync(
            fileContents,
            output,
            _config,
            excludes, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var str = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("using System;", str);
        Assert.Contains("public class Test {}", str);
        Assert.DoesNotContain("using System;\n\npublic", str); // Ensure no double newline exists in content area
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST-004 — Streaming limit tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateMarkdownStreamingAsync_EnforcesMemoryLimit_InMemory()
    {
        var generator = new MarkdownGenerator(_logger);
        var entry = new FileEntry("", "test.cs", "cs", "cs", 5000);
        var content = new string('A', 5000);

        var config = _config with { Limits = _config.Limits with { MaxMemoryBytes = "1KB" } };

        var fileContents = new List<FileContent> { new(entry, content, content.Length) };
        using var output = new MemoryStream();

        var result = await generator.GenerateMarkdownStreamingAsync(
            fileContents,
            output,
            config,
            null, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("exceed maximum output limit", result.Error);
        Assert.Equal(0, output.Length); // Ensure no partial output is written before failure
    }

    [Fact]
    public async Task GenerateMarkdownStreamingAsync_EnforcesMemoryLimit_Streaming()
    {
        var generator = new MarkdownGenerator(_logger);
        var tempFile = Path.GetTempFileName();

        try
        {
            var content = new string('A', 5000);
            await File.WriteAllTextAsync(tempFile, content);
            var entry = new FileEntry("", tempFile, "cs", "cs", 5000);

            var config = _config with { Limits = _config.Limits with { MaxMemoryBytes = "1KB" } };

            var fileContents = new List<FileContent> { new(entry, null, 5000) };
            using var output = new MemoryStream();

            var result = await generator.GenerateMarkdownStreamingAsync(
                fileContents,
                output,
                config,
                null, null, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains("exceed maximum output limit", result.Error);
            Assert.Equal(0, output.Length); // Ensure no partial output is written before failure
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}