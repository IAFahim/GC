namespace gc.Domain.Models;

using System.IO;

/// <summary>
/// Represents a file entry discovered during context generation.
/// 
/// The model is honest about paths:
/// - Root: the repository/root directory (absolute path)
/// - Relative: path relative to Root
/// - Absolute: computed as Path.GetFullPath(Path.Combine(Root, Relative))
/// 
/// This ensures Size is always computed against the correct base directory
/// and avoids the bug where cluster mode computed file sizes against CWD.
/// </summary>
public readonly record struct FileEntry(
    string Root,
    string Relative,
    string Extension,
    string Language,
    long Size,
    string? Display = null)
{
    /// <summary>
    /// Computed absolute path to the file. Always resolved against Root + Relative.
    /// </summary>
    public string Absolute => string.IsNullOrEmpty(Root) 
        ? Relative 
        : System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, Relative));

    /// <summary>
    /// Returns the relative path for backward compatibility.
    /// </summary>
    public string RelativePath => Relative;

    /// <summary>
    /// Returns the absolute path for backward compatibility.
    /// </summary>
    public string AbsolutePath => Absolute;

    /// <summary>
    /// Returns the display path or falls back to relative path.
    /// </summary>
    public string? DisplayPath => Display ?? (string.IsNullOrEmpty(Relative) ? Root : Relative);

    /// <summary>
    /// Returns the path (relative for backward compatibility).
    /// </summary>
    public string Path => Relative;
}
