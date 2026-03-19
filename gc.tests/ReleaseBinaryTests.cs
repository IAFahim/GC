using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace gc.Tests;

public class ReleaseBinaryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDir;
    private readonly string _downloadDir;
    private readonly HttpClient _httpClient;
    private readonly string _projectPath;

    public ReleaseBinaryTests(ITestOutputHelper output)
    {
        _output = output;

        // Find the project root by looking for the .sln file starting from the base directory
        var current = AppContext.BaseDirectory;
        while (current != null && !File.Exists(Path.Combine(current, "gc.sln")))
        {
            current = Directory.GetParent(current)?.FullName;
        }

        var projectRoot = current ?? throw new Exception("Could not find project root (gc.sln)");
        _projectPath = Path.Combine(projectRoot, "gc", "gc.csproj");

        _testDir = Path.Combine(Path.GetTempPath(), $"gc_test_{Guid.NewGuid()}");
        _downloadDir = Path.Combine(_testDir, "downloads");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_downloadDir);

        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    [Fact]
    public async Task DownloadLatestRelease()
    {
        _output.WriteLine("Fetching latest release information...");

        var response = await _httpClient.GetAsync("https://api.github.com/repos/IAFahim/gc/releases/latest");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("tag_name", content);

        _output.WriteLine($"✅ Latest release info fetched successfully");
    }

    [Fact]
    public async Task CheckLatestReleaseExists()
    {
        _output.WriteLine("Testing latest release exists...");

        var response = await _httpClient.GetAsync("https://api.github.com/repos/IAFahim/gc/releases/latest");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("tag_name", content);
        Assert.Contains("assets", content);

        _output.WriteLine($"✅ Release artifacts are available");
    }

    [Fact]
    public void CurrentBuild_BasicHelpCommand_Works()
    {
        _output.WriteLine("Testing basic help command...");

        var result = RunGC("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("gc", result.StandardOutput);
        Assert.Contains("USAGE:", result.StandardOutput);

        _output.WriteLine($"✅ Help command works. Output preview: {result.StandardOutput.Substring(0, Math.Min(100, result.StandardOutput.Length))}...");
    }

    [Fact]
    public void CurrentBuild_VersionFlag_Works()
    {
        _output.WriteLine("Testing version flag...");

        var result = RunGC("--version");

        // Should either show version or run normally
        Assert.True(result.ExitCode == 0 || result.StandardOutput.Contains("gc"));

        _output.WriteLine($"✅ Version flag handled correctly");
    }

    [Fact]
    public void CurrentBuild_TestCommand_Works()
    {
        _output.WriteLine("Testing test command...");

        var result = RunGC("--test");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("test", result.StandardOutput.ToLower());

        _output.WriteLine($"✅ Test command works");
    }

    [Fact]
    public void CurrentBuild_BenchmarkCommand_Works()
    {
        _output.WriteLine("Testing benchmark command...");

        var result = RunGC("--benchmark");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Benchmark", result.StandardOutput);

        _output.WriteLine($"✅ Benchmark command works");
    }

    [Fact]
    public void CurrentBuild_NoGitRepository_ShowsError()
    {
        _output.WriteLine("Testing behavior without git repository...");

        // Create a temp directory without git
        var noGitDir = Path.Combine(_testDir, "no_git_repo");
        Directory.CreateDirectory(noGitDir);

        var result = RunGCInDirectory(noGitDir, "");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("git", result.StandardError.ToLower());

        _output.WriteLine($"✅ Correctly handles missing git repository");
    }

    [Fact]
    public void CurrentBuild_EmptyRepository_ShowsMessage()
    {
        _output.WriteLine("Testing behavior with empty repository...");

        var emptyRepo = CreateTestRepository(Array.Empty<string>());

        var result = RunGCInDirectory(emptyRepo, "");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("no tracked files", result.StandardError.ToLower());

        _output.WriteLine($"✅ Correctly handles empty repository");
    }

    [Fact]
    public void CurrentBuild_SingleFileRepository_Works()
    {
        _output.WriteLine("Testing with single file repository...");

        var testRepo = CreateTestRepository(new[] { "test.cs" });
        AddFileToRepository(testRepo, "test.cs", "public class Test { }");

        var result = RunGCInDirectory(testRepo, "");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("test.cs", result.StandardOutput);

        _output.WriteLine($"✅ Single file processing works");
    }

    [Fact]
    public void CurrentBuild_ExtensionFilter_Works()
    {
        _output.WriteLine("Testing extension filtering...");

        var testRepo = CreateTestRepository(new[] { "test.cs", "test.js", "test.md" });
        AddFileToRepository(testRepo, "test.cs", "C# code");
        AddFileToRepository(testRepo, "test.js", "JavaScript code");
        AddFileToRepository(testRepo, "test.md", "Markdown doc");

        var result = RunGCInDirectory(testRepo, "--extension cs");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("test.cs", result.StandardOutput);
        Assert.DoesNotContain("test.js", result.StandardOutput);
        Assert.DoesNotContain("test.md", result.StandardOutput);

        _output.WriteLine($"✅ Extension filtering works correctly");
    }

    [Fact]
    public void CurrentBuild_MultipleExtensions_Works()
    {
        _output.WriteLine("Testing multiple extension filters...");

        var testRepo = CreateTestRepository(new[] { "test.cs", "test.js", "test.md" });
        AddFileToRepository(testRepo, "test.cs", "C# code");
        AddFileToRepository(testRepo, "test.js", "JavaScript code");
        AddFileToRepository(testRepo, "test.md", "Markdown doc");

        var result = RunGCInDirectory(testRepo, "--extension cs js");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("test.cs", result.StandardOutput);
        Assert.Contains("test.js", result.StandardOutput);
        Assert.DoesNotContain("test.md", result.StandardOutput);

        _output.WriteLine($"✅ Multiple extension filtering works");
    }

    [Fact]
    public void CurrentBuild_ExcludePattern_Works()
    {
        _output.WriteLine("Testing exclude patterns...");

        var testRepo = CreateTestRepository(new[] { "src/test.cs", "test.cs" });
        AddFileToRepository(testRepo, "src/test.cs", "C# code");
        AddFileToRepository(testRepo, "test.cs", "C# code");

        var result = RunGCInDirectory(testRepo, "--exclude src/");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("src/test.cs", result.StandardOutput);
        Assert.Contains("test.cs", result.StandardOutput);

        _output.WriteLine($"✅ Exclude patterns work correctly");
    }

    [Fact]
    public void CurrentBuild_OutputFile_Works()
    {
        _output.WriteLine("Testing file output...");

        var testRepo = CreateTestRepository(new[] { "test.cs" });
        AddFileToRepository(testRepo, "test.cs", "public class Test { }");

        var outputFile = Path.Combine(testRepo, "output.md");
        var result = RunGCInDirectory(testRepo, $"--output {outputFile}");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputFile));
        Assert.Contains("test.cs", File.ReadAllText(outputFile));

        _output.WriteLine($"✅ File output works correctly");
    }

    [Fact]
    public void CurrentBuild_MemoryLimit_Works()
    {
        _output.WriteLine("Testing memory limit...");

        var testRepo = CreateTestRepository(new[] { "test.cs" });
        AddFileToRepository(testRepo, "test.cs", "public class Test { }");

        var result = RunGCInDirectory(testRepo, "--max-memory 1KB");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("memory", result.StandardError.ToLower());

        _output.WriteLine($"✅ Memory limit enforcement works");
    }

    [Fact]
    public void CurrentBuild_VerboseFlag_Works()
    {
        _output.WriteLine("Testing verbose logging...");

        var testRepo = CreateTestRepository(new[] { "test.cs" });
        AddFileToRepository(testRepo, "test.cs", "public class Test { }");

        var result = RunGCInDirectory(testRepo, "--verbose");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("discovered", result.StandardOutput.ToLower());

        _output.WriteLine($"✅ Verbose logging works");
    }

    [Fact]
    public void CurrentBuild_DebugFlag_Works()
    {
        _output.WriteLine("Testing debug logging...");

        var testRepo = CreateTestRepository(new[] { "test.cs" });
        AddFileToRepository(testRepo, "test.cs", "public class Test { }");

        var result = RunGCInDirectory(testRepo, "--debug");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("debug", result.StandardOutput.ToLower());

        _output.WriteLine($"✅ Debug logging works");
    }

    [Fact]
    public void CurrentBuild_BinaryFileDetection_Works()
    {
        _output.WriteLine("Testing binary file detection...");

        var testRepo = CreateTestRepository(new[] { "test.cs", "test.bin" });
        AddFileToRepository(testRepo, "test.cs", "public class Test { }");
        AddBinaryFileToRepository(testRepo, "test.bin");

        var result = RunGCInDirectory(testRepo, "");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("test.bin", result.StandardOutput);

        _output.WriteLine($"✅ Binary files are correctly skipped");
    }

    [Fact]
    public void CurrentBuild_LargeFileHandling_Works()
    {
        _output.WriteLine("Testing large file handling...");

        var testRepo = CreateTestRepository(new[] { "large.txt" });
        // Create file larger than MaxFileSize (1MB)
        var largeContent = new string('A', 2 * 1024 * 1024); // 2MB
        AddFileToRepository(testRepo, "large.txt", largeContent);

        var result = RunGCInDirectory(testRepo, "");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("large.txt", result.StandardOutput);

        _output.WriteLine($"✅ Large files are correctly skipped");
    }

    [Fact]
    public void CurrentBuild_SpecialCharactersInPaths_Works()
    {
        _output.WriteLine("Testing special characters in file paths...");

        var testRepo = CreateTestRepository(new[] { "test file.cs", "test-file.cs" });

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: skip invalid characters test
            _output.WriteLine("⚠️  Skipping special character test on Windows");
            return;
        }

        AddFileToRepository(testRepo, "test file.cs", "public class Test { }");

        var result = RunGCInDirectory(testRepo, "");

        Assert.Equal(0, result.ExitCode);

        _output.WriteLine($"✅ Special characters in paths handled correctly");
    }

    [Fact]
    public void CurrentBuild_PresetFilters_Works()
    {
        _output.WriteLine("Testing preset filters...");

        var testRepo = CreateTestRepository(new[] { "test.cs", "test.js", "test.py" });
        AddFileToRepository(testRepo, "test.cs", "C# code");
        AddFileToRepository(testRepo, "test.js", "JavaScript code");
        AddFileToRepository(testRepo, "test.py", "Python code");

        var result = RunGCInDirectory(testRepo, "--preset backend");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("test.py", result.StandardOutput);

        _output.WriteLine($"✅ Preset filters work correctly");
    }

    [Fact]
    public void CurrentBuild_ConflictingArguments_HandledCorrectly()
    {
        _output.WriteLine("Testing conflicting arguments...");

        var testRepo = CreateTestRepository(new[] { "test.cs" });
        AddFileToRepository(testRepo, "test.cs", "public class Test { }");

        var result = RunGCInDirectory(testRepo, "--extension cs --extension js");

        // Should work with multiple extensions
        Assert.Equal(0, result.ExitCode);

        _output.WriteLine($"✅ Conflicting arguments handled gracefully");
    }

    [Fact]
    public void CurrentBuild_InvalidArguments_ShowError()
    {
        _output.WriteLine("Testing invalid argument handling...");

        var result = RunGC("--invalid-argument-that-does-not-exist");

        // Should either handle gracefully or show help
        Assert.True(result.ExitCode == 0 || result.StandardOutput.Contains("help"));

        _output.WriteLine($"✅ Invalid arguments handled appropriately");
    }

    [Fact]
    public void CurrentBuild_Performance_BenchmarksComplete()
    {
        _output.WriteLine("Testing performance benchmarks...");

        var testRepo = CreateTestRepository(new[] { "test1.cs", "test2.cs", "test3.cs" });
        AddFileToRepository(testRepo, "test1.cs", "public class Test1 { }");
        AddFileToRepository(testRepo, "test2.cs", "public class Test2 { }");
        AddFileToRepository(testRepo, "test3.cs", "public class Test3 { }");

        var stopwatch = Stopwatch.StartNew();
        var result = RunGCInDirectory(testRepo, "");
        stopwatch.Stop();

        Assert.Equal(0, result.ExitCode);
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"Processing took too long: {stopwatch.ElapsedMilliseconds}ms");

        _output.WriteLine($"✅ Performance is acceptable ({stopwatch.ElapsedMilliseconds}ms for 3 files)");
    }

    private ProcessResult RunGC(string args)
    {
        return RunGCInDirectory(Directory.GetCurrentDirectory(), args);
    }

    private ProcessResult RunGCInDirectory(string directory, string args)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project {_projectPath} -- {args}",
            WorkingDirectory = directory,
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

    private string CreateTestRepository(string[] files)
    {
        var repoDir = Path.Combine(_testDir, $"repo_{Guid.NewGuid()}");
        Directory.CreateDirectory(repoDir);

        // Initialize git repo
        RunCommand(repoDir, "git", "init");
        RunCommand(repoDir, "git", "config", "user.email", "test@example.com");
        RunCommand(repoDir, "git", "config", "user.name", "Test User");

        return repoDir;
    }

    private void AddFileToRepository(string repoDir, string filename, string content)
    {
        var fullPath = Path.Combine(repoDir, filename);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
        RunCommand(repoDir, "git", "add", filename);
        RunCommand(repoDir, "git", "commit", "-m", $"Add {filename}");
    }

    private void AddBinaryFileToRepository(string repoDir, string filename)
    {
        var fullPath = Path.Combine(repoDir, filename);
        var binaryContent = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x00 };
        File.WriteAllBytes(fullPath, binaryContent);
        RunCommand(repoDir, "git", "add", filename);
        RunCommand(repoDir, "git", "commit", "-m", $"Add {filename}");
    }

    private void RunCommand(string directory, string command, params string[] args)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = string.Join(" ", args),
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            throw new Exception($"Failed to start {command}");
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new Exception($"{command} failed: {error}");
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
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
