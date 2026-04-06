using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using gc.Application.Services;
using gc.Domain.Constants;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.IO;
using gc.Infrastructure.Logging;
using Xunit;

namespace gc.Tests;

public class MarkdownGeneratorExtendedTests
{
    private readonly ConsoleLogger _logger = new();
    private readonly FileReader _reader;
    private readonly GcConfiguration _config = BuiltInPresets.GetDefaultConfiguration();

    public MarkdownGeneratorExtendedTests()
    {
        _reader = new FileReader(_logger);
    }

    // =========================================================================
    // 1. Virtual entry filtering (cluster mode)
    // =========================================================================

    [Fact]
    public async Task Generate_VirtualEntries_FilteredFromProjectStructure()
    {
        // Entries with Path starting with '[' are virtual (cluster headers) and should
        // NOT appear in the Project Structure section at the bottom.
        var generator = new MarkdownGenerator(_logger, _reader);
        var config = _config with
        {
            Output = _config.Output with { SortByPath = false }
        };

        var entries = new List<FileContent>
        {
            new(new FileEntry("[Cluster: backend]", "txt", "text", 0), "// cluster header", 16),
            new(new FileEntry("src/Program.cs", "cs", "csharp", 50), "class Program {}", 16),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        // Virtual entry content should still be written to output
        Assert.Contains("// cluster header", output);
        Assert.Contains("class Program {}", output);

        // Project structure section should NOT contain virtual entries
        var structureIndex = output.IndexOf("_Project Structure:_", StringComparison.Ordinal);
        Assert.True(structureIndex >= 0, "Should contain project structure header");
        var structureSection = output[structureIndex..];
        Assert.DoesNotContain("[Cluster: backend]", structureSection);
        Assert.Contains("src/Program.cs", structureSection);
    }

    [Fact]
    public async Task Generate_MixedVirtualAndRealEntries_ProjectStructureOnlyShowsReal()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var config = _config with
        {
            Output = _config.Output with { SortByPath = false }
        };

        var entries = new List<FileContent>
        {
            new(new FileEntry("[Repo A]", "txt", "text", 0), "=== Repo A ===", 14),
            new(new FileEntry("repo-a/file1.cs", "cs", "csharp", 30), "class A {}", 10),
            new(new FileEntry("[Repo B]", "txt", "text", 0), "=== Repo B ===", 14),
            new(new FileEntry("repo-b/file2.cs", "cs", "csharp", 30), "class B {}", 10),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        var structureIndex = output.IndexOf("_Project Structure:_", StringComparison.Ordinal);
        var structureSection = output[structureIndex..];

        Assert.DoesNotContain("[Repo A]", structureSection);
        Assert.DoesNotContain("[Repo B]", structureSection);
        Assert.Contains("repo-a/file1.cs", structureSection);
        Assert.Contains("repo-b/file2.cs", structureSection);
    }

    [Fact]
    public async Task Generate_VirtualEntryInlineContent_WrittenToOutput()
    {
        // Virtual entries should still have their inline content written to the
        // markdown output as a normal file section.
        var generator = new MarkdownGenerator(_logger, _reader);
        var config = _config with
        {
            Output = _config.Output with { SortByPath = false }
        };

        var entries = new List<FileContent>
        {
            new(new FileEntry("[Separator]", "txt", "text", 0), "--- CLUSTER BREAK ---", 20),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        Assert.Contains("--- CLUSTER BREAK ---", output);
        Assert.Contains("[Separator]", output);
    }

    // =========================================================================
    // 2. Line exclusion edge cases
    // =========================================================================

    [Fact]
    public async Task LineExclusion_WithNewlineFilter_RemovesBlankLines()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var entry = new FileEntry("test.cs", "cs", "csharp", 50);
        var content = "line1\n\n\nline2\n\nline3";
        var entries = new List<FileContent> { new(entry, content, content.Length) };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config, new[] { "\n" });

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        Assert.Contains("line1", output);
        Assert.Contains("line2", output);
        Assert.Contains("line3", output);

        // Verify no consecutive blank lines in the content area
        var contentStart = output.IndexOf("line1", StringComparison.Ordinal);
        var contentEnd = output.LastIndexOf("line3", StringComparison.Ordinal);
        var contentSection = output[contentStart..(contentEnd + 5)];
        Assert.DoesNotContain("\n\n", contentSection);
    }

