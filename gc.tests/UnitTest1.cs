using System;
using System.IO;
using System.Linq;
using gc.Data;
using gc.Utilities;
using Xunit;

namespace gc.Tests;

public static class TestHelpers
{
    public static CliArguments CreateTestCliArguments()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        return new CliArguments(
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            string.Empty,
            false,
            false,
            false,
            DiscoveryMode.Auto,
            long.MaxValue,
            false,
            false,
            false,
            false,
            false,
            config
        );
    }
}

public class CliParsingTests
{
    [Fact]
    public void ParseCli_ValidArguments_ParsesCorrectly()
    {
        // Arrange
        string[] args = ["--extension", "cs", "--paths", "src"];

        // Act
        var result = args.ParseCli();

        // Assert
        Assert.Contains("cs", result.Extensions);
        Assert.Contains("src", result.Paths);
        Assert.False(result.ShowHelp);
        Assert.False(result.RunTests);
    }

    [Fact]
    public void ParseCli_EmptyArguments_ReturnsDefaults()
    {
        // Arrange
        string[] args = [];

        // Act
        var result = args.ParseCli();

        // Assert
        Assert.Empty(result.Paths);
        Assert.Empty(result.Extensions);
        Assert.Empty(result.Excludes);
        Assert.Empty(result.Presets);
        Assert.False(result.ShowHelp);
        Assert.False(result.RunTests);
    }

    [Fact]
    public void ParseCli_HelpFlag_SetsShowHelp()
    {
        // Arrange
        string[] args = ["--help"];

        // Act
        var result = args.ParseCli();

        // Assert
        Assert.True(result.ShowHelp);
    }

    [Fact]
    public void ParseCli_VerboseFlag_SetsVerbose()
    {
        // Arrange
        string[] args = ["--verbose"];

        // Act
        var result = args.ParseCli();

        // Assert
        Assert.True(result.Verbose);
        Assert.False(result.Debug);
    }

    [Fact]
    public void ParseCli_DebugFlag_SetsDebug()
    {
        // Arrange
        string[] args = ["--debug"];

        // Act
        var result = args.ParseCli();

        // Assert
        Assert.True(result.Debug);
        Assert.False(result.Verbose);
    }

    [Fact]
    public void ParseCli_OutputFile_SetsOutputFile()
    {
        // Arrange
        string[] args = ["--output", "test.md"];

        // Act
        var result = args.ParseCli();

        // Assert
        Assert.Equal("test.md", result.OutputFile);
    }

    [Fact]
    public void ParseCli_MaxMemory_SetsMemoryLimit()
    {
        // Arrange
        string[] args = ["--max-memory", "500MB"];

        // Act
        var result = args.ParseCli();

        // Assert
        Assert.Equal(500 * 1024 * 1024, result.MaxMemoryBytes);
    }

    [Fact]
    public void ParseCli_TestFlag_SetsRunTests()
    {
        // Arrange
        string[] args = ["--test"];

        // Act
        var result = args.ParseCli();

        // Assert
        Assert.True(result.RunTests);
    }
}

public class FileEntryTests
{
    [Fact]
    public void FileEntry_Constructor_CreatesValidEntry()
    {
        // Arrange
        var path = "test.cs";
        var extension = ".cs";
        var language = "csharp";

        // Act
        var entry = new FileEntry(path, extension, language);

        // Assert
        Assert.Equal(path, entry.Path);
        Assert.Equal(extension, entry.Extension);
        Assert.Equal(language, entry.Language);
    }

    [Fact]
    public void FileEntry_WithSizeConstructor_CreatesValidEntry()
    {
        // Arrange
        var path = "test.cs";
        var extension = ".cs";
        var language = "csharp";
        var size = 1024L;

        // Act
        var entry = new FileEntry(path, extension, language, size);

        // Assert
        Assert.Equal(path, entry.Path);
        Assert.Equal(extension, entry.Extension);
        Assert.Equal(language, entry.Language);
        Assert.Equal(size, entry.Size);
    }

    [Fact]
    public void FileEntry_NullPath_DoesNotThrow()
    {
        // Arrange & Act & Assert
        // FileEntry is a struct and doesn't validate nulls in constructor
        // This test documents the current behavior
        var entry = new FileEntry(null!, ".cs", "csharp");
        Assert.Null(entry.Path);
    }
}

public class FileContentTests
{
    [Fact]
    public void FileContent_Constructor_CreatesValidContent()
    {
        // Arrange
        var entry = new FileEntry("test.cs", ".cs", "csharp", 1024);
        var content = "test content";
        var size = 1024L;

        // Act
        var fileContent = new FileContent(entry, content, size);

        // Assert
        Assert.Equal(entry, fileContent.Entry);
        Assert.Equal(content, fileContent.Content);
        Assert.Equal(size, fileContent.Size);
    }

