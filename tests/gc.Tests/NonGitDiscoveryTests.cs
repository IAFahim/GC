using System.Diagnostics;
using Xunit.Abstractions;

namespace gc.Tests;

public class NonGitDiscoveryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDir;
    private readonly string _binaryPath;

    public NonGitDiscoveryTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Create isolated test directory first to avoid global state conflicts
        _testDir = Path.Combine(Path.GetTempPath(), $"gc_nongit_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        
        // Find the project root by looking for the .sln file starting from the base directory
        // Use a local variable to avoid sharing state across parallel test instances
        var searchStart = AppContext.BaseDirectory;
        var current = searchStart;
        while (current != null && !File.Exists(Path.Combine(current, "gc.sln")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        
        var projectRoot = current ?? throw new Exception("Could not find project root (gc.sln)");
        
        var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
        var binaryName = isWindows ? "gc.exe" : "gc";
        _binaryPath = Path.Combine(projectRoot, "src", "gc.CLI", "bin", "Debug", "net10.0", binaryName);

        if (!File.Exists(_binaryPath))
        {
            throw new Exception($"Could not find binary at {_binaryPath}. Please build the project first.");
        }
    }

    [Fact]
    public void NonGitDiscovery_BasicFunctionality_Works()
    {
        _output.WriteLine("Testing non-git discovery with basic files...");

        // Create test files
        File.WriteAllText(Path.Combine(_testDir, "test.js"), "console.log('test');");
        File.WriteAllText(Path.Combine(_testDir, "test.cs"), "public class Test { }");
        File.WriteAllText(Path.Combine(_testDir, "test.py"), "print('test')");

            var outputFile = Path.Combine(_testDir, "output.md");
            var result = RunGC(_testDir, $"--output {outputFile}");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("[OK] Exported to", result.StandardOutput);
            Assert.Contains(outputFile, result.StandardOutput);
            
            var content = File.ReadAllText(outputFile);
            Assert.Contains("test.js", content);
            Assert.Contains("test.cs", content);

            _output.WriteLine($"✅ Non-git discovery works with multiple files");
    }

    [Fact]
    public void NonGitDiscovery_ExtensionFilter_Works()
    {
        _output.WriteLine("Testing non-git discovery with extension filter...");

        // Create test files
        File.WriteAllText(Path.Combine(_testDir, "test.js"), "console.log('test');");
        File.WriteAllText(Path.Combine(_testDir, "test.cs"), "public class Test { }");
        File.WriteAllText(Path.Combine(_testDir, "test.py"), "print('test')");

            var outputFile = Path.Combine(_testDir, "output.md");
            var result = RunGC(_testDir, $"--extension js --output {outputFile}");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("[OK] Exported to", result.StandardOutput);
            
            var content = File.ReadAllText(outputFile);
            Assert.Contains("test.js", content);
            Assert.DoesNotContain("test.cs", content);

            _output.WriteLine($"✅ Extension filtering works in non-git mode");
    }

    [Fact]
    public void NonGitDiscovery_PresetFilter_Works()
    {
        _output.WriteLine("Testing non-git discovery with preset filter...");

        // Create test files
        File.WriteAllText(Path.Combine(_testDir, "test.js"), "console.log('test');");
        File.WriteAllText(Path.Combine(_testDir, "test.cs"), "public class Test { }");
        File.WriteAllText(Path.Combine(_testDir, "test.py"), "print('test')");

            var outputFile = Path.Combine(_testDir, "output.md");
            var result = RunGC(_testDir, $"--preset web --output {outputFile}");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("[OK] Exported to", result.StandardOutput);
            
            var content = File.ReadAllText(outputFile);
            Assert.Contains("test.js", content);

            _output.WriteLine($"✅ Preset filtering works in non-git mode");
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

            var outputFile = Path.Combine(_testDir, "output.md");
            var result = RunGC(_testDir, $"--extension js --output {outputFile}");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("[OK] Exported to", result.StandardOutput);
            
            var content = File.ReadAllText(outputFile);
            Assert.Contains("test.js", content);
            Assert.DoesNotContain("node_modules", content);

            _output.WriteLine($"✅ System directories are properly ignored");
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

            var outputFile = Path.Combine(_testDir, "output.md");
            var result = RunGC(_testDir, $"--preset web --output {outputFile}");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("[OK] Exported to", result.StandardOutput);
            
            var content = File.ReadAllText(outputFile);
            Assert.Contains("helper.ts", content);
            Assert.Contains("parser.js", content);

            _output.WriteLine($"✅ Nested directories are handled correctly");
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

            var outputFile = Path.Combine(_testDir, "output.md");
            var result = RunGC(_testDir, $"--paths src --output {outputFile}");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("[OK] Exported to", result.StandardOutput);
            
            var content = File.ReadAllText(outputFile);
            Assert.Contains("src/file1.cs", content);
            Assert.DoesNotContain("lib/file2.cs", content);
            Assert.DoesNotContain("root.cs", content);

            _output.WriteLine($"✅ Paths filtering works in non-git mode");
    }

    [Fact]
    public void NonGitDiscovery_ExplicitFileSystemMode_Works()
    {
        _output.WriteLine("Testing explicit filesystem discovery mode...");

        File.WriteAllText(Path.Combine(_testDir, "test.cs"), "public class Test { }");

            var outputFile = Path.Combine(_testDir, "output.md");
            var result = RunGC(_testDir, $"--discovery filesystem --output {outputFile}");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("[OK] Exported to", result.StandardOutput);
            
            var content = File.ReadAllText(outputFile);
            Assert.Contains("test.cs", content);

            _output.WriteLine($"✅ Explicit filesystem mode works");
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

            var outputFile = Path.Combine(_testDir, "third_party_copy.md");
            var result = RunGC(libDir, $"--preset web --output {outputFile}");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("[OK] Exported to", result.StandardOutput);
            Assert.True(File.Exists(outputFile));

            var content = File.ReadAllText(outputFile);
            Assert.Contains("src/index.ts", content);
            Assert.Contains("lib/helper.js", content);
            // Should include source files, not dist files
            Assert.DoesNotContain("dist/bundle.js", content);

            _output.WriteLine($"✅ Non-git mode is useful for analyzing third-party libraries");
    }

    private ProcessResult RunGC(string workingDir, string args)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = _binaryPath,
            Arguments = args,
            WorkingDirectory = workingDir,
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
        TryDeleteDirectory(_testDir);
    }

    private void TryDeleteDirectory(string path, int retryCount = 0)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            // Reset file attributes to Normal to ensure deletable
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch { }
            }

            Directory.Delete(path, true);
        }
        catch when (retryCount < 3)
        {
            // Retry with exponential backoff
            System.Threading.Thread.Sleep(100 * (retryCount + 1));
            TryDeleteDirectory(path, retryCount + 1);
        }
        catch
        {
            // Ignore cleanup errors after retries
        }
    }

    private record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
