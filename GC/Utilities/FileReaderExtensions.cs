using System;
using System.Collections.Generic;
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
            .Select(entry => TryReadFile(entry, ref processedCount, ref skippedCount, ref errorCount, entries.Length))
            .OfType<FileContent>()
            .ToArray();

        Logger.LogVerbose($"Successfully read {results.Length} files (skipped: {skippedCount}, errors: {errorCount})");

        return results;
    }

    private static FileContent? TryReadFile(FileEntry entry, ref int processedCount, ref int skippedCount, ref int errorCount, int totalEntries)
    {
        var exists = File.Exists(entry.Path);
        if (!exists)
        {
            Logger.LogDebug($"File not found: {entry.Path}");
            Interlocked.Increment(ref skippedCount);
            return null;
        }

        var fileInfo = new FileInfo(entry.Path);
        if (fileInfo.Length == 0 || fileInfo.Length > Constants.MaxFileSize)
        {
            Logger.LogDebug($"Skipping {entry.Path}: size={fileInfo.Length}, max={Constants.MaxFileSize}");
            Interlocked.Increment(ref skippedCount);
            return null;
        }

        try
        {
            // Check for binary files before reading to prevent corruption
            if (IsBinaryFile(entry.Path, fileInfo.Length))
            {
                Logger.LogDebug($"Skipping binary file: {entry.Path}");
                Interlocked.Increment(ref skippedCount);
                return null;
            }

            var text = File.ReadAllText(entry.Path);
            Interlocked.Increment(ref processedCount);
            if (Logger.CurrentLevel >= LogLevel.Verbose && processedCount % 100 == 0)
            {
                Logger.LogVerbose($"Read {processedCount}/{totalEntries} files...");
            }
            return new FileContent(entry, text, fileInfo.Length);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to read {entry.Path}", ex);
            Interlocked.Increment(ref errorCount);
            return null;
        }
    }

    private static bool IsBinaryFile(string path, long maxSize)
    {
        // Read first 4KB to check for null bytes (indicator of binary files)
        var bufferSize = Math.Min(4096, maxSize);
        var buffer = new byte[bufferSize];

        using var fs = File.OpenRead(path);
        var bytesRead = fs.Read(buffer, 0, buffer.Length);

        // Check for null byte (strong indicator of binary content)
        for (var i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == 0)
                return true;
        }

        return false;
    }

    private static string FormatSize(long bytes)
    {
        return bytes < 1024 ? $"{bytes} B" :
            bytes < 1048576 ? $"{bytes / 1024.0:F2} KB" :
            $"{bytes / 1048576.0:F2} MB";
    }

    public static IEnumerable<FileContent> ReadContentsLazy(this FileEntry[] entries, CliArguments args)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));

        using var _ = Logger.TimeOperation("File reading (streaming)");

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
            yield break;
        }

        // Warning if approaching limit
        if (totalSize > args.MaxMemoryBytes * 0.8)
        {
            var maxSizeStr = FormatSize(args.MaxMemoryBytes);
            var totalSizeStr = FormatSize(totalSize);
            var percentage = (totalSize * 100.0 / args.MaxMemoryBytes).ToString("F1");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[WARNING] ");
            Console.ResetColor();
            Console.WriteLine($"Approaching memory limit: {totalSizeStr} ({percentage}% of {maxSizeStr})");
        }

        var processedCount = 0;
        var skippedCount = 0;
        var errorCount = 0;

        // Process files sequentially for true streaming (one file at a time)
        foreach (var entry in entries)
        {
            var result = TryReadFile(entry, ref processedCount, ref skippedCount, ref errorCount, entries.Length);
            if (result != null)
            {
                yield return result.Value;
            }
        }

        Logger.LogVerbose($"Successfully read {processedCount} files (skipped: {skippedCount}, errors: {errorCount})");
    }
}