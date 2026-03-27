using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
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

    // Phase 0.4: Cache constant byte counts — avoid redundant Encoding.GetByteCount on every file
    private static readonly int NewlineByteCount = Utf8NoBom.GetByteCount(Environment.NewLine);
    private static readonly int DefaultFenceByteCount = Utf8NoBom.GetByteCount("```");

    // Phase 0.1: Buffer size for StreamWriter — 64KB instead of 8KB
    private const int WriterBufferSize = 65536;

    // Phase 1.4: SearchValues for SIMD-accelerated binary detection
    private static readonly SearchValues<byte> NullByte = SearchValues.Create([(byte)0]);

    // Phase 1.2: Thread-local StringBuilder to avoid per-file allocation during line exclusion
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
            // Phase 0.1: Removed AutoFlush (was forcing kernel flush on EVERY write).
            // Single FlushAsync at the end. Buffer increased from 8KB to 64KB.
            using var writer = new StreamWriter(outputStream, Utf8NoBom, bufferSize: WriterBufferSize, leaveOpen: true);
            
            // Sort by path only if configured to do so
            var sortedContents = config.Output.SortByPath 
                ? contents.OrderBy(c => c.Entry.Path, StringComparer.OrdinalIgnoreCase)
                : contents;

            long totalBytes = 0;
            var fileList = new List<string>();
            var maxMemoryBytes = config.Limits.GetMaxMemoryBytesValue();
            var maxFileSize = config.Limits.GetMaxFileSizeBytes();

            foreach (var content in sortedContents)
            {
                ct.ThrowIfCancellationRequested();

                if (content.Content != null)
                {
                    // Filter lines if needed
                    var actualContent = content.Content;
                    if (excludeLineIfStart != null && excludeLineIfStart.Any())
                    {
                        var excludeArray = excludeLineIfStart.ToArray();
                        var excludeNewline = excludeArray.Contains("\n");
                        
                        var span = actualContent.AsSpan();
                        // Phase 1.2: Reuse thread-local StringBuilder — avoid allocation per file
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

                    // In-memory content processing
                    var fence = "```";
                    if (actualContent.Contains("`````")) fence = "``````````";
                    else if (actualContent.Contains("````")) fence = "``````";

                    // Phase 0.4: Use cached NewlineByteCount instead of recalculating per file
                    var header = config.Markdown.FileHeaderTemplate.Replace("{path}", content.Entry.Path, StringComparison.OrdinalIgnoreCase);
                    var headerBytes = Utf8NoBom.GetByteCount(header) + NewlineByteCount;
                    var fenceLine = $"{fence}{content.Entry.Language}";
                    var fenceLineBytes = Utf8NoBom.GetByteCount(fenceLine) + NewlineByteCount;
                    var closingFenceBytes = Utf8NoBom.GetByteCount(fence) + NewlineByteCount;
                    var newlineBytes = NewlineByteCount;
                    var contentBytes = Utf8NoBom.GetByteCount(actualContent);

                    // Skip the final trailing new line that gets added by splitting or when not needed
                    actualContent = actualContent.TrimEnd(' ', '\t', '\r', '\n');
                    contentBytes = Utf8NoBom.GetByteCount(actualContent);

                    bool needsTrailingNewline = !actualContent.EndsWith('\n');
                    long entryTotalBytes = headerBytes + fenceLineBytes + contentBytes + closingFenceBytes + newlineBytes;
                    if (needsTrailingNewline && actualContent.Length > 0) entryTotalBytes += newlineBytes;

                    if (totalBytes + entryTotalBytes > maxMemoryBytes)
                    {
                        return Result<long>.Failure($"Output size ({totalBytes + entryTotalBytes} bytes) would exceed maximum output limit ({maxMemoryBytes} bytes). Note: This limit applies to total output size, not RAM usage during streaming.");
                    }

                    await writer.WriteLineAsync(header);
                    await writer.WriteLineAsync(fenceLine);
                    await writer.WriteLineAsync(actualContent);
                    await writer.WriteLineAsync(fence);
                    await writer.WriteLineAsync();

                    fileList.Add(content.Entry.Path);
                    totalBytes += entryTotalBytes;
                }
                else
                {
                    // Streaming processing - open file ONCE
                    try
                    {
                        var fileInfo = new FileInfo(content.Entry.Path);
                        if (!fileInfo.Exists)
                        {
                            var errorMsg = $"[Error reading file: File not found: {content.Entry.Path}]";
                            await writer.WriteLineAsync(errorMsg);
                            totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                            continue;
                        }
                        if (fileInfo.Length > maxFileSize)
                        {
                            var errorMsg = $"[File size ({fileInfo.Length} bytes) exceeds maximum allowed size ({maxFileSize} bytes)]";
                            await writer.WriteLineAsync(errorMsg);
                            totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                            continue;
                        }

                        // Phase 3.4: SequentialScan hint for OS read-ahead prefetch
                        using var fs = new FileStream(content.Entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 0, FileOptions.SequentialScan);
                        
                        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(65536);
                        try
                        {
                            var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                            
                            // Phase 1.4: Binary check using SearchValues — SIMD accelerated via AVX2/SSE2
                            int checkLen = Math.Min(bytesRead, 4096);
                            bool isBinary = buffer.AsSpan(0, checkLen).ContainsAny(NullByte);

                            if (isBinary)
                            {
                                var errorMsg = $"[Skipping binary file: {content.Entry.Path}]";
                                await writer.WriteLineAsync(errorMsg);
                                totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                continue;
                            }

                            // Check safe fence
                            var sample = Utf8NoBom.GetString(buffer, 0, bytesRead);
                            var fence = "```";
                            if (sample.Contains("`````")) fence = "``````````";
                            else if (sample.Contains("````")) fence = "``````";

                            // Phase 0.4: Use cached NewlineByteCount
                            var header = config.Markdown.FileHeaderTemplate.Replace("{path}", content.Entry.Path, StringComparison.OrdinalIgnoreCase);
                            var headerBytes = Utf8NoBom.GetByteCount(header) + NewlineByteCount;
                            var fenceLine = $"{fence}{content.Entry.Language}";
                            var fenceLineBytes = Utf8NoBom.GetByteCount(fenceLine) + NewlineByteCount;
                            var closingFenceBytes = Utf8NoBom.GetByteCount(fence) + NewlineByteCount;
                            var newlineBytes = NewlineByteCount;
                            
                            await writer.WriteLineAsync(header);
                            await writer.WriteLineAsync(fenceLine);

                            long contentBytesWritten = 0;
                            
                            if (excludeLineIfStart != null && excludeLineIfStart.Any())
                            {
                                fs.Position = 0; // Reset position since we read header
                                var excludeArray = excludeLineIfStart.ToArray();
                                var excludeNewline = excludeArray.Contains("\n");
                                
                                var pipeReader = PipeReader.Create(fs, new StreamPipeReaderOptions(leaveOpen: true));
                                
                                while (true)
                                {
                                    var result = await pipeReader.ReadAsync(ct);
                                    var pipeBuffer = result.Buffer;
                                    
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
                                    
                                    if (result.IsCompleted)
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
                                if (bytesRead > 0)
                                {
                                    await writer.BaseStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                                    contentBytesWritten += bytesRead;
                                }
                                
                                await fs.CopyToAsync(writer.BaseStream, ct);
                                contentBytesWritten += fileInfo.Length - bytesRead;
                            }

                            await writer.WriteLineAsync();
                            await writer.WriteLineAsync(fence);
                            await writer.WriteLineAsync();

                            long entryTotalBytes = headerBytes + fenceLineBytes + contentBytesWritten + closingFenceBytes + (newlineBytes * 2);
                            
                            if (totalBytes + entryTotalBytes > maxMemoryBytes)
                            {
                                return Result<long>.Failure($"Output size ({totalBytes + entryTotalBytes} bytes) would exceed maximum output limit ({maxMemoryBytes} bytes). Note: This limit applies to total output size, not RAM usage during streaming.");
                            }

                            fileList.Add(content.Entry.Path);
                            totalBytes += entryTotalBytes;
                        }
                        finally
                        {
                            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to stream file {content.Entry.Path}", ex);
                        var errorMsg = $"[Error reading file: {ex.Message}]";
                        await writer.WriteLineAsync(errorMsg);
                        totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                    }
                }
            }

            await WriteProjectStructureAsync(writer, fileList, config);
            await writer.FlushAsync();

            // Phase 0.4: Use cached NewlineByteCount for project structure section
            var projectStructureBytes = Utf8NoBom.GetByteCount(config.Markdown.ProjectStructureHeader) + NewlineByteCount;
            projectStructureBytes += Utf8NoBom.GetByteCount($"{config.Markdown.Fence}text") + NewlineByteCount;
            foreach (var path in fileList)
            {
                projectStructureBytes += Utf8NoBom.GetByteCount(path) + NewlineByteCount;
            }
            projectStructureBytes += Utf8NoBom.GetByteCount(config.Markdown.Fence) + NewlineByteCount;
            // Add one more newline that's written after the closing fence
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

    private async Task WriteProjectStructureAsync(StreamWriter writer, IEnumerable<string> filePaths, GcConfiguration config)
    {
        await writer.WriteLineAsync(config.Markdown.ProjectStructureHeader);
        await writer.WriteLineAsync($"{config.Markdown.Fence}text");

        foreach (var path in filePaths)
        {
            await writer.WriteLineAsync(path);
        }

        await writer.WriteLineAsync(config.Markdown.Fence);
        await writer.WriteLineAsync(); // Final blank line
    }

    private void ProcessLineSequence(ReadOnlySequence<byte> lineSequence, string[] excludeArray, bool excludeNewline, StreamWriter writer, ref long contentBytesWritten, int newlineBytes)
    {
        var length = (int)lineSequence.Length;
        if (length == 0)
        {
            if (!excludeNewline)
            {
                writer.WriteLine();
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
            writer.WriteLine(lineStr);
            contentBytesWritten += Utf8NoBom.GetByteCount(lineStr) + newlineBytes;
        }
    }
}
