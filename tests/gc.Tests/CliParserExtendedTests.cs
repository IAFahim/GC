using gc.CLI.Models;
using gc.CLI.Services;
using gc.Domain.Constants;
using gc.Domain.Models.Configuration;
using Xunit;

namespace gc.Tests;

public class CliParserExtendedTests
{
    private readonly CliParser _parser = new();
    private readonly GcConfiguration _config = BuiltInPresets.GetDefaultConfiguration();

    // =========================================================================
    // 1. Missing value errors (single-value flags without values)
    // =========================================================================

    [Fact]
    public void Parse_OutputWithoutValue_ReturnsError()
    {
        var result = _parser.Parse(["--output"], _config);
        Assert.False(result.IsSuccess);
        Assert.Contains("Missing value", result.Error!);
    }

    [Fact]
    public void Parse_MaxMemoryWithoutValue_ReturnsError()
    {
        var result = _parser.Parse(["--max-memory"], _config);
        Assert.False(result.IsSuccess);
        Assert.Contains("Missing value", result.Error!);
    }

    [Fact]
    public void Parse_DepthWithoutValue_ReturnsError()
    {
        var result = _parser.Parse(["--depth"], _config);
        Assert.False(result.IsSuccess);
        Assert.Contains("Missing value", result.Error!);
    }

    [Fact]
    public void Parse_ClusterDirWithoutValue_ReturnsError()
    {
        var result = _parser.Parse(["--cluster-dir"], _config);
        Assert.False(result.IsSuccess);
        Assert.Contains("Missing value", result.Error!);
    }

    [Fact]
    public void Parse_ClusterDepthWithoutValue_ReturnsError()
    {
        var result = _parser.Parse(["--cluster-depth"], _config);
        Assert.False(result.IsSuccess);
        Assert.Contains("Missing value", result.Error!);
    }

    // =========================================================================
    // 2. Value parsing edge cases
    // =========================================================================

    [Fact]
    public void Parse_DepthInvalidNumber_IgnoresValue()
    {
        // "abc" is not a valid int; the parser silently ignores it, keeping config default
        var result = _parser.Parse(["--depth", "abc"], _config);
        Assert.True(result.IsSuccess);
        // Depth stays at the config default (not changed to a parsed value)
        Assert.Equal(_config.Discovery.MaxDepth, result.Value!.Depth);
    }

    [Fact]
    public void Parse_ClusterDepthZeroOrNegative_IgnoresValue()
    {
        var result = _parser.Parse(["--cluster-depth", "0"], _config);
        Assert.True(result.IsSuccess);
        // Zero is not > 0, so clusterDepth stays null
        Assert.Null(result.Value!.ClusterDepth);
    }

