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

    public long LastEstimatedTokens { get; private set; }

    public int LastEmittedFileCount { get; private set; }



    public async Task<Result<long>> GenerateMarkdownStreamingAsync(IEnumerable<FileContent> contents,
        Stream outputStream, GcConfiguration config, CompiledContentPatterns contentFilter,
        IEnumerable<string>? excludeLineIfStart = null, IBrainCrusher? brainCrusher = null,
        CancellationToken ct = default)
    {
        try
        {
            LastEstimatedTokens = 0;
            LastEmittedFileCount = 0;

            string[] excludeArray = excludeLineIfStart as string[]
                ?? excludeLineIfStart?.ToArray()
                ?? Array.Empty<string>();
            bool hasExclude = excludeArray.Length > 0;
            bool excludeNewline = Array.IndexOf(excludeArray, "\n") >= 0;

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

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = cts.Token;

            var channel = Channel.CreateBounded<(int Index, byte[]? Buffer, int Length, string? Error)>(
                new BoundedChannelOptions(maxPendingFiles) { SingleReader = true, SingleWriter = false });

            var generateTask = Task.Run(async () =>
            {
                try
                {
                    await Parallel.ForEachAsync(
                        Enumerable.Range(0, sortedList.Count),
                        new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism, CancellationToken = token },
                        async (index, threadToken) =>
                        {
                            var content = sortedList[index];
                            if (content.Content != null)
                            {
                                await channel.Writer.WriteAsync((index, null, 0, null), threadToken);
                                return;
                            }

                            try
                            {
                                // Compute the absolute path once: FileEntry.AbsolutePath runs
                                // Path.GetFullPath+Combine on every access, and this hot parallel loop
                                // would otherwise normalize it 3x per file (FileInfo + two open() calls).
                                var absolutePath = content.Entry.AbsolutePath;
                                var fileInfo = new FileInfo(absolutePath);
                                if (!fileInfo.Exists)
                                {
                                    await channel.Writer.WriteAsync(
                                        (index, null, 0,
                                            $"File not found: {content.Entry.DisplayPath ?? content.Entry.RelativePath}"),
                                        threadToken);
                                    return;
                                }

                                if (fileInfo.Length > maxFileSize)
                                {
                                    await channel.Writer.WriteAsync(
                                        (index, null, 0,
                                            $"File size ({fileInfo.Length} bytes) exceeds maximum allowed size ({maxFileSize} bytes)"),
                                        threadToken);
                                    return;
                                }

                                SafeFileHandle handle;
                                if (OperatingSystem.IsLinux())
                                {
                                    var fd = LinuxFastPath.open(absolutePath, 0x40000);
                                    if (fd < 0) fd = LinuxFastPath.open(absolutePath, 0);
                                    if (fd >= 0)
                                    {
                                        LinuxFastPath.posix_fadvise(fd, 0, 0, LinuxFastPath.POSIX_FADV_SEQUENTIAL);
                                        handle = new SafeFileHandle(fd, true);
                                    }
                                    else
                                    {
                                        handle = File.OpenHandle(absolutePath, FileMode.Open,
                                            FileAccess.Read, FileShare.ReadWrite,
                                            FileOptions.SequentialScan | FileOptions.Asynchronous);
                                    }
                                }
                                else
                                {
                                    handle = File.OpenHandle(absolutePath, FileMode.Open, FileAccess.Read,
                                        FileShare.ReadWrite, FileOptions.SequentialScan | FileOptions.Asynchronous);
                                }

                                using (handle)
                                {
                                    var fileLength = RandomAccess.GetLength(handle);

                                    // The file is already gated by maxFileSize above; read it whole up to the
                                    // single-buffer ceiling (int.MaxValue). Only genuinely enormous files
                                    // (>2 GB, unbufferable in one array) fall through to the placeholder, so a
                                    // user who raises maxFileSize past 10 MB now gets the real content.
                                    if (fileLength <= int.MaxValue)
                                    {
                                        var len = (int)fileLength;
                                        var buffer = ArrayPool<byte>.Shared.Rent(len);
                                        var deposited = false;
                                        try
                                        {
                                            var bytesRead = await RandomAccess.ReadAsync(handle, buffer.AsMemory(0, len),
                                                0, threadToken);
                                            await channel.Writer.WriteAsync((index, buffer, bytesRead, null),
                                                threadToken);
                                            deposited = true;
                                        }
                                        finally
                                        {
                                            if (!deposited) ArrayPool<byte>.Shared.Return(buffer);
                                        }
                                    }
                                    else
                                    {
                                        await channel.Writer.WriteAsync((index, null, -1, null), threadToken);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(
                                    $"Failed to read file {content.Entry.DisplayPath ?? content.Entry.RelativePath}",
                                    ex);
                                await channel.Writer.WriteAsync((index, null, 0, ex.Message), threadToken);
                            }
                        });
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, token);

            var nextExpectedIndex = 0;
            var outOfOrderBuffer = new Dictionary<int, (byte[]? Buffer, int Length, string? Error)>();
            long estimatedTokensAccumulator = 0;

            try
            {
                await foreach (var result in channel.Reader.ReadAllAsync(token))
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
                                // Synthetic cluster markers (repo separators/headers, Path "[...]") carry
                                // their own ready-made markdown; never line-filter or brain-crush them, or
                                // a -z prefix / minifier could mangle the separators.
                                var isSynthetic = content.Entry.Path.StartsWith('[');
                                if (hasExclude && !isSynthetic)
                                {
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

                                var header = (config.Markdown.FileHeaderTemplate ?? "## File: {path}").Replace("{path}",
                                    content.Entry.DisplayPath ?? content.Entry.RelativePath,
                                    StringComparison.Ordinal);
                                var headerBytes = Utf8NoBom.GetByteCount(header) + NewlineByteCount;
                                var fenceLine = $"{fence}{content.Entry.Language}";
                                var fenceLineBytes = Utf8NoBom.GetByteCount(fenceLine) + NewlineByteCount;
                                var closingFenceBytes = Utf8NoBom.GetByteCount(fence) + NewlineByteCount;
                                var newlineBytes = NewlineByteCount;

                                actualContent = actualContent.TrimEnd(' ', '\t', '\r', '\n');

                                if (brainCrusher != null && !isSynthetic)
                                    actualContent = brainCrusher.CrushBlock(actualContent, content.Entry.Language);

                                var contentBytes = Utf8NoBom.GetByteCount(actualContent);

                                var needsTrailingNewline = !actualContent.EndsWith('\n');
                                long entryTotalBytes = headerBytes + fenceLineBytes + contentBytes + closingFenceBytes +
                                                       newlineBytes;
                                if (needsTrailingNewline && actualContent.Length > 0) entryTotalBytes += newlineBytes;

                                if (totalBytes + entryTotalBytes > maxMemoryBytes)
                                {
                                    cts.Cancel();
                                    return Result<long>.Failure(
                                        $"Output size ({totalBytes + entryTotalBytes} bytes) would exceed maximum output limit ({maxMemoryBytes} bytes).");
                                }

                                WriteStringLine(writer, header);
                                WriteStringLine(writer, fenceLine);
                                WriteStringLine(writer, actualContent);
                                WriteStringLine(writer, fence);
                                WriteStringLine(writer, "");

                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(header);
                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(fenceLine);
                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(actualContent);
                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(fence);

                                fileList.Add(content.Entry.DisplayPath ?? content.Entry.RelativePath);
                                totalBytes += entryTotalBytes;
                                if (!content.Entry.Path.StartsWith('[')) LastEmittedFileCount++;
                            }
                            else
                            {
                                if (ready.Error != null)
                                {
                                    var errorMsg = $"[Error reading file: {ready.Error}]";
                                    WriteStringLine(writer, errorMsg);
                                    totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                    estimatedTokensAccumulator += TokenEstimator.EstimateTokens(errorMsg);
                                    continue;
                                }

                                if (ready.Length == -1)
                                {
                                    var errorMsg =
                                        $"[File too large to buffer (>2GB): {content.Entry.DisplayPath ?? content.Entry.RelativePath}]";
                                    WriteStringLine(writer, errorMsg);
                                    totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                    estimatedTokensAccumulator += TokenEstimator.EstimateTokens(errorMsg);
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
                                    estimatedTokensAccumulator += TokenEstimator.EstimateTokens(errorMsg);
                                    continue;
                                }

                                // Apply compiled content filter to preview bytes — fast reject for streaming files.
                                if (!contentFilter.IsEmpty && !contentFilter.ShouldInclude(ready.Buffer!, ready.Length))
                                {
                                    var errorMsg =
                                        $"[Skipped by content filter: {content.Entry.DisplayPath ?? content.Entry.RelativePath}]";
                                    WriteStringLine(writer, errorMsg);
                                    totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                    estimatedTokensAccumulator += TokenEstimator.EstimateTokens(errorMsg);
                                    continue;
                                }

                                var fence = GetFenceForBytes(ready.Buffer!, ready.Length);

                                var header = (config.Markdown.FileHeaderTemplate ?? "## File: {path}").Replace("{path}",
                                    content.Entry.DisplayPath ?? content.Entry.RelativePath,
                                    StringComparison.Ordinal);
                                var headerBytes = Utf8NoBom.GetByteCount(header) + NewlineByteCount;
                                var fenceLine = $"{fence}{content.Entry.Language}";
                                var fenceLineBytes = Utf8NoBom.GetByteCount(fenceLine) + NewlineByteCount;
                                var closingFenceBytes = Utf8NoBom.GetByteCount(fence) + NewlineByteCount;
                                var newlineBytes = NewlineByteCount;

                                // Pre-calculate entry size BEFORE writing to enforce maxMemoryBytes limit
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
                                else if (hasExclude)
                                {
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
                                {
                                    cts.Cancel();
                                    return Result<long>.Failure(
                                        $"Output size ({totalBytes + entryTotalBytes} bytes) would exceed maximum output limit ({maxMemoryBytes} bytes).");
                                }

                                // Write now that we've confirmed we have budget
                                WriteStringLine(writer, header);
                                WriteStringLine(writer, fenceLine);

                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(header);
                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(fenceLine);

                                if (crushedContent != null && crushedContent.Length > 0)
                                {
                                    WriteStringLine(writer, crushedContent);
                                    estimatedTokensAccumulator += TokenEstimator.EstimateTokens(crushedContent);
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
                                    var decoder = Utf8NoBom.GetDecoder();
                                    var byteRemaining = (int)contentBytesWritten;
                                    var byteOffset = 0;
                                    const int decodeChunk = 65536;
                                    var charBuf = ArrayPool<char>.Shared.Rent(decodeChunk + 8);
                                    // Streaming estimator carries the partial word across decode chunks so an
                                    // identifier split by a 64KB boundary is not counted twice.
                                    var streamingEstimator = new TokenEstimator.StreamingTokenEstimator();
                                    try
                                    {
                                        while (byteRemaining > 0)
                                        {
                                            var byteCount = Math.Min(byteRemaining, decodeChunk);
                                            var flush = byteCount == byteRemaining;
                                            var charsDecoded = decoder.GetChars(
                                                contentBuffer.AsSpan(byteOffset, byteCount),
                                                charBuf.AsSpan(),
                                                flush);
                                            streamingEstimator.Append(charBuf.AsSpan(0, charsDecoded));
                                            byteOffset += byteCount;
                                            byteRemaining -= byteCount;
                                        }
                                        streamingEstimator.Flush();
                                        estimatedTokensAccumulator += streamingEstimator.Tokens;
                                    }
                                    finally
                                    {
                                        ArrayPool<char>.Shared.Return(charBuf);
                                    }
                                }

                                if (needsTrailingNewline) WriteStringLine(writer, "");
                                WriteStringLine(writer, fence);
                                WriteStringLine(writer, "");

                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(fence);

                                fileList.Add(content.Entry.DisplayPath ?? content.Entry.RelativePath);
                                totalBytes += entryTotalBytes;
                                if (!content.Entry.Path.StartsWith('[')) LastEmittedFileCount++;
                            }
                        }
                        finally
                        {
                            if (ready.Buffer != null) ArrayPool<byte>.Shared.Return(ready.Buffer);
                        }
                    }
                }
            }
            finally
            {
                cts.Cancel();

                // Return any remaining buffers in outOfOrderBuffer to ArrayPool
                foreach (var item in outOfOrderBuffer.Values)
                {
                    if (item.Buffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(item.Buffer);
                    }
                }

                try
                {
                    await generateTask;
                }
                catch
                {
                    // Ignore exceptions from background task during cleanup
                }

                // Drain any remaining items in the channel
                while (channel.Reader.TryRead(out var result))
                {
                    if (result.Buffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(result.Buffer);
                    }
                }
            }

            WriteProjectStructure(writer, fileList, config);
            await writer.FlushAsync();
            await writer.CompleteAsync();

            var structureHeader = config.Markdown.ProjectStructureHeader ?? "_Project Structure:_ ";
            var structureFence = config.Markdown.Fence ?? "```";
            var projectStructureBytes =
                Utf8NoBom.GetByteCount(structureHeader) + NewlineByteCount;
            projectStructureBytes += Utf8NoBom.GetByteCount($"{structureFence}text") + NewlineByteCount;
            foreach (var path in fileList)
            {
                if (path.StartsWith('[')) continue;
                projectStructureBytes += Utf8NoBom.GetByteCount(path) + NewlineByteCount;
            }

            projectStructureBytes += Utf8NoBom.GetByteCount(structureFence) + NewlineByteCount;
            projectStructureBytes += NewlineByteCount;
            totalBytes += projectStructureBytes;

            LastEstimatedTokens = estimatedTokensAccumulator;

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
        var structureFence = config.Markdown.Fence ?? "```";
        WriteStringLine(writer, config.Markdown.ProjectStructureHeader ?? "_Project Structure:_ ");
        WriteStringLine(writer, $"{structureFence}text");

        foreach (var path in filePaths)
        {
            if (path.StartsWith('[')) continue;
            WriteStringLine(writer, path);
        }

        WriteStringLine(writer, structureFence);
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
}