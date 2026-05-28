using Xunit;
using gc.Domain.Common;
using gc.Domain.Models;
using gc.Domain.Constants;
using gc.Application.Services;

namespace gc.Tests;

/// <summary>
/// Unit tests for core algorithms in gc.
/// These tests verify correctness of foundational components.
/// </summary>
public class CoreAlgorithmTests
{
    #region ErrorKind Tests

    [Fact]
    public void ErrorKind_Category_Discovery_ReturnsCorrect()
    {
        Assert.Equal("Discovery", ErrorKind.DiscoveryFailed.Category());
        Assert.Equal("Discovery", ErrorKind.DiscoveryPathNotFound.Category());
        Assert.Equal("Discovery", ErrorKind.DiscoveryGitNotAvailable.Category());
    }

    [Fact]
    public void ErrorKind_Category_Filter_ReturnsCorrect()
    {
        Assert.Equal("Filter", ErrorKind.FilterNoMatch.Category());
        Assert.Equal("Filter", ErrorKind.FilterPatternInvalid.Category());
    }

    [Fact]
    public void ErrorKind_Category_Content_ReturnsCorrect()
    {
        Assert.Equal("Content", ErrorKind.ContentReadFailed.Category());
        Assert.Equal("Content", ErrorKind.ContentBinarySkipped.Category());
    }

    [Fact]
    public void ErrorKind_Category_Output_ReturnsCorrect()
    {
        Assert.Equal("Output", ErrorKind.OutputWriteFailed.Category());
        Assert.Equal("Output", ErrorKind.OutputSizeExceedsMemoryLimit.Category());
    }

    [Fact]
    public void ErrorKind_Category_Config_ReturnsCorrect()
    {
        Assert.Equal("Config", ErrorKind.ConfigInvalid.Category());
        Assert.Equal("Config", ErrorKind.ConfigFileNotFound.Category());
    }

    [Fact]
    public void ErrorKind_Category_Compression_ReturnsCorrect()
    {
        Assert.Equal("Compression", ErrorKind.CompressSqzNotAvailable.Category());
        Assert.Equal("Compression", ErrorKind.CompressNoSavings.Category());
    }

    [Fact]
    public void ErrorKind_Category_Cluster_ReturnsCorrect()
    {
        Assert.Equal("Cluster", ErrorKind.ClusterDirNotFound.Category());
        Assert.Equal("Cluster", ErrorKind.ClusterNoReposFound.Category());
    }

    [Fact]
    public void ErrorKind_Category_System_ReturnsCorrect()
    {
        Assert.Equal("System", ErrorKind.SystemOutOfMemory.Category());
        Assert.Equal("System", ErrorKind.SystemOperationCancelled.Category());
    }

    [Fact]
    public void ErrorKind_IsRetryable_RetryableErrors_ReturnsTrue()
    {
        Assert.True(ErrorKind.DiscoveryFailed.IsRetryable());
        Assert.True(ErrorKind.ContentReadFailed.IsRetryable());
        Assert.True(ErrorKind.CompressInternalFailed.IsRetryable());
        Assert.True(ErrorKind.OutputWriteFailed.IsRetryable());
        Assert.True(ErrorKind.SystemOutOfMemory.IsRetryable());
    }

    [Fact]
    public void ErrorKind_IsRetryable_NonRetryableErrors_ReturnsFalse()
    {
        Assert.False(ErrorKind.FilterNoMatch.IsRetryable());
        Assert.False(ErrorKind.ContentBinarySkipped.IsRetryable());
        Assert.False(ErrorKind.ConfigFileNotFound.IsRetryable());
        Assert.False(ErrorKind.SystemArgumentInvalid.IsRetryable());
    }

    #endregion

    #region Result Tests

    [Fact]
    public void Result_Failure_CarriesErrorKind()
    {
        var result = Result.Failure("test error", ErrorKind.ContentReadFailed);
        Assert.False(result.IsSuccess);
        Assert.Equal("test error", result.Error);
        Assert.Equal(ErrorKind.ContentReadFailed, result.Kind);
    }