    [Fact]
    public void Parse_ClusterDepthNegative_IgnoresValue()
    {
        var result = _parser.Parse(["--cluster-depth", "-5"], _config);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ClusterDepth);
    }

    [Fact]
    public void Parse_MaxMemoryValidFormats_GB()
    {
        var result = _parser.Parse(["--max-memory", "1GB"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal(1073741824L, result.Value!.MaxMemoryBytes);
    }

    [Fact]
    public void Parse_MaxMemoryValidFormats_MB()
    {
        var result = _parser.Parse(["--max-memory", "512MB"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal(512L * 1048576, result.Value!.MaxMemoryBytes);
    }

    [Fact]
    public void Parse_MaxMemoryValidFormats_Bytes()
    {
        var result = _parser.Parse(["--max-memory", "1024B"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal(1024L, result.Value!.MaxMemoryBytes);
    }

    [Fact]
    public void Parse_ExtensionWithLeadingDot_StripsDot()
    {
        var result = _parser.Parse(["-e", ".cs"], _config);
        Assert.True(result.IsSuccess);
        Assert.Contains("cs", result.Value!.Extensions);
        Assert.DoesNotContain(".cs", result.Value!.Extensions);
    }

    [Fact]
    public void Parse_CommaSeparatedExtensions()
    {
        var result = _parser.Parse(["-e", "cs,js,ts"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Extensions.Length);
        Assert.Contains("cs", result.Value!.Extensions);
        Assert.Contains("js", result.Value!.Extensions);
        Assert.Contains("ts", result.Value!.Extensions);
    }

    [Fact]
    public void Parse_ExcludeLineIfStart_BackslashN()
    {
        // The literal string "\\n" should be converted to newline "\n"
        var result = _parser.Parse(["--exclude-line-if-start", "\\n"], _config);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.ExcludeLineIfStart);
        Assert.Equal("\n", result.Value!.ExcludeLineIfStart[0]);
    }

    // =========================================================================
    // 3. Unknown flag handling
    // =========================================================================

    [Fact]
    public void Parse_UnknownFlag_SetsShowHelp()
    {
        var result = _parser.Parse(["--unknown-flag"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ShowHelp);
    }

    [Fact]
    public void Parse_MultipleFlags_AllProcessed()
    {
        var result = _parser.Parse(["-f", "--verbose", "--debug", "--no-sort"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Force);
        Assert.True(result.Value!.Verbose);
        Assert.True(result.Value!.Debug);
        Assert.True(result.Value!.NoSort);
    }

    // =========================================================================
    // 4. Default argument handling
    // =========================================================================

    [Fact]
    public void Parse_BareArgs_TreatedAsPaths()
    {
        var result = _parser.Parse(["src", "lib/utils"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Paths.Length);
        Assert.Contains("src", result.Value!.Paths);
        Assert.Contains("lib/utils", result.Value!.Paths);
    }

    [Fact]
    public void Parse_DoubleDash_StopsFlagParsing()
    {
        // Everything after "--" should be treated as a bare path
        var result = _parser.Parse(["--force", "--", "--unknown", "path1"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Force);
        Assert.Contains("--unknown", result.Value!.Paths);
        Assert.Contains("path1", result.Value!.Paths);
    }

    [Fact]
    public void Parse_EmptyArgs_ReturnsDefaults()
    {
        var result = _parser.Parse([], _config);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Paths);
        Assert.Empty(result.Value!.Extensions);
        Assert.Empty(result.Value!.Excludes);
        Assert.Empty(result.Value!.Presets);
        Assert.Equal(string.Empty, result.Value!.OutputFile);
        Assert.False(result.Value!.ShowHelp);
        Assert.False(result.Value!.ShowVersion);
        Assert.False(result.Value!.Force);
        Assert.False(result.Value!.Verbose);
        Assert.False(result.Value!.Debug);
        Assert.False(result.Value!.Append);
        Assert.False(result.Value!.NoSort);
        Assert.False(result.Value!.Cluster);
        Assert.Equal(string.Empty, result.Value!.ClusterDir);
        Assert.Null(result.Value!.ClusterDepth);
    }

    // =========================================================================
    // 5. Case handling
    // =========================================================================

    [Fact]
    public void Parse_UpperCaseHelpFlag_Works()
    {
        var result = _parser.Parse(["--Help"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ShowHelp);
    }

    [Fact]
    public void Parse_UpperCaseVersionFlag_Works()
    {
        var result = _parser.Parse(["--Version"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ShowVersion);
    }

    [Fact]
    public void Parse_UpperCaseForceFlag_Works()
    {
        var result = _parser.Parse(["--Force"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Force);
    }

    [Fact]
    public void Parse_MixedCaseExtension_Lowercased()
    {
        var result = _parser.Parse(["-e", "CS"], _config);
        Assert.True(result.IsSuccess);
        Assert.Contains("cs", result.Value!.Extensions);
    }

    // =========================================================================
    // 6. History index
    // =========================================================================

    [Fact]
    public void Parse_HistoryWithIndex_SetsHistoryIndex()
    {
        var result = _parser.Parse(["--history", "3"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ShowHistory);
        Assert.Equal(3, result.Value!.HistoryIndex);
    }

    [Fact]
    public void Parse_HistoryWithInvalidIndex_NoIndex()
    {
        // "abc" is not a valid int, so it becomes a path instead
        var result = _parser.Parse(["--history", "abc"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ShowHistory);
        Assert.Null(result.Value!.HistoryIndex);
        // "abc" falls through as a bare arg / path
        Assert.Contains("abc", result.Value!.Paths);
    }

    [Fact]
    public void Parse_HistoryIndexZero_NoIndex()
    {
        // 0 is not > 0, so index stays null
        var result = _parser.Parse(["--history", "0"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ShowHistory);
        Assert.Null(result.Value!.HistoryIndex);
    }

    // =========================================================================
    // 7. State transitions
    // =========================================================================

    [Fact]
    public void Parse_FlagAfterSingleValueState_ResetsState()
    {
        // --output sets Output state, "file.txt" consumes it and resets state,
        // then --force is recognized as a flag (not a path)
        var result = _parser.Parse(["--output", "file.txt", "--force"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal("file.txt", result.Value!.OutputFile);
        Assert.True(result.Value!.Force);
    }

    [Fact]
    public void Parse_MultipleMultiValueFlags_CollectsAll()
    {
        var result = _parser.Parse(["-e", "cs", "-x", "bin", "-e", "js", "-x", "obj"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Extensions.Length);
        Assert.Equal(2, result.Value!.Excludes.Length);
        Assert.Contains("cs", result.Value!.Extensions);
        Assert.Contains("js", result.Value!.Extensions);
        Assert.Contains("bin", result.Value!.Excludes);
        Assert.Contains("obj", result.Value!.Excludes);
    }
}
