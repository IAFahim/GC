namespace gc.Domain.Interfaces;

/// <summary>
/// Token crusher for Brain Mode — compresses code for LLM context windows.
/// </summary>
public interface IBrainCrusher
{
    /// <summary>
    /// Crush a single code block (content inside markdown fences).
    /// </summary>
    string CrushBlock(string code);

    /// <summary>
    /// Decodes a crushed string back to readable form.
    /// </summary>
    string Uncrush(string crushed);

    /// <summary>
    /// Gets the dictionary header to prepend to crushed output.
    /// </summary>
    string GetDictionaryHeader();
}
