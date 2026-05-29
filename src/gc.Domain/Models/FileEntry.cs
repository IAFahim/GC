namespace gc.Domain.Models;

public readonly record struct FileEntry(
    string AbsolutePath, 
    string RelativePath, 
    string Extension, 
    string Language, 
    long Size, 
    string? DisplayPath = null)
{
    // Backwards compatibility for older code expecting Path to be the relative path
    public string Path => RelativePath;
}