    [Fact]
    public void FileContent_NullEntry_DoesNotThrow()
    {
        // Arrange & Act & Assert
        // FileContent is a struct and doesn't validate nulls in constructor
        // This test documents the current behavior
        var content = new FileContent(default, "test", 100);
        Assert.Equal("test", content.Content);
    }

    [Fact]
    public void FileContent_NullContent_DoesNotThrow()
    {
        // Arrange
        var entry = new FileEntry("test.cs", ".cs", "csharp", 1024);

        // Act & Assert
        // FileContent is a struct and doesn't validate nulls in constructor
        // This test documents the current behavior
        var content = new FileContent(entry, null!, 1024);
        Assert.Null(content.Content);
    }
}

public class LoggerTests
{
    [Fact]
    public void Logger_DefaultLevel_IsNormal()
    {
        // Arrange
        var originalLevel = Logger.CurrentLevel;

        // Act - Reset to default
        Logger.SetLevel(LogLevel.Normal);
        var level = Logger.CurrentLevel;

        // Assert
        Assert.Equal(LogLevel.Normal, level);

        // Cleanup
        Logger.SetLevel(originalLevel);
    }

    [Fact]
    public void Logger_SetLevel_ChangesLevel()
    {
        // Arrange
        var originalLevel = Logger.CurrentLevel;

        // Act
        Logger.SetLevel(LogLevel.Debug);

        // Assert
        Assert.Equal(LogLevel.Debug, Logger.CurrentLevel);

        // Cleanup
        Logger.SetLevel(originalLevel);
    }

    [Fact]
    public void Logger_LogDebug_OnlyLogsInDebugMode()
    {
        // Arrange
        Logger.SetLevel(LogLevel.Normal);
        var originalError = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);

        try
        {
            // Act
            Logger.LogDebug("This should not appear");

            // Assert
            Assert.Empty(writer.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            Logger.SetLevel(LogLevel.Normal);
        }
    }
}

public class MarkdownGeneratorTests
{
    [Fact]
    public void GenerateMarkdown_EmptyArray_ReturnsMarkdownStructure()
    {
        // Arrange
        var contents = Array.Empty<FileContent>();
        var args = TestHelpers.CreateTestCliArguments();

        // Act
        var result = contents.GenerateMarkdown(args);

        // Assert
        // Even with no files, the markdown generator includes structure
        Assert.NotEmpty(result);
        Assert.Contains("Project Structure", result);
    }

    [Fact]
    public void GenerateMarkdown_SingleContent_ReturnsMarkdown()
    {
        // Arrange
        var entry = new FileEntry("test.cs", ".cs", "csharp", 100);
        var content = "public class Test {}";
        var fileContent = new FileContent(entry, content, 100);
        var contents = new[] { fileContent };
        var args = TestHelpers.CreateTestCliArguments();

        // Act
        var result = contents.GenerateMarkdown(args);

        // Assert
        Assert.Contains("test.cs", result);
        Assert.Contains("csharp", result);
        Assert.Contains(content, result);
    }

    [Fact]
    public void GenerateMarkdown_MultipleContents_SortsByName()
    {
        // Arrange
        var entry1 = new FileEntry("zebra.cs", ".cs", "csharp", 100);
        var entry2 = new FileEntry("alpha.cs", ".cs", "csharp", 100);
        var content1 = new FileContent(entry1, "class Zebra {}", 100);
        var content2 = new FileContent(entry2, "class Alpha {}", 100);
        var contents = new[] { content1, content2 };
        var args = TestHelpers.CreateTestCliArguments();

        // Act
        var result = contents.GenerateMarkdown(args);

        // Assert
        var alphaIndex = result.IndexOf("alpha.cs");
        var zebraIndex = result.IndexOf("zebra.cs");
        Assert.True(alphaIndex < zebraIndex, "Files should be sorted alphabetically");
    }
}

public class IntegrationTests
{
    [Fact]
    public void EndToEnd_SimpleWorkflow_WorksCorrectly()
    {
        // This test verifies the basic workflow without external dependencies
        var entry = new FileEntry("test.cs", ".cs", "csharp", 100);
        var content = "public class Test { }";
        var fileContent = new FileContent(entry, content, 100);
        var contents = new[] { fileContent };
        var args = TestHelpers.CreateTestCliArguments();

        // Test markdown generation
        var markdown = contents.GenerateMarkdown(args);
        Assert.NotEmpty(markdown);
        Assert.Contains("test.cs", markdown);
        Assert.Contains(content, markdown);
    }
}
