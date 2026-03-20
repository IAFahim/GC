using gc.Data;

namespace gc.Utilities;

public static class FileReaderExtensions
{
    public static FileContent[] ReadContents(this FileEntry[] entries, CliArguments args)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));

        using var _ = Logger.TimeOperation("File reading");

        // Calculate total size before reading
        long totalSize = entries.Sum(e => e.Size);

        Logger.LogVerbose($"Reading {entries.Length} files (total size: {Formatting.FormatSize(totalSize)})...");

        // Check against memory limit
        if (totalSize > args.MaxMemoryBytes)
        {
            var maxSizeStr = Formatting.FormatSize(args.MaxMemoryBytes);
            var totalSizeStr = Formatting.FormatSize(totalSize);

            Logger.LogError($"Memory limit exceeded: {totalSizeStr} > {maxSizeStr}");
            Console.WriteLine($"Use --max-memory to increase the limit (e.g., --max-memory 500MB)");
            Environment.Exit(1);
            return Array.Empty<FileContent>();
        }

        // Warning if approaching limit
        if (totalSize > args.MaxMemoryBytes * 0.8)
        {
            var maxSizeStr = Formatting.FormatSize(args.MaxMemoryBytes);
            var totalSizeStr = Formatting.FormatSize(totalSize);
            var percentage = (totalSize * 100.0 / args.MaxMemoryBytes).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

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
            .Select(entry => TryReadFile(entry, ref processedCount, ref skippedCount, ref errorCount, entries.Length, args, false))
            .OfType<FileContent>()
            .ToArray();

        Logger.LogVerbose($"Successfully read {results.Length} files (skipped: {skippedCount}, errors: {errorCount})");

        return results;
    }

    private static FileContent? TryReadFile(FileEntry entry, ref int processedCount, ref int skippedCount, ref int errorCount, int totalEntries, CliArguments args, bool streamContent = false)
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
            // Check for binary files by reading up to 4KB
            int checkLength = (int)Math.Min(4096, fileLength);
            byte[] checkBytes = new byte[checkLength];
            
            using (var fs = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int bytesRead = 0;
                while (bytesRead < checkLength)
                {
                    int read = fs.Read(checkBytes, bytesRead, checkLength - bytesRead);
                    if (read == 0) break;
                    bytesRead += read;
                }
            }

            int consecutiveNulls = 0;
            int nonPrintableCount = 0;
            bool isBinary = false;

            for (var i = 0; i < checkLength; i++)
            {
                byte b = checkBytes[i];

                // Track consecutive null bytes (indicates UTF-16 LE binary data)
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

                // Non-printable control characters, excluding standard whitespace (tab, LF, CR)
                if (b < 32 && b != 9 && b != 10 && b != 13 && b != 0x00)
                {
                    nonPrintableCount++;
                }
            }

            // Additional check for high non-printable density (excluding UTF-16 nulls)
            if (!isBinary && checkLength > 0 && ((double)nonPrintableCount / checkLength) > 0.1)
            {
                isBinary = true;
            }

            if (isBinary)
            {
                Logger.LogDebug($"Skipping binary file: {entry.Path}");
                Interlocked.Increment(ref skippedCount);
                return null;
            }

            Interlocked.Increment(ref processedCount);
            if (Logger.CurrentLevel >= LogLevel.Verbose && processedCount % 100 == 0)
            {
                Logger.LogVerbose($"Read {processedCount}/{totalEntries} files...");
            }

            if (streamContent)
            {
                return new FileContent(entry, null!, fileLength);
            }
            
            // Read the entire file into memory for non-streaming mode
            var text = File.ReadAllText(entry.Path, System.Text.Encoding.UTF8);
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

        // Calculate total size for logging only (streaming doesn't load into memory)
        long totalSize = entries.Sum(e => e.Size);

        Logger.LogVerbose($"Streaming {entries.Length} files (total size: {Formatting.FormatSize(totalSize)})...");

        var processedCount = 0;
        var skippedCount = 0;
        var errorCount = 0;

        // Process files sequentially for true streaming (one file at a time)
        foreach (var entry in entries)
        {
            var result = TryReadFile(entry, ref processedCount, ref skippedCount, ref errorCount, entries.Length, args, true);
            if (result != null)
            {
                yield return result.Value;
            }
        }

        Logger.LogVerbose($"Successfully read {processedCount} files (skipped: {skippedCount}, errors: {errorCount})");
    }
}