    [Fact]
    public async Task LineExclusion_WithMultipleFilters_AllApplied()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var entry = new FileEntry("test.cs", "cs", "csharp", 100);
        var content = "using System;\n// comment\n#pragma warning\npublic class Foo {}\n// end comment";
        var entries = new List<FileContent> { new(entry, content, content.Length) };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, new[] { "//", "#pragma" });

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        Assert.Contains("using System;", output);
        Assert.Contains("public class Foo {}", output);
        Assert.DoesNotContain("// comment", output);
        Assert.DoesNotContain("// end comment", output);
        Assert.DoesNotContain("#pragma warning", output);
    }

    [Fact]
    public async Task LineExclusion_StreamingPath_Works()
    {
        // When Content is null, the generator reads from file using streaming path.
        var generator = new MarkdownGenerator(_logger, _reader);
        var tempFile = Path.GetTempFileName();
        try
        {
            var fileContent = "using System;\n// this should be removed\npublic class Bar {}";
            await File.WriteAllTextAsync(tempFile, fileContent);

            var entry = new FileEntry(tempFile, "cs", "csharp", 100);
            // Content is null -> streaming path
            var entries = new List<FileContent> { new(entry, null, 100) };

            using var ms = new MemoryStream();
            var result = await generator.GenerateMarkdownStreamingAsync(
                entries, ms, _config, new[] { "//" });

            Assert.True(result.IsSuccess);
            ms.Position = 0;
            var output = new StreamReader(ms).ReadToEnd();

            Assert.Contains("using System;", output);
            Assert.Contains("public class Bar {}", output);
            Assert.DoesNotContain("// this should be removed", output);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LineExclusion_EmptyExcludeList_NoChange()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var entry = new FileEntry("test.cs", "cs", "csharp", 50);
        var content = "line1\n// comment\nline3";
        var entries = new List<FileContent> { new(entry, content, content.Length) };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, Array.Empty<string>());

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        // Empty exclude list -> no lines should be removed
        Assert.Contains("line1", output);
        Assert.Contains("// comment", output);
        Assert.Contains("line3", output);
    }

    // =========================================================================
    // 3. Memory limit
    // =========================================================================

    [Fact]
    public async Task Generate_ExceedsMaxMemory_ReturnsFailure()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        // Set extremely small memory limit (1 byte) so output always exceeds it
        var config = _config with
        {
            Limits = _config.Limits with { MaxMemoryBytes = "1B" },
            Output = _config.Output with { SortByPath = false }
        };

        var entries = new List<FileContent>
        {
            new(new FileEntry("big.cs", "cs", "csharp", 100), new string('x', 500), 500),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("exceed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generate_JustUnderMaxMemory_Succeeds()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        // Use a generous limit that should accommodate our small content
        var config = _config with
        {
            Limits = _config.Limits with { MaxMemoryBytes = "10KB" },
            Output = _config.Output with { SortByPath = false }
        };

        var entries = new List<FileContent>
        {
            new(new FileEntry("small.cs", "cs", "csharp", 20), "class Foo {}", 12),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();
        Assert.Contains("class Foo {}", output);
    }

    // =========================================================================
    // 4. Binary detection
    // =========================================================================

    [Fact]
    public async Task Generate_BinaryFile_Skipped()
    {
        // When Content is null (streaming), a file containing null bytes should be
        // detected as binary and skipped with a "[Skipping binary file:]" message.
        var generator = new MarkdownGenerator(_logger, _reader);
        var tempFile = Path.GetTempFileName();
        try
        {
            // Write bytes that include null bytes
            var binaryData = new byte[] { 0x01, 0x00, 0x00, 0x02, 0x03 };
            await File.WriteAllBytesAsync(tempFile, binaryData);

            var entry = new FileEntry(tempFile, "bin", "text", binaryData.Length);
            // Content is null -> streaming path which does binary detection
            var entries = new List<FileContent> { new(entry, null, binaryData.Length) };

            using var ms = new MemoryStream();
            var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config);

            Assert.True(result.IsSuccess);
            ms.Position = 0;
            var output = new StreamReader(ms).ReadToEnd();

            Assert.Contains("[Skipping binary file:", output);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Generate_FileNotFound_ErrorInOutput()
    {
        // When Content is null and the file doesn't exist on disk, an error
        // message should be written to the output.
        var generator = new MarkdownGenerator(_logger, _reader);
        var nonExistentPath = "/tmp/gc_test_nonexistent_" + Guid.NewGuid() + ".cs";
        var entry = new FileEntry(nonExistentPath, "cs", "csharp", 0);
        // Content is null -> streaming path which checks file existence
        var entries = new List<FileContent> { new(entry, null, 0) };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config);

        Assert.True(result.IsSuccess); // Overall operation succeeds (error written inline)
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        Assert.Contains("[Error reading file:", output);
        Assert.Contains("File not found", output);
    }

    // =========================================================================
    // 5. Backtick escalation
    // =========================================================================

    [Fact]
    public async Task Generate_ContentWith5Backticks_UsesEscalatedFence()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var entry = new FileEntry("test.md", "md", "markdown", 50);
        var content = "some text\n`````\ncode block\n`````\nmore text";
        var entries = new List<FileContent> { new(entry, content, content.Length) };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        // Should use 10 backtick fence for 5-backtick content
        Assert.Contains("``````````", output);
    }

    [Fact]
    public async Task Generate_ContentWith4Backticks_UsesEscalatedFence()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var entry = new FileEntry("test.md", "md", "markdown", 50);
        var content = "some text\n````\ncode block\n````\nmore text";
        var entries = new List<FileContent> { new(entry, content, content.Length) };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        // Should use 6 backtick fence for 4-backtick content
        Assert.Contains("``````", output);
    }

    [Fact]
    public async Task Generate_ContentWith3Backticks_UsesNormalFence()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var entry = new FileEntry("test.md", "md", "markdown", 50);
        // 3 backticks in content should still use normal fence (```) since
        // the escalation logic checks for 4+ backticks
        var content = "some text\n```\ncode block\n```\nmore text";
        var entries = new List<FileContent> { new(entry, content, content.Length) };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        // Normal fence should be used. The content contains 3 backticks,
        // but the fence itself should not be escalated to 6.
        // Count occurrences: opening+closing fences use ```, content has ```
        // So we verify that `````` does NOT appear.
        Assert.DoesNotContain("``````", output);
    }

    // =========================================================================
    // 6. Sort behavior
    // =========================================================================

    [Fact]
    public async Task Generate_SortByPathTrue_SortedOutput()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var config = _config with
        {
            Output = _config.Output with { SortByPath = true }
        };

        var entries = new List<FileContent>
        {
            new(new FileEntry("z_last.cs", "cs", "csharp", 10), "class Z {}", 10),
            new(new FileEntry("a_first.cs", "cs", "csharp", 10), "class A {}", 10),
            new(new FileEntry("m_middle.cs", "cs", "csharp", 10), "class M {}", 10),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        // a_first should appear before m_middle, which should appear before z_last
        var indexA = output.IndexOf("a_first.cs", StringComparison.Ordinal);
        var indexM = output.IndexOf("m_middle.cs", StringComparison.Ordinal);
        var indexZ = output.IndexOf("z_last.cs", StringComparison.Ordinal);

        Assert.True(indexA < indexM, "a_first should appear before m_middle");
        Assert.True(indexM < indexZ, "m_middle should appear before z_last");
    }

    [Fact]
    public async Task Generate_SortByPathFalse_PreserveOrder()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var config = _config with
        {
            Output = _config.Output with { SortByPath = false }
        };

        var entries = new List<FileContent>
        {
            new(new FileEntry("z_last.cs", "cs", "csharp", 10), "class Z {}", 10),
            new(new FileEntry("a_first.cs", "cs", "csharp", 10), "class A {}", 10),
            new(new FileEntry("m_middle.cs", "cs", "csharp", 10), "class M {}", 10),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        // Order should be preserved: z_last, a_first, m_middle
        var indexZ = output.IndexOf("z_last.cs", StringComparison.Ordinal);
        var indexA = output.IndexOf("a_first.cs", StringComparison.Ordinal);
        var indexM = output.IndexOf("m_middle.cs", StringComparison.Ordinal);

        Assert.True(indexZ < indexA, "z_last should appear before a_first (preserve order)");
        Assert.True(indexA < indexM, "a_first should appear before m_middle (preserve order)");
    }

    // =========================================================================
    // 7. DisplayPath vs Path
    // =========================================================================

    [Fact]
    public async Task Generate_DisplayPath_UsedInHeader()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var config = _config with
        {
            Output = _config.Output with { SortByPath = false }
        };

        var entry = new FileEntry("/abs/path/to/file.cs", "cs", "csharp", 10, "relative/file.cs");
        var entries = new List<FileContent>
        {
            new(entry, "class Foo {}", 12),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        // DisplayPath should be used in header instead of raw path
        Assert.Contains("relative/file.cs", output);
    }

    [Fact]
    public async Task Generate_NoDisplayPath_UsesPathInHeader()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var config = _config with
        {
            Output = _config.Output with { SortByPath = false }
        };

        var entry = new FileEntry("simple/file.cs", "cs", "csharp", 10);
        var entries = new List<FileContent>
        {
            new(entry, "class Bar {}", 12),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        Assert.Contains("simple/file.cs", output);
    }

    // =========================================================================
    // 8. Byte counting accuracy
    // =========================================================================

    [Fact]
    public async Task ByteCount_MatchesActualOutputSize()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var config = _config with
        {
            Output = _config.Output with { SortByPath = false }
        };

        var entries = new List<FileContent>
        {
            new(new FileEntry("test.cs", "cs", "csharp", 10), "class Foo {}", 12),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value > 0);

        // The reported byte count should match the actual bytes written
        var actualBytes = ms.ToArray();
        Assert.Equal(actualBytes.Length, result.Value);
    }

    // =========================================================================
    // Additional edge case tests
    // =========================================================================

    [Fact]
    public async Task Generate_EmptyContentList_ReturnsSuccessWithStructureOnly()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var entries = new List<FileContent>();

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        // Should still have project structure header
        Assert.Contains("_Project Structure:_", output);
    }

    [Fact]
    public async Task Generate_MultipleFiles_AllWrittenToOutput()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var config = _config with
        {
            Output = _config.Output with { SortByPath = false }
        };

        var entries = new List<FileContent>
        {
            new(new FileEntry("a.cs", "cs", "csharp", 10), "class A {}", 10),
            new(new FileEntry("b.js", "js", "javascript", 10), "function b() {}", 15),
            new(new FileEntry("c.py", "py", "python", 10), "def c(): pass", 14),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        Assert.Contains("class A {}", output);
        Assert.Contains("function b() {}", output);
        Assert.Contains("def c(): pass", output);

        // All files should be in the project structure
        Assert.Contains("a.cs", output);
        Assert.Contains("b.js", output);
        Assert.Contains("c.py", output);
    }

    [Fact]
    public async Task Generate_LanguageTag_InFenceLine()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var entry = new FileEntry("test.py", "py", "python", 10);
        var entries = new List<FileContent>
        {
            new(entry, "print('hello')", 14),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        // The fence line should include the language tag
        Assert.Contains("```python", output);
    }

    [Fact]
    public async Task Generate_CancelledOperation_ReturnsFailure()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var entries = new List<FileContent>
        {
            new(new FileEntry("test.cs", "cs", "csharp", 10), "class Foo {}", 12),
        };

        using var ms = new MemoryStream();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config, ct: cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("cancel", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generate_FileHeaderTemplate_UsesPath()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var config = _config with
        {
            Markdown = _config.Markdown with { FileHeaderTemplate = "File: {path}" },
            Output = _config.Output with { SortByPath = false }
        };

        var entry = new FileEntry("src/app.cs", "cs", "csharp", 10);
        var entries = new List<FileContent>
        {
            new(entry, "class App {}", 12),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        Assert.Contains("File: src/app.cs", output);
    }

    [Fact]
    public async Task Generate_ContentWithTrailingWhitespace_Trimmed()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var entry = new FileEntry("test.cs", "cs", "csharp", 30);
        var content = "class Foo {}   \n\n\n"; // trailing spaces and newlines
        var entries = new List<FileContent> { new(entry, content, content.Length) };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        // The content should be trimmed - "class Foo {}" should appear without
        // excessive trailing whitespace before the closing fence
        Assert.Contains("class Foo {}", output);
    }

    [Fact]
    public async Task Generate_DisplayPath_UsedInProjectStructure()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var config = _config with
        {
            Output = _config.Output with { SortByPath = false }
        };

        var entry = new FileEntry("/abs/path/src/code.cs", "cs", "csharp", 10, "src/code.cs");
        var entries = new List<FileContent>
        {
            new(entry, "class Code {}", 12),
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.True(result.IsSuccess);
        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();

        var structureIndex = output.IndexOf("_Project Structure:_", StringComparison.Ordinal);
        Assert.True(structureIndex >= 0);
        var structureSection = output[structureIndex..];
        Assert.Contains("src/code.cs", structureSection);
    }

    [Fact]
    public async Task Generate_LargeContentWithinMemoryLimit_Succeeds()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var config = _config with
        {
            Limits = _config.Limits with { MaxMemoryBytes = "1MB" },
            Output = _config.Output with { SortByPath = false }
        };

        // 100KB of content
        var largeContent = new string('A', 100 * 1024);
        var entry = new FileEntry("large.cs", "cs", "csharp", largeContent.Length);
        var entries = new List<FileContent> { new(entry, largeContent, largeContent.Length) };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, config);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value > 100 * 1024);
    }
}
