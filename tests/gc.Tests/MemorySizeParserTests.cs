using gc.Domain.Common;
using gc.Domain.Constants;
using Xunit;

namespace gc.Tests.FeatureTests;

public class MemorySizeParserTests
{
    [Fact]
    public void Parse_OverflowingGB_ReturnsDefault()
    {
        // Arrange & Act & Assert
        var result = MemorySizeParser.Parse("999999999GB");
        Assert.Equal(104857600, result); // Should return default (100MB)
    }

    [Fact]
    public void Parse_OverflowingMB_ReturnsDefault()
    {
        // Arrange & Act & Assert
        var result = MemorySizeParser.Parse("999999999MB");
        Assert.Equal(104857600, result); // Should return default (100MB)
    }

    [Fact]
    public void Parse_OverflowingKB_ReturnsDefault()
    {
        // Arrange & Act & Assert
        var result = MemorySizeParser.Parse("999999999KB");
        Assert.Equal(104857600, result); // Should return default (100MB)
    }

    [Fact]
    public void Parse_LargeButValidGB_Works()
    {
        // Arrange & Act & Assert
        var result = MemorySizeParser.Parse("1000GB");
        Assert.Equal(1000L * 1024 * 1024 * 1024, result);
    }

    [Fact]
    public void Parse_MaximumGB_Works()
    {
        // Arrange & Act & Assert
        var result = MemorySizeParser.Parse("8192GB"); // 8 TB
        Assert.Equal(8192L * 1024 * 1024 * 1024, result);
    }

    [Fact]
    public void Parse_DecimalOverflow_ReturnsDefault()
    {
        // Arrange & Act & Assert
        var result = MemorySizeParser.Parse("999999.99GB");
        Assert.Equal(104857600, result); // Should return default (100MB)
    }

    [Fact]
    public void Parse_ValidDecimalGB_Works()
    {
        // Arrange & Act & Assert
        var result = MemorySizeParser.Parse("1.5GB");
        Assert.Equal((long)(1.5 * 1024 * 1024 * 1024), result);
    }

    [Fact]
    public void Parse_BillionKB_Works()
    {
        // Arrange & Act & Assert
        var result = MemorySizeParser.Parse("1000000KB"); // 1 million KB
        Assert.Equal(1000000L * 1024, result);
    }

    [Fact]
    public void Parse_MillionMB_ReturnsDefault()
    {
        // Arrange & Act & Assert
        var result = MemorySizeParser.Parse("1000000MB"); // 1 million MB would overflow
        Assert.Equal(104857600, result); // Should return default (100MB)
    }

    [Fact]
    public void Parse_ZeroWithUnit_Works()
    {
        // Arrange & Act & Assert
        var result = MemorySizeParser.Parse("0GB");
        Assert.Equal(0, result);
    }

    [Fact]
    public void Parse_VeryLargeNumberWithUnit_ReturnsDefault()
    {
        // Arrange & Act & Assert
        var result = MemorySizeParser.Parse("999999999999999999999GB");
        Assert.Equal(104857600, result); // Should return default (100MB)
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_NullOrWhitespace_ReturnsDefault(string? input)
    {
        // Arrange & Act & Assert
        var result = MemorySizeParser.Parse(input!);
        Assert.Equal(104857600, result); // Should return default (100MB)
    }
}
