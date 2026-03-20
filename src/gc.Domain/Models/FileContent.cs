namespace gc.Domain.Models;

public sealed record FileContent(FileEntry Entry, string? Content, long Size)
{
    public bool IsStreaming => Content == null;
}
