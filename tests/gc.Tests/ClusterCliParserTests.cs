using gc.CLI.Services;
using gc.Domain.Constants;
using gc.Domain.Models.Configuration;
using Xunit;

namespace gc.Tests.FeatureTests;

public class ClusterCliParserTests
{
    [Fact]
    public void Parse_ClusterFlag_SetsCluster()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--cluster" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Cluster);
    }

    [Fact]
    public void Parse_ClusterDir_SetsClusterDir()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--cluster-dir", "/path/to/repos" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("/path/to/repos", result.Value!.ClusterDir);
    }

    [Fact]
    public void Parse_ClusterDepth_SetsClusterDepth()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--cluster-depth", "5" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.ClusterDepth);
    }

    [Fact]
    public void Parse_AllClusterFlags_Together()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--cluster", "--cluster-dir", "/path", "--cluster-depth", "5" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Cluster);
        Assert.Equal("/path", result.Value!.ClusterDir);
        Assert.Equal(5, result.Value!.ClusterDepth);
    }

    [Fact]
    public void Parse_ClusterWithoutClusterDir_DefaultsClusterDirToEmpty()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--cluster" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Cluster);
        Assert.Equal(string.Empty, result.Value!.ClusterDir);
    }

    [Fact]
    public void Parse_ClusterDirWithoutCluster_StillParses()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--cluster-dir", "/some/dir" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Cluster);
        Assert.Equal("/some/dir", result.Value!.ClusterDir);
    }

    [Fact]
    public void Parse_ClusterDepthNegative_IsIgnored()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--cluster-depth", "-3" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ClusterDepth);
    }

    [Fact]
    public void Parse_ClusterWithOutput_Works()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--cluster", "--output", "output.txt" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Cluster);
        Assert.Equal("output.txt", result.Value!.OutputFile);
    }

    [Fact]
    public void Parse_ClusterWithExtensionFilter_Works()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--cluster", "-e", ".cs" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Cluster);
        Assert.Contains("cs", result.Value!.Extensions);
    }

    [Fact]
    public void Parse_ClusterWithExcludePattern_Works()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--cluster", "-x", "node_modules" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Cluster);
        Assert.Contains("node_modules", result.Value!.Excludes);
    }
}
