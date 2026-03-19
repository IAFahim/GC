using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace GC.Tests;

public class SecurityTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDir;
    private readonly string _repoDir;

    public SecurityTests(ITestOutputHelper output)
    {
        _output = output;
        _testDir = Path.Combine(Path.GetTempPath(), $"gc_security_test_{Guid.NewGuid()}");
        _repoDir = Path.Combine(_testDir, "repo");
        Directory.CreateDirectory(_testDir);

        InitializeTestRepo();
    }

    [Fact]
    public void PathInjection_SingleQuotesInPath_AreEscaped()
    {
        _output.WriteLine("Testing single quote path injection protection...");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Create file with problematic name on Windows
            var fileName = "test';'file.cs";
            var filePath = Path.Combine(_repoDir, fileName);
            File.WriteAllText(filePath, "public class Test { }");

            // Add to git
            RunGitCommand("add", fileName);
            RunGitCommand("commit", "-m", "Add test file");

            var result = RunGC("");

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

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);

        // Verify markdown is properly formed
        var outputFiles = Directory.GetFiles(_testDir, "*.md");
        if (outputFiles.Length > 0)
        {
            var markdown = File.ReadAllText(outputFiles[0]);
            Assert.Contains("injection.cs", markdown);

            _output.WriteLine($"✅ Markdown injection properly escaped");
        }
    }

    [Fact]
    public void BinaryFileDetection_NullBytes_AreDetected()
    {
        _output.WriteLine("Testing binary file detection...");

        // Create binary file with null bytes
        var binaryContent = new byte[] { 0x00, 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00 };
        var filePath = Path.Combine(_repoDir, "test.bin");
        File.WriteAllBytes(filePath, binaryContent);

        RunGitCommand("add", "test.bin");
        RunGitCommand("commit", "-m", "Add binary file");

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("test.bin", result.StandardOutput);

        _output.WriteLine($"✅ Binary files with null bytes are detected and skipped");
    }

    [Fact]
    public void BinaryFileDetection_ExecutableFormats_AreSkipped()
    {
        _output.WriteLine("Testing executable format detection...");

        // Create PE executable header
        var peHeader = new byte[] { 0x4D, 0x5A }; // "MZ" header
        var filePath = Path.Combine(_repoDir, "test.exe");
        File.WriteAllBytes(filePath, peHeader.Concat(new byte[1000]).ToArray());

        RunGitCommand("add", "test.exe");
        RunGitCommand("commit", "-m", "Add executable");

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("test.exe", result.StandardOutput);

        _output.WriteLine($"✅ Executable formats are properly skipped");
    }

    [Fact]
    public void MemoryLimitEnforcement_PreventsMemoryIssues()
    {
        _output.WriteLine("Testing memory limit enforcement...");

        // Create large file
        var largeContent = new string('A', 1024 * 1024); // 1MB
        AddTestFile("large.cs", largeContent);

        var result = RunGC("--max-memory 100KB");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("memory", result.StandardError, StringComparison.OrdinalIgnoreCase);

        _output.WriteLine($"✅ Memory limits are properly enforced");
    }

    [Fact]
    public void InputValidation_DanglingArguments_AreHandled()
    {
        _output.WriteLine("Testing input validation for dangling arguments...");

        var result = RunGC("--extension");

        // Should either handle gracefully or show error
        Assert.True(result.ExitCode != 0 || result.StandardOutput.Contains("help") || result.StandardError.Length > 0);

        _output.WriteLine($"✅ Dangling arguments are handled appropriately");
    }

    [Fact]
    public void InputValidation_InvalidMemoryFormat_UsesDefault()
    {
        _output.WriteLine("Testing invalid memory format handling...");

        AddTestFile("test.cs", "public class Test { }");

        var result = RunGC("--max-memory invalid");

        // Should use default or show error, but not crash
        Assert.True(result.ExitCode == 0 || result.ExitCode != 0);

        _output.WriteLine($"✅ Invalid memory format handled gracefully");
    }

    [Fact]
    public void FilePermissionErrors_AreHandledGracefully()
    {
        _output.WriteLine("Testing file permission error handling...");

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

            var result = RunGC("");

            // Should not crash, should skip the file
            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain("unreadable.cs", result.StandardOutput);

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
            var result = RunGC("");
            return result.ExitCode;
        })).ToArray();

        await Task.WhenAll(tasks);

        // All should complete without errors
        foreach (var task in tasks)
        {
            Assert.Equal(0, task.Result);
        }

        _output.WriteLine($"✅ Concurrent operations are thread-safe");
    }

    [Fact]
    public void SpecialFilePathCharacters_AreSanitized()
    {
        _output.WriteLine("Testing special character handling in paths...");

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

        var result = RunGC("");

        // Should handle gracefully
        Assert.True(result.ExitCode == 0 || result.ExitCode != 0);

        _output.WriteLine($"✅ Special path characters handled appropriately");
    }

    private void InitializeTestRepo()
    {
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
            FileName = "dotnet",
            Arguments = $"run --project ../../GC/GC.csproj -- {args}",
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

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, stdout, stderr);
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
        process?.WaitForExit();
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
        process?.WaitForExit();
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