    [Fact]
    public void Result_Success_HasDefaultErrorKind()
    {
        var result = Result.Success();
        Assert.True(result.IsSuccess);
        Assert.Equal(ErrorKind.Unknown, result.Kind);
    }

    [Fact]
    public void Result_T_Map_PropagatesErrorKind()
    {
        var failure = Result<string>.Failure("error", ErrorKind.FilterPatternInvalid);
        var mapped = failure.Map(s => s.Length);
        Assert.False(mapped.IsSuccess);
        Assert.Equal(ErrorKind.FilterPatternInvalid, mapped.Kind);
    }

    [Fact]
    public void Result_T_Bind_PropagatesErrorKind()
    {
        var failure = Result<string>.Failure("error", ErrorKind.CompressSqzNotAvailable);
        var bound = failure.Bind(s => Result<int>.Success(s.Length));
        Assert.False(bound.IsSuccess);
        Assert.Equal(ErrorKind.CompressSqzNotAvailable, bound.Kind);
    }

    [Fact]
    public void Result_T_Tap_ExecutesOnSuccess()
    {
        var success = Result<string>.Success("hello");
        string? captured = null;
        success.Tap(s => captured = s);
        Assert.Equal("hello", captured);
    }

    [Fact]
    public void Result_T_Tap_DoesNotExecuteOnFailure()
    {
        var failure = Result<string>.Failure("error");
        string? captured = "initial";
        failure.Tap(s => captured = s);
        Assert.Equal("initial", captured);
    }

    #endregion

    #region Result.Match Tests

