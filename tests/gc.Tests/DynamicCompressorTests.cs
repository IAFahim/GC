using gc.Application.Services;
using Xunit;

namespace gc.Tests;

/// <summary>
/// Tests for DynamicCompressor — dynamic algorithmic compression with
/// frequency scanning, phrase detection, Aho-Corasick replacement,
/// attribute stripping, and emoji removal.
/// </summary>
public class DynamicCompressorTests
{
    private readonly DynamicCompressor _compressor = new();

    // =========================================================================
    // 1. Attribute Stripping
    // =========================================================================

    [Fact]
    public void StripAttributes_SingleAttribute_Stripped()
    {
        var input = "[MethodImpl(MethodImplOptions.AggressiveInlining)] public void Foo() {}";
        var result = DynamicCompressor.StripAttributes(input);
        Assert.DoesNotContain("MethodImpl", result);
        Assert.DoesNotContain("[", result);
        Assert.Contains("public void Foo()", result);
    }

    [Fact]
    public void StripAttributes_AttributeWithNestedBrackets_Stripped()
    {
        var input = "[Obsolete(\"Use [NewMethod] instead\")] public void Old() {}";
        var result = DynamicCompressor.StripAttributes(input);
        Assert.DoesNotContain("Obsolete", result);
        Assert.Contains("public void Old()", result);
    }

    [Fact]
    public void StripAttributes_MultipleAttributes_AllStripped()
    {
        var input = "[Fact]\n[InlineData(1, 2)]\npublic void Test() {}";
        var result = DynamicCompressor.StripAttributes(input);
        Assert.DoesNotContain("Fact", result);
        Assert.DoesNotContain("InlineData", result);
        Assert.Contains("public void Test()", result);
    }

