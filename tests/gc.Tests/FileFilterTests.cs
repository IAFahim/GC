using gc.Application.Services;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Logging;
using Xunit;

namespace gc.Tests.FeatureTests;

public class FileFilterTests
{
    private readonly ILogger _logger;
    private readonly FileFilter _filter;

    public FileFilterTests()
    {
        _logger = new ConsoleLogger();
        _filter = new FileFilter(_logger);
    }

    [Fact]
    public void FilterFiles_WithExtensions_FiltersCorrectly()
    {
        var rawFiles = new[] { "src/main.cs", "docs/readme.md", "Dockerfile", "app/program.cs", "src/util.CS" };
        var config = new GcConfiguration();
        
        // Exact extensions matching (case insensitive)
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), new[] { "cs", "Dockerfile" });
        
        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        
        Assert.Equal(4, entries.Count);
        Assert.Contains(entries, e => e.Path == "src/main.cs");
        Assert.Contains(entries, e => e.Path == "Dockerfile");
        Assert.Contains(entries, e => e.Path == "app/program.cs");
        Assert.Contains(entries, e => e.Path == "src/util.CS");
        Assert.DoesNotContain(entries, e => e.Path == "docs/readme.md");
    }

    [Fact]
    public void FilterFiles_WithExcludePatterns_UsesAhoCorasickCorrectly()
    {
        var rawFiles = new[] { 
            "src/main.cs", 
            "src/node_modules/lodash/index.js", 
            "build/output.dll", 
            "src/.git/config",
            "foo.bin",
            "bin/debug/app.exe"
        };
        
        var config = new GcConfiguration
        {
            Filters = new FiltersConfiguration
            {
                SystemIgnoredPatterns = new[] { "node_modules", ".git" }
            }
        };
        
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), new[] { ".bin", "build/" }, Array.Empty<string>());
        
        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Path == "src/main.cs");
        Assert.Contains(entries, e => e.Path == "bin/debug/app.exe"); // 'bin/' doesn't match '.bin' or 'build/'
        
        Assert.DoesNotContain(entries, e => e.Path == "src/node_modules/lodash/index.js");
        Assert.DoesNotContain(entries, e => e.Path == "build/output.dll");
        Assert.DoesNotContain(entries, e => e.Path == "src/.git/config");
        Assert.DoesNotContain(entries, e => e.Path == "foo.bin");
    }

    [Fact]
    public void FilterFiles_WithSearchPaths_FiltersCorrectly()
    {
        var rawFiles = new[] { "src/main.cs", "test/test.cs", "docs/readme.md", "src/utils/math.cs" };
        var config = new GcConfiguration();
        
        var result = _filter.FilterFiles(rawFiles, config, new[] { "src" }, Array.Empty<string>(), Array.Empty<string>());
        
        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Path == "src/main.cs");
        Assert.Contains(entries, e => e.Path == "src/utils/math.cs");
        Assert.DoesNotContain(entries, e => e.Path == "test/test.cs");
    }
}
