namespace gc.Domain.Interfaces;

public interface IBrainCrusher
{
    /// <summary>
    /// Minifies a block of code using the given language/extension context.
    /// </summary>
    string CrushBlock(string code, string? language = null);

    string Uncrush(string crushed);

    string GetDictionaryHeader();
}
