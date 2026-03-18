using System;
using System.Text;
using GC.Data;

namespace GC.Utilities;

public static class MarkdownGeneratorExtensions
{
    public static string GenerateMarkdown(this FileContent[] contents)
    {
        var sortedContents = new FileContent[contents.Length];
        Array.Copy(contents, sortedContents, contents.Length);
        Array.Sort(sortedContents, (a, b) => string.Compare(a.Entry.Path, b.Entry.Path, StringComparison.OrdinalIgnoreCase));

        var builder = new StringBuilder(contents.Length * 4096);

        foreach (var content in sortedContents)
        {
            builder.AppendLine($"## File: {content.Entry.Path}");
            builder.AppendLine($"{Constants.MarkdownFence}{content.Entry.Language}");
            builder.AppendLine(content.Content);
            builder.AppendLine(Constants.MarkdownFence);
            builder.AppendLine();
        }

        builder.AppendLine(Constants.ProjectStructureHeader);
        builder.AppendLine($"{Constants.MarkdownFence}{Constants.TextLang}");

        foreach (var content in sortedContents)
        {
            builder.AppendLine(content.Entry.Path);
        }

        builder.AppendLine(Constants.MarkdownFence);

        return builder.ToString();
    }
}