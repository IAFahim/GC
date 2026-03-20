namespace gc.Domain.Models;

public sealed record FileEntry(string Path, string Extension, string Language, long Size);
