using System;
using System.IO;
using System.Linq;
using System.Threading;
using GC.Data;

namespace GC.Utilities;

public static class FileReaderExtensions
{
    public static FileContent[] ReadContents(this FileEntry[] entries, in CliArguments args)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));

        using var _ = Logger.TimeOperation("File reading");

        // Calculate total size before reading
        long totalSize = entries.Sum(e => e.Size);

        Logger.LogVerbose($"Reading {entries.Length} files (total size: {FormatSize(totalSize)})...");

        // Check against memory limit
        if (totalSize > args.MaxMemoryBytes)
        {
            var maxSizeStr = FormatSize(args.MaxMemoryBytes);
            var totalSizeStr = FormatSize(totalSize);

            Logger.LogError($"Memory limit exceeded: {totalSizeStr} > {maxSizeStr}");
            Console.WriteLine($"Use --max-memory to increase the limit (e.g., --max-memory 500MB)");
            Environment.Exit(1);
            return Array.Empty<FileContent>();
        }

        // Warning if approaching limit
        if (totalSize > args.MaxMemoryBytes * 0.8)
        {
            var maxSizeStr = FormatSize(args.MaxMemoryBytes);
            var totalSizeStr = FormatSize(totalSize);
            var percentage = (totalSize * 100.0 / args.MaxMemoryBytes).ToString("F1");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"[WARNING] ");
            Console.ResetColor();
            Console.WriteLine($"Approaching memory limit: {totalSizeStr} ({percentage}% of {maxSizeStr})");
        }

        var processedCount = 0;
        var skippedCount = 0;
        var errorCount = 0;

        var results = entries
            .AsParallel()
            .Where(entry =>
            {
                var exists = File.Exists(entry.Path);
                if (!exists)
                {
                    Logger.LogDebug($"File not found: {entry.Path}");
                    skippedCount++;
                }
                return exists;
            })
            .Select(entry =>
            {
                var fileInfo = new FileInfo(entry.Path);
                if (fileInfo.Length == 0 || fileInfo.Length > Constants.MaxFileSize)
                {
                    Logger.LogDebug($"Skipping {entry.Path}: size={fileInfo.Length}, max={Constants.MaxFileSize}");
                    Interlocked.Increment(ref skippedCount);
                    return (FileContent?)null;
                }

                try
                {
                    var text = File.ReadAllText(entry.Path);
                    processedCount++;
                    if (Logger.CurrentLevel >= LogLevel.Verbose && processedCount % 100 == 0)
                    {
                        Logger.LogVerbose($"Read {processedCount}/{entries.Length} files...");
                    }
                    return new FileContent(entry, text, fileInfo.Length);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to read {entry.Path}", ex);
                    Interlocked.Increment(ref errorCount);
                    return (FileContent?)null;
                }
            })
            .OfType<FileContent>()
            .ToArray();

        Logger.LogVerbose($"Successfully read {results.Length} files (skipped: {skippedCount}, errors: {errorCount})");

        return results;
    }

    private static string FormatSize(long bytes)
    {
        return bytes < 1024 ? $"{bytes} B" :
            bytes < 1048576 ? $"{bytes / 1024.0:F2} KB" :
            $"{bytes / 1048576.0:F2} MB";
    }
}