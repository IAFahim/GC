using gc.CLI.Services;
using gc.Domain.Constants;

namespace gc.Tests.FeatureTests;

public class CliParserTests
{
    [Fact]
    public void Parse_AppendFlag_SetsAppendMode()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--append" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Append);
    }

    [Fact]
    public void Parse_NoFlags_Defaults()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new string[] { };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Append); // default is no-append
    }

    [Fact]
    public void Parse_DepthFlag_SetsDepth()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "-d", "5" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Depth);
    }

    [Fact]
    public void Parse_ForceFlag_SetsForce()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "-f" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Force);
    }

    [Fact]
    public void Parse_NoSortFlag_SetsNoSort()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--no-sort" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.NoSort);
    }

    [Fact]
    public void Parse_CompressFlag_SetsCompress()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--compress" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Compress);
    }

    [Fact]
    public void Parse_CompressShortFlag_SetsCompress()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "-c" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Compress);
    }

    [Fact]
    public void Parse_CompressKeyword_SetsCompress()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "compress" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Compress);
    }

    [Fact]
    public void Parse_NoCacheFlag_SetsNoCache()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--no-cache" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.NoCache);
    }

    [Fact]
    public void Parse_CompressWithNoCache_BothSet()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--compress", "--no-cache" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Compress);
        Assert.True(result.Value!.NoCache);
    }

    [Fact]
    public void Parse_CompressAndBrain_BothSet()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--compress", "--brain" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Compress);
        Assert.True(result.Value!.BrainMode);
    }

    [Fact]
    public void Parse_DefaultCompressAndNoCache_AreFalse()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new string[] { };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Compress);
        Assert.False(result.Value!.NoCache);
    }

    [Fact]
    public void Parse_InstallCompletion_SetsAutoSentinel()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();

        var result = parser.Parse(["--install-completion"], config);

        Assert.True(result.IsSuccess);
        Assert.Equal("", result.Value!.InstallCompletion); // "" = auto-detect
        Assert.Null(result.Value!.PrintCompletion);
    }

    [Fact]
    public void Parse_InstallCompletion_NotRequestedByDefault()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();

        var result = parser.Parse([], config);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.InstallCompletion);
    }

    [Fact]
    public void Parse_InstallCompletion_ExplicitShell_LandsInPaths()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();

        // `--install-completion` is a bare flag, so an explicit shell is captured as a path
        // and resolved by Program. Verify both pieces are present.
        var result = parser.Parse(["--install-completion", "zsh"], config);

        Assert.True(result.IsSuccess);
        Assert.Equal("", result.Value!.InstallCompletion);
        Assert.Equal(["zsh"], result.Value!.Paths);
    }

    [Fact]
    public void Parse_PrintCompletion_CapturesShell()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();

        var result = parser.Parse(["--print-completion", "bash"], config);

        Assert.True(result.IsSuccess);
        Assert.Equal("bash", result.Value!.PrintCompletion);
    }
}