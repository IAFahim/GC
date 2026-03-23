using gc.Application.Services;
using gc.Domain.Common;
using gc.Domain.Constants;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Logging;
using Xunit;

namespace gc.Tests.FeatureTests;

public class CompactModeTests
{
    private readonly ILogger _logger = new ConsoleLogger();

    [Fact]
    public void CompactMild_RemovesEmptyLines()
    {
        // Arrange
        var input = "line1\n\nline2\n\n\nline3";
        var expected = "line1\nline2\nline3";

        // Act
        var result = MarkdownGenerator.CompactMarkdown(input, CompactLevel.Mild);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CompactMild_CollapsesWhitespace()
    {
        // Arrange
        var input = "line1    with     spaces";
        var result = MarkdownGenerator.CompactMarkdown(input, CompactLevel.Mild);

        // Assert - Should collapse multiple spaces but preserve structure
        Assert.Contains("line1", result);
        Assert.Contains("with", result);
        Assert.Contains("spaces", result);
        Assert.DoesNotContain("    ", result); // Should not have 4+ consecutive spaces
    }

    [Fact]
    public void CompactMild_PreservesIndentation()
    {
        // Arrange
        var input = "    indented line\n        more indented";
        var result = MarkdownGenerator.CompactMarkdown(input, CompactLevel.Mild);

        // Assert - Should preserve leading indentation
        Assert.StartsWith(" ", result); // Should start with space (indentation)
    }

    [Fact]
    public void CompactAggressive_TruncatesLongComments()
    {
        // Arrange
        var input = "// This is a very long comment that exceeds the 80 character limit and should be truncated";
        var result = MarkdownGenerator.CompactMarkdown(input, CompactLevel.Aggressive);

        // Assert - Should truncate long comments
        Assert.True(result.Length <= 80 || !result.Contains("// This is a very long comment that exceeds the 80 character limit"));
    }

    [Fact]
    public void CompactAggressive_RemovesMetadata()
    {
        // Arrange
        var input = "<!-- This is an HTML comment -->\nContent here";
        var result = MarkdownGenerator.CompactMarkdown(input, CompactLevel.Aggressive);

        // Assert - Should remove HTML comments
        Assert.DoesNotContain("<!--", result);
        Assert.DoesNotContain("-->", result);
    }

    [Fact]
    public void Compact_None_PreservesOriginal()
    {
        // Arrange
        var input = "line1\n\nline2\n  indented";
        var expected = input;

        // Act
        var result = MarkdownGenerator.CompactMarkdown(input, CompactLevel.None);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Compact_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var input = "";
        var input2 = (string?)null;

        // Act
        var result1 = MarkdownGenerator.CompactMarkdown(input, CompactLevel.Mild);
        var result2 = MarkdownGenerator.CompactMarkdown(input2!, CompactLevel.Mild);

        // Assert
        Assert.Empty(result1);
        Assert.Empty(result2);
    }

    [Fact]
    public void Compact_MultipleLevels_CorrectBehavior()
    {
        // Arrange
        var input = "line1\n\nline2    with     spaces\n// Very long comment that should be truncated when aggressive mode is enabled but preserved in mild mode";

        // Act - Mild mode
        var mildResult = MarkdownGenerator.CompactMarkdown(input, CompactLevel.Mild);

        // Assert - Mild mode should remove empty lines and collapse whitespace but preserve comments
        Assert.DoesNotContain("\n\n", mildResult); // No empty lines
        Assert.Contains("// Very long comment", mildResult); // Comment preserved

        // Act - Aggressive mode
        var aggressiveResult = MarkdownGenerator.CompactMarkdown(input, CompactLevel.Aggressive);

        // Assert - Aggressive mode should truncate long comments
        Assert.True(aggressiveResult.Length < input.Length); // Should be shorter
    }

    [Fact]
    public async Task GenerateMarkdownAsync_AppliesCompactMode()
    {
        // Arrange
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Compact = CompactLevel.Mild };

        var generator = new MarkdownGenerator(_logger);
        var fileEntry = new FileEntry("test.cs", "cs", "csharp", 20);
        var content = "line1\n\nline2\n\nline3"; // Content with empty lines
        var fileContents = new List<FileContent>
        {
            new(fileEntry, content, content.Length)
        };

        // Act
        var result = await generator.GenerateMarkdownAsync(fileContents, config);

        // Assert
        Assert.True(result.IsSuccess);
        var markdown = result.Value;

        // Compact mode should have removed empty lines from content
        Assert.DoesNotContain("\n\n", markdown.Split(new[] { "```" }, StringSplitOptions.None)[1]);
    }

    [Fact]
    public async Task GenerateMarkdownAsync_NoCompactWhenDisabled()
    {
        // Arrange
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Compact = CompactLevel.None };

        var generator = new MarkdownGenerator(_logger);
        var fileEntry = new FileEntry("test.cs", "cs", "csharp", 20);
        var content = "line1\n\nline2\n\nline3"; // Content with empty lines
        var fileContents = new List<FileContent>
        {
            new(fileEntry, content, content.Length)
        };

        // Act
        var result = await generator.GenerateMarkdownAsync(fileContents, config);

        // Assert
        Assert.True(result.IsSuccess);
        var markdown = result.Value;

        // Should preserve empty lines when compact is disabled
        var contentSection = markdown.Split(new[] { "```" }, StringSplitOptions.None)[1];
        Assert.Contains("\n\n", contentSection); // Should have empty lines
    }
}
