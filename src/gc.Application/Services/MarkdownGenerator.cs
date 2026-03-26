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

    public MarkdownGenerator(ILogger logger, IFileReader reader)
    {
        _logger = logger;
        _reader = reader;
    }

    public async Task<Result<long>> GenerateMarkdownStreamingAsync(IEnumerable<FileContent> contents, Stream outputStream, GcConfiguration config, IEnumerable<string>? excludeLineIfStart = null, CancellationToken ct = default)
    {
        try
        {
            using var writer = new StreamWriter(outputStream, Utf8NoBom, bufferSize: 8192, leaveOpen: true);
            
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
                        var lines = actualContent.Split('\n');
                        var keptLines = new List<string>();
                        foreach (var line in lines)
                        {
                            var trimmedLine = line.TrimStart();
                            bool shouldExclude = false;
                            
                            foreach (var startStr in excludeLineIfStart)
                            {
                                if (startStr == "\n" && string.IsNullOrWhiteSpace(line))
                                {
                                    shouldExclude = true;
                                    break;
                                }
                                else if (startStr != "\n" && trimmedLine.StartsWith(startStr))
                                {
                                    shouldExclude = true;
                                    break;
                                }
                            }
                            
                            if (!shouldExclude)
                            {
                                keptLines.Add(line);
                            }
                        }
                        actualContent = string.Join("\n", keptLines);
                    }

                    // In-memory content processing
                    var fence = "```";
                    if (actualContent.Contains("`````")) fence = "``````````";
                    else if (actualContent.Contains("````")) fence = "``````";

                    var header = config.Markdown.FileHeaderTemplate.Replace("{path}", content.Entry.Path, StringComparison.OrdinalIgnoreCase);
                    var headerBytes = Utf8NoBom.GetByteCount(header) + Utf8NoBom.GetByteCount(Environment.NewLine);
                    var fenceLine = $"{fence}{content.Entry.Language}";
                    var fenceLineBytes = Utf8NoBom.GetByteCount(fenceLine) + Utf8NoBom.GetByteCount(Environment.NewLine);
                    var closingFenceBytes = Utf8NoBom.GetByteCount(fence) + Utf8NoBom.GetByteCount(Environment.NewLine);
                    var newlineBytes = Utf8NoBom.GetByteCount(Environment.NewLine);
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
                    var fileInfo = new FileInfo(content.Entry.Path);
                    if (fileInfo.Length > maxFileSize)
                    {
                        var errorMsg = $"[File size ({fileInfo.Length} bytes) exceeds maximum allowed size ({maxFileSize} bytes)]";
                        await writer.WriteLineAsync(errorMsg);
                        totalBytes += Utf8NoBom.GetByteCount(errorMsg) + Utf8NoBom.GetByteCount(Environment.NewLine);
                        continue;
                    }

                    try
                    {
                        using var fs = new FileStream(content.Entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        
                        var buffer = new byte[8192];
                        var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                        
                        // Check binary
                        bool isBinary = false;
                        int checkLen = Math.Min(bytesRead, 4096);
                        for (int i = 0; i < checkLen; i++)
                        {
                            if (buffer[i] == 0) { isBinary = true; break; }
                        }

                        if (isBinary)
                        {
                            var errorMsg = $"[Skipping binary file: {content.Entry.Path}]";
                            await writer.WriteLineAsync(errorMsg);
                            totalBytes += Utf8NoBom.GetByteCount(errorMsg) + Utf8NoBom.GetByteCount(Environment.NewLine);
                            continue;
                        }

                        // Check safe fence
                        var sample = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        var fence = "```";
                        if (sample.Contains("`````")) fence = "``````````";
                        else if (sample.Contains("````")) fence = "``````";

                        // Calculate bytes
                        var header = config.Markdown.FileHeaderTemplate.Replace("{path}", content.Entry.Path, StringComparison.OrdinalIgnoreCase);
                        var headerBytes = Utf8NoBom.GetByteCount(header) + Utf8NoBom.GetByteCount(Environment.NewLine);
                        var fenceLine = $"{fence}{content.Entry.Language}";
                        var fenceLineBytes = Utf8NoBom.GetByteCount(fenceLine) + Utf8NoBom.GetByteCount(Environment.NewLine);
                        var closingFenceBytes = Utf8NoBom.GetByteCount(fence) + Utf8NoBom.GetByteCount(Environment.NewLine);
                        var newlineBytes = Utf8NoBom.GetByteCount(Environment.NewLine);
                        var contentBytes = fileInfo.Length;

                        long entryTotalBytes = headerBytes + fenceLineBytes + contentBytes + newlineBytes + closingFenceBytes + newlineBytes;

                        if (totalBytes + entryTotalBytes > maxMemoryBytes)
                        {
                            return Result<long>.Failure($"Output size ({totalBytes + entryTotalBytes} bytes) would exceed maximum output limit ({maxMemoryBytes} bytes). Note: This limit applies to total output size, not RAM usage during streaming.");
                        }

                        await writer.WriteLineAsync(header);
                        await writer.WriteLineAsync(fenceLine);
                        await writer.FlushAsync();

                        if (bytesRead > 0)
                        {
                            await writer.BaseStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        }
                        
                        await fs.CopyToAsync(writer.BaseStream, ct);

                        await writer.WriteLineAsync();
                        await writer.WriteLineAsync(fence);
                        await writer.WriteLineAsync();

                        fileList.Add(content.Entry.Path);
                        totalBytes += entryTotalBytes;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to stream file {content.Entry.Path}", ex);
                        var errorMsg = $"[Error reading file: {ex.Message}]";
                        await writer.WriteLineAsync(errorMsg);
                        totalBytes += Utf8NoBom.GetByteCount(errorMsg) + Utf8NoBom.GetByteCount(Environment.NewLine);
                    }
                }
            }

            await WriteProjectStructureAsync(writer, fileList, config);
            await writer.FlushAsync();

            // Add project structure bytes to total
            var projectStructureBytes = Utf8NoBom.GetByteCount(config.Markdown.ProjectStructureHeader) + Utf8NoBom.GetByteCount(Environment.NewLine);
            projectStructureBytes += Utf8NoBom.GetByteCount($"{config.Markdown.Fence}text") + Utf8NoBom.GetByteCount(Environment.NewLine);
            foreach (var path in fileList)
            {
                projectStructureBytes += Utf8NoBom.GetByteCount(path) + Utf8NoBom.GetByteCount(Environment.NewLine);
            }
            projectStructureBytes += Utf8NoBom.GetByteCount(config.Markdown.Fence) + Utf8NoBom.GetByteCount(Environment.NewLine);
            // Add one more newline that's written after the closing fence
            projectStructureBytes += Utf8NoBom.GetByteCount(Environment.NewLine);
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
}
