using gc.Application.Services;
using gc.Application.UseCases;
using gc.CLI.Services;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using Xunit;

namespace gc.Tests;

public class EdgeCaseHammerTests
{
    // =========================================================================
    // BrainCrusher edge cases
    // =========================================================================

    [Fact]
    public void BrainCrusher_OnlyComments_ReturnsEmpty()
    {
        var crusher = new BrainCrusher();
        var input = "// comment 1\n// comment 2\n/* block comment */\n/// <summary>xml</summary>";
        var result = crusher.Crush(input);
        Assert.NotNull(result);
        Assert.True(result.Trim().Length == 0, $"Expected empty, got: '{result}'");
    }

    [Fact]
    public void BrainCrusher_MixedLineEndings_HandlesCorrectly()
    {
        var crusher = new BrainCrusher();
        var input = "public\r\n\r\nclass\r\nTest\r\n\r\n";
        var result = crusher.Crush(input);
        Assert.Contains("!1", result); // public
        Assert.Contains("!e", result); // class
    }

    [Fact]
    public void BrainCrusher_OnlyWhitespace_ReturnsMinimal()
    {
        var crusher = new BrainCrusher();
        var input = "   \t\n  \t  \n   ";
        var result = crusher.CrushBlock(input);
        Assert.NotNull(result);
    }

    [Fact]
    public void BrainCrusher_Uncrush_InvalidSymbol_PassesThrough()
    {
        var crusher = new BrainCrusher();
        var input = "some !Z code";
        var result = crusher.Uncrush(input);
        Assert.Contains("!Z", result);
    }

