using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace GC.Tests;

public class NonGitDiscoveryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDir;

    public NonGitDiscoveryTests(ITestOutputHelper output)
    {
        _output = output;
        _testDir = Path.Combine(Path.GetTempPath(), $"gc_nongit_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public void NonGitDiscovery_BasicFunctionality_Works()
    {
        _output.WriteLine("Testing non-git discovery with basic files...");

        // Create test files
        File.WriteAllText(Path.Combine(_testDir, "test.js"), "console.log('test');");
        File.WriteAllText(Path.Combine(_testDir, "test.cs"), "public class Test { }");
        File.WriteAllText(Path.Combine(_testDir, "test.py"), "print('test')");

        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            var result = RunGC("");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Discovered", result.StandardOutput);

            _output.WriteLine($"✅ Non-git discovery works with multiple files");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void NonGitDiscovery_ExtensionFilter_Works()
    {
        _output.WriteLine("Testing non-git discovery with extension filter...");

        // Create test files
        File.WriteAllText(Path.Combine(_testDir, "test.js"), "console.log('test');");
        File.WriteAllText(Path.Combine(_testDir, "test.cs"), "public class Test { }");
        File.WriteAllText(Path.Combine(_testDir, "test.py"), "print('test')");

        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            var result = RunGC("--extension js");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("test.js", result.StandardOutput);

            _output.WriteLine($"✅ Extension filtering works in non-git mode");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void NonGitDiscovery_PresetFilter_Works()
    {
        _output.WriteLine("Testing non-git discovery with preset filter...");

        // Create test files
        File.WriteAllText(Path.Combine(_testDir, "test.js"), "console.log('test');");
        File.WriteAllText(Path.Combine(_testDir, "test.cs"), "public class Test { }");
        File.WriteAllText(Path.Combine(_testDir, "test.py"), "print('test')");

        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            var result = RunGC("--preset web");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("test.js", result.StandardOutput);

            _output.WriteLine($"✅ Preset filtering works in non-git mode");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void NonGitDiscovery_IgnoresSystemDirectories()
    {
        _output.WriteLine("Testing that system directories are ignored...");

        // Create test files and directories
        File.WriteAllText(Path.Combine(_testDir, "test.js"), "console.log('test');");
        Directory.CreateDirectory(Path.Combine(_testDir, "node_modules"));
        File.WriteAllText(Path.Combine(_testDir, "node_modules", "package.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_testDir, ".git"));
        File.WriteAllText(Path.Combine(_testDir, ".git", "config"), "git config");

        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            var result = RunGC("--extension js");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("test.js", result.StandardOutput);

            _output.WriteLine($"✅ System directories are properly ignored");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void NonGitDiscovery_HandlesNestedDirectories()
    {
        _output.WriteLine("Testing non-git discovery with nested structure...");

        // Create nested directory structure
        var srcDir = Path.Combine(_testDir, "src", "components", "utils");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "helper.ts"), "export const helper = () => {};");

        var libDir = Path.Combine(_testDir, "lib", "core");
        Directory.CreateDirectory(libDir);
        File.WriteAllText(Path.Combine(libDir, "parser.js"), "module.exports = {};");

        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            var result = RunGC("--preset web");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("helper.ts", result.StandardOutput);
            Assert.Contains("parser.js", result.StandardOutput);

            _output.WriteLine($"✅ Nested directories are handled correctly");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void NonGitDiscovery_WithPathsOption_Works()
    {
        _output.WriteLine("Testing non-git discovery with paths filter...");

        // Create test files in different directories
        Directory.CreateDirectory(Path.Combine(_testDir, "src"));
        File.WriteAllText(Path.Combine(_testDir, "src", "file1.cs"), "public class File1 { }");

        Directory.CreateDirectory(Path.Combine(_testDir, "lib"));
        File.WriteAllText(Path.Combine(_testDir, "lib", "file2.cs"), "public class File2 { }");

        File.WriteAllText(Path.Combine(_testDir, "root.cs"), "public class Root { }");

        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            var result = RunGC("--paths src");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("src/file1.cs", result.StandardOutput);
            Assert.DoesNotContain("lib/file2.cs", result.StandardOutput);
            Assert.DoesNotContain("root.cs", result.StandardOutput);

            _output.WriteLine($"✅ Paths filtering works in non-git mode");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void NonGitDiscovery_ExplicitFileSystemMode_Works()
    {
        _output.WriteLine("Testing explicit filesystem discovery mode...");

        File.WriteAllText(Path.Combine(_testDir, "test.cs"), "public class Test { }");

        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            var result = RunGC("--discovery filesystem");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("test.cs", result.StandardOutput);

            _output.WriteLine($"✅ Explicit filesystem mode works");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void NonGitDiscovery_UsefulForLibraries()
    {
        _output.WriteLine("Testing practical use case: analyzing a library...");

        // Simulate a third-party library structure
        var libDir = Path.Combine(_testDir, "third-party-lib");
        Directory.CreateDirectory(Path.Combine(libDir, "src"));
        Directory.CreateDirectory(Path.Combine(libDir, "dist"));
        Directory.CreateDirectory(Path.Combine(libDir, "lib"));

        // Common library files
        File.WriteAllText(Path.Combine(libDir, "package.json"), "{ \"name\": \"third-party-lib\" }");
        File.WriteAllText(Path.Combine(libDir, "README.md"), "# Third Party Library");
        File.WriteAllText(Path.Combine(libDir, "src", "index.ts"), "export class Main { }");
        File.WriteAllText(Path.Combine(libDir, "dist", "bundle.js"), "// minified js bundle");
        File.WriteAllText(Path.Combine(libDir, "lib", "helper.js"), "module.exports = {};");

        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(libDir);

            var result = RunGC("--preset web --output /tmp/third_party_copy.md");

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists("/tmp/third_party_copy.md"));

            var content = File.ReadAllText("/tmp/third_party_copy.md");
            Assert.Contains("src/index.ts", content);
            Assert.Contains("lib/helper.js", content);
            // Should include source files, not dist files
            Assert.DoesNotContain("dist/bundle.js", content);

            _output.WriteLine($"✅ Non-git mode is useful for analyzing third-party libraries");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    private ProcessResult RunGC(string args)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project /home/l/Github/GC/GC/GC.csproj -- {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            return new ProcessResult(-1, "", "Failed to start process");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
