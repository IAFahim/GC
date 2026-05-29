namespace gc.Domain.Interfaces;

/// <summary>
///     Immutable compiled pattern set for batch content filtering.
///     Build once, reuse for all files to avoid rebuilding automatons per-file.
/// </summary>
public readonly struct CompiledContentPatterns
{
    /// <summary>
    ///     Delegate that checks if content should be included.
    /// </summary>
    private readonly Func<string, bool>? _shouldIncludeText;

    private readonly Func<byte[], int, bool>? _shouldIncludeBytes;

    public CompiledContentPatterns(
        Func<string, bool> shouldIncludeText,
        Func<byte[], int, bool>? shouldIncludeBytes = null)
    {
        _shouldIncludeText = shouldIncludeText;
        _shouldIncludeBytes = shouldIncludeBytes;
    }

    public bool IsEmpty => _shouldIncludeText == null;

    /// <summary>
    ///     Check if text content passes the filter.
    /// </summary>
    public bool ShouldInclude(string content)
    {
        return _shouldIncludeText?.Invoke(content) ?? true;
    }

    /// <summary>
    ///     Check if raw bytes (UTF-8 preview) pass the filter.
    /// </summary>
    public bool ShouldInclude(byte[] buffer, int length)
    {
        return _shouldIncludeBytes?.Invoke(buffer, length) ?? true;
    }
}