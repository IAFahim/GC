using gc.Application.Services;

namespace gc.Tests;

/// <summary>
///     Tests for DynamicCompressor — dynamic algorithmic compression with
///     identifier-aware scanning and BPE-style replacement.
/// </summary>
public class DynamicCompressorTests
{
    private readonly DynamicCompressor _compressor = new();

    // =========================================================================
    // 1. Token Frequency Scanner
    // =========================================================================

    [Fact]
    public void Compress_RepeatedToken_Replaced()
    {
        // "bucketCapacityMask" repeated many times should be replaced
        var input = string.Join(" ", Enumerable.Repeat("bucketCapacityMask = true;", 15));
        var result = _compressor.Compress(input);
        Assert.True(result.ReplacementCount > 0, "Expected at least one replacement");
        Assert.True(result.TokensSaved > 0, "Expected token savings");
        Assert.True(result.Output.Length < input.Length, "Output should be shorter than input");
    }

    [Fact]
    public void Compress_ShortTokens_NotReplaced()
    {
        // Short tokens (< 10 chars) shouldn't be replaced in our refined 'flawless' mode
        // unless they are extremely frequent, but we have a MinPhraseLength of 10 now.
        var input = string.Join(" ", Enumerable.Repeat("int x = 1;", 20));
        var result = _compressor.Compress(input);
        Assert.Equal(0, result.ReplacementCount);
    }

    [Fact]
    public void Compress_NoRepeatedTokens_NoReplacements()
    {
        var input =
            "this is a very unique string with absolutely no repeated phrases of significant length anywhere in it";
        var result = _compressor.Compress(input);
        Assert.Equal(0, result.ReplacementCount);
    }

    // =========================================================================
    // 2. Phrase Detection
    // =========================================================================

    [Fact]
    public void Compress_RepeatedLongIdentifier_Detected()
    {
        var id = "ConfigurationValidatorServiceInstance";
        var input = string.Join("\n", Enumerable.Repeat($"var x = {id};", 20));
        var result = _compressor.Compress(input);
        Assert.True(result.ReplacementCount > 0);
    }

    // =========================================================================
    // 3. Legend Generation
    // =========================================================================

    [Fact]
    public void Compress_WithReplacements_ContainsLegend()
    {
        var input = string.Join(" ", Enumerable.Repeat("bucketCapacityMask = true;", 15));
        var result = _compressor.Compress(input);
        Assert.True(result.ReplacementCount > 0);
        Assert.Contains("GC_DICT", result.Legend);
    }

    [Fact]
    public void Compress_NoReplacements_EmptyLegend()
    {
        var input = "unique words here";
        var result = _compressor.Compress(input);
        Assert.Equal("", result.Legend);
    }

    [Fact]
    public void Compress_LegendMapsSymbolToOriginal()
    {
        var input = string.Join(" ", Enumerable.Repeat("bucketCapacityMask = true;", 15));
        var result = _compressor.Compress(input);
        Assert.Contains("bucketCapacityMask", result.Legend);
    }

    // =========================================================================
    // 4. Overlapping Patterns
    // =========================================================================

    [Fact]
    public void Compress_OverlappingPatterns_LongestMatchWins()
    {
        // If both "bucket" and "bucketCapacityMask" are candidates,
        // the longer one should win
        var input = string.Join(" ", Enumerable.Repeat("bucketCapacityMask", 20));
        var result = _compressor.Compress(input);
        Assert.DoesNotContain("bucketCapacityMask", result.Output);
    }

    // =========================================================================
    // 5. Realistic Scenarios
    // =========================================================================

    [Fact]
    public void Compress_RealisticCode_ReducesSize()
    {
        var code = @"public sealed class ConfigurationValidatorServiceInstance
{
    private readonly IConfigurationValidator _configurationValidator;
    private readonly ILogger _logger;
    private readonly IConfigurationValidator _configurationValidator2;

    public ConfigurationValidatorServiceInstance(IConfigurationValidator configurationValidator, ILogger logger)
    {
        _configurationValidator = configurationValidator;
        _configurationValidator2 = configurationValidator;
        _logger = logger;
    }

    public Result InitializeConfigAsync()
    {
        var configurationValidator = _configurationValidator;
        return configurationValidator.Validate(new GcConfiguration());
    }
}";
        // Repeat the block to ensure high ROI
        var input = string.Join("\n", Enumerable.Repeat(code, 5));
        var result = _compressor.Compress(input);
        Assert.True(result.ReplacementCount > 0, "Expected some compression for repeated identifiers");
        Assert.True(result.Output.Length < input.Length, "Expected size reduction");
    }

    [Fact]
    public void Compress_EmptyString_ReturnsEmpty()
    {
        var result = _compressor.Compress("");
        Assert.Equal("", result.Output);
        Assert.Equal("", result.Legend);
        Assert.Equal(0, result.TokensSaved);
    }

    [Fact]
    public void Compress_Null_ReturnsNull()
    {
        var result = _compressor.Compress(null!);
        Assert.Null(result.Output);
    }

    // =========================================================================
    // 6. SuffixArray directly (preserved as it's a utility used elsewhere or for future)
    // =========================================================================

    [Fact]
    public void SuffixArray_Build_CorrectOrder()
    {
        var text = "banana";
        var sa = SuffixArray.Build(text);
        // Suffixes sorted: a, ana, anana, banana, na, nana
        // sa should be: [5, 3, 1, 0, 4, 2]
        Assert.Equal(6, sa.Length);
        Assert.Equal(5, sa[0]); // "a"
        Assert.Equal(3, sa[1]); // "ana"
        Assert.Equal(1, sa[2]); // "anana"
        Assert.Equal(0, sa[3]); // "banana"
        Assert.Equal(4, sa[4]); // "na"
        Assert.Equal(2, sa[5]); // "nana"
    }
}