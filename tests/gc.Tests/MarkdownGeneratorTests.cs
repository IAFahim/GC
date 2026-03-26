using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using gc.Application.Services;
using gc.Domain.Constants;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.IO;
using gc.Infrastructure.Logging;
using Xunit;

namespace gc.Tests;

public class MarkdownGeneratorTests
{
    private readonly ConsoleLogger _logger = new();
    private readonly FileReader _reader;
    private readonly GcConfiguration _config = BuiltInPresets.GetDefaultConfiguration();

    public MarkdownGeneratorTests()
    {
        _reader = new FileReader(_logger);
    }

    [Fact]
    public async Task ExcludeLineIfStart_InMemory_FiltersCorrectly()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var entry = new FileEntry("test.cs", "cs", "cs", 100);
        var content = "using System;\n// This is a comment\npublic class Test {\n  // Another comment\n}\n\n";
        
        var fileContents = new List<FileContent> { new(entry, content, content.Length) };
        using var output = new MemoryStream();
        
        var excludes = new[] { "//", "\n" };

        var result = await generator.GenerateMarkdownStreamingAsync(
            fileContents, 
            output, 
            _config, 
            excludes,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var str = Encoding.UTF8.GetString(output.ToArray());
        
        Assert.Contains("using System;", str);
        Assert.Contains("public class Test {", str);
        Assert.Contains("}", str);
        
        // Excluded
        Assert.DoesNotContain("// This is a comment", str);
        Assert.DoesNotContain("// Another comment", str);
    }

    [Fact]
    public async Task ExcludeLineIfStart_Streaming_FiltersCorrectly()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var tempFile = Path.GetTempFileName();
        
        try
        {
            await File.WriteAllTextAsync(tempFile, "using System;\r\n// A comment\npublic class Test {\n  // Another comment\r\n}");
            var entry = new FileEntry(tempFile, "cs", "cs", 100);
            
            // Content is null so it falls back to streaming
            var fileContents = new List<FileContent> { new(entry, null, 100) };
            using var output = new MemoryStream();
            
            var excludes = new[] { "//", "\n" };

            var result = await generator.GenerateMarkdownStreamingAsync(
                fileContents, 
                output, 
                _config, 
                excludes,
                CancellationToken.None
            );

            Assert.True(result.IsSuccess);
            var str = Encoding.UTF8.GetString(output.ToArray());
            
            Assert.Contains("using System;", str);
            Assert.Contains("public class Test {", str);
            Assert.Contains("}", str);
            
            // Excluded
            Assert.DoesNotContain("// A comment", str);
            Assert.DoesNotContain("// Another comment", str);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ProcessLineSequence_HandlesEmptyLine_WithNewlineExclude()
    {
        var generator = new MarkdownGenerator(_logger, _reader);
        var entry = new FileEntry("test.cs", "cs", "cs", 100);
        var content = "using System;\n\npublic class Test {}";
        
        var fileContents = new List<FileContent> { new(entry, content, content.Length) };
        using var output = new MemoryStream();
        
        var excludes = new[] { "\n" };

        var result = await generator.GenerateMarkdownStreamingAsync(
            fileContents, 
            output, 
            _config, 
            excludes,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var str = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("using System;", str);
        Assert.Contains("public class Test {}", str);
        Assert.DoesNotContain("using System;\n\npublic", str); // Ensure no double newline exists in content area
    }
}
