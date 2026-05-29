using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using gc.Application.Native;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using Microsoft.Win32.SafeHandles;
using CompiledContentPatterns = gc.Domain.Interfaces.CompiledContentPatterns;

namespace gc.Application.Services;

public sealed class MarkdownGenerator : IMarkdownGenerator
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static readonly int NewlineByteCount = Utf8NoBom.GetByteCount(Environment.NewLine);
    private static readonly int DefaultFenceByteCount = Utf8NoBom.GetByteCount("```");
    private static readonly byte[] NewlineBytes = Utf8NoBom.GetBytes(Environment.NewLine);

    private static readonly SearchValues<byte> NullByte = SearchValues.Create(0);

    [ThreadStatic] private static StringBuilder? t_lineExclusionBuilder;

    private readonly ILogger _logger;

    public MarkdownGenerator(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<Result<long>> GenerateMarkdownStreamingAsync(IEnumerable<FileContent> contents,
        Stream outputStream, GcConfiguration config, IEnumerable<string>? excludeLineIfStart = null,
        IBrainCrusher? brainCrusher = null, CancellationToken ct = default)
    {
        return await GenerateMarkdownStreamingAsync(contents, outputStream, config, default, excludeLineIfStart,
            brainCrusher, ct);
    }

    public async Task<Result<long>> GenerateMarkdownStreamingAsync(IEnumerable<FileContent> contents,
        Stream outputStream, GcConfiguration config, CompiledContentPatterns contentFilter,
        IEnumerable<string>? excludeLineIfStart = null, IBrainCrusher? brainCrusher = null,
        CancellationToken ct = default)
    {
        try
        {
            var pipeOptions = new StreamPipeWriterOptions(leaveOpen: true, minimumBufferSize: 65536);
            var writer = PipeWriter.Create(outputStream, pipeOptions);

            var sortedContents = config.Output.SortByPath.GetValueOrDefault()
                ? contents.OrderBy(c => c.Entry.DisplayPath ?? c.Entry.Path, StringComparer.OrdinalIgnoreCase)
                : contents;

            long totalBytes = 0;
            var fileList = new List<string>();
            var maxMemoryBytes = config.Limits.GetMaxMemoryBytesValue();
            var maxFileSize = config.Limits.GetMaxFileSizeBytes();

            var degreeOfParallelism = Environment.ProcessorCount;
            var maxPendingFiles = degreeOfParallelism * 2;

            var sortedList = sortedContents.ToList();

            var channel = Channel.CreateBounded<(int Index, byte[]? Buffer, int Length, string? Error)>(
                new BoundedChannelOptions(maxPendingFiles) { SingleReader = true, SingleWriter = false });

            var generateTask = Task.Run(async () =>
            {
                try
                {
                    await Parallel.ForEachAsync(
                        Enumerable.Range(0, sortedList.Count),
                        new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism, CancellationToken = ct },
                        async (index, token) =>
                        {
                            var content = sortedList[index];
                            if (content.Content != null)
                            {
                                await channel.Writer.WriteAsync((index, null, 0, null), token);
                                return;
                            }

                            try
                            {
                                var fileInfo = new FileInfo(content.Entry.AbsolutePath);
                                if (!fileInfo.Exists)
                                {
                                    await channel.Writer.WriteAsync(
                                        (index, null, 0,
                                            $"File not found: {content.Entry.DisplayPath ?? content.Entry.RelativePath}"),
                                        token);
                                    return;
                                }

                                if (fileInfo.Length > maxFileSize)
                                {
                                    await channel.Writer.WriteAsync(
                                        (index, null, 0,
                                            $"File size ({fileInfo.Length} bytes) exceeds maximum allowed size ({maxFileSize} bytes)"),
                                        token);
                                    return;
                                }

                                SafeFileHandle handle;
                                if (OperatingSystem.IsLinux())
                                {
                                    var fd = LinuxFastPath.open(content.Entry.AbsolutePath, 0x40000);
                                    if (fd < 0) fd = LinuxFastPath.open(content.Entry.AbsolutePath, 0);
                                    if (fd >= 0)
                                    {
                                        LinuxFastPath.posix_fadvise(fd, 0, 0, LinuxFastPath.POSIX_FADV_SEQUENTIAL);
                                        handle = new SafeFileHandle(fd, true);
                                    }
                                    else
                                    {
                                        handle = File.OpenHandle(content.Entry.AbsolutePath, FileMode.Open,
                                            FileAccess.Read, FileShare.ReadWrite,
                                            FileOptions.SequentialScan | FileOptions.Asynchronous);
                                    }
                                }
                                else
                                {
                                    handle = File.OpenHandle(content.Entry.AbsolutePath, FileMode.Open, FileAccess.Read,
                                        FileShare.ReadWrite, FileOptions.SequentialScan | FileOptions.Asynchronous);
                                }

                                using (handle)
                                {
                                    var fileLength = RandomAccess.GetLength(handle);

                                    if (fileLength <= 10 * 1024 * 1024)
                                    {
                                        var len = (int)fileLength;
                                        var buffer = ArrayPool<byte>.Shared.Rent(len);
                                        var bytesRead = await RandomAccess.ReadAsync(handle, buffer.AsMemory(0, len), 0,
                                            token);
                                        await channel.Writer.WriteAsync((index, buffer, bytesRead, null), token);
                                    }
                                    else
                                    {
                                        await channel.Writer.WriteAsync((index, null, -1, null), token);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(
                                    $"Failed to read file {content.Entry.DisplayPath ?? content.Entry.RelativePath}",
                                    ex);
                                await channel.Writer.WriteAsync((index, null, 0, ex.Message), token);
                            }
                        });
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, ct);

            var nextExpectedIndex = 0;
            var outOfOrderBuffer = new Dictionary<int, (byte[]? Buffer, int Length, string? Error)>();

            await foreach (var result in channel.Reader.ReadAllAsync(ct))
            {
                outOfOrderBuffer[result.Index] = (result.Buffer, result.Length, result.Error);

                while (outOfOrderBuffer.TryGetValue(nextExpectedIndex, out var ready))
                {
                    outOfOrderBuffer.Remove(nextExpectedIndex);
                    var content = sortedList[nextExpectedIndex];
                    nextExpectedIndex++;

                    try
                    {
                        if (content.Content != null)
                        {
                            var actualContent = content.Content;
                            if (excludeLineIfStart != null && excludeLineIfStart.Any())
                            {
                                var excludeArray = excludeLineIfStart.ToArray();
                                var excludeNewline = excludeArray.Contains("\n");

                                var span = actualContent.AsSpan();
                                var sb = t_lineExclusionBuilder ??= new StringBuilder(4096);
                                sb.Clear();
                                sb.EnsureCapacity(actualContent.Length);
                                var first = true;

                                foreach (var line in span.EnumerateLines())
                                {
                                    var trimmedLine = line.TrimStart().TrimStart('\uFEFF');
                                    if (excludeNewline && (trimmedLine.IsEmpty || trimmedLine.IsWhiteSpace()))
                                        continue;

                                    var shouldExclude = false;
                                    foreach (var startStr in excludeArray)
                                        if (startStr != "\n" &&
                                            trimmedLine.StartsWith(startStr, StringComparison.Ordinal))
                                        {
                                            shouldExclude = true;
                                            break;
                                        }

                                    if (shouldExclude)
                                        continue;

                                    if (!first) sb.Append('\n');
                                    sb.Append(line);
                                    first = false;
                                }

                                actualContent = sb.ToString();
                            }

                            var fence = GetFenceForContent(actualContent);

                            var header = config.Markdown.FileHeaderTemplate.Replace("{path}",
                                content.Entry.DisplayPath ?? content.Entry.RelativePath,
                                StringComparison.OrdinalIgnoreCase);
                            var headerBytes = Utf8NoBom.GetByteCount(header) + NewlineByteCount;
                            var fenceLine = $"{fence}{content.Entry.Language}";
                            var fenceLineBytes = Utf8NoBom.GetByteCount(fenceLine) + NewlineByteCount;
                            var closingFenceBytes = Utf8NoBom.GetByteCount(fence) + NewlineByteCount;
                            var newlineBytes = NewlineByteCount;

                            actualContent = actualContent.TrimEnd(' ', '\t', '\r', '\n');

                            if (brainCrusher != null)
                                actualContent = brainCrusher.CrushBlock(actualContent, content.Entry.Language);

                            var contentBytes = Utf8NoBom.GetByteCount(actualContent);

                            var needsTrailingNewline = !actualContent.EndsWith('\n');
                            long entryTotalBytes = headerBytes + fenceLineBytes + contentBytes + closingFenceBytes +
                                                   newlineBytes;
                            if (needsTrailingNewline && actualContent.Length > 0) entryTotalBytes += newlineBytes;

                            if (totalBytes + entryTotalBytes > maxMemoryBytes)
                                return Result<long>.Failure(
                                    $"Output size ({totalBytes + entryTotalBytes} bytes) would exceed maximum output limit ({maxMemoryBytes} bytes).");

                            WriteStringLine(writer, header);
                            WriteStringLine(writer, fenceLine);
                            WriteStringLine(writer, actualContent);
                            WriteStringLine(writer, fence);
                            WriteStringLine(writer, "");

                            fileList.Add(content.Entry.DisplayPath ?? content.Entry.RelativePath);
                            totalBytes += entryTotalBytes;
                        }
                        else
                        {
                            if (ready.Error != null)
                            {
                                var errorMsg = $"[Error reading file: {ready.Error}]";
                                WriteStringLine(writer, errorMsg);
                                totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                continue;
                            }

                            if (ready.Length == -1)
                            {
                                var errorMsg =
                                    $"[File too large for fast streaming: {content.Entry.DisplayPath ?? content.Entry.RelativePath}]";
                                WriteStringLine(writer, errorMsg);
                                totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                continue;
                            }

                            var checkLen = Math.Min(ready.Length, 4096);
                            var isBinary = ready.Buffer!.AsSpan(0, checkLen).ContainsAny(NullByte);

                            if (isBinary)
                            {
                                var errorMsg =
                                    $"[Skipping binary file: {content.Entry.DisplayPath ?? content.Entry.RelativePath}]";
                                WriteStringLine(writer, errorMsg);
                                totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                continue;
                            }

                            // Apply compiled content filter to preview bytes — fast reject for streaming files.
                            if (!contentFilter.IsEmpty && !contentFilter.ShouldInclude(ready.Buffer!, ready.Length))
                            {
                                var errorMsg =
                                    $"[Skipped by content filter: {content.Entry.DisplayPath ?? content.Entry.RelativePath}]";
                                WriteStringLine(writer, errorMsg);
                                totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                continue;
                            }

                            var fence = GetFenceForBytes(ready.Buffer!, ready.Length);

                            var header = config.Markdown.FileHeaderTemplate.Replace("{path}",
                                content.Entry.DisplayPath ?? content.Entry.RelativePath,
                                StringComparison.OrdinalIgnoreCase);
                            var headerBytes = Utf8NoBom.GetByteCount(header) + NewlineByteCount;
                            var fenceLine = $"{fence}{content.Entry.Language}";
                            var fenceLineBytes = Utf8NoBom.GetByteCount(fenceLine) + NewlineByteCount;
                            var closingFenceBytes = Utf8NoBom.GetByteCount(fence) + NewlineByteCount;
                            var newlineBytes = NewlineByteCount;

                            // Pre-calculate entry size BEFORE writing to enforce maxMemoryBytes limit
                            // (BUG-002 fix: streaming branch was writing partial output before checking limit)
                            long contentBytesWritten = 0;
                            byte[]? contentBuffer = null;
                            string? crushedContent = null;

                            if (brainCrusher != null)
                            {
                                var rawText = Utf8NoBom.GetString(ready.Buffer!, 0, ready.Length);
                                crushedContent = brainCrusher.CrushBlock(rawText, content.Entry.Language);
                                contentBytesWritten = crushedContent.Length > 0
                                    ? Utf8NoBom.GetByteCount(crushedContent) + NewlineByteCount
                                    : 0;
                            }
                            else if (excludeLineIfStart != null && excludeLineIfStart.Any())
                            {
                                var excludeArray = excludeLineIfStart.ToArray();
                                var excludeNewline = excludeArray.Contains("\n");
                                var sb = new StringBuilder();
                                var span = Utf8NoBom.GetString(ready.Buffer!, 0, ready.Length).AsSpan();
                                var first = true;
                                foreach (var line in span.EnumerateLines())
                                {
                                    var trimmedLine = line.TrimStart().TrimStart('\uFEFF');
                                    if (excludeNewline && (trimmedLine.IsEmpty || trimmedLine.IsWhiteSpace())) continue;
                                    var shouldExclude = false;
                                    foreach (var startStr in excludeArray)
                                        if (startStr != "\n" &&
                                            trimmedLine.StartsWith(startStr, StringComparison.Ordinal))
                                        {
                                            shouldExclude = true;
                                            break;
                                        }

                                    if (shouldExclude) continue;
                                    if (!first) sb.Append('\n');
                                    sb.Append(line);
                                    first = false;
                                }

                                crushedContent = sb.ToString();
                                contentBytesWritten = Utf8NoBom.GetByteCount(crushedContent) + NewlineByteCount;
                            }
                            else
                            {
                                contentBuffer = ready.Buffer!;
                                var validLength = ready.Length;
                                while (validLength > 0)
                                {
                                    var b = contentBuffer[validLength - 1];
                                    if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n')
                                        validLength--;
                                    else
                                        break;
                                }

                                contentBytesWritten = validLength;
                            }

                            var needsTrailingNewline = true;
                            if (crushedContent != null && crushedContent.Length == 0) needsTrailingNewline = false;
                            if (contentBuffer != null && contentBytesWritten == 0) needsTrailingNewline = false;

                            var entryTotalBytes = headerBytes + fenceLineBytes + contentBytesWritten +
                                                  closingFenceBytes + newlineBytes;
                            if (needsTrailingNewline) entryTotalBytes += newlineBytes;
                            if (totalBytes + entryTotalBytes > maxMemoryBytes)
                                return Result<long>.Failure(
                                    $"Output size ({totalBytes + entryTotalBytes} bytes) would exceed maximum output limit ({maxMemoryBytes} bytes).");

                            // Write now that we've confirmed we have budget
                            WriteStringLine(writer, header);
                            WriteStringLine(writer, fenceLine);

                            if (crushedContent != null && crushedContent.Length > 0)
                            {
                                WriteStringLine(writer, crushedContent);
                            }
                            else if (contentBuffer != null && contentBytesWritten > 0)
                            {
                                const int chunkSize = 65536;
                                var remaining = (int)contentBytesWritten;
                                var offset = 0;
                                while (remaining > 0)
                                {
                                    var toWrite = Math.Min(remaining, chunkSize);
                                    var destMemory = writer.GetMemory(toWrite);
                                    contentBuffer.AsSpan(offset, toWrite).CopyTo(destMemory.Span);
                                    writer.Advance(toWrite);
                                    offset += toWrite;
                                    remaining -= toWrite;
                                }
                            }

                            if (needsTrailingNewline) WriteStringLine(writer, "");
                            WriteStringLine(writer, fence);
                            WriteStringLine(writer, "");

                            fileList.Add(content.Entry.DisplayPath ?? content.Entry.RelativePath);
                            totalBytes += entryTotalBytes;
                        }
                    }
                    finally
                    {
                        if (ready.Buffer != null) ArrayPool<byte>.Shared.Return(ready.Buffer);
                    }
                }
            }

            await generateTask;

            WriteProjectStructure(writer, fileList, config);
            await writer.FlushAsync();
            await writer.CompleteAsync();

            var projectStructureBytes =
                Utf8NoBom.GetByteCount(config.Markdown.ProjectStructureHeader) + NewlineByteCount;
            projectStructureBytes += Utf8NoBom.GetByteCount($"{config.Markdown.Fence}text") + NewlineByteCount;
            foreach (var path in fileList)
            {
                if (path.StartsWith('[')) continue;
                projectStructureBytes += Utf8NoBom.GetByteCount(path) + NewlineByteCount;
            }

            projectStructureBytes += Utf8NoBom.GetByteCount(config.Markdown.Fence) + NewlineByteCount;
            projectStructureBytes += NewlineByteCount;
            totalBytes += projectStructureBytes;

            return Result<long>.Success(totalBytes);
        }
        catch (OperationCanceledException)
        {
            return Result<long>.Failure("Operation cancelled.");
        }
        catch (Exception ex)
        {
            _logger.Error("Markdown generation failed", ex);
            return Result<long>.Failure(ex.Message);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteString(PipeWriter writer, ReadOnlySpan<char> str)
    {
        if (str.IsEmpty) return;

        // Optimization for small strings (most headers, fences, short lines)
        if (str.Length < 1024)
        {
            var maxBytes = Utf8NoBom.GetMaxByteCount(str.Length);
            var span = writer.GetSpan(maxBytes);
            var written = Utf8NoBom.GetBytes(str, span);
            writer.Advance(written);
            return;
        }

        // For larger strings, write in chunks to ensure we don't request 
        // excessively large spans from the PipeWriter which might cause 
        // internal buffer reallocations.
        const int chunkCharCount = 4096;
        var offset = 0;
        while (offset < str.Length)
        {
            var length = Math.Min(chunkCharCount, str.Length - offset);
            var chunk = str.Slice(offset, length);
            var maxBytes = Utf8NoBom.GetMaxByteCount(chunk.Length);
            var span = writer.GetSpan(maxBytes);
            var written = Utf8NoBom.GetBytes(chunk, span);
            writer.Advance(written);
            offset += length;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteStringLine(PipeWriter writer, string str)
    {
        WriteString(writer, str.AsSpan());
        var newlineSpan = writer.GetSpan(NewlineBytes.Length);
        NewlineBytes.CopyTo(newlineSpan);
        writer.Advance(NewlineBytes.Length);
    }

    private void WriteProjectStructure(PipeWriter writer, IEnumerable<string> filePaths, GcConfiguration config)
    {
        WriteStringLine(writer, config.Markdown.ProjectStructureHeader);
        WriteStringLine(writer, $"{config.Markdown.Fence}text");

        foreach (var path in filePaths)
        {
            if (path.StartsWith('[')) continue;
            WriteStringLine(writer, path);
        }

        WriteStringLine(writer, config.Markdown.Fence);
        WriteStringLine(writer, "");
    }

    // =========================================================================
    // Fence selection: find the longest backtick run in content and use run+1.
    // Shared by both in-memory and streamed paths so we never close early.
    // =========================================================================
    private static string GetFenceForContent(string content)
    {
        var longestRun = 3;
        var run = 0;
        for (var i = 0; i < content.Length; i++)
            if (content[i] == '`')
            {
                run++;
                if (run > longestRun) longestRun = run;
            }
            else
            {
                run = 0;
            }

        return new string('`', longestRun + 1);
    }

    // For streaming: scan the full byte span for the longest backtick run.
    private static string GetFenceForBytes(byte[] buffer, int length)
    {
        var longestRun = 3;
        var run = 0;
        for (var i = 0; i < length; i++)
            if (buffer[i] == '`')
            {
                run++;
                if (run > longestRun) longestRun = run;
            }
            else
            {
                run = 0;
            }

        return new string('`', longestRun + 1);
    }

    private void ProcessLineSequence(ReadOnlySequence<byte> lineSequence, string[] excludeArray, bool excludeNewline,
        PipeWriter writer, ref long contentBytesWritten, int newlineBytes)
    {
        var length = (int)lineSequence.Length;
        if (length == 0)
        {
            if (!excludeNewline)
            {
                var newlineSpan = writer.GetSpan(NewlineBytes.Length);
                NewlineBytes.CopyTo(newlineSpan);
                writer.Advance(NewlineBytes.Length);
                contentBytesWritten += newlineBytes;
            }

            return;
        }

        if (length > 0)
        {
            var lastByte = lineSequence.Slice(length - 1).FirstSpan[0];
            if (lastByte == '\r')
            {
                lineSequence = lineSequence.Slice(0, length - 1);
                length--;
            }
        }

        if (length == 0 && excludeNewline) return;

        var lineStr = Utf8NoBom.GetString(lineSequence);
        var trimmedLine = lineStr.TrimStart().TrimStart('\uFEFF');

        if (excludeNewline && (string.IsNullOrEmpty(lineStr) || string.IsNullOrWhiteSpace(lineStr))) return;

        var shouldExclude = false;
        foreach (var startStr in excludeArray)
            if (startStr != "\n" && trimmedLine.StartsWith(startStr, StringComparison.Ordinal))
            {
                shouldExclude = true;
                break;
            }

        if (!shouldExclude)
        {
            WriteStringLine(writer, lineStr);
            contentBytesWritten += Utf8NoBom.GetByteCount(lineStr) + newlineBytes;
        }
    }
}