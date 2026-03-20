using System.Diagnostics;
using System.Globalization;
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
            var process = Process.GetCurrentProcess();
            var initialMemory = process.WorkingSet64 / 1024 / 1024;

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
                DiscoveryMode.Auto,
                long.MaxValue,
                false,
                true, false,
                false,
                false,
                config
            );

            var discoveryWatch = Stopwatch.StartNew();
            var rawFiles = args.DiscoverFiles();
            discoveryWatch.Stop();

            Console.WriteLine($"  • Files discovered: {rawFiles.Length.ToString("N0", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"  • Discovery time:   {discoveryWatch.ElapsedMilliseconds} ms");

            if (rawFiles.Length == 0)
            {
                Console.WriteLine("  ⚠️  No files found, skipping remaining benchmarks");
                return;
            }

            var totalSize = rawFiles.Where(f => File.Exists(f)).Sum(f => new FileInfo(f).Length);
            Console.WriteLine($"  • Total size:       {Formatting.FormatSize(totalSize)}");
            Console.WriteLine();

            Console.WriteLine("📊 Phase 2: File Reading (Non-Streaming)");

            var fileEntries = rawFiles.Where(f => File.Exists(f))
                .Select(path => new FileEntry(path, GetExtension(path), GetLanguage(path), new FileInfo(path).Length))
                .ToArray();

            var readWatch = Stopwatch.StartNew();
            var fileContents = fileEntries.ReadContents(args);
            readWatch.Stop();

            var currentMemory = process.WorkingSet64 / 1024 / 1024;
            var memoryUsed = currentMemory - initialMemory;

            Console.WriteLine(
                $"  • Files read:       {fileContents.Length.ToString("N0", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"  • Read time:        {readWatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"  • Memory used:      {memoryUsed} MB");
            Console.WriteLine(
                $"  • Throughput:       {FormatThroughput(fileContents.Length, readWatch.ElapsedMilliseconds)}");
            Console.WriteLine(
                $"  • Avg time/file:    {(readWatch.ElapsedMilliseconds * 1000000.0 / fileContents.Length).ToString("F2", CultureInfo.InvariantCulture)} μs/file");
            Console.WriteLine();

            Console.WriteLine("📊 Phase 3: Markdown Generation");
            var markdownWatch = Stopwatch.StartNew();
            var markdown = fileContents.GenerateMarkdown(args);
            markdownWatch.Stop();

            Console.WriteLine($"  • Markdown size:    {Formatting.FormatSize(markdown.Length)}");
            Console.WriteLine($"  • Generation time:  {markdownWatch.ElapsedMilliseconds} ms");
            var writeSpeed = (long)(markdown.Length / (markdownWatch.ElapsedMilliseconds / 1000.0));
            Console.WriteLine($"  • Write speed:      {Formatting.FormatSize(writeSpeed)}/sec");
            Console.WriteLine();

            Console.WriteLine("📊 Phase 4: Streaming Performance");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            Thread.Sleep(100);

            var streamStartMemory = process.WorkingSet64 / 1024 / 1024;
            var streamWatch = Stopwatch.StartNew();

            using var memoryStream = new MemoryStream();
            var lazyContents = fileEntries.ReadContentsLazy(args);
            var (streamedCount, totalBytes) = lazyContents.GenerateMarkdownStreaming(memoryStream, args);

            streamWatch.Stop();
            var streamEndMemory = process.WorkingSet64 / 1024 / 1024;
            var streamMemoryUsed = streamEndMemory - streamStartMemory;

            Console.WriteLine($"  • Files processed:  {streamedCount.ToString("N0", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"  • Stream time:      {streamWatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"  • Memory used:      {streamMemoryUsed} MB");
            Console.WriteLine(
                $"  • Throughput:       {FormatThroughput(streamedCount, streamWatch.ElapsedMilliseconds)}");
            Console.WriteLine();

            Console.WriteLine("📊 Performance Summary");
            var totalTime = discoveryWatch.ElapsedMilliseconds +
                            readWatch.ElapsedMilliseconds +
                            markdownWatch.ElapsedMilliseconds;

            Console.WriteLine($"  • Total time:       {totalTime} ms");
            Console.WriteLine($"  • Peak memory:      {Math.Max(memoryUsed, streamMemoryUsed)} MB");
            Console.WriteLine(
                $"  • Files/sec:        {(fileContents.Length * 1000.0 / totalTime).ToString("F1", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"  • Memory efficiency:{streamMemoryUsed} MB (streaming)");

            Console.WriteLine();
            Console.WriteLine("🏆 Performance Ratings");
            RatePerformance("Discovery Speed", discoveryWatch.ElapsedMilliseconds, rawFiles.Length);
            RatePerformance("Read Speed", readWatch.ElapsedMilliseconds, fileContents.Length);
            RatePerformance("Memory Usage", streamMemoryUsed, 100);
            RatePerformance("Markdown Speed", markdownWatch.ElapsedMilliseconds, markdown.Length / 1024);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    private static void RatePerformance(string metric, double value, double baseline)
    {
        var ratio = value / baseline;
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

        Process.Start("git", new[] { "init" }).WaitForExit();
        Process.Start("git", new[] { "config", "user.email", "benchmark@gc.test" }).WaitForExit();
        Process.Start("git", new[] { "config", "user.name", "GC Benchmark" }).WaitForExit();

        var testDir = Path.Combine(path, "src");
        Directory.CreateDirectory(testDir);

        for (var i = 0; i < 100; i++)
        {
            var content = GenerateRandomCode(i);
            File.WriteAllText(Path.Combine(testDir, $"file{i}.cs"), content);
        }

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
        }
    }

    private static string GenerateRandomCode(int seed)
    {
        var random = new Random(seed);
        var lines = new[]
        {
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


    private static string FormatThroughput(int items, long milliseconds)
    {
        if (milliseconds == 0) return "∞ items/sec";
        var itemsPerSec = items * 1000.0 / milliseconds;
        return $"{itemsPerSec.ToString("F0", CultureInfo.InvariantCulture)} files/sec";
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