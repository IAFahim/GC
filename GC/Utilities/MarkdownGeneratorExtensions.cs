using System;
using System.IO;
using System.Text;
using GC.Data;

namespace GC.Utilities;

public static class MarkdownGeneratorExtensions
{
    public static string GenerateMarkdown(this FileContent[] contents)
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

        WriteMarkdownToStream(writer, sortedContents);
        writer.Flush();

        Logger.LogVerbose($"Generated markdown: {memoryStream.Length} bytes");

        return Encoding.UTF8.GetString(memoryStream.ToArray());
    }

    public static void GenerateMarkdownToStream(this FileContent[] contents, Stream outputStream)
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
        WriteMarkdownToStream(writer, sortedContents);
        writer.Flush();

        Logger.LogVerbose("Markdown generation complete");
    }

    public static void GenerateMarkdownToFile(this FileContent[] contents, string filePath)
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
        WriteMarkdownToStream(writer, sortedContents);

        Logger.LogVerbose($"Markdown written to {filePath}");
    }

    private static void WriteMarkdownToStream(StreamWriter writer, FileContent[] sortedContents)
    {
        // Write file contents
        foreach (var content in sortedContents)
        {
            writer.WriteLine($"## File: {content.Entry.Path}");
            writer.WriteLine($"{Constants.MarkdownFence}{content.Entry.Language}");
            writer.WriteLine(content.Content);
            writer.WriteLine(Constants.MarkdownFence);
            writer.WriteLine();
        }

        Logger.LogDebug("Generated file content sections");

        // Write project structure
        writer.WriteLine(Constants.ProjectStructureHeader);
        writer.WriteLine($"{Constants.MarkdownFence}{Constants.TextLang}");

        foreach (var content in sortedContents)
        {
            writer.WriteLine(content.Entry.Path);
        }

        writer.WriteLine(Constants.MarkdownFence);
    }
}