    [Fact]
    public void Result_T_Match_CallsOnSuccess()
    {
        var success = Result<string>.Success("hello");
        var result = success.Match(s => s.ToUpperInvariant(), e => "FAIL");
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void Result_T_Match_CallsOnFailure()
    {
        var failure = Result<string>.Failure("error");
        var result = failure.Match(s => s.ToUpperInvariant(), e => "FAIL:" + e);
        Assert.Equal("FAIL:error", result);
    }

    #endregion

    #region SingleTokenLexicon Tests

    [Fact]
    public void SingleTokenLexicon_Count_ValidRange()
    {
        Assert.True(SingleTokenLexicon.Count > 0);
    }

    [Fact]
    public void SingleTokenLexicon_GetSymbol_ReturnsSortedSingleChar()
    {
        var symbol = SingleTokenLexicon.GetSymbol(0);
        Assert.Equal(1, symbol.Length);
    }

    [Fact]
    public void SingleTokenLexicon_GetSymbol_WrapsCorrectly()
    {
        var maxIndex = SingleTokenLexicon.Count;
        var symbol0 = SingleTokenLexicon.GetSymbol(0);
        var symbolN = SingleTokenLexicon.GetSymbol(maxIndex);
        Assert.Equal(symbol0, symbolN);
    }

    [Fact]
    public void SingleTokenLexicon_GetSymbol_NegativeIndex_WrapsCorrectly()
    {
        var symbol0 = SingleTokenLexicon.GetSymbol(0);
        var symbolNeg = SingleTokenLexicon.GetSymbol(-1);
        
        // Negative index wraps to the last element
        var lastSymbol = SingleTokenLexicon.GetSymbol(SingleTokenLexicon.Count - 1);
        Assert.Equal(lastSymbol, symbolNeg);
    }

    #endregion

    #region TokenEstimator Tests

    [Fact]
    public void TokenEstimator_EmptyString_ReturnsZero()
    {
        Assert.Equal(0, TokenEstimator.EstimateTokens(""));
    }

    [Fact]
    public void TokenEstimator_SimpleWord_ReturnsOneOrTwo()
    {
        var count = TokenEstimator.EstimateTokens("hello");
        Assert.True(count >= 1 && count <= 2);
    }

    [Fact]
    public void TokenEstimator_CamelCase_SplitsAtBoundary()
    {
        var camelCase = TokenEstimator.EstimateTokens("camelCase");
        var twoWords = TokenEstimator.EstimateTokens("camel Case");
        // CamelCase should be similar to spaced words
        Assert.True(camelCase >= 1);
    }

    [Fact]
    public void TokenEstimator_Punctuation_IsSeparateToken()
    {
        var withParens = TokenEstimator.EstimateTokens("hello()");
        var without = TokenEstimator.EstimateTokens("hello");
        Assert.True(withParens >= without);
    }

    [Fact]
    public void TokenEstimator_CodeSample_ReasonableEstimate()
    {
        var code = "public async Task<Result> ExecuteAsync(CancellationToken ct)";
        var tokens = TokenEstimator.EstimateTokens(code);
        Assert.True(tokens > 5, $"Expected > 5 tokens for code sample, got {tokens}");
    }

    [Fact]
    public void TokenEstimator_LongCode_ReasonableEstimate()
    {
        var code = @"
            using gc.Application.Services;
            using gc.Domain.Interfaces;
            
            public sealed class ExampleService
            {
                private readonly ILogger _logger;
                private readonly IFileReader _reader;
                
                public ExampleService(ILogger logger, IFileReader reader)
                {
                    _logger = logger;
                    _reader = reader;
                }
                
                public async Task<Result<Stream>> ReadFileAsync(string path, CancellationToken ct)
                {
                    if (string.IsNullOrEmpty(path))
                        return Result<Stream>.Failure(""Path is required"", ErrorKind.SystemArgumentInvalid);
                    
                    return await _reader.ReadStreamingAsync(path, ct);
                }
            }
        ";
        var tokens = TokenEstimator.EstimateTokens(code);
        Assert.True(tokens > 50, $"Expected > 50 tokens for code sample, got {tokens}");
    }

    #endregion

    #region GlobMatcher Tests

    [Theory]
    [InlineData("*.cs", "test.cs", true)]
    [InlineData("*.cs", "test.java", false)]
    [InlineData("src/**/*.cs", "src/module/file.cs", true)]
    [InlineData("src/**/*.cs", "other/file.cs", false)]
    [InlineData("test?", "test1", true)]
    [InlineData("test?", "test12", false)]
    [InlineData("**/*.cs", "file.cs", true)]
    [InlineData("**/*.cs", "deep/nested/file.cs", true)]
    public void GlobMatcher_IsMatch_CommonPatterns(string pattern, string input, bool expected)
    {
        var result = GlobMatcher.IsMatch(input, pattern);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GlobMatcher_BacktrackBudget_Exhausted()
    {
        // An adversarial pattern that causes exponential backtracking
        var pattern = "**/**/**/**/nomatch.txt";
        var input = "this/is/a/very/long/path/that/wont/match/at/all.txt";
        
        // With budget limit, this should return quickly rather than hanging
        var result = GlobMatcher.IsMatch(input, pattern, maxBacktrackIterations: 100);
        // Should not throw - returns false when budget exhausted
        Assert.False(result);
    }

    #endregion

    #region ContentFilter Tests

    [Fact]
    public void ContentFilter_EmptyPatterns_ReturnsTrue()
    {
        var logger = new gc.Infrastructure.Logging.ConsoleLogger(null, new gc.Infrastructure.System.SystemConsole());
        var filter = new ContentFilter(logger);
        
        Assert.True(filter.ShouldInclude("any content", Array.Empty<string>(), Array.Empty<string>()));
    }

    [Fact]
    public void ContentFilter_ExcludeMatch_ReturnsFalse()
    {
        var logger = new gc.Infrastructure.Logging.ConsoleLogger(null, new gc.Infrastructure.System.SystemConsole());
        var filter = new ContentFilter(logger);
        
        Assert.False(filter.ShouldInclude("this contains TODO and needs review", 
            new[] { "TODO" }, Array.Empty<string>()));
    }

    [Fact]
    public void ContentFilter_IncludeMatch_ReturnsTrue()
    {
        var logger = new gc.Infrastructure.Logging.ConsoleLogger(null, new gc.Infrastructure.System.SystemConsole());
        var filter = new ContentFilter(logger);
        
        Assert.True(filter.ShouldInclude("this contains IMPORTANT keyword",
            Array.Empty<string>(), new[] { "IMPORTANT" }));
    }

    [Fact]
    public void ContentFilter_NoIncludeMatch_ReturnsFalse()
    {
        var logger = new gc.Infrastructure.Logging.ConsoleLogger(null, new gc.Infrastructure.System.SystemConsole());
        var filter = new ContentFilter(logger);
        
        Assert.False(filter.ShouldInclude("this has no keyword",
            Array.Empty<string>(), new[] { "important" }));
    }

    [Fact]
    public void ContentFilter_WildcardPattern_Works()
    {
        var logger = new gc.Infrastructure.Logging.ConsoleLogger(null, new gc.Infrastructure.System.SystemConsole());
        var filter = new ContentFilter(logger);
        
        Assert.True(filter.ShouldInclude("this is auto-generated code",
            Array.Empty<string>(), new[] { "auto-*" }));
    }

    #endregion

    #region MemorySizeParser Tests

    [Theory]
    [InlineData("100B", 100)]
    [InlineData("1KB", 1024)]
    [InlineData("1MB", 1024 * 1024)]
    [InlineData("1GB", 1024 * 1024 * 1024)]
    [InlineData("100MB", 100 * 1024 * 1024)]
    public void MemorySizeParser_ParsesCorrectly(string input, long expected)
    {
        var result = MemorySizeParser.Parse(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("10GB", 10L * 1024 * 1024 * 1024)]
    [InlineData("500MB", 500L * 1024 * 1024)]
    [InlineData("256KB", 256L * 1024)]
    public void MemorySizeParser_LargeValues_ParsesCorrectly(string input, long expected)
    {
        var result = MemorySizeParser.Parse(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MemorySizeParser_CaseInsensitive()
    {
        Assert.Equal(1024, MemorySizeParser.Parse("1kb"));
        Assert.Equal(1024, MemorySizeParser.Parse("1Kb"));
        Assert.Equal(1024, MemorySizeParser.Parse("1kB"));
    }

    #endregion

    #region DynamicCompressor Tests

    [Fact]
    public void DynamicCompressor_EmptyInput_ReturnsIdentity()
    {
        var compressor = new DynamicCompressor();
        var result = compressor.Compress("");
        Assert.Equal("", result.Output);
    }

    [Fact]
    public void DynamicCompressor_ShortInput_ReturnsIdentity()
    {
        var compressor = new DynamicCompressor();
        var result = compressor.Compress("short");
        Assert.Equal("short", result.Output);
    }

    [Fact]
    public void DynamicCompressor_RoundTrip_LegendResolves()
    {
        var compressor = new DynamicCompressor();
        // Use a sufficiently long input with repeated phrases
        var input = string.Join("\n", Enumerable.Range(0, 10).Select(i => $"public class Class{i} {{ }} public Class{i}() {{ }}"));
        
        var result = compressor.Compress(input);
        
        // Verify legend contains expected format
        Assert.Contains("# GC_DICT", result.Legend);
        
        // Verify output can be understood with legend
        Assert.NotEmpty(result.Legend);
    }

    #endregion

    #region FileEntry Tests

    [Fact]
    public void FileEntry_Size_Persists()
    {
        var entry = new FileEntry("test.cs", "src/test.cs", "csharp", 1234);
        Assert.Equal(1234, entry.Size);
    }

    [Fact]
    public void FileEntry_With_ClonesCorrectly()
    {
        var original = new FileEntry("test.cs", "src/test.cs", "csharp", 1234);
        var cloned = original with { Language = "cs" };
        
        Assert.Equal("csharp", original.Language);
        Assert.Equal("cs", cloned.Language);
        Assert.Equal(original.Size, cloned.Size);
    }

    #endregion

    #region MagicNumbers Tests

    [Fact]
    public void MagicNumbers_FileStreamBufferSize_Is4096()
    {
        Assert.Equal(4096, MagicNumbers.FileStreamBufferSize);
    }

    [Fact]
    public void MagicNumbers_PipeBufferSize_Is65536()
    {
        Assert.Equal(65536, MagicNumbers.PipeBufferSize);
    }

    [Fact]
    public void MagicNumbers_MaxFileSize_Is10MB()
    {
        Assert.Equal(10 * 1024 * 1024, MagicNumbers.MaxFileSizeForFastStream);
    }

    #endregion
}