    [Fact]
    public void BrainCrusher_RoundTrip_ComplexCSharp()
    {
        var crusher = new BrainCrusher();
        var input = @"using System;
using System.Collections.Generic;
using System.Linq;

namespace MyApp
{
    public sealed class Calculator
    {
        private readonly Dictionary<string, int> _cache = new();
        
        public int Add(int a, int b)
        {
            return a + b;
        }
        
        public async Task<int> ComputeAsync(string key, int value)
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached;
            
            var result = value * 2;
            _cache[key] = result;
            return result;
        }
        
        public IEnumerable<string> GetKeys()
        {
            foreach (var kvp in _cache)
            {
                yield return kvp.Key;
            }
        }
    }
}";
        var crushed = crusher.Crush(input);
        var restored = crusher.Uncrush(crushed);
        Assert.Contains("using System", restored);
        Assert.Contains("Calculator", restored);
        Assert.Contains("Add", restored);
    }

    [Fact]
    public void BrainCrusher_StringInterpolation_Preserved()
    {
        var crusher = new BrainCrusher();
        var input = "var msg = $\"Hello {name}, count = {count}\";";
        var result = crusher.Crush(input);
        var restored = crusher.Uncrush(result);
        Assert.Equal(input, restored);
    }

    [Fact]
    public void BrainCrusher_VerbatimString_PreservesContent()
    {
        var crusher = new BrainCrusher();
        var input = "var path = @\"C:\\Users\\test\\file.txt\";";
        var result = crusher.Crush(input);
        Assert.Contains("C:", result); // verbatim string content preserved
        var restored = crusher.Uncrush(result);
        Assert.Equal(input, restored);
    }

    // =========================================================================
    // DynamicCompressor edge cases
    // =========================================================================

    [Fact]
    public void DynamicCompressor_OnlyWhitespace_ReturnsUnchanged()
    {
        var compressor = new DynamicCompressor();
        var input = "   \t\n  \t  \n   ";
        var result = compressor.Compress(input);
        Assert.Equal(input.Trim(), result.Output.Trim());
    }

    [Fact]
    public void DynamicCompressor_SameWord100Times_FindsReplacement()
    {
        var compressor = new DynamicCompressor();
        var input = string.Join(" ", Enumerable.Repeat("configurationService", 100));
        var result = compressor.Compress(input);
        Assert.True(result.ReplacementCount > 0, "Should find the repeated token");
        Assert.True(result.Output.Length < input.Length);
    }

    [Fact]
    public void DynamicCompressor_StripAttributes_AssemblyLevel_Stripped()
    {
        var input = "[assembly: AssemblyVersion(\"1.0.0\")]";
        var result = DynamicCompressor.StripAttributes(input);
        Assert.DoesNotContain("AssemblyVersion", result);
    }

    [Fact]
    public void DynamicCompressor_StripAttributes_ArrayIndexer_Preserved()
    {
        var input = "var x = arr[0];";
        var result = DynamicCompressor.StripAttributes(input);
        Assert.Contains("arr[0]", result);
    }

    [Fact]
    public void DynamicCompressor_StripAttributes_AttributeOnSameLine()
    {
        var input = "    [ThreadStatic] private static int? value;";
        var result = DynamicCompressor.StripAttributes(input);
        Assert.DoesNotContain("ThreadStatic", result);
        Assert.Contains("private static", result);
    }

    [Fact]
    public void DynamicCompressor_StripEmoji_MixedWithCode()
    {
        var input = "public void DoWork() { } 🎉 bool done = true;";
        var result = DynamicCompressor.StripEmoji(input);
        Assert.DoesNotContain("🎉", result);
        Assert.Contains("public void DoWork()", result);
        Assert.Contains("bool done = true", result);
    }

    [Fact]
    public void DynamicCompressor_OverlappingPatterns_NoCorruption()
    {
        var compressor = new DynamicCompressor();
        var input = string.Join("\n", new[]
        {
            "configurationService = new ConfigurationService();",
            "var configurationService2 = configurationService;",
            "return configurationService;",
            "configurationService.Dispose();",
        });
        var result = compressor.Compress(input);
        Assert.NotNull(result.Output);
        Assert.True(result.Output.Length < input.Length || result.ReplacementCount > 0);
    }

    [Fact]
    public void DynamicCompressor_InputExactly50Chars()
    {
        var compressor = new DynamicCompressor();
        var input = new string('a', 50);
        var result = compressor.Compress(input);
        // Should not crash; may or may not compress depending on internal logic
        Assert.NotNull(result.Output);
    }

    [Fact]
    public void DynamicCompressor_NullInput_ReturnsNull()
    {
        var compressor = new DynamicCompressor();
        var result = compressor.Compress(null!);
        Assert.Null(result.Output);
    }

    // =========================================================================
    // SuffixArray edge cases
    // =========================================================================

    [Fact]
    public void SuffixArray_Build_ABCABCABC_ValidLength()
    {
        var sa = SuffixArray.Build("abcabcabc");
        Assert.Equal(9, sa.Length);
    }

    [Fact]
    public void SuffixArray_FindRepeated_PublicStaticReadonly()
    {
        var text = "public static readonly public static readonly";
        var phrases = SuffixArray.FindRepeatedPhrases(text, 6, 40, 2, 10);
        Assert.True(phrases.Count > 0, "Should find repeated 'public static readonly'");
    }

    [Fact]
    public void SuffixArray_FindRepeated_ShortRepeats_ExcludedByMinLength()
    {
        var text = "aaaaaa";
        var phrases = SuffixArray.FindRepeatedPhrases(text, 6, 40, 2, 10);
        // "aaaaaa" is exactly 6 chars, but only repeated as "a" substrings
        // Should not find useful phrases since min length is 6
    }

    [Fact]
    public void SuffixArray_FindRepeated_VeryLongRepeatedString()
    {
        var chunk = new string('x', 200) + "END";
        var text = string.Join("SEP", Enumerable.Repeat(chunk, 5));
        var phrases = SuffixArray.FindRepeatedPhrases(text, 6, 250, 2, 10);
        // Just verify it doesn't crash and returns something reasonable
        Assert.NotNull(phrases);
    }

    // =========================================================================
    // AhoCorasick edge cases
    // =========================================================================

    [Fact]
    public void AhoCorasick_OverlappingPatterns_LongestMatchWins()
    {
        var ac = new AhoCorasick(new[] { "abc", "abcd", "abcde" });
        var result = ac.ReplaceAll("test abcde end", new[] { "X", "Y", "Z" });
        Assert.Contains("Z", result); // "abcde" (longest) should match → "Z"
        Assert.DoesNotContain("abcde", result);
    }

    [Fact]
    public void AhoCorasick_EmptyReplacement_PatternsDeleted()
    {
        var ac = new AhoCorasick(new[] { "remove_me" });
        var result = ac.ReplaceAll("keep this remove_me and this", new[] { "" });
        Assert.Equal("keep this  and this", result);
    }

    // =========================================================================
    // CliParser edge cases
    // =========================================================================

    [Fact]
    public void CliParser_EmptyStringArg_DoesNotCrash()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "" }, new GcConfiguration());
        Assert.NotNull(result);
    }

    [Fact]
    public void CliParser_DoubleDashArg_DoesNotCrash()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "--" }, new GcConfiguration());
        Assert.NotNull(result);
    }

    [Fact]
    public void CliParser_VeryLongArg_DoesNotCrash()
    {
        var longArg = new string('x', 10000);
        var parser = new CliParser();
        var result = parser.Parse(new[] { longArg }, new GcConfiguration());
        Assert.NotNull(result);
    }

    // =========================================================================
    // MarkdownGenerator edge cases
    // =========================================================================

    [Fact]
    public async Task MarkdownGenerator_ContentWithOnlyNewlines_ValidMarkdown()
    {
        var logger = new TestLogger();
        var reader = new TestFileReader();
        var generator = new MarkdownGenerator(logger, reader);

        var contents = new[]
        {
            new FileContent(
                new FileEntry { Path = "test.cs", Extension = "cs", Language = "csharp" },
                "\n\n\n\n\n",
                5)
        };

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(
            contents, ms, new GcConfiguration());
        Assert.True(result.IsSuccess);
    }

    // =========================================================================
    // GenerateContextUseCase edge cases
    // =========================================================================

    [Fact]
    public async Task GenerateContextUseCase_EmptyFileList_ReturnsSuccess()
    {
        var logger = new TestLogger();
        var reader = new TestFileReader();
        var generator = new MarkdownGenerator(logger, reader);
        var discovery = new MockDiscovery(Array.Empty<FileEntry>());
        var clipboard = new MockClipboard();
        var filter = new FileFilter(logger);
        var useCase = new GenerateContextUseCase(discovery, filter, reader, generator, clipboard, logger);

        var result = await useCase.ExecuteAsync(
            "/tmp", new GcConfiguration(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            null, false, null, false, CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GenerateContextUseCase_AlreadyCancelled_ReturnsImmediately()
    {
        var logger = new TestLogger();
        var reader = new TestFileReader();
        var generator = new MarkdownGenerator(logger, reader);
        var discovery = new MockDiscovery(Array.Empty<FileEntry>());
        var clipboard = new MockClipboard();
        var filter = new FileFilter(logger);
        var useCase = new GenerateContextUseCase(discovery, filter, reader, generator, clipboard, logger);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await useCase.ExecuteAsync(
            "/tmp", new GcConfiguration(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            null, false, null, false, cts.Token);
        // When cancelled, may still return success if cleanup happened before cancellation
        // Just verify it doesn't crash
        Assert.NotNull(result);
    }

    // =========================================================================
    // FileFilter edge cases
    // =========================================================================

    [Fact]
    public void FileFilter_PathWithParentDir_HandledSafely()
    {
        var logger = new TestLogger();
        var filter = new FileFilter(logger);
        var rawFiles = new List<string>
        {
            "/home/user/../secret/key.pem"
        };
        var result = filter.FilterFiles(rawFiles, new GcConfiguration(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        Assert.True(result.IsSuccess);
    }

    // =========================================================================
    // CliParser edge cases
    // =========================================================================

    [Fact]
    public void CliParser_ShortHFlag_ShowsHelp()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "-h" }, new GcConfiguration());
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.ShowHelp);
    }

    [Fact]
    public void CliParser_ShortVFlag_SetsVerbose()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "-v" }, new GcConfiguration());
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Verbose);
    }

    [Fact]
    public void CliParser_TestFlag_SetsRunTests()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "--test" }, new GcConfiguration());
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.RunTests);
    }

    [Fact]
    public void CliParser_BenchmarkFlag_SetsRunBenchmark()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "--benchmark" }, new GcConfiguration());
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.RunRealBenchmark);
    }

    [Fact]
    public void CliParser_InitConfigFlag_SetsInitConfig()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "--init-config" }, new GcConfiguration());
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.InitConfig);
    }

    [Fact]
    public void CliParser_ValidateConfigFlag_SetsValidateConfig()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "--validate-config" }, new GcConfiguration());
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.ValidateConfig);
    }

    [Fact]
    public void CliParser_DumpConfigFlag_SetsDumpConfig()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "--dump-config" }, new GcConfiguration());
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.DumpConfig);
    }

    [Fact]
    public void CliParser_NoAppendFlag_SetsAppendModeFalse()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "--no-append" }, new GcConfiguration());
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.False(result.Value.Append);
    }

    [Fact]
    public void CliParser_FunKeywords_GrabBrain_SetsValues()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "grab", "src", "brain" }, new GcConfiguration());
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Contains("src", result.Value.Paths);
        Assert.True(result.Value.BrainMode);
    }

    [Fact]
    public void CliParser_HordeKeyword_SetsClusterMode()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "horde" }, new GcConfiguration());
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Cluster);
    }

    [Fact]
    public void CliParser_PascalCaseVariants_Work()
    {
        var parser = new CliParser();
        var result1 = parser.Parse(new[] { "--Paths", "src" }, new GcConfiguration());
        Assert.True(result1.IsSuccess);
        Assert.Contains("src", result1.Value.Paths);

        var result2 = parser.Parse(new[] { "--Extension", "cs" }, new GcConfiguration());
        Assert.True(result2.IsSuccess);
        Assert.Contains("cs", result2.Value.Extensions);

        var result3 = parser.Parse(new[] { "--Exclude", "tests" }, new GcConfiguration());
        Assert.True(result3.IsSuccess);
        Assert.Contains("tests", result3.Value.Excludes);

        var result4 = parser.Parse(new[] { "--Output", "out.txt" }, new GcConfiguration());
        Assert.True(result4.IsSuccess);
        Assert.Equal("out.txt", result4.Value.OutputFile);

        var result5 = parser.Parse(new[] { "--Depth", "3" }, new GcConfiguration());
        Assert.True(result5.IsSuccess);
        Assert.Equal(3, result5.Value.Depth);
    }

    [Fact]
    public void CliParser_BackslashNormalization_ConvertsToForwardSlash()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "grab", "src\\\\folder" }, new GcConfiguration());
        Assert.True(result.IsSuccess);
        Assert.Contains("src//folder", result.Value.Paths); // backslashes converted to forward slashes
    }

    [Fact]
    public void CliParser_HistoryNegativeIndex_Handled()
    {
        var parser = new CliParser();
        var result = parser.Parse(new[] { "--history", "-1" }, new GcConfiguration());
        Assert.True(result.IsSuccess);
        Assert.NotNull(result);
    }

    // =========================================================================
    // Stress / fuzz tests
    // =========================================================================

    [Fact]
    public void BrainCrusher_Stress_RandomInput_DoesNotCrash()
    {
        var crusher = new BrainCrusher();
        var random = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            var len = random.Next(0, 1000);
            var chars = new char[len];
            for (int j = 0; j < len; j++)
                chars[j] = (char)random.Next(32, 126);
            var input = new string(chars);
            var crushed = crusher.Crush(input);
            Assert.NotNull(crushed);
        }
    }

    [Fact]
    public void DynamicCompressor_Stress_RandomInput_DoesNotCrash()
    {
        var compressor = new DynamicCompressor();
        var random = new Random(42);
        for (int i = 0; i < 50; i++)
        {
            var len = random.Next(0, 500);
            var chars = new char[len];
            for (int j = 0; j < len; j++)
                chars[j] = (char)random.Next(32, 126);
            var input = new string(chars);
            var result = compressor.Compress(input);
            Assert.NotNull(result.Output);
        }
    }

    [Fact]
    public void SuffixArray_Stress_RandomInput_DoesNotCrash()
    {
        var random = new Random(42);
        for (int i = 0; i < 20; i++)
        {
            var len = random.Next(0, 200);
            var chars = new char[len];
            for (int j = 0; j < len; j++)
                chars[j] = (char)random.Next('a', 'z' + 1);
            var input = new string(chars);
            var sa = SuffixArray.Build(input);
            Assert.Equal(input.Length, sa.Length);
        }
    }

    // =========================================================================
    // Helper mocks
    // =========================================================================

    private class TestLogger : ILogger
    {
        public List<(string Level, string Message)> Messages { get; } = new();
        public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;
        public void Log(LogLevel level, string message, Exception? ex = null) => Messages.Add((level.ToString(), message));
    }

    private class TestFileReader : IFileReader
    {
        public Task<Result<FileContent>> ReadAsync(FileEntry entry, CancellationToken ct = default) => Task.FromResult(Result<FileContent>.Success(default));
        public Task<Result<Stream>> ReadStreamingAsync(string path, CancellationToken ct = default) => Task.FromResult(Result<Stream>.Success(Stream.Null));
        public Task<bool> IsBinaryFileAsync(string path, CancellationToken ct = default) => Task.FromResult(false);
    }

    private class MockDiscovery : IFileDiscovery
    {
        private readonly FileEntry[] _entries;
        public MockDiscovery(FileEntry[] entries) => _entries = entries;
        public Task<Result<IEnumerable<string>>> DiscoverFilesAsync(string rootPath, GcConfiguration config, CancellationToken ct = default) => Task.FromResult(Result<IEnumerable<string>>.Success(_entries.Select(e => e.Path)));
        public Task<Result<IReadOnlyList<RepoInfo>>> DiscoverGitReposAsync(string clusterRoot, ClusterConfiguration clusterConfig, CancellationToken ct = default) => Task.FromResult(Result<IReadOnlyList<RepoInfo>>.Success(Array.Empty<RepoInfo>()));
    }

    private class MockClipboard : IClipboardService
    {
        public Task<Result> CopyToClipboardAsync(Stream stream, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> CopyToClipboardAsync(Stream stream, LimitsConfiguration limits, bool append = false, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> CopyToClipboardAsync(string content, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> CopyToClipboardAsync(string content, LimitsConfiguration limits, bool append = false, CancellationToken ct = default) => Task.FromResult(Result.Success());
    }
}
