using System;
using System.IO;
using GC.Data;
using GC.Utilities;

namespace GC;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args == null) throw new ArgumentNullException(nameof(args));

        var cliArgs = args.ParseCli();

        if (cliArgs.ShowHelp)
        {
            PrintHelp();
            return;
        }

        if (cliArgs.RunTests)
        {
            TestRunner.RunTests();
            return;
        }

        Logger.LogDebug($"GC started with verbose logging. Arguments: {string.Join(" ", args)}");

        var rawFiles = cliArgs.DiscoverFiles();
        if (rawFiles.Length == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.WriteLine("No tracked files found in this repository.");
            Console.ResetColor();
            Console.Error.WriteLine("The repository appears to be empty (no files have been committed).");
            return;
        }

        var filteredFiles = rawFiles.FilterFiles(cliArgs);
        if (filteredFiles.Length == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.WriteLine($"No files match the specified filters.");
            Console.ResetColor();
            Console.Error.WriteLine($"Found {rawFiles.Length} total files, but all were filtered out.");
            Console.Error.WriteLine("Try adjusting your --paths, --extension, or --exclude options.");
            return;
        }

        // Use streaming for file output to reduce memory usage
        if (!string.IsNullOrEmpty(cliArgs.OutputFile))
        {
            // Stream directly to file without holding everything in memory
            using var outputStream = File.Create(cliArgs.OutputFile);
            var (fileCount, totalBytes) = filteredFiles.ReadContentsLazy(cliArgs).GenerateMarkdownStreaming(outputStream);

            // Print stats
            var tokens = totalBytes / 4;
            var sizeStr = totalBytes < 1024 ? $"{totalBytes} B" :
                totalBytes < 1048576 ? $"{totalBytes / 1024.0:F2} KB" :
                $"{totalBytes / 1048576.0:F2} MB";
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[OK] ");
            Console.ResetColor();
            Console.WriteLine($"Exported to {cliArgs.OutputFile}: {fileCount} files | Size: {sizeStr} | Tokens: ~{tokens}");
        }
        else
        {
            // For clipboard, we still need the string
            var fileContents = filteredFiles.ReadContents(cliArgs);
            var markdown = fileContents.GenerateMarkdown();
            markdown.HandleOutput(cliArgs, fileContents);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"GIT-COPY (C# Native Edition)

USAGE:
    git-copy-cs [OPTIONS]

OPTIONS:
    -p, --paths <paths>        Filter by starting paths (e.g. -p src libs)
    -e, --extension <ext>      Filter by extension (e.g. -e js ts)
    -x, --exclude <path>       Exclude folder, path or pattern (e.g. -x node_modules *.md)
    --preset <name>            Use predefined preset (web, backend, dotnet, unity, etc)
    -o, --output <file>        Save output to file instead of clipboard
    --max-memory <size>        Maximum memory limit (default: 100MB, e.g., 500MB, 1GB)
    -v, --verbose              Enable verbose logging (show file-by-file progress)
    --debug                    Enable debug logging (show git commands, timing, errors)
    --test                     Run built-in test suite
    -h, --help                 Show this help message");
    }
}

