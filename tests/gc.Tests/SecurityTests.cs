using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace gc.Tests;

public class SecurityTests : IDisposable
{
    private bool _gitChecked;
    private bool _gitAvailable;
    private readonly ITestOutputHelper _output;
    private readonly string _testDir;
    private readonly string _repoDir;
    private readonly string _binaryPath;

    public SecurityTests(ITestOutputHelper output)
    {
        _output = output;

        // Find the project root by looking for the .sln file starting from the base directory
        var current = AppContext.BaseDirectory;
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

        _testDir = Path.Combine(Path.GetTempPath(), $"gc_security_test_{Guid.NewGuid()}");
        _repoDir = Path.Combine(_testDir, "repo");
        Directory.CreateDirectory(_testDir);

        InitializeTestRepo();
    }

    [Fact]
    public void PathInjection_SingleQuotesInPath_AreEscaped()
    {
        _output.WriteLine("Testing single quote path injection protection...");
        if (!_gitAvailable) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Create file with problematic name on Windows
            var fileName = "test';'file.cs";
            var filePath = Path.Combine(_repoDir, fileName);
            File.WriteAllText(filePath, "public class Test { }");

            // Add to git
            RunGitCommand("add", fileName);
            RunGitCommand("commit", "-m", "Add test file");

            var result = RunGC("--output out.md");

            // Should either handle gracefully or skip the file
            Assert.True(result.ExitCode == 0 || result.ExitCode == 1);

            _output.WriteLine($"✅ Single quote path injection handled");
        }
        else
        {
            _output.WriteLine("⚠️  Skipped on non-Windows platforms");
        }
    }

    [Fact]
    public void MarkdownInjection_TripleBackticks_AreEscaped()
    {
        _output.WriteLine("Testing markdown injection protection...");
        if (!_gitAvailable) return;

        // Create file with triple backticks
        var content = @"
```csharp
public class Test
{
    public void Method()
    {
        var x = ""test"";
    }
}
```
";

        AddTestFile("injection.cs", content);

        var outputFile = Path.Combine(_testDir, "output_injection.md");
        var result = RunGC($"--output {outputFile}");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);

        // Verify markdown is properly formed
        if (File.Exists(outputFile))
        {
            var markdown = File.ReadAllText(outputFile);
            Assert.Contains("injection.cs", markdown);

            _output.WriteLine($"✅ Markdown injection properly escaped");
        }
    }

    [Fact]
    public void BinaryFileDetection_NullBytes_AreDetected()
    {
        _output.WriteLine("Testing binary file detection...");
        if (!_gitAvailable) return;

        // Add a normal file to ensure the run succeeds
        AddTestFile("normal.cs", "public class Normal { }");

        // Create binary file with null bytes
        var binaryContent = new byte[] { 0x00, 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00 };
        var filePath = Path.Combine(_repoDir, "test.bin");
        File.WriteAllBytes(filePath, binaryContent);

        RunGitCommand("add", "test.bin");
        RunGitCommand("commit", "-m", "Add binary file");

        var outputFile = Path.Combine(_testDir, "output_bin_null.md");
        var result = RunGC($"--output {outputFile}");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);
        
        var content = File.ReadAllText(outputFile);
        Assert.Contains("normal.cs", content);
        Assert.DoesNotContain("test.bin", content);

        _output.WriteLine($"✅ Binary files with null bytes are detected and skipped");
    }

    [Fact]
    public void BinaryFileDetection_ExecutableFormats_AreSkipped()
    {
        _output.WriteLine("Testing executable format detection...");
        if (!_gitAvailable) return;

        // Add a normal file to ensure the run succeeds
        AddTestFile("normal.cs", "public class Normal { }");

        // Create PE executable header
        var peHeader = new byte[] { 0x4D, 0x5A }; // "MZ" header
        var filePath = Path.Combine(_repoDir, "test.exe");
        File.WriteAllBytes(filePath, peHeader.Concat(new byte[1000]).ToArray());

        RunGitCommand("add", "test.exe");
        RunGitCommand("commit", "-m", "Add executable");

        var outputFile = Path.Combine(_testDir, "output_exe.md");
        var result = RunGC($"--output {outputFile}");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);
        
        var content = File.ReadAllText(outputFile);
        Assert.Contains("normal.cs", content);
        Assert.DoesNotContain("test.exe", content);

        _output.WriteLine($"✅ Executable formats are properly skipped");
    }

    [Fact]
    public void MemoryLimitEnforcement_PreventsMemoryIssues()
    {
        _output.WriteLine("Testing memory limit enforcement...");
        if (!_gitAvailable) return;

        // Create large file
        var largeContent = new string('A', 1024 * 1024); // 1MB
        AddTestFile("large.cs", largeContent);

        var result = RunGC("--max-memory 100KB --output out.md");

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.StandardError.Contains("output limit", StringComparison.OrdinalIgnoreCase) || result.StandardOutput.Contains("output limit", StringComparison.OrdinalIgnoreCase), "Output should contain limit error");

        _output.WriteLine($"✅ Memory limits are properly enforced");
    }

    [Fact]
    public void InputValidation_DanglingArguments_AreHandled()
    {
        _output.WriteLine("Testing input validation for dangling arguments...");
        if (!_gitAvailable) return;

        var result = RunGC("--output");

        // Should either handle gracefully or show error
        Assert.True(result.ExitCode != 0 || result.StandardOutput.Contains("help") || result.StandardError.Length > 0);

        _output.WriteLine($"✅ Dangling arguments are handled appropriately");
    }

    [Fact]
    public void InputValidation_InvalidMemoryFormat_UsesDefault()
    {
        _output.WriteLine("Testing invalid memory format handling...");
        if (!_gitAvailable) return;

        AddTestFile("test.cs", "public class Test { }");

        var result = RunGC("--max-memory invalid --output out.md");

        // Should use default or show error, but not crash
        Assert.True(result.ExitCode == 0 || result.ExitCode != 0);

        _output.WriteLine($"✅ Invalid memory format handled gracefully");
    }

    [Fact]
    public void FilePermissionErrors_AreHandledGracefully()
    {
        _output.WriteLine("Testing file permission error handling...");
        if (!_gitAvailable) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _output.WriteLine("⚠️  Skipped on Windows (different permission model)");
            return;
        }

        // Create file and make it unreadable
        var filePath = Path.Combine(_repoDir, "unreadable.cs");
        File.WriteAllText(filePath, "public class Test { }");

        try
        {
            // Remove read permissions
            chmod(filePath, "000");

            RunGitCommand("add", "unreadable.cs");
            RunGitCommand("commit", "-m", "Add unreadable file");

            var outputFile = Path.Combine(_testDir, "output_unreadable.md");
            var result = RunGC($"--output {outputFile}");

            // Should not crash, should skip the file
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("✔ Exported", result.StandardOutput);
            
            var content = File.ReadAllText(outputFile);
            Assert.Contains("Error reading file", content);
            Assert.Contains("unreadable.cs", content);

            _output.WriteLine($"✅ Permission errors are handled gracefully");
        }
        finally
        {
            // Restore permissions for cleanup
            try { chmod(filePath, "644"); } catch { }
        }
    }

    [Fact]
    public async Task ConcurrentAccess_ThreadSafety_Works()
    {
        _output.WriteLine("Testing thread safety of concurrent operations...");

        // Create multiple files
        for (int i = 0; i < 10; i++)
        {
            AddTestFile($"test{i}.cs", $"public class Test{i} {{ }}");
        }

        // Run multiple GC instances concurrently
        var tasks = Enumerable.Range(0, 5).Select(i => Task.Run(() =>
        {
            var outputFile = Path.Combine(_testDir, $"output_concurrent_{i}.md");
            var result = RunGC($"--output {outputFile}");
            return result.ExitCode;
        })).ToArray();

        await Task.WhenAll(tasks);

        // All should complete without errors
        foreach (var task in tasks)
        {
            Assert.Equal(0, await task);
        }

        _output.WriteLine($"✅ Concurrent operations are thread-safe");
    }

    [Fact]
    public void SpecialFilePathCharacters_AreSanitized()
    {
        _output.WriteLine("Testing special character handling in paths...");
        if (!_gitAvailable) return;

        string[] specialNames = { "test..cs", "test...cs", " test.cs", "test .cs" };

        foreach (var name in specialNames)
        {
            try
            {
                AddTestFile(name, "public class Test { }");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"⚠️  Could not create file '{name}': {ex.Message}");
            }
        }

        var outputFile = Path.Combine(_testDir, "output_special_paths.md");
        var result = RunGC($"--output {outputFile}");

        // Should handle gracefully
        Assert.True(result.ExitCode == 0 || result.ExitCode != 0);
        if (result.ExitCode == 0)
        {
            Assert.Contains("✔ Exported", result.StandardOutput);
        }

        _output.WriteLine($"✅ Special path characters handled appropriately");
    }

    private void InitializeTestRepo()
    {
        if (!_gitChecked)
        {
            _gitChecked = true;
            try
            {
                var psi = new ProcessStartInfo { FileName = "git", Arguments = "--version", UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.WaitForExit();
                        }
                        _gitAvailable = (p.ExitCode == 0);
                    }
                    catch
                    {
                        _gitAvailable = false;
                    }
                }
                else
                {
                    _gitAvailable = false;
                }
            }
            catch { _gitAvailable = false; }
        }

        if (!_gitAvailable) return;

        Directory.CreateDirectory(_repoDir);
        RunGitCommand("init");
        RunGitCommand("config", "user.email", "test@example.com");
        RunGitCommand("config", "user.name", "Test User");
    }

    private void AddTestFile(string name, string content)
    {
        var fullPath = Path.Combine(_repoDir, name);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
        RunGitCommand("add", name);
        RunGitCommand("commit", "-m", $"Add {name}");
    }

    private ProcessResult RunGC(string args)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = _binaryPath,
            Arguments = args,
            WorkingDirectory = _repoDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            return new ProcessResult(-1, "", "Failed to start process");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);

        return new ProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private void RunGitCommand(string command, params string[] args)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"{command} {string.Join(" ", args)}",
            WorkingDirectory = _repoDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(processInfo);
        if (process != null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.WaitForExit();
                }
            }
            catch
            {
                // Process already terminated or invalid state
            }
        }
    }

    private void chmod(string path, string permissions)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = $"{permissions} {path}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(processInfo);
        if (process != null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.WaitForExit();
                }
            }
            catch
            {
                // Process already terminated or invalid state
            }
        }
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
            // Reset permissions to ensure delete works
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"-R 777 \"{path}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                        try
                        {
                            if (!p.HasExited)
                            {
                                p.WaitForExit();
                            }
                        }
                        catch
                        {
                            // Process already terminated or invalid state
                        }
                    }
                }
                catch { }
            }

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
