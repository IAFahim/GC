using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using gc.Application.Services;
using gc.Domain.Common;
using gc.Domain.Constants;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Configuration;
using gc.Infrastructure.Discovery;
using gc.Infrastructure.IO;
using gc.Infrastructure.Logging;
using Xunit.Abstractions;

namespace gc.Tests;

/// <summary>
/// Phase 1 performance optimization tests — zero-allocation hot path, Span-based filtering,
/// SearchValues binary detection, StringBuilder pooling.
/// </summary>
public class Phase1PerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger _logger;
    private readonly string _tempDir;
    private readonly GcConfiguration _config;

    public Phase1PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = new ConsoleLogger();
        _tempDir = Path.Combine(Path.GetTempPath(), $"gc_phase1_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _config = BuiltInPresets.GetDefaultConfiguration();
    }

    // ========================================================================
    // 1.1 — Span-based FileFilter + FrozenSet extensions
    // ========================================================================

    [Fact]
    public void Optimization11_FrozenSet_ExtensionMatching()
    {
        // Verify FrozenSet-based extension matching works correctly
        var filter = new FileFilter(_logger);
        var files = new[]
        {
            "src/main.cs",
            "src/utils.js",
            "src/helpers.py",
            "src/readme.md",
            "src/config.json"
        };

        var result = filter.FilterFiles(files, _config,
            Array.Empty<string>(), Array.Empty<string>(), new[] { "cs", "js" });

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Path == "src/main.cs");
        Assert.Contains(entries, e => e.Path == "src/utils.js");
    }

    [Fact]
    public void Optimization11_SpanBased_ExtensionExtraction()
    {
        // Verify extension extraction works without Path.GetExtension allocation
        var filter = new FileFilter(_logger);
        var files = new[]
        {
            "src/test.CS",        // uppercase extension
            "src/test.Razor",     // mixed case
            "Dockerfile",          // no extension
            "src/.gitignore",      // dot-file
            "src/file.test.js",    // double extension
        };

        var result = filter.FilterFiles(files, _config,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();

        var csEntry = entries.First(e => e.Path == "src/test.CS");
        Assert.Equal("cs", csEntry.Extension); // lowercased

        var razorEntry = entries.First(e => e.Path == "src/test.Razor");
        Assert.Equal("razor", razorEntry.Extension);

        var jsEntry = entries.First(e => e.Path == "src/file.test.js");
        Assert.Equal("js", jsEntry.Extension);
    }

    [Fact]
    public void Optimization11_PathNormalization_SkipsWhenNoBackslashes()
    {
        // Paths from git are already forward-slash — verify no allocation happens
        var filter = new FileFilter(_logger);
        var forwardSlashPaths = Enumerable.Range(0, 100)
            .Select(i => $"src/module/subdir/file{i}.cs")
            .ToArray();

        var sw = Stopwatch.StartNew();
        for (int iter = 0; iter < 100; iter++)
        {
            filter.FilterFiles(forwardSlashPaths, _config,
                Array.Empty<string>(), Array.Empty<string>(), new[] { "cs" });
        }
        sw.Stop();

        _output.WriteLine($"100 iterations of 100-file filtering: {sw.ElapsedMilliseconds}ms");
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Filtering took {sw.ElapsedMilliseconds}ms — should be fast with no allocation");
    }

    [Fact]
    public void Optimization11_BackslashPaths_StillWork()
    {
        // Windows-style paths with backslashes should still be handled
        var filter = new FileFilter(_logger);
        var files = new[]
        {
            @"src\module\test.cs",
            @"src\other\test.js"
        };

        var result = filter.FilterFiles(files, _config,
            Array.Empty<string>(), Array.Empty<string>(), new[] { "cs" });

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Single(entries);
        Assert.Equal(@"src\module\test.cs", entries[0].Path);
    }

    [Fact]
    public void Optimization11_SystemIgnorePatterns_StillWork()
    {
        var filter = new FileFilter(_logger);
        var files = new[]
        {
            "src/main.cs",
            "node_modules/dep/index.js",
            "bin/Debug/app.dll",
            "src/image.png",
            "obj/Release/app.dll"
        };

        var result = filter.FilterFiles(files, _config,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Single(entries);
        Assert.Equal("src/main.cs", entries[0].Path);
    }

    [Fact]
    public void Optimization11_SearchPaths_StillWork()
    {
        var filter = new FileFilter(_logger);
        var files = new[]
        {
            "src/main.cs",
            "tests/test.cs",
            "docs/readme.md"
        };

        var result = filter.FilterFiles(files, _config,
            new[] { "src" }, Array.Empty<string>(), Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Single(entries);
        Assert.Equal("src/main.cs", entries[0].Path);
    }

    [Fact]
    public void Optimization11_ExcludePatterns_StillWork()
    {
        var filter = new FileFilter(_logger);
        var files = new[]
        {
            "src/main.cs",
            "src/generated.cs",
            "src/test.cs"
        };

        var result = filter.FilterFiles(files, _config,
            Array.Empty<string>(), new[] { "generated" }, Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Equal(2, entries.Count);
        Assert.DoesNotContain(entries, e => e.Path.Contains("generated"));
    }

    [Fact]
    public void Optimization11_Performance_1000Files_Under20ms()
    {
        // Phase 1.1 should make filtering of 1000 files very fast
        var filter = new FileFilter(_logger);
        var files = Enumerable.Range(0, 1000)
            .Select(i => $"src/module{i % 50}/component{i}.cs")
            .ToArray();

        // Warm up
        filter.FilterFiles(files.Take(10), _config,
            Array.Empty<string>(), Array.Empty<string>(), new[] { "cs" });

        var sw = Stopwatch.StartNew();
        var result = filter.FilterFiles(files, _config,
            Array.Empty<string>(), Array.Empty<string>(), new[] { "cs" });
        sw.Stop();

        Assert.True(result.IsSuccess);
        Assert.Equal(1000, result.Value!.Count());
        _output.WriteLine($"1000 files filtered in {sw.ElapsedTicks} ticks ({sw.ElapsedMilliseconds}ms)");
        Assert.True(sw.ElapsedMilliseconds < 20,
            $"Filtering took {sw.ElapsedMilliseconds}ms — expected < 20ms");
    }

    [Fact]
    public void Optimization11_LanguageMappings_StillResolve()
    {
        var filter = new FileFilter(_logger);
        var files = new[]
        {
            "test.js",
            "test.ts",
            "test.py",
            "test.cs",
            "test.sh",
            "Dockerfile",
            "Makefile"
        };

        var result = filter.FilterFiles(files, _config,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();

        Assert.Equal("javascript", entries.First(e => e.Extension == "js").Language);
        Assert.Equal("typescript", entries.First(e => e.Extension == "ts").Language);
        Assert.Equal("python", entries.First(e => e.Extension == "py").Language);
        Assert.Equal("cs", entries.First(e => e.Extension == "cs").Language);
        Assert.Equal("bash", entries.First(e => e.Extension == "sh").Language);
    }

    // ========================================================================
    // 1.2 — StringBuilder pooling in MarkdownGenerator
    // ========================================================================

    [Fact]
    public async Task Optimization12_StringBuilderReuse_LineExclusion()
    {
        // Verify line exclusion still works with pooled StringBuilder
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var content = "// comment 1\ncode line\n// comment 2\nmore code\n// comment 3";
        var entries = new List<FileContent>
        {
            new(new FileEntry("test.cs", "cs", "cs", content.Length), content, content.Length)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, new[] { "//" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("code line", output);
        Assert.Contains("more code", output);
        Assert.DoesNotContain("// comment", output);
    }

    [Fact]
    public async Task Optimization12_StringBuilderReuse_MultipleFilesInSequence()
    {
        // Verify the pooled StringBuilder works correctly across multiple files
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = Enumerable.Range(0, 10).Select(i =>
        {
            var c = $"// header {i}\ncode_{i}_line1\n// footer {i}\ncode_{i}_line2";
            return new FileContent(
                new FileEntry($"file{i}.cs", "cs", "cs", c.Length), c, c.Length);
        }).ToList();

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, new[] { "//" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());

        for (int i = 0; i < 10; i++)
        {
            Assert.Contains($"code_{i}_line1", output);
            Assert.Contains($"code_{i}_line2", output);
            Assert.DoesNotContain($"// header {i}", output);
            Assert.DoesNotContain($"// footer {i}", output);
        }

        Assert.Equal(ms.ToArray().Length, result.Value);
    }

    [Fact]
    public async Task Optimization12_StringBuilderReuse_EmptyLineExclusion()
    {
        // Verify excluding blank lines works with pooled SB
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var content = "line1\n\nline2\n\n\nline3";
        var entries = new List<FileContent>
        {
            new(new FileEntry("test.txt", "txt", "text", content.Length), content, content.Length)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, new[] { "\n" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("line1", output);
        Assert.Contains("line2", output);
        Assert.Contains("line3", output);
        Assert.Equal(ms.ToArray().Length, result.Value);
    }

    [Fact]
    public async Task Optimization12_Performance_LineExclusion_200Files()
    {
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = Enumerable.Range(0, 200).Select(i =>
        {
            var c = string.Join("\n", Enumerable.Range(0, 20).Select(j =>
                j % 3 == 0 ? $"// comment {j}" : $"code_line_{j}"));
            return new FileContent(
                new FileEntry($"file{i}.cs", "cs", "cs", c.Length), c, c.Length);
        }).ToList();

        // Warm up
        using (var warmup = new MemoryStream())
        {
            await generator.GenerateMarkdownStreamingAsync(
                entries.Take(1), warmup, _config, new[] { "//" }, CancellationToken.None);
        }

        using var ms = new MemoryStream();
        var sw = Stopwatch.StartNew();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, new[] { "//" }, CancellationToken.None);
        sw.Stop();

        Assert.True(result.IsSuccess);
        _output.WriteLine($"200 files with line exclusion: {sw.ElapsedMilliseconds}ms");
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Generation with line exclusion took {sw.ElapsedMilliseconds}ms");
    }

    // ========================================================================
    // 1.3 — Pre-sized list in git discovery
    // ========================================================================

    [Fact]
    public async Task Optimization13_PreSizedList_StillWorksCorrectly()
    {
        var projectRoot = FindProjectRoot();
        if (projectRoot == null)
        {
            _output.WriteLine("Skipping: not running from a git repo");
            return;
        }

        var discovery = new FileDiscovery(_logger);
        var result = await discovery.DiscoverFilesAsync(projectRoot, _config);

        Assert.True(result.IsSuccess);
        var files = result.Value!.ToList();
        Assert.True(files.Count > 10, $"Expected many files, got {files.Count}");

        // Verify known files exist
        Assert.Contains(files, f => f.EndsWith(".cs"));
        Assert.Contains(files, f => f.EndsWith(".csproj"));
        _output.WriteLine($"Discovered {files.Count} files");
    }

    // ========================================================================
    // 1.4 — SearchValues<byte> for SIMD binary detection
    // ========================================================================

    [Fact]
    public async Task Optimization14_SearchValues_DetectsBinaryFiles()
    {
        // Binary file should be detected and skipped
        var tmpFile = Path.Combine(_tempDir, "binary_test.dat");
        var data = new byte[1024];
        new Random(42).NextBytes(data);
        data[100] = 0x00; // null byte = binary
        await File.WriteAllBytesAsync(tmpFile, data);

        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = new List<FileContent>
        {
            new(new FileEntry(tmpFile, "dat", "dat", -1), null, -1)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Skipping binary file", output);
    }

    [Fact]
    public async Task Optimization14_SearchValues_AllowsTextFiles()
    {
        // Text file (no null bytes) should pass through
        var tmpFile = Path.Combine(_tempDir, "text_test.txt");
        await File.WriteAllTextAsync(tmpFile, "This is valid text content\nWith multiple lines\n");

        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = new List<FileContent>
        {
            new(new FileEntry(tmpFile, "txt", "text", -1), null, -1)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("This is valid text content", output);
        Assert.DoesNotContain("Skipping binary file", output);
    }

    [Fact]
    public async Task Optimization14_SearchValues_LargeBinaryFile()
    {
        // Large binary with null byte near the start — should detect quickly
        var tmpFile = Path.Combine(_tempDir, "large_binary.bin");
        var data = new byte[100_000];
        Array.Fill(data, (byte)'A');
        data[500] = 0x00; // null byte in first 4096 bytes
        await File.WriteAllBytesAsync(tmpFile, data);

        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = new List<FileContent>
        {
            new(new FileEntry(tmpFile, "bin", "bin", -1), null, -1)
        };

        using var ms = new MemoryStream();
        var sw = Stopwatch.StartNew();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, CancellationToken.None);
        sw.Stop();

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Skipping binary file", output);
        _output.WriteLine($"Binary detection took {sw.ElapsedTicks} ticks");
    }

    [Fact]
    public async Task Optimization14_SearchValues_NullByteAfter4096_NotDetected()
    {
        // Null byte after the 4096-byte check window — should NOT be detected as binary
        var tmpFile = Path.Combine(_tempDir, "late_null.txt");
        var data = new byte[8192];
        Array.Fill(data, (byte)'A');
        data[5000] = 0x00; // after the 4096-byte check window
        await File.WriteAllBytesAsync(tmpFile, data);

        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = new List<FileContent>
        {
            new(new FileEntry(tmpFile, "txt", "text", -1), null, -1)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        // Not detected as binary because null byte is past the check window
        Assert.DoesNotContain("Skipping binary file", output);
    }

    [Fact]
    public void Optimization14_SearchValuesField_ExistsAndCorrect()
    {
        // Verify the static SearchValues field exists via reflection
        var type = typeof(MarkdownGenerator);
        var field = type.GetField("NullByte",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.IsAssignableFrom<SearchValues<byte>>(field!.GetValue(null));
    }

    // ========================================================================
    // End-to-End Phase 1 regression tests
    // ========================================================================

    [Fact]
    public async Task EndToEnd_AllPhase1Optimizations_CorrectOutput()
    {
        // Full pipeline test: filter with FrozenSet → generate with pooled SB → binary detect with SearchValues
        var filesDir = Path.Combine(_tempDir, "e2e_phase1");
        Directory.CreateDirectory(filesDir);

        // Create text files
        for (int i = 0; i < 5; i++)
        {
            File.WriteAllText(
                Path.Combine(filesDir, $"file{i}.cs"),
                $"// comment\nusing System;\nnamespace N{i} {{ class C{i} {{ }} }}");
        }

        // Create a binary file (should be skipped)
        var binData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00 }; // PNG-like header with null
        File.WriteAllBytes(Path.Combine(filesDir, "image.png"), binData);

        // Filter files
        var allFiles = Directory.GetFiles(filesDir).Select(f => Path.GetRelativePath(filesDir, f)).ToArray();
        var filter = new FileFilter(_logger);
        var filterResult = filter.FilterFiles(allFiles, _config,
            Array.Empty<string>(), Array.Empty<string>(), new[] { "cs" });

        Assert.True(filterResult.IsSuccess);
        var entries = filterResult.Value!.ToList();
        Assert.Equal(5, entries.Count);
        Assert.True(entries.All(e => e.Size == -1)); // Deferred stat

        // Generate with line exclusion
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var contents = entries.Select(e =>
        {
            var fullPath = Path.Combine(filesDir, e.Path);
            return new FileContent(
                new FileEntry(fullPath, e.Extension, e.Language, -1), null, -1);
        }).ToList();

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            contents, ms, _config, new[] { "//" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());

        // Verify comments excluded, code included
        Assert.DoesNotContain("// comment", output);
        Assert.Contains("using System;", output);
        Assert.Contains("_Project Structure:_", output);
        Assert.Equal(ms.ToArray().Length, result.Value);
    }

    [Fact]
    public async Task EndToEnd_Phase1_ByteCountStillAccurate()
    {
        // Verify all Phase 1 changes maintain byte count accuracy
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));

        // In-memory with line exclusion (uses pooled SB)
        var content = "// skip\nkeep1\n// skip\nkeep2\n// skip\nkeep3";
        var entries = new List<FileContent>
        {
            new(new FileEntry("test.cs", "cs", "cs", content.Length), content, content.Length)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, new[] { "//" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ms.ToArray().Length, result.Value);
    }

    // ========================================================================
    // Helper methods
    // ========================================================================

    private string? FindProjectRoot()
    {
        var current = AppContext.BaseDirectory;
        while (current != null && !File.Exists(Path.Combine(current, "gc.sln")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        return current;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                foreach (var file in Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }
}
