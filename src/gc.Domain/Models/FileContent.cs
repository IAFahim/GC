namespace gc.Domain.Models;

public readonly record struct FileContent(FileEntry Entry, string? Content, long Size)
{
    public bool IsStreaming => Content == null;
}
