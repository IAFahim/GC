using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using gc.Data;
using gc.Utilities;

namespace gc;

public static class RealBenchmark
{
    public static void RunRealBenchmark()
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║            GC (Git Copy) - Real Repository Benchmark            ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var currentDir = Directory.GetCurrentDirectory();

        // Test against the current repository
        if (Directory.Exists(Path.Combine(currentDir, ".git")))
        {
            Console.WriteLine("🔍 Testing against current repository...");
            Console.WriteLine($"   Repository: {currentDir}");
            Console.WriteLine();

            RunRepositoryBenchmark(currentDir);
        }
        else
        {
            Console.WriteLine("⚠️  Not in a git repository. Creating simulated test...");
            Console.WriteLine();

            // Create a test repository with real files
            var testRepo = Path.Combine(Path.GetTempPath(), "gc_benchmark_test");
            CreateTestRepository(testRepo);
            RunRepositoryBenchmark(testRepo);
            CleanupTestRepository(testRepo);
        }

        Console.WriteLine();
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    Benchmark Complete                            ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
    }

    private static void RunRepositoryBenchmark(string repoPath)
    {
        var originalDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(repoPath);

        try
        {
            // Get initial memory
            var process = Process.GetCurrentProcess();
            var initialMemory = process.WorkingSet64 / 1024 / 1024;

            // Benchmark 1: File Discovery
            Console.WriteLine("📊 Phase 1: File Discovery");
            var config = ConfigurationLoader.LoadConfiguration();
            var args = new CliArguments(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                string.Empty,
                false,
                false,
                false,
                gc.Data.DiscoveryMode.Auto,
                long.MaxValue,
                false,
                true,  // Enable debug for timing info
                false,
                false,
                false,
                config
            );

            var discoveryWatch = Stopwatch.StartNew();
            var rawFiles = args.DiscoverFiles();
            discoveryWatch.Stop();

            Console.WriteLine($"  • Files discovered: {rawFiles.Length:N0}");
            Console.WriteLine($"  • Discovery time:   {discoveryWatch.ElapsedMilliseconds} ms");

            if (rawFiles.Length == 0)
            {
                Console.WriteLine("  ⚠️  No files found, skipping remaining benchmarks");
                return;
            }

            // Calculate total size (only for existing files)
            long totalSize = rawFiles.Where(f => File.Exists(f)).Sum(f => new FileInfo(f).Length);
            Console.WriteLine($"  • Total size:       {FormatSize(totalSize)}");
            Console.WriteLine();

            // Benchmark 2: File Reading (Non-Streaming)
            Console.WriteLine("📊 Phase 2: File Reading (Non-Streaming)");

            // Convert string[] to FileEntry[] (only for existing files)
            var fileEntries = rawFiles.Where(f => File.Exists(f))
                .Select(path => new FileEntry(path, GetExtension(path), GetLanguage(path), new FileInfo(path).Length))
                .ToArray();

            var readWatch = Stopwatch.StartNew();
            var fileContents = fileEntries.ReadContents(args);
            readWatch.Stop();

            var currentMemory = process.WorkingSet64 / 1024 / 1024;
            var memoryUsed = currentMemory - initialMemory;

            Console.WriteLine($"  • Files read:       {fileContents.Length:N0}");
            Console.WriteLine($"  • Read time:        {readWatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"  • Memory used:      {memoryUsed} MB");
            Console.WriteLine($"  • Throughput:       {FormatThroughput(fileContents.Length, readWatch.ElapsedMilliseconds)}");
            Console.WriteLine($"  • Avg time/file:    {readWatch.ElapsedMilliseconds * 1000000.0 / fileContents.Length:F2} μs/file");
            Console.WriteLine();

            // Benchmark 3: Markdown Generation
            Console.WriteLine("📊 Phase 3: Markdown Generation");
            var markdownWatch = Stopwatch.StartNew();
            var markdown = fileContents.GenerateMarkdown(args);
            markdownWatch.Stop();

            Console.WriteLine($"  • Markdown size:    {FormatSize(markdown.Length)}");
            Console.WriteLine($"  • Generation time:  {markdownWatch.ElapsedMilliseconds} ms");
            var writeSpeed = (long)(markdown.Length / (markdownWatch.ElapsedMilliseconds / 1000.0));
            Console.WriteLine($"  • Write speed:      {FormatSize(writeSpeed)}/sec");
            Console.WriteLine();

            // Benchmark 4: Streaming Performance
            Console.WriteLine("📊 Phase 4: Streaming Performance");

            // Force garbage collection before streaming test
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            Thread.Sleep(100);

            var streamStartMemory = process.WorkingSet64 / 1024 / 1024;
            var streamWatch = Stopwatch.StartNew();

            using var memoryStream = new MemoryStream();
            var lazyContents = fileEntries.ReadContentsLazy(args);
            var (streamedCount, totalBytes) = lazyContents.GenerateMarkdownStreaming(memoryStream, args);

            streamWatch.Stop();
            var streamEndMemory = process.WorkingSet64 / 1024 / 1024;
            var streamMemoryUsed = streamEndMemory - streamStartMemory;

            Console.WriteLine($"  • Files processed:  {streamedCount:N0}");
            Console.WriteLine($"  • Stream time:      {streamWatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"  • Memory used:      {streamMemoryUsed} MB");
            Console.WriteLine($"  • Throughput:       {FormatThroughput(streamedCount, streamWatch.ElapsedMilliseconds)}");
            Console.WriteLine();

            // Summary
            Console.WriteLine("📊 Performance Summary");
            var totalTime = discoveryWatch.ElapsedMilliseconds +
                           readWatch.ElapsedMilliseconds +
                           markdownWatch.ElapsedMilliseconds;

            Console.WriteLine($"  • Total time:       {totalTime} ms");
            Console.WriteLine($"  • Peak memory:      {Math.Max(memoryUsed, streamMemoryUsed)} MB");
            Console.WriteLine($"  • Files/sec:        {fileContents.Length * 1000.0 / totalTime:F1}");
            Console.WriteLine($"  • Memory efficiency:{streamMemoryUsed} MB (streaming)");

            // Performance ratings
            Console.WriteLine();
            Console.WriteLine("🏆 Performance Ratings");
            RatePerformance("Discovery Speed", discoveryWatch.ElapsedMilliseconds, rawFiles.Length);
            RatePerformance("Read Speed", readWatch.ElapsedMilliseconds, fileContents.Length);
            RatePerformance("Memory Usage", streamMemoryUsed, 100);
            RatePerformance("Markdown Speed", markdownWatch.ElapsedMilliseconds, (long)(markdown.Length / 1024));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    private static void RatePerformance(string metric, double value, double baseline)
    {
        double ratio = value / baseline;
        string rating;

        if (ratio <= 0.1)
            rating = "⚡⚡⚡ Excellent";
        else if (ratio <= 0.5)
            rating = "⚡⚡ Very Good";
        else if (ratio <= 1.0)
            rating = "⚡ Good";
        else if (ratio <= 2.0)
            rating = "🟡 Fair";
        else
            rating = "🔴 Needs Improvement";

        Console.WriteLine($"  • {metric}:     {rating}");
    }

    private static void CreateTestRepository(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);

        Directory.CreateDirectory(path);

        // Initialize git repo
        Process.Start("git", new[] { "init" }).WaitForExit();
        Process.Start("git", new[] { "config", "user.email", "benchmark@gc.test" }).WaitForExit();
        Process.Start("git", new[] { "config", "user.name", "GC Benchmark" }).WaitForExit();

        // Create test files
        var testDir = Path.Combine(path, "src");
        Directory.CreateDirectory(testDir);

        for (var i = 0; i < 100; i++)
        {
            var content = GenerateRandomCode(i);
            File.WriteAllText(Path.Combine(testDir, $"file{i}.cs"), content);
        }

        // Commit files
        Process.Start("git", new[] { "add", "." }).WaitForExit();
        Process.Start("git", new[] { "commit", "-m", "Initial commit" }).WaitForExit();
    }

    private static void CleanupTestRepository(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private static string GenerateRandomCode(int seed)
    {
        var random = new Random(seed);
        var lines = new[] {
            "using System;",
            "using System.Collections.Generic;",
            "",
            $"namespace TestProject{seed % 10}",
            "{",
            $"    public class Class{seed}",
            "    {",
            "        public void Method()",
            "        {",
            $"            // Generated code {seed}",
            "            var list = new List<string>();",
            "            for (int j = 0; j < 100; j++)",
            "            {",
            "                list.Add($\"item{seed}_{j}\");",
            "            }",
            "        }",
            "    }",
            "}"
        };

        return string.Join("\n", lines);
    }

    private static string FormatSize(long bytes)
    {
        return bytes < 1024 ? $"{bytes} B" :
            bytes < 1048576 ? $"{bytes / 1024.0:F2} KB" :
            $"{bytes / 1048576.0:F2} MB";
    }

    private static string FormatThroughput(int items, long milliseconds)
    {
        if (milliseconds == 0) return "∞ items/sec";
        var itemsPerSec = items * 1000.0 / milliseconds;
        return $"{itemsPerSec:F0} files/sec";
    }

    private static string GetExtension(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        return string.IsNullOrEmpty(ext) ? "txt" : ext;
    }

    private static string GetLanguage(string path)
    {
        var ext = GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            "cs" => "csharp",
            "js" => "javascript",
            "ts" => "typescript",
            "py" => "python",
            "java" => "java",
            "go" => "go",
            "rs" => "rust",
            "cpp" or "cc" or "cxx" => "cpp",
            "c" or "h" => "c",
            "md" => "markdown",
            "json" => "json",
            "xml" => "xml",
            "yaml" or "yml" => "yaml",
            "sh" or "bash" => "bash",
            "ps1" => "powershell",
            "dockerfile" => "dockerfile",
            _ => "text"
        };
    }
}
