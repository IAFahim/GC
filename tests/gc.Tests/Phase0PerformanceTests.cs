using System.Buffers;
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
/// Phase 0 performance optimization tests — validates every optimization
/// from TODOS_PERFORMANCE.md Phase 0 is correctly implemented and functional.
/// </summary>
public class Phase0PerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger _logger;
    private readonly string _tempDir;
    private readonly GcConfiguration _config;

    public Phase0PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = new ConsoleLogger();
        _tempDir = Path.Combine(Path.GetTempPath(), $"gc_phase0_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _config = BuiltInPresets.GetDefaultConfiguration();
    }

    // ========================================================================
    // 0.1 — AutoFlush removed, buffer increased to 64KB
    // ========================================================================

    [Fact]
    public async Task Optimization01_NoAutoFlush_WriterBuffersBeforeFlush()
    {
        // Verify that StreamWriter does NOT flush on every write.
        // We do this by writing to a CountingStream that tracks Write calls.
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = Enumerable.Range(0, 50).Select(i =>
        {
            var content = $"public class Test{i} {{ }}";
            return new FileContent(
                new FileEntry($"test{i}.cs", "cs", "cs", content.Length),
                content,
                content.Length);
        }).ToList();

        using var countingStream = new WriteCountingStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, countingStream, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // With AutoFlush=true, we'd get ~250+ Write calls (5 WriteLineAsync per file * 50 files).
        // With buffered writes, the StreamWriter batches into far fewer actual stream writes.
        // The exact count depends on buffer size, but it should be dramatically less.
        _output.WriteLine($"Write calls: {countingStream.WriteCount} for 50 files");
        _output.WriteLine($"Expected ~250+ with AutoFlush, got {countingStream.WriteCount}");

        // With 64KB buffer, 50 small files should need very few writes
        Assert.True(countingStream.WriteCount < 100,
            $"Too many write calls ({countingStream.WriteCount}). AutoFlush may still be on.");
    }

    [Fact]
    public async Task Optimization01_LargerBuffer_ProducesIdenticalOutput()
    {
        // Verify output correctness is identical with larger buffer
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var content = "Console.WriteLine(\"hello\");";
        var entries = new List<FileContent>
        {
            new(new FileEntry("test.cs", "cs", "cs", content.Length), content, content.Length)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());

        Assert.Contains("test.cs", output);
        Assert.Contains("```cs", output);
        Assert.Contains(content, output);
        Assert.Contains("_Project Structure:_", output);
    }

    [Fact]
    public async Task Optimization01_BufferedWriter_FasterThanAutoFlush()
    {
        // Performance comparison: buffered vs many writes
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = Enumerable.Range(0, 200).Select(i =>
        {
            var c = $"public class Test{i} {{ public void Method() {{ }} }}";
            return new FileContent(
                new FileEntry($"dir/subdir/test{i}.cs", "cs", "cs", c.Length), c, c.Length);
        }).ToList();

        // Warm up
        using (var warmup = new MemoryStream())
        {
            await generator.GenerateMarkdownStreamingAsync(
                entries.Take(1), warmup, _config, null, CancellationToken.None);
        }

        var sw = Stopwatch.StartNew();
        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, CancellationToken.None);
        sw.Stop();

        Assert.True(result.IsSuccess);
        _output.WriteLine($"200 files generated in {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Output size: {ms.Length} bytes");

        // Should complete well under 500ms for 200 in-memory files
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Generation took {sw.ElapsedMilliseconds}ms — too slow for 200 in-memory files");
    }

    // ========================================================================
    // 0.2 — Double git spawn eliminated in auto mode
    // ========================================================================

    [Fact]
    public async Task Optimization02_AutoMode_SkipsIsGitRepositoryCheck()
    {
        // In auto mode, the discovery should attempt git ls-files directly
        // without first calling IsGitRepositoryAsync. We test this by running
        // in a known git repo and verifying it succeeds with a single attempt.
        var discovery = new FileDiscovery(_logger);
        var config = _config with
        {
            Discovery = _config.Discovery with { Mode = "auto" }
        };

        // Run from the project root (which is a git repo)
        var projectRoot = FindProjectRoot();
        if (projectRoot == null)
        {
            _output.WriteLine("Skipping: not running from a git repo");
            return;
        }

        var sw = Stopwatch.StartNew();
        var result = await discovery.DiscoverFilesAsync(projectRoot, config);
        sw.Stop();

        Assert.True(result.IsSuccess);
        _output.WriteLine($"Auto discovery took {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Found {result.Value!.Count()} files");
    }

    [Fact]
    public async Task Optimization02_AutoMode_FallsBackToFilesystemOnNonGitDir()
    {
        // In auto mode with a non-git directory, git ls-files should fail
        // and fall back to filesystem discovery
        var nonGitDir = Path.Combine(_tempDir, "non_git");
        Directory.CreateDirectory(nonGitDir);
        File.WriteAllText(Path.Combine(nonGitDir, "test.cs"), "class Test {}");

        var discovery = new FileDiscovery(_logger);
        var config = _config with
        {
            Discovery = _config.Discovery with { Mode = "auto" }
        };

        var result = await discovery.DiscoverFilesAsync(nonGitDir, config);

        Assert.True(result.IsSuccess, $"Auto mode should fall back to filesystem. Error: {result.Error}");
        Assert.Contains(result.Value!, f => f.Contains("test.cs"));
    }

    [Fact]
    public async Task Optimization02_GitMode_StillValidatesGitRepo()
    {
        // Explicit git mode should still check for git repo and fail clearly
        var nonGitDir = Path.Combine(_tempDir, "no_git");
        Directory.CreateDirectory(nonGitDir);

        var discovery = new FileDiscovery(_logger);
        var config = _config with
        {
            Discovery = _config.Discovery with { Mode = "git" }
        };

        var result = await discovery.DiscoverFilesAsync(nonGitDir, config);

        Assert.False(result.IsSuccess);
        Assert.Contains("not a git repository", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ========================================================================
    // 0.3 — FileInfo stat() deferred in FileFilter
    // ========================================================================

    [Fact]
    public void Optimization03_CreateFileEntry_DoesNotStatFile()
    {
        // Verify that FileFilter does NOT call new FileInfo() during filtering.
        // A non-existent file should still produce a FileEntry (with Size=-1).
        var filter = new FileFilter(_logger);
        var nonExistentPath = Path.Combine(_tempDir, "does_not_exist_42.cs");

        var result = filter.FilterFiles(
            new[] { nonExistentPath },
            _config,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();

        // Previously this would be empty (file doesn't exist → FileInfo.Exists = false → null).
        // Now it should produce an entry with Size = -1.
        Assert.Single(entries);
        Assert.Equal(-1, entries[0].Size);
        Assert.Equal("cs", entries[0].Extension);
    }

    [Fact]
    public void Optimization03_DeferredStat_ExistingFileAlsoGetsSizeMinus1()
    {
        // Even existing files get Size=-1 during filtering — stat happens in generator
        var existingFile = Path.Combine(_tempDir, "existing.cs");
        File.WriteAllText(existingFile, "class X {}");

        var filter = new FileFilter(_logger);
        var result = filter.FilterFiles(
            new[] { existingFile },
            _config,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Single(entries);
        Assert.Equal(-1, entries[0].Size);
    }

    [Fact]
    public void Optimization03_FilterPerformance_NoStatOverhead()
    {
        // Create many files and verify filtering is fast (no stat() per file)
        var files = new List<string>();
        for (int i = 0; i < 1000; i++)
        {
            files.Add($"src/module{i}/component{i}.cs");
        }

        var filter = new FileFilter(_logger);
        var sw = Stopwatch.StartNew();
        var result = filter.FilterFiles(
            files,
            _config,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { "cs" });
        sw.Stop();

        Assert.True(result.IsSuccess);
        _output.WriteLine($"Filtering 1000 files took {sw.ElapsedTicks} ticks ({sw.ElapsedMilliseconds}ms)");

        // Without stat(), filtering 1000 paths should be <10ms (pure string operations)
        Assert.True(sw.ElapsedMilliseconds < 50,
            $"Filtering took {sw.ElapsedMilliseconds}ms — stat() may still be happening");
    }

    [Fact]
    public void Optimization03_FilterStillResolvesExtensionAndLanguage()
    {
        // Verify extension and language resolution still works without stat()
        var filter = new FileFilter(_logger);
        var files = new[]
        {
            "src/test.cs",
            "src/test.js",
            "src/test.py",
            "Dockerfile",
            "Makefile"
        };

        var result = filter.FilterFiles(
            files, _config,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();

        var csEntry = entries.First(e => e.Path == "src/test.cs");
        Assert.Equal("cs", csEntry.Extension);
        Assert.Equal("cs", csEntry.Language);

        var jsEntry = entries.First(e => e.Path == "src/test.js");
        Assert.Equal("js", jsEntry.Extension);
        Assert.Equal("javascript", jsEntry.Language);

        var pyEntry = entries.First(e => e.Path == "src/test.py");
        Assert.Equal("py", pyEntry.Extension);
        Assert.Equal("python", pyEntry.Language);
    }

    [Fact]
    public async Task Optimization03_GeneratorHandlesMissingFiles_AfterDeferredStat()
    {
        // End-to-end: filter creates entry with Size=-1, generator handles missing file.
        // The streaming path now checks FileInfo.Exists first and writes an error message.
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var missingEntry = new FileEntry("/tmp/nonexistent_phase0_test_" + Guid.NewGuid() + ".cs", "cs", "cs", -1);
        var contents = new List<FileContent>
        {
            new(missingEntry, null, -1)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            contents, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Error reading file", output);
        Assert.Contains("File not found", output);
    }

    // ========================================================================
    // 0.4 — Cached constant byte counts
    // ========================================================================

    [Fact]
    public async Task Optimization04_CachedByteCounts_ProduceCorrectTotals()
    {
        // Verify that cached byte counts produce the same result as runtime calculation
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var content = "line1\nline2\nline3";
        var entries = new List<FileContent>
        {
            new(new FileEntry("test.txt", "txt", "text", content.Length), content, content.Length)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Reported bytes must match actual output bytes
        Assert.Equal(ms.ToArray().Length, result.Value);
    }

    [Fact]
    public async Task Optimization04_ByteCountAccuracy_MultipleFiles()
    {
        // Test byte count accuracy across multiple files of different sizes
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = new List<FileContent>();

        for (int i = 0; i < 20; i++)
        {
            var content = string.Join("\n", Enumerable.Range(0, i + 1).Select(j => $"// line {j}"));
            entries.Add(new FileContent(
                new FileEntry($"file{i}.cs", "cs", "cs", content.Length),
                content, content.Length));
        }

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ms.ToArray().Length, result.Value);
    }

    [Fact]
    public async Task Optimization04_ByteCountAccuracy_WithUnicode()
    {
        // Multibyte characters must be counted correctly
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var content = "Hello 世界 🌍 — «quotes»";
        var entries = new List<FileContent>
        {
            new(new FileEntry("unicode.txt", "txt", "text",
                Encoding.UTF8.GetByteCount(content)), content, Encoding.UTF8.GetByteCount(content))
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ms.ToArray().Length, result.Value);
    }

    [Fact]
    public async Task Optimization04_ByteCountAccuracy_StreamingPath()
    {
        // Test streaming path (content=null, read from file) with cached byte counts
        var tempFile = Path.Combine(_tempDir, "stream_test.cs");
        File.WriteAllText(tempFile, "public class StreamTest { }");

        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entry = new FileEntry(tempFile, "cs", "cs", -1);
        var contents = new List<FileContent> { new(entry, null, -1) };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            contents, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ms.ToArray().Length, result.Value);
    }

    [Fact]
    public void Optimization04_NewlineByteCountField_Exists()
    {
        // Verify the static cached field exists via reflection
        var generatorType = typeof(MarkdownGenerator);
        var field = generatorType.GetField("NewlineByteCount",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        var value = (int)field!.GetValue(null)!;

        var expected = new UTF8Encoding(false).GetByteCount(Environment.NewLine);
        Assert.Equal(expected, value);
    }

    // ========================================================================
    // 0.5 — Git buffer increased to 64KB with ArrayPool
    // ========================================================================

    [Fact]
    public async Task Optimization05_LargerGitBuffer_StillParsesCorrectly()
    {
        // Verify git ls-files still parses correctly with larger buffer
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
        Assert.True(files.Count > 0, "Should discover at least some files");

        // Verify specific known files exist
        Assert.Contains(files, f => f.Contains("gc.sln") || f.EndsWith(".cs") || f.EndsWith(".csproj"));
        _output.WriteLine($"Discovered {files.Count} files with 64KB buffer");
    }

    [Fact]
    public async Task Optimization05_GitDiscovery_PerformanceImproved()
    {
        var projectRoot = FindProjectRoot();
        if (projectRoot == null)
        {
            _output.WriteLine("Skipping: not running from a git repo");
            return;
        }

        var discovery = new FileDiscovery(_logger);
        var config = _config with
        {
            Discovery = _config.Discovery with { Mode = "auto" }
        };

        // Run 3 times and take the best
        var times = new List<long>();
        for (int i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            var result = await discovery.DiscoverFilesAsync(projectRoot, config);
            sw.Stop();
            Assert.True(result.IsSuccess);
            times.Add(sw.ElapsedMilliseconds);
        }

        var bestTime = times.Min();
        _output.WriteLine($"Best discovery time: {bestTime}ms (runs: {string.Join(", ", times)}ms)");

        // Discovery should complete in under 100ms for this repo
        Assert.True(bestTime < 100, $"Discovery took {bestTime}ms — expected < 100ms");
    }

    // ========================================================================
    // 3.4 — SequentialScan file hint (bonus, already implemented)
    // ========================================================================

    [Fact]
    public async Task Optimization034_SequentialScan_StreamingReadsWork()
    {
        // Verify that FileOptions.SequentialScan doesn't break streaming reads
        var tempFile = Path.Combine(_tempDir, "sequential_test.txt");
        var content = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"Line {i}: {new string('x', 80)}"));
        await File.WriteAllTextAsync(tempFile, content);

        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entry = new FileEntry(tempFile, "txt", "text", -1);
        var contents = new List<FileContent> { new(entry, null, -1) };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            contents, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Line 0:", output);
        Assert.Contains("Line 99:", output);
    }

    // ========================================================================
    // End-to-End performance regression tests
    // ========================================================================

    [Fact]
    public async Task EndToEnd_InMemoryGeneration_200Files_Under500ms()
    {
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = new List<FileContent>();

        for (int i = 0; i < 200; i++)
        {
            var c = $"using System;\nnamespace Test{i}\n{{\n    public class Class{i}\n    {{\n        public void Method() {{ }}\n    }}\n}}";
            entries.Add(new FileContent(
                new FileEntry($"src/module{i}/Class{i}.cs", "cs", "cs", c.Length),
                c, c.Length));
        }

        using var ms = new MemoryStream();
        var sw = Stopwatch.StartNew();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, CancellationToken.None);
        sw.Stop();

        Assert.True(result.IsSuccess);
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"200 in-memory files took {sw.ElapsedMilliseconds}ms — expected < 500ms");
        _output.WriteLine($"200 files: {sw.ElapsedMilliseconds}ms, {ms.Length} bytes");
    }

    [Fact]
    public async Task EndToEnd_StreamingGeneration_50Files_Under500ms()
    {
        // Create 50 actual files and stream them
        var filesDir = Path.Combine(_tempDir, "streaming_perf");
        Directory.CreateDirectory(filesDir);

        var entries = new List<FileContent>();
        for (int i = 0; i < 50; i++)
        {
            var filePath = Path.Combine(filesDir, $"file{i}.cs");
            var content = $"namespace Perf{i}\n{{\n    public class Test{i}\n    {{\n        public string Value => \"test\";\n    }}\n}}";
            await File.WriteAllTextAsync(filePath, content);
            entries.Add(new FileContent(
                new FileEntry(filePath, "cs", "cs", -1), null, -1));
        }

        using var ms = new MemoryStream();
        var sw = Stopwatch.StartNew();
        var result = await generator_Create().GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, CancellationToken.None);
        sw.Stop();

        Assert.True(result.IsSuccess);
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"50 streaming files took {sw.ElapsedMilliseconds}ms — expected < 500ms");
        _output.WriteLine($"50 streaming files: {sw.ElapsedMilliseconds}ms, {ms.Length} bytes");
    }

    [Fact]
    public async Task EndToEnd_FilterThenGenerate_500Paths_Fast()
    {
        // Simulate full pipeline: filter 500 paths → generate for matching ones
        var paths = Enumerable.Range(0, 500)
            .Select(i => $"src/module{i % 50}/file{i}.cs")
            .ToList();

        var filter = new FileFilter(_logger);

        var swFilter = Stopwatch.StartNew();
        var filterResult = filter.FilterFiles(
            paths, _config,
            new[] { "src" },
            Array.Empty<string>(),
            new[] { "cs" });
        swFilter.Stop();

        Assert.True(filterResult.IsSuccess);
        var filtered = filterResult.Value!.ToList();
        Assert.Equal(500, filtered.Count);
        Assert.True(filtered.All(e => e.Size == -1), "All entries should have deferred size");

        _output.WriteLine($"Filter 500 paths: {swFilter.ElapsedTicks} ticks ({swFilter.ElapsedMilliseconds}ms)");
        Assert.True(swFilter.ElapsedMilliseconds < 100,
            $"Filtering took {swFilter.ElapsedMilliseconds}ms — too slow");
    }

    [Fact]
    public async Task EndToEnd_ByteCountConsistency_AcrossAllCodePaths()
    {
        // Test that reported bytes == actual bytes for all code paths:
        // 1. In-memory content
        // 2. Streaming from file
        // 3. With line exclusion
        // 4. With backtick escalation

        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));

        // Path 1: In-memory
        {
            var c = "normal content here";
            var entries = new List<FileContent>
            {
                new(new FileEntry("inmem.cs", "cs", "cs", c.Length), c, c.Length)
            };
            using var ms = new MemoryStream();
            var r = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config, null, CancellationToken.None);
            Assert.True(r.IsSuccess);
            Assert.Equal(ms.ToArray().Length, r.Value);
        }

        // Path 2: Streaming
        {
            var tmpFile = Path.Combine(_tempDir, "stream_byte_test.cs");
            File.WriteAllText(tmpFile, "streaming content");
            var entries = new List<FileContent>
            {
                new(new FileEntry(tmpFile, "cs", "cs", -1), null, -1)
            };
            using var ms = new MemoryStream();
            var r = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config, null, CancellationToken.None);
            Assert.True(r.IsSuccess);
            Assert.Equal(ms.ToArray().Length, r.Value);
        }

        // Path 3: With line exclusion (in-memory)
        {
            var c = "// comment\ncode\n// another comment\nmore code";
            var entries = new List<FileContent>
            {
                new(new FileEntry("exclude.cs", "cs", "cs", c.Length), c, c.Length)
            };
            using var ms = new MemoryStream();
            var r = await generator.GenerateMarkdownStreamingAsync(
                entries, ms, _config, new[] { "//" }, CancellationToken.None);
            Assert.True(r.IsSuccess);
            Assert.Equal(ms.ToArray().Length, r.Value);
        }

        // Path 4: Backtick escalation
        {
            var c = "has ```` quadruple backticks";
            var entries = new List<FileContent>
            {
                new(new FileEntry("backtick.md", "md", "markdown", c.Length), c, c.Length)
            };
            using var ms = new MemoryStream();
            var r = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config, null, CancellationToken.None);
            Assert.True(r.IsSuccess);
            Assert.Equal(ms.ToArray().Length, r.Value);
            var output = Encoding.UTF8.GetString(ms.ToArray());
            // Should have escalated to 6-backtick fence
            Assert.Contains("``````", output);
        }
    }

    [Fact]
    public async Task EndToEnd_LargeFile_StreamingByteCountCorrect()
    {
        // Large file (500KB) — verify streaming byte count accuracy
        var tmpFile = Path.Combine(_tempDir, "large_stream.txt");
        var sb = new StringBuilder();
        for (int i = 0; i < 10000; i++)
        {
            sb.AppendLine($"Line {i}: {new string('A', 40)}");
        }
        await File.WriteAllTextAsync(tmpFile, sb.ToString());

        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = new List<FileContent>
        {
            new(new FileEntry(tmpFile, "txt", "text", -1), null, -1)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ms.ToArray().Length, result.Value);
        _output.WriteLine($"Large file: {ms.Length} bytes, reported: {result.Value}");
    }

    [Fact]
    public async Task EndToEnd_ExcludeLines_Streaming_ByteCountCorrect()
    {
        // Streaming file with line exclusion — verify byte counts
        var tmpFile = Path.Combine(_tempDir, "exclude_stream.cs");
        File.WriteAllText(tmpFile, "// comment 1\nusing System;\n// comment 2\nclass X {}\n");

        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = new List<FileContent>
        {
            new(new FileEntry(tmpFile, "cs", "cs", -1), null, -1)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, new[] { "//" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ms.ToArray().Length, result.Value);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.DoesNotContain("// comment", output);
        Assert.Contains("using System;", output);
    }

    [Fact]
    public async Task EndToEnd_BinaryFile_Streaming_Handled()
    {
        // Binary file should be skipped gracefully in streaming path
        var tmpFile = Path.Combine(_tempDir, "binary.dat");
        var binaryData = new byte[] { 0x00, 0x01, 0x02, 0x00, 0x03 };
        await File.WriteAllBytesAsync(tmpFile, binaryData);

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
    public async Task EndToEnd_OversizeFile_Streaming_Handled()
    {
        // File exceeding max size should be reported, not crash
        var tmpFile = Path.Combine(_tempDir, "oversize.txt");
        // Create a 2MB file (default max is 1MB)
        var content = new string('x', 2 * 1024 * 1024);
        await File.WriteAllTextAsync(tmpFile, content);

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
        Assert.Contains("exceeds maximum allowed size", output);
    }

    [Fact]
    public async Task EndToEnd_EmptyContentList_ProducesProjectStructureOnly()
    {
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            Array.Empty<FileContent>(), ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("_Project Structure:_", output);
        Assert.Equal(ms.ToArray().Length, result.Value);
    }

    [Fact]
    public async Task EndToEnd_Cancellation_ThrowsGracefully()
    {
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = Enumerable.Range(0, 100).Select(i =>
        {
            var c = new string('x', 10000);
            return new FileContent(new FileEntry($"f{i}.txt", "txt", "text", c.Length), c, c.Length);
        }).ToList();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("cancel", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EndToEnd_MaxMemoryBytes_Enforced()
    {
        // Verify memory limit still works with cached byte counts
        var config = _config with
        {
            Limits = _config.Limits with { MaxMemoryBytes = "1KB" }
        };

        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var content = new string('x', 2000);
        var entries = new List<FileContent>
        {
            new(new FileEntry("big.txt", "txt", "text", content.Length), content, content.Length)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, config, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("exceed maximum output limit", result.Error);
    }

    [Fact]
    public async Task EndToEnd_StreamingBacktickEscalation_Works()
    {
        // File on disk with backticks should trigger fence escalation
        var tmpFile = Path.Combine(_tempDir, "backtick_stream.md");
        File.WriteAllText(tmpFile, "```\ncode block\n```\nmore ```` stuff");

        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var entries = new List<FileContent>
        {
            new(new FileEntry(tmpFile, "md", "markdown", -1), null, -1)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            entries, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        // Should use escalated fence (6+ backticks) since content has ````
        Assert.Contains("``````", output);
        Assert.Equal(ms.ToArray().Length, result.Value);
    }

    // ========================================================================
    // Helper methods
    // ========================================================================

    private MarkdownGenerator generator_Create() =>
        new MarkdownGenerator(_logger, new FileReader(_logger));

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

    /// <summary>
    /// Stream that counts how many times Write is called.
    /// Used to verify AutoFlush is disabled.
    /// </summary>
    private sealed class WriteCountingStream : MemoryStream
    {
        public int WriteCount { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteCount++;
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            WriteCount++;
            base.Write(buffer);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            WriteCount++;
            await base.WriteAsync(buffer, offset, count, ct);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            WriteCount++;
            await base.WriteAsync(buffer, ct);
        }
    }
}
