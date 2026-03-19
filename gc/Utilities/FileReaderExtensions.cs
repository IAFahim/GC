using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using gc.Data;

namespace gc.Utilities;

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
            .WithDegreeOfParallelism(Environment.ProcessorCount)
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
        if (fileInfo.Length > Constants.MaxFileSize)
        {
            Logger.LogDebug($"Skipping {entry.Path}: size={fileInfo.Length}, max={Constants.MaxFileSize}");
            Interlocked.Increment(ref skippedCount);
            return null;
        }

        try
        {
            // Read the entire file into memory (since we need it anyway)
            var bytes = File.ReadAllBytes(entry.Path);
            
            // Check for binary files by looking for non-printable characters in the first 4KB
            // Allow null bytes for UTF-16/UTF-32 support, but check ratio of text vs non-text
            int checkLength = Math.Min(4096, bytes.Length);
            int nonPrintableCount = 0;
            bool isBinary = false;
            
            for (var i = 0; i < checkLength; i++)
            {
                byte b = bytes[i];
                // Non-printable control characters, excluding standard whitespace (tab, LF, CR)
                if (b < 32 && b != 9 && b != 10 && b != 13)
                {
                    // If it's a null byte, we don't immediately fail, but we count it
                    nonPrintableCount++;
                }
            }

            // If more than 10% of characters are non-printable, consider it binary
            // Also consider files with very high null-byte ratios as binary
            if (checkLength > 0 && ((double)nonPrintableCount / checkLength) > 0.1)
            {
                isBinary = true;
            }

            if (isBinary)
            {
                Logger.LogDebug($"Skipping binary file: {entry.Path}");
                Interlocked.Increment(ref skippedCount);
                return null;
            }

            var text = System.Text.Encoding.UTF8.GetString(bytes);
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