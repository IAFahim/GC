using System.Runtime.CompilerServices;
using System.Text;

namespace gc.Application.Services;

public sealed class DynamicCompressor
{
    public readonly record struct CompressResult(
        string Output,
        string Legend,
        int TokensSaved,
        int ReplacementCount);

    private const int MinTokenLength = 5;
    private const int MaxDynamicReplacements = 40;
    private const int MinPhraseFrequency = 2;
    private const int MinPhraseLength = 6;
    private const int MaxPhraseLength = 80;

    public CompressResult Compress(string text, int maxReplacements = MaxDynamicReplacements)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 50)
            return new CompressResult(text, "", 0, 0);

        text = StripAttributes(text);

        text = StripEmoji(text);

        var tokenCandidates = FindTokenCandidates(text, maxReplacements / 2);

        var phraseCandidates = SuffixArray.FindRepeatedPhrases(
            text, MinPhraseLength, MaxPhraseLength, MinPhraseFrequency, maxReplacements / 2);

        var allCandidates = MergeCandidates(tokenCandidates, phraseCandidates, maxReplacements);

        if (allCandidates.Count == 0)
            return new CompressResult(text, "", 0, 0);

        var (patterns, replacements, symbolMap) = GenerateSymbols(allCandidates);

        var ac = new AhoCorasick(patterns);
        var compressed = ac.ReplaceAll(text, replacements);

        var legend = BuildLegend(symbolMap);

        var totalSaved = allCandidates.Sum(c => c.Savings);
        return new CompressResult(compressed, legend, totalSaved, allCandidates.Count);
    }

    public string PreviewLegend(string text, int maxReplacements = MaxDynamicReplacements)
    {
        var result = Compress(text, maxReplacements);
        return result.Legend;
    }


    private static List<Candidate> FindTokenCandidates(string text, int maxCount)
    {
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        var i = 0;
        var len = text.Length;

        while (i < len)
        {
            if (!IsIdentStart(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < len && IsIdentChar(text[i]))
                i++;

            var token = text.Substring(start, i - start);

            if (token.Length >= MinTokenLength)
            {
                freq.TryGetValue(token, out var count);
                freq[token] = count + 1;
            }
        }

        var candidates = new List<Candidate>();
        foreach (var kvp in freq)
        {
            var savings = (kvp.Key.Length - 2) * kvp.Value;
            if (savings > 10)
            {
                candidates.Add(new Candidate(kvp.Key, kvp.Value, savings));
            }
        }

        candidates.Sort((a, b) => b.Savings.CompareTo(a.Savings));

        return candidates.Take(maxCount).ToList();
    }


    private static List<Candidate> MergeCandidates(
        List<Candidate> tokens,
        List<PhraseCandidate> phrases,
        int maxTotal)
    {
        var merged = new List<Candidate>();

        foreach (var p in phrases)
        {
            merged.Add(new Candidate(p.Phrase, p.Frequency, p.Savings));
        }

        var added = new HashSet<string>(merged.Select(m => m.Token));
        foreach (var t in tokens)
        {
            var isSubstr = merged.Any(m => m.Token.Contains(t.Token, StringComparison.Ordinal));
            if (!isSubstr && !added.Contains(t.Token))
            {
                merged.Add(t);
                added.Add(t.Token);
            }
        }

        merged.Sort((a, b) => b.Savings.CompareTo(a.Savings));
        return merged.Take(maxTotal).ToList();
    }


    private static (string[] patterns, string[] replacements, List<(string Symbol, string Original)> symbolMap)
        GenerateSymbols(List<Candidate> candidates)
    {
        var patterns = new string[candidates.Count];
        var replacements = new string[candidates.Count];
        var symbolMap = new List<(string Symbol, string Original)>(candidates.Count);

        var symbolIdx = 0;
        foreach (var c in candidates)
        {
            var symbol = GenerateSymbol(symbolIdx++);
            patterns[symbolMap.Count] = c.Token;
            replacements[symbolMap.Count] = symbol;
            symbolMap.Add((symbol, c.Token));
        }

        return (patterns, replacements, symbolMap);
    }

    private static string GenerateSymbol(int index)
    {
        if (index < 26)
            return $"_{(char)('a' + index)}";
        if (index < 52)
            return $"_{(char)('A' + index - 26)}";
        if (index < 152)
            return $"~{index - 52}";
        var i = index - 152;
        var c1 = (char)('a' + (i / 26));
        var c2 = (char)('a' + (i % 26));
        return $"_{c1}{c2}";
    }
    public static string StripAttributes(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length);
        var i = 0;
        var len = text.Length;

        while (i < len)
        {
            // Detect attribute start: '[' preceded by start-of-line or whitespace,
            // followed by identifier char. C# attributes always appear at statement start,
            // never mid-expression like array[index].
            if (text[i] == '[' && i + 1 < len && IsIdentStart(text[i + 1])
                && (i == 0 || text[i - 1] == '\n' || text[i - 1] == '\r' || char.IsWhiteSpace(text[i - 1])))
            {
                var depth = 1;
                i++;
                while (i < len && depth > 0)
                {
                    if (text[i] == '[') depth++;
                    else if (text[i] == ']') depth--;
                    i++;
                }
                sb.Append(' ');
                continue;
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }


    public static string StripEmoji(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var v = rune.Value;
            if (IsEmoji(v)) continue;
            sb.Append(rune);
        }

        return sb.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsEmoji(int codepoint)
    {
        return codepoint is >= 0x1F600 and <= 0x1F64F
            or >= 0x1F300 and <= 0x1F5FF
            or >= 0x1F680 and <= 0x1F6FF
            or >= 0x1F1E0 and <= 0x1F1FF
            or >= 0x2600 and <= 0x26FF
            or >= 0x2700 and <= 0x27BF
            or >= 0xFE00 and <= 0xFE0F
            or >= 0x1F900 and <= 0x1F9FF
            or >= 0x1FA00 and <= 0x1FA6F
            or >= 0x1FA70 and <= 0x1FAFF
            or 0x200D
            or 0x20E3
            or >= 0x2702 and <= 0x27B0
            or >= 0x1F004 and <= 0x1F0CF
            or >= 0x1F170 and <= 0x1F251;
    }


    private static string BuildLegend(List<(string Symbol, string Original)> symbolMap)
    {
        if (symbolMap.Count == 0) return "";

        var sb = new StringBuilder(symbolMap.Count * 40);
        sb.AppendLine("# Dynamic Compression Legend");

        var first = true;
        foreach (var (symbol, original) in symbolMap)
        {
            var clean = original.Replace("\r", "").Replace("\n", " ");
            if (!first) sb.Append(" | ");
            sb.Append($"{symbol}={clean}");
            first = false;
        }
        sb.AppendLine();
        sb.AppendLine();

        return sb.ToString();
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private readonly record struct Candidate(string Token, int Frequency, int Savings);
}
