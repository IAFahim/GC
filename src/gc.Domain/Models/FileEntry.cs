namespace gc.Domain.Models;

public readonly record struct FileEntry(string Path, string Extension, string Language, long Size);
