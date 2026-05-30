namespace gc.Domain.Models;

/// <summary>
///     Represents a file entry discovered during context generation.
///     The model is honest about paths:
///     - Root: the repository/root directory (absolute path)
///     - Relative: path relative to Root
///     - Absolute: computed as Path.GetFullPath(Path.Combine(Root, Relative))
///     This ensures Size is always computed against the correct base directory
///     and avoids the bug where cluster mode computed file sizes against CWD.
/// </summary>
public readonly record struct FileEntry
{
    public string Root { get; init; }
    public string Relative { get; init; }
    public string Extension { get; init; }
    public string Language { get; init; }
    public long Size { get; init; }
    public string? Display { get; init; }
    public string Absolute => string.IsNullOrEmpty(Root)
        ? Relative
        : System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, Relative));

    public FileEntry(
        string root,
        string relative,
        string extension,
        string language,
        long size,
        string? display = null)
    {
        Root = root;
        Relative = relative;
        Extension = extension;
        Language = language;
        Size = size;
        Display = display;
    }

    /// <summary>
    ///     Returns the relative path for backward compatibility.
    /// </summary>
    public string RelativePath => Relative;

    /// <summary>
    ///     Returns the absolute path for backward compatibility.
    /// </summary>
    public string AbsolutePath => Absolute;

    /// <summary>
    ///     Returns the display path or falls back to relative path.
    /// </summary>
    public string? DisplayPath => Display ?? (string.IsNullOrEmpty(Relative) ? Root : Relative);

    /// <summary>
    ///     Returns the path (relative for backward compatibility).
    /// </summary>
    public string Path => Relative;
}