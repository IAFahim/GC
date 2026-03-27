using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.Application.Services;

public sealed class MarkdownGenerator : IMarkdownGenerator
{
    private readonly ILogger _logger;
    private readonly IFileReader _reader;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static readonly int NewlineByteCount = Utf8NoBom.GetByteCount(Environment.NewLine);
    private static readonly int DefaultFenceByteCount = Utf8NoBom.GetByteCount("```");
    private static readonly byte[] NewlineBytes = Utf8NoBom.GetBytes(Environment.NewLine);

    private static readonly SearchValues<byte> NullByte = SearchValues.Create([(byte)0]);

    [ThreadStatic]
    private static StringBuilder? t_lineExclusionBuilder;

    public MarkdownGenerator(ILogger logger, IFileReader reader)
    {
        _logger = logger;
        _reader = reader;
    }

    public async Task<Result<long>> GenerateMarkdownStreamingAsync(IEnumerable<FileContent> contents, Stream outputStream, GcConfiguration config, IEnumerable<string>? excludeLineIfStart = null, CancellationToken ct = default)
    {
        try
        {
            var pipeOptions = new StreamPipeWriterOptions(leaveOpen: true, minimumBufferSize: 65536);
            var writer = PipeWriter.Create(outputStream, pipeOptions);
            
            var sortedContents = config.Output.SortByPath 
                ? contents.OrderBy(c => c.Entry.Path, StringComparer.OrdinalIgnoreCase)
                : contents;

            long totalBytes = 0;
            var fileList = new List<string>();
            var maxMemoryBytes = config.Limits.GetMaxMemoryBytesValue();
            var maxFileSize = config.Limits.GetMaxFileSizeBytes();

            int degreeOfParallelism = Environment.ProcessorCount;
            int maxPendingFiles = degreeOfParallelism * 2;
            
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
                                var fileInfo = new FileInfo(content.Entry.Path);
                                if (!fileInfo.Exists)
                                {
                                    await channel.Writer.WriteAsync((index, null, 0, $"File not found: {content.Entry.Path}"), token);
                                    return;
                                }
                                if (fileInfo.Length > maxFileSize)
                                {
                                    await channel.Writer.WriteAsync((index, null, 0, $"File size ({fileInfo.Length} bytes) exceeds maximum allowed size ({maxFileSize} bytes)"), token);
                                    return;
                                }

                                using var handle = File.OpenHandle(content.Entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.SequentialScan | FileOptions.Asynchronous);
                                long fileLength = RandomAccess.GetLength(handle);
                                
                                if (fileLength <= 10 * 1024 * 1024)
                                {
                                    int len = (int)fileLength;
                                    var buffer = ArrayPool<byte>.Shared.Rent(len);
                                    int bytesRead = await RandomAccess.ReadAsync(handle, buffer.AsMemory(0, len), 0, token);
                                    await channel.Writer.WriteAsync((index, buffer, bytesRead, null), token);
                                }
                                else
                                {
                                    await channel.Writer.WriteAsync((index, null, -1, null), token);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error($"Failed to read file {content.Entry.Path}", ex);
                                await channel.Writer.WriteAsync((index, null, 0, ex.Message), token);
                            }
                        });
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, ct);

            int nextExpectedIndex = 0;
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
                                bool first = true;
                                
                                foreach (var line in span.EnumerateLines())
                                {
                                    var trimmedLine = line.TrimStart().TrimStart('\uFEFF');
                                    if (excludeNewline && (trimmedLine.IsEmpty || trimmedLine.IsWhiteSpace()))
                                        continue;
                                        
                                    bool shouldExclude = false;
                                    foreach (var startStr in excludeArray)
                                    {
                                        if (startStr != "\n" && trimmedLine.StartsWith(startStr, StringComparison.Ordinal))
                                        {
                                            shouldExclude = true;
                                            break;
                                        }
                                    }
                                    
                                    if (shouldExclude)
                                        continue;
                                        
                                    if (!first) sb.Append('\n');
                                    sb.Append(line);
                                    first = false;
                                }
                                actualContent = sb.ToString();
                            }

                            var fence = "```";
                            if (actualContent.Contains("`````")) fence = "``````````";
                            else if (actualContent.Contains("````")) fence = "``````";

                            var header = config.Markdown.FileHeaderTemplate.Replace("{path}", content.Entry.Path, StringComparison.OrdinalIgnoreCase);
                            var headerBytes = Utf8NoBom.GetByteCount(header) + NewlineByteCount;
                            var fenceLine = $"{fence}{content.Entry.Language}";
                            var fenceLineBytes = Utf8NoBom.GetByteCount(fenceLine) + NewlineByteCount;
                            var closingFenceBytes = Utf8NoBom.GetByteCount(fence) + NewlineByteCount;
                            var newlineBytes = NewlineByteCount;

                            actualContent = actualContent.TrimEnd(' ', '\t', '\r', '\n');
                            var contentBytes = Utf8NoBom.GetByteCount(actualContent);

                            bool needsTrailingNewline = !actualContent.EndsWith('\n');
                            long entryTotalBytes = headerBytes + fenceLineBytes + contentBytes + closingFenceBytes + newlineBytes;
                            if (needsTrailingNewline && actualContent.Length > 0) entryTotalBytes += newlineBytes;

                            if (totalBytes + entryTotalBytes > maxMemoryBytes)
                            {
                                return Result<long>.Failure($"Output size ({totalBytes + entryTotalBytes} bytes) would exceed maximum output limit ({maxMemoryBytes} bytes).");
                            }

                            WriteStringLine(writer, header);
                            WriteStringLine(writer, fenceLine);
                            WriteStringLine(writer, actualContent);
                            WriteStringLine(writer, fence);
                            WriteStringLine(writer, "");

                            fileList.Add(content.Entry.Path);
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
                                // Fallback for massive files (streamed sequentially to avoid OOM)
                                // Not implemented here to keep test small, we'll just skip them in this draft
                                var errorMsg = $"[File too large for fast streaming: {content.Entry.Path}]";
                                WriteStringLine(writer, errorMsg);
                                totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                continue;
                            }

                            // Phase 1.4: Binary check using SearchValues — SIMD accelerated via AVX2/SSE2
                            int checkLen = Math.Min(ready.Length, 4096);
                            bool isBinary = ready.Buffer!.AsSpan(0, checkLen).ContainsAny(NullByte);

                            if (isBinary)
                            {
                                var errorMsg = $"[Skipping binary file: {content.Entry.Path}]";
                                WriteStringLine(writer, errorMsg);
                                totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                continue;
                            }

                            var sample = Utf8NoBom.GetString(ready.Buffer!, 0, Math.Min(ready.Length, 4096));
                            var fence = "```";
                            if (sample.Contains("`````")) fence = "``````````";
                            else if (sample.Contains("````")) fence = "``````";

                            var header = config.Markdown.FileHeaderTemplate.Replace("{path}", content.Entry.Path, StringComparison.OrdinalIgnoreCase);
                            var headerBytes = Utf8NoBom.GetByteCount(header) + NewlineByteCount;
                            var fenceLine = $"{fence}{content.Entry.Language}";
                            var fenceLineBytes = Utf8NoBom.GetByteCount(fenceLine) + NewlineByteCount;
                            var closingFenceBytes = Utf8NoBom.GetByteCount(fence) + NewlineByteCount;
                            var newlineBytes = NewlineByteCount;

                            WriteStringLine(writer, header);
                            WriteStringLine(writer, fenceLine);

                            long contentBytesWritten = 0;

                            if (excludeLineIfStart != null && excludeLineIfStart.Any())
                            {
                                var excludeArray = excludeLineIfStart.ToArray();
                                var excludeNewline = excludeArray.Contains("\n");
                                
                                var ms = new MemoryStream(ready.Buffer!, 0, ready.Length, writable: false);
                                var pipeReader = PipeReader.Create(ms, new StreamPipeReaderOptions(leaveOpen: true));
                                
                                while (true)
                                {
                                    var pr = await pipeReader.ReadAsync(ct);
                                    var pipeBuffer = pr.Buffer;
                                    
                                    SequencePosition? position = null;
                                    
                                    do
                                    {
                                        position = pipeBuffer.PositionOf((byte)'\n');
                                        if (position != null)
                                        {
                                            var lineSequence = pipeBuffer.Slice(0, position.Value);
                                            ProcessLineSequence(lineSequence, excludeArray, excludeNewline, writer, ref contentBytesWritten, newlineBytes);
                                            pipeBuffer = pipeBuffer.Slice(pipeBuffer.GetPosition(1, position.Value));
                                        }
                                    }
                                    while (position != null);
                                    
                                    pipeReader.AdvanceTo(pipeBuffer.Start, pipeBuffer.End);
                                    
                                    if (pr.IsCompleted)
                                    {
                                        if (!pipeBuffer.IsEmpty)
                                        {
                                            ProcessLineSequence(pipeBuffer, excludeArray, excludeNewline, writer, ref contentBytesWritten, newlineBytes);
                                        }
                                        break;
                                    }
                                }
                                await pipeReader.CompleteAsync();
                            }
                            else
                            {
                                if (ready.Length > 0)
                                {
                                    var destMemory = writer.GetMemory(ready.Length);
                                    ready.Buffer.AsSpan(0, ready.Length).CopyTo(destMemory.Span);
                                    writer.Advance(ready.Length);
                                    contentBytesWritten += ready.Length;
                                }
                            }

                            WriteStringLine(writer, "");
                            WriteStringLine(writer, fence);
                            WriteStringLine(writer, "");

                            long entryTotalBytes = headerBytes + fenceLineBytes + contentBytesWritten + closingFenceBytes + (newlineBytes * 2);

                            if (totalBytes + entryTotalBytes > maxMemoryBytes)
                            {
                                return Result<long>.Failure($"Output size ({totalBytes + entryTotalBytes} bytes) would exceed maximum output limit ({maxMemoryBytes} bytes).");
                            }

                            fileList.Add(content.Entry.Path);
                            totalBytes += entryTotalBytes;
                        }
                    }
                    finally
                    {
                        if (ready.Buffer != null)
                        {
                            System.Buffers.ArrayPool<byte>.Shared.Return(ready.Buffer);
                        }
                    }
                }
            }

            await generateTask;

            WriteProjectStructure(writer, fileList, config);
            await writer.FlushAsync();
            await writer.CompleteAsync();

            var projectStructureBytes = Utf8NoBom.GetByteCount(config.Markdown.ProjectStructureHeader) + NewlineByteCount;
            projectStructureBytes += Utf8NoBom.GetByteCount($"{config.Markdown.Fence}text") + NewlineByteCount;
            foreach (var path in fileList)
            {
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
    private static void WriteStringLine(PipeWriter writer, string str)
    {
        if (str.Length > 0)
        {
            int maxBytes = Utf8NoBom.GetMaxByteCount(str.Length);
            var span = writer.GetSpan(maxBytes);
            int written = Utf8NoBom.GetBytes(str, span);
            writer.Advance(written);
        }
        
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
            WriteStringLine(writer, path);
        }

        WriteStringLine(writer, config.Markdown.Fence);
        WriteStringLine(writer, "");
    }

    private void ProcessLineSequence(ReadOnlySequence<byte> lineSequence, string[] excludeArray, bool excludeNewline, PipeWriter writer, ref long contentBytesWritten, int newlineBytes)
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
        
        string lineStr = Utf8NoBom.GetString(lineSequence);
        var trimmedLine = lineStr.TrimStart().TrimStart('\uFEFF');
        
        if (excludeNewline && (string.IsNullOrEmpty(lineStr) || string.IsNullOrWhiteSpace(lineStr))) return;
        
        bool shouldExclude = false;
        foreach (var startStr in excludeArray)
        {
            if (startStr != "\n" && trimmedLine.StartsWith(startStr, StringComparison.Ordinal))
            {
                shouldExclude = true;
                break;
            }
        }
        
        if (!shouldExclude)
        {
            WriteStringLine(writer, lineStr);
            contentBytesWritten += Utf8NoBom.GetByteCount(lineStr) + newlineBytes;
        }
    }
}
