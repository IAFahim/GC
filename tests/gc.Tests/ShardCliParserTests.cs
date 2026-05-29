using gc.CLI.Services;
using gc.Domain.Constants;
using gc.Domain.Models;

namespace gc.Tests.FeatureTests;

public class ShardCliParserTests
{
    [Fact]
    public void Parse_ShardFormatDot_ParsesCorrectly()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--shard", "2.3" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.ShardInfo);
        Assert.Equal(2, result.Value.ShardInfo.Slice);
        Assert.Equal(3, result.Value.ShardInfo.Of);
    }

    [Fact]
    public void Parse_ShardFormatSlash_ParsesCorrectly()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--shard", "1/4" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.ShardInfo);
        Assert.Equal(1, result.Value.ShardInfo.Slice);
        Assert.Equal(4, result.Value.ShardInfo.Of);
    }

    [Fact]
    public void Parse_ShardInvalid_Throws()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--shard", "invalid" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ShardInfo);
    }

    [Fact]
    public void Parse_ShardSliceExceedsTotal_IsIgnored()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--shard", "5.3" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ShardInfo);
    }

    [Fact]
    public void Parse_ShardSliceZero_IsIgnored()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--shard", "0.3" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ShardInfo);
    }

    [Fact]
    public void Parse_ShardTotalZero_IsIgnored()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--shard", "1.0" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ShardInfo);
    }

    [Fact]
    public void Parse_ShardWithCluster_Works()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--cluster", "--shard", "1.2" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Cluster);
        Assert.NotNull(result.Value.ShardInfo);
        Assert.Equal(1, result.Value.ShardInfo.Slice);
        Assert.Equal(2, result.Value.ShardInfo.Of);
    }

    [Fact]
    public void Parse_ShardWithDryRun_Works()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--dry-run", "--shard", "3.5" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.DryRun);
        Assert.NotNull(result.Value.ShardInfo);
        Assert.Equal(3, result.Value.ShardInfo.Slice);
        Assert.Equal(5, result.Value.ShardInfo.Of);
    }

    [Fact]
    public void Parse_ShardWithOutput_Works()
    {
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--shard", "2.2", "--output", "out.txt" };

        var result = parser.Parse(args, config);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.ShardInfo);
        Assert.Equal("out.txt", result.Value.OutputFile);
    }

    [Fact]
    public void ShardInfo_TryParse_ValidInputs()
    {
        Assert.NotNull(ShardInfo.TryParse("1.3"));
        Assert.NotNull(ShardInfo.TryParse("2/3"));
        Assert.NotNull(ShardInfo.TryParse("10.10"));
        Assert.NotNull(ShardInfo.TryParse(" 3.4 "));

        // Edge cases
        var parsed = ShardInfo.TryParse("1.1");
        Assert.NotNull(parsed);
        Assert.Equal(1, parsed.Slice);
        Assert.Equal(1, parsed.Of);
    }

    [Fact]
    public void ShardInfo_TryParse_InvalidInputs()
    {
        Assert.Null(ShardInfo.TryParse(null));
        Assert.Null(ShardInfo.TryParse(""));
        Assert.Null(ShardInfo.TryParse("1"));
        Assert.Null(ShardInfo.TryParse("1.2.3"));
        Assert.Null(ShardInfo.TryParse("abc"));
        Assert.Null(ShardInfo.TryParse("2.1")); // slice > total
        Assert.Null(ShardInfo.TryParse("0.1"));
        Assert.Null(ShardInfo.TryParse("1.0"));
    }

    [Fact]
    public void ShardInfo_Validate_Bounds()
    {
        var info = new ShardInfo(2, 3);
        Assert.Equal(2, info.Slice);
        Assert.Equal(3, info.Of);

        var infoSlice0 = new ShardInfo(0, 3);
        Assert.Equal(1, infoSlice0.Slice);

        var infoOf0 = new ShardInfo(2, 0);
        Assert.Equal(1, infoOf0.Of);
    }
}