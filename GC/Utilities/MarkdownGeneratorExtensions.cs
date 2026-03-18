using System;
using System.Text;
using GC.Data;

namespace GC.Utilities;

public static class MarkdownGeneratorExtensions
{
    public static string GenerateMarkdown(this FileContent[] contents)
    {
        var builder = new StringBuilder(contents.Length * 4096);
        var paths = new string[contents.Length];

        for (var i = 0; i < contents.Length; i++)
        {
            var content = contents[i];
            paths[i] = content.Entry.Path;

            builder.AppendLine($"## File: {content.Entry.Path}");
            builder.AppendLine($"{Constants.MarkdownFence}{content.Entry.Language}");
            builder.AppendLine(content.Content);
            builder.AppendLine(Constants.MarkdownFence);
            builder.AppendLine();
        }

        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);

        builder.AppendLine(Constants.ProjectStructureHeader);
        builder.AppendLine($"{Constants.MarkdownFence}{Constants.TextLang}");
        
        for (var i = 0; i < paths.Length; i++)
        {
            builder.AppendLine(paths[i]);
        }
        
        builder.AppendLine(Constants.MarkdownFence);

        return builder.ToString();
    }
}