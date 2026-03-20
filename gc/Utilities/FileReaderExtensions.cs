using System.Globalization;
using System.Text;
using gc.Data;

namespace gc.Utilities;

public static class FileReaderExtensions
{
    public static FileContent[] ReadContents(this FileEntry[] entries, CliArguments args)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));

        using var _ = Logger.TimeOperation("File reading");

        var totalSize = entries.Sum(e => e.Size);

        Logger.LogVerbose($"Reading {entries.Length} files (total size: {Formatting.FormatSize(totalSize)})...");

        if (totalSize > args.MaxMemoryBytes)
        {
            var maxSizeStr = Formatting.FormatSize(args.MaxMemoryBytes);
            var totalSizeStr = Formatting.FormatSize(totalSize);

            Logger.LogError($"Memory limit exceeded: {totalSizeStr} > {maxSizeStr}");
            Console.WriteLine("Use --max-memory to increase the limit (e.g., --max-memory 500MB)");
            Environment.Exit(1);
            return Array.Empty<FileContent>();
        }

        if (totalSize > args.MaxMemoryBytes * 0.8)
        {
            var maxSizeStr = Formatting.FormatSize(args.MaxMemoryBytes);
            var totalSizeStr = Formatting.FormatSize(totalSize);
            var percentage = (totalSize * 100.0 / args.MaxMemoryBytes).ToString("F1", CultureInfo.InvariantCulture);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[WARNING] ");
            Console.ResetColor();
            Console.WriteLine($"Approaching memory limit: {totalSizeStr} ({percentage}% of {maxSizeStr})");
        }

        var processedCount = 0;
        var skippedCount = 0;
        var errorCount = 0;

        var results = entries
            .AsParallel()
            .WithDegreeOfParallelism(Environment.ProcessorCount)
            .Select(entry =>
                TryReadFile(entry, ref processedCount, ref skippedCount, ref errorCount, entries.Length, args))
            .OfType<FileContent>()
            .ToArray();

        Logger.LogVerbose($"Successfully read {results.Length} files (skipped: {skippedCount}, errors: {errorCount})");

        return results;
    }

    private static FileContent? TryReadFile(FileEntry entry, ref int processedCount, ref int skippedCount,
        ref int errorCount, int totalEntries, CliArguments args, bool streamContent = false)
    {
        var exists = File.Exists(entry.Path);
        if (!exists)
        {
            Logger.LogDebug($"File not found: {entry.Path}");
            Interlocked.Increment(ref skippedCount);
            return null;
        }

        long fileLength;
        try
        {
            var fileInfo = new FileInfo(entry.Path);
            fileLength = fileInfo.Length;
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"Failed to get file info for {entry.Path}: {ex.Message}");
            Interlocked.Increment(ref errorCount);
            return null;
        }

        var maxFileSize = args.Configuration?.Limits?.GetMaxFileSizeBytes() ?? 1048576;
        if (fileLength > maxFileSize)
        {
            Logger.LogDebug($"Skipping {entry.Path}: size={fileLength}, max={maxFileSize}");
            Interlocked.Increment(ref skippedCount);
            return null;
        }

        try
        {
            var checkLength = (int)Math.Min(4096, fileLength);
            var checkBytes = new byte[checkLength];

            using (var fs = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var bytesRead = 0;
                while (bytesRead < checkLength)
                {
                    var read = fs.Read(checkBytes, bytesRead, checkLength - bytesRead);
                    if (read == 0) break;
                    bytesRead += read;
                }
            }

            var consecutiveNulls = 0;
            var nonPrintableCount = 0;
            var isBinary = false;

            for (var i = 0; i < checkLength; i++)
            {
                var b = checkBytes[i];

                if (b == 0x00)
                {
                    consecutiveNulls++;
                    if (consecutiveNulls >= 3)
                    {
                        isBinary = true;
                        break;
                    }
                }
                else
                {
                    consecutiveNulls = 0;
                }

                if (b < 32 && b != 9 && b != 10 && b != 13 && b != 0x00) nonPrintableCount++;
            }

            if (!isBinary && checkLength > 0 && (double)nonPrintableCount / checkLength > 0.1) isBinary = true;

            if (isBinary)
            {
                Logger.LogDebug($"Skipping binary file: {entry.Path}");
                Interlocked.Increment(ref skippedCount);
                return null;
            }

            Interlocked.Increment(ref processedCount);
            if (Logger.CurrentLevel >= LogLevel.Verbose && processedCount % 100 == 0)
                Logger.LogVerbose($"Read {processedCount}/{totalEntries} files...");

            if (streamContent) return new FileContent(entry, null!, fileLength);

            var text = File.ReadAllText(entry.Path, Encoding.UTF8);
            return new FileContent(entry, text, fileLength);
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to read {entry.Path}", ex);
            Interlocked.Increment(ref errorCount);
            return null;
        }
    }


    public static IEnumerable<FileContent> ReadContentsLazy(this FileEntry[] entries, CliArguments args)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));

        using var _ = Logger.TimeOperation("File reading (streaming)");

        var totalSize = entries.Sum(e => e.Size);

        Logger.LogVerbose($"Streaming {entries.Length} files (total size: {Formatting.FormatSize(totalSize)})...");

        var processedCount = 0;
        var skippedCount = 0;
        var errorCount = 0;

        foreach (var entry in entries)
        {
            var result = TryReadFile(entry, ref processedCount, ref skippedCount, ref errorCount, entries.Length, args,
                true);
            if (result != null) yield return result.Value;
        }

        Logger.LogVerbose($"Successfully read {processedCount} files (skipped: {skippedCount}, errors: {errorCount})");
    }
}