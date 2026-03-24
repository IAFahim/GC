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

    public async Task<Result<long>> GenerateMarkdownStreamingAsync(IEnumerable<FileContent> contents, Stream outputStream, GcConfiguration config, CancellationToken ct = default)
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

                // Calculate total bytes
                long entryTotalBytes;
                if (content.Content != null)
                {
                    // For in-memory content, add conditional newline if content doesn't end with \n
                    bool needsTrailingNewline = !content.Content.EndsWith('\n');
                    if (needsTrailingNewline)
                    {
                        // header\n + fence+lang\n + content + \n + fence\n + \n
                        entryTotalBytes = headerBytes + fenceLineBytes + contentBytes + newlineBytes + closingFenceBytes + newlineBytes;
                    }
                    else
                    {
                        // header\n + fence+lang\n + content (already has \n) + fence\n + \n
                        entryTotalBytes = headerBytes + fenceLineBytes + contentBytes + closingFenceBytes + newlineBytes;
                    }
                }
                else
                {
                    // header\n + fence+lang\n + content + \n + fence\n + \n
                    entryTotalBytes = headerBytes + fenceLineBytes + contentBytes + newlineBytes + closingFenceBytes + newlineBytes;
                }
                
                if (totalBytes + entryTotalBytes > maxMemoryBytes)
                {
                    return Result<long>.Failure($"Output size ({totalBytes + entryTotalBytes} bytes) would exceed maximum output limit ({maxMemoryBytes} bytes). Note: This limit applies to total output size, not RAM usage during streaming.");
                }

                await writer.WriteLineAsync(header);
                await writer.WriteLineAsync($"{fence}{content.Entry.Language}");

                if (content.Content != null)
                {
                    await writer.WriteAsync(content.Content);
                    // Ensure newline after content before closing fence
                    if (!content.Content.EndsWith('\n'))
                    {
                        await writer.WriteLineAsync();
                    }
                }
                else
                {
                    await writer.FlushAsync();
                    totalBytes += await StreamFileToOutputAsync(content.Entry.Path, writer.BaseStream, maxFileSize, ct);
                }

                // Ensure trailing newline before fence to prevent corruption (for streaming content)
                if (content.Content == null)
                {
                    await writer.WriteLineAsync();
                }
                await writer.WriteLineAsync(fence);
                await writer.WriteLineAsync();

                fileList.Add(content.Entry.Path);

                // Add structural bytes (header, fences, newlines) but not content bytes
                // Content bytes are either added during streaming (line 72) or need to be added here (line 87)
                if (content.Content == null)
                {
                    // Streaming case: content bytes already added in StreamFileToOutputAsync
                    totalBytes += entryTotalBytes - contentBytes;
                }
                else
                {
                    // Non-streaming case: add both content bytes and structural bytes
                    totalBytes += entryTotalBytes;
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

    public async Task<Result<string>> GenerateMarkdownAsync(IEnumerable<FileContent> contents, GcConfiguration config, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        var result = await GenerateMarkdownStreamingAsync(contents, ms, config, ct);
        if (!result.IsSuccess) return Result<string>.Failure(result.Error!);

        ms.Position = 0;
        using var reader = new StreamReader(ms, Utf8NoBom);
        var markdown = await reader.ReadToEndAsync(ct);

        // Apply compact mode if enabled
        if (config.Compact != CompactLevel.None)
        {
            markdown = CompactMarkdown(markdown, config.Compact);
        }

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
            // Check if file is binary first
            var isBinary = await _reader.IsBinaryFileAsync(path, ct);
            if (isBinary)
            {
                var errorMsg = $"[Skipping binary file: {path}]";
                var errorBytes = Utf8NoBom.GetBytes(errorMsg);
                await output.WriteAsync(errorBytes.AsMemory(0, errorBytes.Length), ct);
                return errorBytes.Length;
            }

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
        await writer.WriteLineAsync(); // Final blank line
    }

    /// <summary>
    /// Applies compression strategies based on CompactLevel to reduce token count.
    /// </summary>
    /// <param name="markdown">The markdown content to compress</param>
    /// <param name="level">Compression level (None, Mild, Aggressive)</param>
    /// <returns>Compressed markdown content</returns>
    public static string CompactMarkdown(string markdown, CompactLevel level)
    {
        if (string.IsNullOrEmpty(markdown) || level == CompactLevel.None)
            return markdown;

        return level switch
        {
            CompactLevel.Mild => CompactMild(markdown),
            CompactLevel.Aggressive => CompactAggressive(markdown),
            _ => markdown
        };
    }

    /// <summary>
    /// Mild compression: removes empty lines but preserves code structure.
    /// Does NOT collapse whitespace inside lines to avoid destroying string literals and indentation.
    /// </summary>
    private static string CompactMild(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        // Remove empty lines only
        var lines = markdown.Split('\n');
        var nonEmptyLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

        return string.Join('\n', nonEmptyLines);
    }

    /// <summary>
    /// Aggressive compression: removes empty lines, collapses whitespace, truncates long comments, removes metadata.
    /// </summary>
    private static string CompactAggressive(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        // Apply mild compression first
        var compressed = CompactMild(markdown);

        // Truncate long comments (lines starting with //)
        var lines = compressed.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // Truncate long comments (>80 chars) but keep code structure
            if (trimmed.StartsWith("//") && trimmed.Length > 80)
            {
                var indentation = line.Length - line.TrimStart().Length;
                lines[i] = new string(' ', indentation) + trimmed.Substring(0, 77) + "...";
            }
        }

        // Remove code fence metadata (keep only the actual content)
        var result = string.Join('\n', lines);

        // Remove common markdown metadata patterns
        result = System.Text.RegularExpressions.Regex.Replace(result, @"<!--.*?-->", "", System.Text.RegularExpressions.RegexOptions.Singleline); // HTML comments (multiline)
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\[.*?\]:\s*$", ""); // Reference-style links

        return result;
    }
}
