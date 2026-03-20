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

        // Stream directly to StringWriter without StringBuilder intermediate buffer
        // This avoids OOM by writing content directly to the output string
        using var writer = new StringWriter();
        WriteMarkdownToStream(writer, sortedContents, args);
        writer.Flush();

        var result = writer.ToString();
        Logger.LogVerbose($"Generated markdown: {result.Length} characters");

        return result;
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

        using var writer = new StreamWriter(outputStream, new UTF8Encoding(false), leaveOpen: true);
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

        using var writer = new StreamWriter(filePath, false, new UTF8Encoding(false));
        WriteMarkdownToStream(writer, sortedContents, args);

        Logger.LogVerbose($"Markdown written to {filePath}");
    }

    private static string GetSafeFence(string? content, string filePath)
    {
        if (content != null)
        {
            if (content.Contains("```")) return "``````";
            if (content.Contains("````")) return "````````";
            if (content.Contains("`````")) return "``````````";
            return "```";
        }

        int maxBackticks = 0;
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.Contains("```")) continue;
                
                int currentBackticks = 0;
                int maxInLine = 0;
                for (int i = 0; i < line.Length; i++)
                {
                    if (line[i] == '`')
                    {
                        currentBackticks++;
                        maxInLine = Math.Max(maxInLine, currentBackticks);
                    }
                    else
                    {
                        currentBackticks = 0;
                    }
                }
                maxBackticks = Math.Max(maxBackticks, maxInLine);
            }
        }
        catch { }

        if (maxBackticks >= 5) return "``````````";
        if (maxBackticks == 4) return "````````";
        if (maxBackticks >= 3) return "``````";
        return "```";
    }

    private static void WriteMarkdownToStream(TextWriter writer, FileContent[] sortedContents, CliArguments args)
    {
        // Get configuration values
        var projectStructureHeader = args.Configuration?.Markdown?.ProjectStructureHeader ?? "_Project Structure:_";
        var textLang = "text"; // Default text language
        var defaultFence = args.Configuration?.Markdown?.Fence ?? "```";

        // Write file contents
        foreach (var content in sortedContents)
        {
            var fence = GetSafeFence(content.Content, content.Entry.Path);
            var fileHeader = args.Configuration?.Markdown?.FileHeaderTemplate ?? "## File: {path}";
            var headerText = fileHeader.Replace("{path}", content.Entry.Path, StringComparison.OrdinalIgnoreCase);

            writer.WriteLine(headerText);
            writer.WriteLine($"{fence}{content.Entry.Language}");
            
            if (content.Content != null)
            {
                writer.WriteLine(content.Content);
            }
            else
            {
                writer.Flush();
                try
                {
                    using var fs = new FileStream(content.Entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs, Encoding.UTF8);
                    char[] buffer = new char[8192];
                    int read;
                    while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        writer.Write(buffer, 0, read);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to read file contents for {content.Entry.Path}", ex);
                    writer.WriteLine($"[Error reading file: {ex.Message}]");
                }
                writer.WriteLine(); // Ensure newline after streaming
            }
            
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

        using var writer = new StreamWriter(outputStream, new UTF8Encoding(false), leaveOpen: true);

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
            var fence = GetSafeFence(content.Content, content.Entry.Path);
            var fileHeader = args.Configuration?.Markdown?.FileHeaderTemplate ?? "## File: {path}";
            var headerText = fileHeader.Replace("{path}", content.Entry.Path, StringComparison.OrdinalIgnoreCase);

            writer.WriteLine(headerText);
            writer.WriteLine($"{fence}{content.Entry.Language}");
            
            if (content.Content != null)
            {
                writer.WriteLine(content.Content);
            }
            else
            {
                writer.Flush();
                try
                {
                    using var fs = new FileStream(content.Entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs, Encoding.UTF8);
                    char[] buffer = new char[8192];
                    int read;
                    while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        writer.Write(buffer, 0, read);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to read file contents for {content.Entry.Path}", ex);
                    writer.WriteLine($"[Error reading file: {ex.Message}]");
                }
                writer.WriteLine(); // Ensure newline after streaming
            }
            
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