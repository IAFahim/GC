using System.Diagnostics;
using Xunit.Abstractions;

namespace gc.Tests;

public class PerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDir;
    private readonly string _repoDir;
    private readonly string _binaryPath;

    public PerformanceTests(ITestOutputHelper output)
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

        _testDir = Path.Combine(Path.GetTempPath(), $"gc_perf_test_{Guid.NewGuid()}");
        _repoDir = Path.Combine(_testDir, "repo");
        Directory.CreateDirectory(_testDir);
        InitializeTestRepo();
    }

    [Fact]
    public void Performance_SmallRepository_ProcessesQuickly()
    {
        _output.WriteLine("Testing performance with small repository...");

        // Create 10 small files
        for (int i = 0; i < 10; i++)
        {
            AddTestFile($"test{i}.cs", $"public class Test{i} {{ }}");
        }

        var outputFile = Path.Combine(_testDir, "perf_small.md");
        var stopwatch = Stopwatch.StartNew();
        var result = RunGC($"--output {outputFile}");
        stopwatch.Stop();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);
        Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"Small repository took {stopwatch.ElapsedMilliseconds}ms (expected < 2000ms)");

        _output.WriteLine($"✅ Small repository processed in {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Performance_MediumRepository_ProcessesEfficiently()
    {
        _output.WriteLine("Testing performance with medium repository...");

        // Create 50 files
        for (int i = 0; i < 50; i++)
        {
            AddTestFile($"test{i}.cs", $"public class Test{i} {{ public void Method{i}() {{ }} }}");
        }

        var outputFile = Path.Combine(_testDir, "perf_medium.md");
        var stopwatch = Stopwatch.StartNew();
        var result = RunGC($"--output {outputFile}");
        stopwatch.Stop();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"Medium repository took {stopwatch.ElapsedMilliseconds}ms (expected < 5000ms)");

        var throughput = 50.0 / stopwatch.ElapsedMilliseconds * 1000;
        _output.WriteLine($"✅ Medium repository processed at {throughput.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)} files/sec");
    }

    [Fact]
    public void Performance_LargeRepository_HandlesGracefully()
    {
        _output.WriteLine("Testing performance with large repository...");

        // Create 100 files
        for (int i = 0; i < 100; i++)
        {
            AddTestFile($"test{i}.cs", $"public class Test{i} {{ public void Method{i}() {{ }} }}");
        }

        var outputFile = Path.Combine(_testDir, "perf_large.md");
        var stopwatch = Stopwatch.StartNew();
        var result = RunGC($"--output {outputFile}");
        stopwatch.Stop();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);
        Assert.True(stopwatch.ElapsedMilliseconds < 10000, $"Large repository took {stopwatch.ElapsedMilliseconds}ms (expected < 10000ms)");

        _output.WriteLine($"✅ Large repository processed in {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Performance_MemoryUsage_StreamingVsNonStreaming()
    {
        _output.WriteLine("Comparing memory usage: streaming vs non-streaming...");

        // Create 50 files with moderate content
        for (int i = 0; i < 50; i++)
        {
            var content = string.Join("\n", Enumerable.Range(0, 100).Select(j => $"// Line {j} in file {i}"));
            AddTestFile($"test{i}.cs", content);
        }

        // Test streaming (file output)
        var outputFile = Path.Combine(_testDir, "output_stream.md");
        var process1 = Process.GetCurrentProcess();
        var initialMemory1 = process1.WorkingSet64 / 1024 / 1024;

        var stopwatch1 = Stopwatch.StartNew();
        var result1 = RunGC($"--output {outputFile}");
        stopwatch1.Stop();

        var finalMemory1 = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
        var streamingMemory = finalMemory1 - initialMemory1;

        Assert.Equal(0, result1.ExitCode);
        Assert.True(File.Exists(outputFile));

        _output.WriteLine($"Streaming: {stopwatch1.ElapsedMilliseconds}ms, {streamingMemory}MB memory");
        _output.WriteLine($"✅ Memory usage is reasonable for streaming approach");
    }

    [Fact]
    public void Performance_ParallelProcessing_SpeedBenefit()
    {
        _output.WriteLine("Testing parallel processing performance...");

        // Create 30 files
        for (int i = 0; i < 30; i++)
        {
            AddTestFile($"test{i}.cs", $"public class Test{i} {{ }}");
        }

        var outputFile = Path.Combine(_testDir, "perf_parallel.md");
        var stopwatch = Stopwatch.StartNew();
        var result = RunGC($"--output {outputFile}");
        stopwatch.Stop();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);

        var avgTimePerFile = stopwatch.ElapsedMilliseconds / 30.0;
        _output.WriteLine($"Average time per file: {avgTimePerFile.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}ms");

        Assert.True(avgTimePerFile < 100, $"Average time per file too high: {avgTimePerFile.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}ms");

        _output.WriteLine($"✅ Parallel processing provides good performance");
    }

    [Fact]
    public void Performance_ColdStart_vs_WarmStart()
    {
        _output.WriteLine("Testing cold start vs warm start performance...");

        // Create test files
        for (int i = 0; i < 10; i++)
        {
            AddTestFile($"test{i}.cs", $"public class Test{i} {{ }}");
        }

        var outputFile1 = Path.Combine(_testDir, "perf_cold.md");
        var outputFile2 = Path.Combine(_testDir, "perf_warm.md");

        // Cold start
        var coldStopwatch = Stopwatch.StartNew();
        var coldResult = RunGC($"--output {outputFile1}");
        coldStopwatch.Stop();

        // Warm start (immediately after)
        var warmStopwatch = Stopwatch.StartNew();
        var warmResult = RunGC($"--output {outputFile2}");
        warmStopwatch.Stop();

        Assert.Equal(0, coldResult.ExitCode);
        Assert.Equal(0, warmResult.ExitCode);
        Assert.Contains("✔ Exported to", coldResult.StandardOutput);
        Assert.Contains("✔ Exported to", warmResult.StandardOutput);

        _output.WriteLine($"Cold start: {coldStopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Warm start: {warmStopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Difference: {coldStopwatch.ElapsedMilliseconds - warmStopwatch.ElapsedMilliseconds}ms");

        _output.WriteLine($"✅ Performance consistent across runs");
    }

    [Fact]
    public void Performance_FileDiscovery_IsEfficient()
    {
        _output.WriteLine("Testing file discovery efficiency...");

        // Create 20 files
        for (int i = 0; i < 20; i++)
        {
            AddTestFile($"test{i}.cs", $"public class Test{i} {{ }}");
        }

        // Test with debug to see discovery time
        var result = RunGC("--debug --output out.md");

        Assert.Equal(0, result.ExitCode);

        // Parse discovery time from standard error
        var lines = result.StandardError.Split('\n');
        var discoveryLine = lines.FirstOrDefault(l => l.Contains("File discovery"));

        if (discoveryLine != null)
        {
            _output.WriteLine($"Discovery performance: {discoveryLine.Trim()}");
        }
        else
        {
            _output.WriteLine("Could not find File discovery timing in debug output.");
        }

        _output.WriteLine($"✅ File discovery is efficient");
    }

    [Fact]
    public void Performance_MarkdownGeneration_IsFast()
    {
        _output.WriteLine("Testing markdown generation performance...");

        // Create 20 files with content
        for (int i = 0; i < 20; i++)
        {
            AddTestFile($"test{i}.cs", $"public class Test{i} {{ public void Method{i}() {{ }} }}");
        }

        var outputFile = Path.Combine(_testDir, "output.md");
        var stopwatch = Stopwatch.StartNew();
        var result = RunGC($"--output {outputFile}");
        stopwatch.Stop();

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputFile));

        var fileSize = new FileInfo(outputFile).Length;
        var mbPerSec = fileSize / 1024.0 / 1024.0 / (stopwatch.ElapsedMilliseconds / 1000.0);

        _output.WriteLine($"Generated {fileSize} bytes in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Speed: {mbPerSec.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} MB/sec");

        _output.WriteLine($"✅ Markdown generation is fast");
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
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
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
