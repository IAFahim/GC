using System;
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

        var sortedContents = new FileContent[contents.Length];
        Array.Copy(contents, sortedContents, contents.Length);
        Array.Sort(sortedContents, (a, b) => string.Compare(a.Entry.Path, b.Entry.Path, StringComparison.OrdinalIgnoreCase));

        Logger.LogDebug("Sorted files by path");

        var builder = new StringBuilder(contents.Length * 4096);

        foreach (var content in sortedContents)
        {
            builder.AppendLine($"## File: {content.Entry.Path}");
            builder.AppendLine($"{Constants.MarkdownFence}{content.Entry.Language}");
            builder.AppendLine(content.Content);
            builder.AppendLine(Constants.MarkdownFence);
            builder.AppendLine();
        }

        Logger.LogDebug("Generated file content sections");

        builder.AppendLine(Constants.ProjectStructureHeader);
        builder.AppendLine($"{Constants.MarkdownFence}{Constants.TextLang}");

        foreach (var content in sortedContents)
        {
            builder.AppendLine(content.Entry.Path);
        }

        builder.AppendLine(Constants.MarkdownFence);

        Logger.LogVerbose($"Generated markdown: {builder.Length} characters");

        return builder.ToString();
    }
}