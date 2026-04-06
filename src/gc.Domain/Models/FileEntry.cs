namespace gc.Domain.Models;

/// <summary>
/// Represents a file to be included in the generated output.
/// </summary>
/// <param name="Path">Absolute filesystem path for reading the file.</param>
/// <param name="Extension">File extension without dot (e.g., "cs", "js").</param>
/// <param name="Language">Language identifier for markdown fence (e.g., "csharp", "javascript").</param>
/// <param name="Size">File size in bytes.</param>
/// <param name="DisplayPath">
/// Optional display path used in markdown headers. When null, <see cref="Path"/> is used.
/// In cluster mode, this is "repo_relative/file_relative" for clarity.
/// </param>
public readonly record struct FileEntry(string Path, string Extension, string Language, long Size, string? DisplayPath = null);
