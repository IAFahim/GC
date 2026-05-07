namespace gc.Domain.Interfaces;

public interface IBrainCrusher
{
    string CrushBlock(string code);

    string Uncrush(string crushed);

    string GetDictionaryHeader();
}