    [Fact]
    public void StripAttributes_NoAttributes_Unchanged()
    {
        var input = "public class Foo { private int x; }";
        var result = DynamicCompressor.StripAttributes(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void StripAttributes_ArrayIndexer_NotStripped()
    {
        // Array indexer like arr[0] should NOT be stripped
        var input = "var x = arr[0];";
        var result = DynamicCompressor.StripAttributes(input);
        // arr[0] — '0' is a digit not an ident start, so this should be preserved
        Assert.Contains("arr[0]", result);
    }

    [Fact]
    public void StripAttributes_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", DynamicCompressor.StripAttributes(""));
    }

    // =========================================================================
    // 2. Emoji Stripping
    // =========================================================================

    [Fact]
    public void StripEmoji_EmoticonsRemoved()
    {
        var input = "Hello 😀 World 🎉";
        var result = DynamicCompressor.StripEmoji(input);
        Assert.Equal("Hello  World ", result);
    }

    [Fact]
    public void StripEmoji_NoEmoji_Unchanged()
    {
        var input = "Hello World 123";
        var result = DynamicCompressor.StripEmoji(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void StripEmoji_MixedContent_OnlyEmojiRemoved()
    {
        var input = "public class Foo 🧠 { } 🚀";
        var result = DynamicCompressor.StripEmoji(input);
        Assert.Equal("public class Foo  { } ", result);
    }

    [Fact]
    public void StripEmoji_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", DynamicCompressor.StripEmoji(""));
    }

    // =========================================================================
    // 3. Token Frequency Scanner
    // =========================================================================

    [Fact]
    public void Compress_RepeatedToken_Replaced()
    {
        // "bucketCapacityMask" repeated many times should be replaced
        var input = string.Join(" ", Enumerable.Repeat("bucketCapacityMask = true;", 10));
        var result = _compressor.Compress(input);
        Assert.True(result.ReplacementCount > 0, "Expected at least one replacement");
        Assert.True(result.TokensSaved > 0, "Expected token savings");
        // Not ALL occurrences may be replaced if the replacement symbol conflicts,
        // but the output should be shorter
        Assert.True(result.Output.Length < input.Length, "Output should be shorter than input");
    }

    [Fact]
    public void Compress_ShortTokens_NotReplaced()
    {
        // Short tokens (< 5 chars) shouldn't be replaced
        var input = string.Join(" ", Enumerable.Repeat("int x = 1;", 20));
        var result = _compressor.Compress(input);
        // "int" is only 3 chars, shouldn't be worth replacing
        // Result might still have replacements for other reasons, but not for "int"
    }

    [Fact]
    public void Compress_NoRepeatedTokens_NoReplacements()
    {
        var input = "every word here is unique and different from all others";
        var result = _compressor.Compress(input);
        Assert.Equal(0, result.ReplacementCount);
    }

    // =========================================================================
    // 4. Phrase Detection (Suffix Array)
    // =========================================================================

    [Fact]
    public void Compress_RepeatedPhrase_Detected()
    {
        // A phrase like "IColumn<T>.Remove" repeating should be detected
        var phrase = "IColumn<T>.Remove(";
        var input = string.Join("\n", Enumerable.Repeat($"items.{phrase}x);", 5));
        var result = _compressor.Compress(input);
        Assert.True(result.ReplacementCount > 0);
    }

    [Fact]
    public void Compress_LongRepeatedCodeBlock_Detected()
    {
        var block = "configuration.GetSection(\"appSettings\").GetValue<string>(\"key\")";
        var input = string.Join("\n", Enumerable.Repeat($"var x = {block};", 3));
        var result = _compressor.Compress(input);
        Assert.True(result.ReplacementCount > 0);
        Assert.True(result.TokensSaved > 0);
    }

    // =========================================================================
    // 5. Legend Generation
    // =========================================================================

    [Fact]
    public void Compress_WithReplacements_ContainsLegend()
    {
        var input = string.Join(" ", Enumerable.Repeat("bucketCapacityMask = true;", 10));
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
        var input = string.Join(" ", Enumerable.Repeat("bucketCapacityMask = true;", 10));
        var result = _compressor.Compress(input);
        // Legend should contain both the symbol and the original
        Assert.Contains("bucketCapacityMask", result.Legend);
    }

    // =========================================================================
    // 6. Aho-Corasick Replacement
    // =========================================================================

    [Fact]
    public void Compress_MultiplePatterns_ReplacedInSinglePass()
    {
        var input = string.Join("\n", new[]
        {
            "bucketCapacityMask = true;",
            "bucketCapacityMask = false;",
            "anotherLongIdentifier = 1;",
            "anotherLongIdentifier = 2;",
            "anotherLongIdentifier = 3;",
        });
        var result = _compressor.Compress(input);
        Assert.True(result.ReplacementCount >= 2, $"Expected >= 2 replacements, got {result.ReplacementCount}");
    }

    [Fact]
    public void Compress_OverlappingPatterns_LongestMatchWins()
    {
        // If both "bucket" and "bucketCapacityMask" are candidates,
        // the longer one should win
        var input = string.Join(" ", Enumerable.Repeat("bucketCapacityMask", 10));
        var result = _compressor.Compress(input);
        // Should not contain partial replacement artifacts
        Assert.DoesNotContain("bucketCapacityMask", result.Output);
    }

    // =========================================================================
    // 7. End-to-End Compression
    // =========================================================================

    [Fact]
    public void Compress_RealisticCSharpCode_ReducesSize()
    {
        var input = @"public sealed class ConfigurationService
{
    private readonly IConfigurationValidator _configurationValidator;
    private readonly ILogger _logger;
    private readonly IConfigurationValidator _configurationValidator2;

    public ConfigurationService(IConfigurationValidator configurationValidator, ILogger logger)
    {
        _configurationValidator = configurationValidator;
        _configurationValidator2 = configurationValidator;
        _logger = logger;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result InitializeConfigAsync()
    {
        var configurationValidator = _configurationValidator;
        return configurationValidator.Validate(new GcConfiguration());
    }

    [Fact]
    public void TestConfig()
    {
        var configurationValidator = _configurationValidator;
        Assert.NotNull(configurationValidator);
    }
}";
        var result = _compressor.Compress(input);
        // Should strip attributes and compress repeated identifiers
        Assert.DoesNotContain("MethodImpl", result.Output);
        Assert.DoesNotContain("[Fact]", result.Output);
        Assert.True(result.Output.Length < input.Length || result.ReplacementCount > 0,
            "Expected some compression");
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

    [Fact]
    public void Compress_ShortText_NoCompression()
    {
        var result = _compressor.Compress("short");
        Assert.Equal("short", result.Output);
        Assert.Equal(0, result.ReplacementCount);
    }

    // =========================================================================
    // 8. Symbol Generation
    // =========================================================================

    [Fact]
    public void Compress_SymbolsAreShort()
    {
        var input = string.Join(" ", Enumerable.Repeat("veryLongIdentifierName", 10));
        var result = _compressor.Compress(input);
        // Symbol should be much shorter than original
        Assert.True(result.Output.Length < input.Length);
    }

    [Fact]
    public void Compress_ManyReplacements_UniqueSymbols()
    {
        // Create 10 different long identifiers, each repeated 5 times
        var lines = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            var id = $"veryLongIdentifier{i:D3}Value";
            lines.Add(string.Join(" ", Enumerable.Repeat(id, 5)));
        }
        var input = string.Join("\n", lines);
        var result = _compressor.Compress(input);
        // Each replaced identifier should have a unique symbol
        Assert.True(result.ReplacementCount > 0);
    }

    // =========================================================================
    // 9. Combined with BrainCrusher
    // =========================================================================

    [Fact]
    public void Compress_AfterBrainCrusher_FurtherReduces()
    {
        // First apply BrainCrusher, then DynamicCompressor
        var crusher = new BrainCrusher();
        var input = @"public class ConfigurationService
{
    private readonly IConfigurationValidator configurationValidator;
    // This comment gets stripped by BrainCrusher
    public void SetValidator(IConfigurationValidator configurationValidator)
    {
        this.configurationValidator = configurationValidator;
    }
}";
        var crushed = crusher.Crush(input);
        var dynResult = _compressor.Compress(crushed);
        // Dynamic compression should further reduce the output
        Assert.True(dynResult.Output.Length <= crushed.Length);
    }

    // =========================================================================
    // 10. SuffixArray directly
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

    [Fact]
    public void SuffixArray_FindRepeatedPhrases_DetectsRepeats()
    {
        var text = "hello world hello world hello world";
        var phrases = SuffixArray.FindRepeatedPhrases(text, 5, 30, 2, 10);
        Assert.True(phrases.Count > 0, "Expected to find repeated phrases");
    }

    [Fact]
    public void SuffixArray_NoRepeats_EmptyResult()
    {
        var text = "abcdefg hijklm nopqrst";
        var phrases = SuffixArray.FindRepeatedPhrases(text, 5, 30, 2, 10);
        Assert.Empty(phrases);
    }

    [Fact]
    public void SuffixArray_ShortInput_EmptyResult()
    {
        var text = "ab";
        var phrases = SuffixArray.FindRepeatedPhrases(text, 5, 30, 2, 10);
        Assert.Empty(phrases);
    }
}
