using System.Text;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.Application.Services;

public sealed class MarkdownGenerator : IMarkdownGenerator
{
    private readonly ILogger _logger;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public MarkdownGenerator(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<Result<long>> GenerateMarkdownStreamingAsync(IEnumerable<FileContent> contents, Stream outputStream, GcConfiguration config, CancellationToken ct = default)
    {
        try
        {
            using var writer = new StreamWriter(outputStream, Utf8NoBom, bufferSize: 8192, leaveOpen: true);
            var sortedContents = contents.OrderBy(c => c.Entry.Path, StringComparer.OrdinalIgnoreCase);

            long totalBytes = 0;
            var fileList = new List<string>();
            var maxMemoryBytes = config.Limits.GetMaxMemoryBytesValue();
            var maxFileSize = config.Limits.GetMaxFileSizeBytes();

            foreach (var content in sortedContents)
            {
                ct.ThrowIfCancellationRequested();

                var fence = await GetSafeFenceAsync(content, ct);
                var header = config.Markdown.FileHeaderTemplate.Replace("{path}", content.Entry.Path, StringComparison.OrdinalIgnoreCase);

                var headerBytes = Utf8NoBom.GetByteCount(header) + Utf8NoBom.GetByteCount(Environment.NewLine);
                var fenceLine = $"{fence}{content.Entry.Language}";
                var fenceLineBytes = Utf8NoBom.GetByteCount(fenceLine) + Utf8NoBom.GetByteCount(Environment.NewLine);
                var closingFenceBytes = Utf8NoBom.GetByteCount(fence) + Utf8NoBom.GetByteCount(Environment.NewLine);
                var newlineBytes = Utf8NoBom.GetByteCount(Environment.NewLine);

                long contentBytes = 0;
                if (content.Content != null)
                {
                    contentBytes = Utf8NoBom.GetByteCount(content.Content);
                }
                else
                {
                    var fileInfo = new FileInfo(content.Entry.Path);
                    contentBytes = fileInfo.Length;
                }

                var entryTotalBytes = headerBytes + fenceLineBytes + contentBytes + newlineBytes + closingFenceBytes + newlineBytes;
                
                if (totalBytes + entryTotalBytes > maxMemoryBytes)
                {
                    return Result<long>.Failure($"Output size ({totalBytes + entryTotalBytes} bytes) would exceed maximum memory limit ({maxMemoryBytes} bytes)");
                }

                await writer.WriteLineAsync(header);
                await writer.WriteLineAsync($"{fence}{content.Entry.Language}");

                if (content.Content != null)
                {
                    await writer.WriteAsync(content.Content);
                }
                else
                {
                    await writer.FlushAsync();
                    totalBytes += await StreamFileToOutputAsync(content.Entry.Path, writer.BaseStream, maxFileSize, ct);
                }

                // Ensure trailing newline before fence to prevent corruption
                if (content.Content == null)
                {
                    await writer.WriteLineAsync();
                }
                await writer.WriteLineAsync(fence);
                await writer.WriteLineAsync();

                fileList.Add(content.Entry.Path);
                
                if (content.Content != null)
                {
                    totalBytes += Utf8NoBom.GetByteCount(content.Content);
                }

                totalBytes += entryTotalBytes - contentBytes;
            }

            await WriteProjectStructureAsync(writer, fileList, config);
            await writer.FlushAsync();

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

    public async Task<Result<string>> GenerateMarkdownAsync(IEnumerable<FileContent> contents, GcConfiguration config, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        var result = await GenerateMarkdownStreamingAsync(contents, ms, config, ct);
        if (!result.IsSuccess) return Result<string>.Failure(result.Error!);

        ms.Position = 0;
        using var reader = new StreamReader(ms, Utf8NoBom);
        var markdown = await reader.ReadToEndAsync(ct);
        return Result<string>.Success(markdown);
    }

    private async Task<string> GetSafeFenceAsync(FileContent content, CancellationToken ct)
    {
        if (content.Content != null)
        {
            if (content.Content.Contains("`````")) return "``````````";
            if (content.Content.Contains("````")) return "````````";
            if (content.Content.Contains("```")) return "``````";
            return "```";
        }

        // Streaming mode: check first few KB
        try
        {
            using var fs = new FileStream(content.Entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[8192];
            var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            var sample = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            if (sample.Contains("`````")) return "``````````";
            if (sample.Contains("````")) return "````````";
            if (sample.Contains("```")) return "``````";
        }
        catch { }

        return "```";
    }

    private async Task<long> StreamFileToOutputAsync(string path, Stream output, long maxFileSize, CancellationToken ct)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > maxFileSize)
            {
                var errorMsg = $"[File size ({fileInfo.Length} bytes) exceeds maximum allowed size ({maxFileSize} bytes)]";
                var errorBytes = Utf8NoBom.GetBytes(errorMsg);
                await output.WriteAsync(errorBytes.AsMemory(0, errorBytes.Length), ct);
                return errorBytes.Length;
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await fs.CopyToAsync(output, ct);
            return fs.Length;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to stream file {path}", ex);
            var errorMsg = $"[Error reading file: {ex.Message}]";
            var errorBytes = Utf8NoBom.GetBytes(errorMsg);
            await output.WriteAsync(errorBytes.AsMemory(0, errorBytes.Length), ct);
            return errorBytes.Length;
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
    }
}
