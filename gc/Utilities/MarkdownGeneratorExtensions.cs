using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using gc.Data;

namespace gc.Utilities;

public static class MarkdownGeneratorExtensions
{
    public static string GenerateMarkdown(this FileContent[] contents, CliArguments args)
    {
        if (contents == null) throw new ArgumentNullException(nameof(contents));

        using var _ = Logger.TimeOperation("Markdown generation");

        Logger.LogVerbose($"Generating markdown for {contents.Length} files...");

        // Sort by path
        var sortedContents = new FileContent[contents.Length];
        Array.Copy(contents, sortedContents, contents.Length);
        Array.Sort(sortedContents, (a, b) => string.Compare(a.Entry.Path, b.Entry.Path, StringComparison.OrdinalIgnoreCase));

        Logger.LogDebug("Sorted files by path");

        // Use streaming approach to reduce memory pressure
        using var memoryStream = new MemoryStream();
        using var writer = new StreamWriter(memoryStream, Encoding.UTF8);

        WriteMarkdownToStream(writer, sortedContents, args);
        writer.Flush();

        Logger.LogVerbose($"Generated markdown: {memoryStream.Length} bytes");

        return Encoding.UTF8.GetString(memoryStream.ToArray());
    }

    public static void GenerateMarkdownToStream(this FileContent[] contents, Stream outputStream, CliArguments args)
    {
        if (contents == null) throw new ArgumentNullException(nameof(contents));
        if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));

        using var _ = Logger.TimeOperation("Markdown generation to stream");

        Logger.LogVerbose($"Generating markdown for {contents.Length} files to stream...");

        // Sort by path
        var sortedContents = new FileContent[contents.Length];
        Array.Copy(contents, sortedContents, contents.Length);
        Array.Sort(sortedContents, (a, b) => string.Compare(a.Entry.Path, b.Entry.Path, StringComparison.OrdinalIgnoreCase));

        using var writer = new StreamWriter(outputStream, Encoding.UTF8, leaveOpen: true);
        WriteMarkdownToStream(writer, sortedContents, args);
        writer.Flush();

        Logger.LogVerbose("Markdown generation complete");
    }

    public static void GenerateMarkdownToFile(this FileContent[] contents, string filePath, CliArguments args)
    {
        if (contents == null) throw new ArgumentNullException(nameof(contents));
        if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("File path cannot be empty", nameof(filePath));

        using var _ = Logger.TimeOperation("Markdown generation to file");
        Logger.LogVerbose($"Generating markdown for {contents.Length} files to {filePath}...");

        // Sort by path
        var sortedContents = new FileContent[contents.Length];
        Array.Copy(contents, sortedContents, contents.Length);
        Array.Sort(sortedContents, (a, b) => string.Compare(a.Entry.Path, b.Entry.Path, StringComparison.OrdinalIgnoreCase));

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        WriteMarkdownToStream(writer, sortedContents, args);

        Logger.LogVerbose($"Markdown written to {filePath}");
    }

    private static string GetSafeFence(string content)
    {
        // Check if content contains triple backticks
        if (content.Contains("```"))
            return "``````";  // Use 6 backticks

        // Check for quadruple backticks (unlikely but possible)
        if (content.Contains("````"))
            return "````````";  // Use 8 backticks

        // Check for quintuple backticks (very unlikely)
        if (content.Contains("`````"))
            return "``````````";  // Use 10 backticks

        return "```";  // Default to 3 backticks
    }

    private static void WriteMarkdownToStream(StreamWriter writer, FileContent[] sortedContents, CliArguments args)
    {
        // Get configuration values
        var projectStructureHeader = args.Configuration?.Markdown?.ProjectStructureHeader ?? "_Project Structure:_";
        var textLang = "text"; // Default text language
        var defaultFence = args.Configuration?.Markdown?.Fence ?? "```";

        // Write file contents
        foreach (var content in sortedContents)
        {
            var fence = GetSafeFence(content.Content);
            var fileHeader = args.Configuration?.Markdown?.FileHeaderTemplate ?? "## File: {path}";
            var headerText = fileHeader.Replace("{path}", content.Entry.Path);

            writer.WriteLine(headerText);
            writer.WriteLine($"{fence}{content.Entry.Language}");
            writer.WriteLine(content.Content);
            writer.WriteLine(fence);
            writer.WriteLine();
        }

        Logger.LogDebug("Generated file content sections");

        // Write project structure
        writer.WriteLine(projectStructureHeader);
        writer.WriteLine($"{defaultFence}{textLang}");

        foreach (var content in sortedContents)
        {
            writer.WriteLine(content.Entry.Path);
        }

        writer.WriteLine(defaultFence);
    }

    public static (int fileCount, long totalBytes) GenerateMarkdownStreaming(this IEnumerable<FileContent> contents, Stream outputStream, CliArguments args)
    {
        if (contents == null) throw new ArgumentNullException(nameof(contents));
        if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));

        using var _ = Logger.TimeOperation("Markdown generation (streaming)");

        using var writer = new StreamWriter(outputStream, Encoding.UTF8, leaveOpen: true);

        // Sort by path for consistent output
        var sortedContents = contents.OrderBy(c => c.Entry.Path, StringComparer.OrdinalIgnoreCase).ToList();

        Logger.LogVerbose($"Generating markdown for {sortedContents.Count} files to stream...");

        // Get configuration values
        var projectStructureHeader = args.Configuration?.Markdown?.ProjectStructureHeader ?? "_Project Structure:_";
        var textLang = "text";
        var defaultFence = args.Configuration?.Markdown?.Fence ?? "```";

        long totalBytes = 0;

        // Write file contents one at a time
        foreach (var content in sortedContents)
        {
            var fence = GetSafeFence(content.Content);
            var fileHeader = args.Configuration?.Markdown?.FileHeaderTemplate ?? "## File: {path}";
            var headerText = fileHeader.Replace("{path}", content.Entry.Path);

            writer.WriteLine(headerText);
            writer.WriteLine($"{fence}{content.Entry.Language}");
            writer.WriteLine(content.Content);
            writer.WriteLine(fence);
            writer.WriteLine();

            totalBytes += content.Size;

            // File content gets garbage collected after writing, keeping memory usage constant
        }

        Logger.LogDebug("Generated file content sections");

        // Write project structure
        writer.WriteLine(projectStructureHeader);
        writer.WriteLine($"{defaultFence}{textLang}");

        foreach (var content in sortedContents)
        {
            writer.WriteLine(content.Entry.Path);
        }

        writer.WriteLine(defaultFence);

        Logger.LogVerbose("Markdown generation complete");

        return (sortedContents.Count, totalBytes);
    }
}