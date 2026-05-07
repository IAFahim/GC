using System.Runtime.CompilerServices;
using System.Text;

namespace gc.Application.Services;

/// <summary>
/// Dynamic Algorithmic Compression — second pass after BrainCrusher.
/// 
/// Architecture:
/// 1. TOKEN SCANNER: Parse text into tokens (identifiers ≥ minTokenLength chars).
///    Count frequency O(N). Score = (Token.Length - 2) * Frequency. Sort descending.
/// 
/// 2. PHRASE FINDER: Suffix array detects repeated multi-word phrases.
///    e.g., "bucketCapacityMask" repeating 10×, "IColumn&lt;T&gt;.Remove" repeating 5×.
/// 
/// 3. REPLACEMENT: Aho-Corasick trie does single-pass O(N) multi-pattern replacement.
///    Generates shortest unique symbols (_a, _b, ~1, ~2, etc).
/// 
/// 4. ATTRIBUTE STRIP: Removes [MethodImpl(...)], [Fact], etc.
///    Brace-counting parser. AI ignores compilation hints.
/// 
/// 5. NO EMOJI: Strips all emoji from output.
/// </summary>
public sealed class DynamicCompressor
{
    /// <summary>
    /// Result of dynamic compression. Contains the compressed output and the legend.
    /// </summary>
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

    /// <summary>
    /// Compresses text using dynamic frequency analysis + phrase detection.
    /// This is the main entry point. Call AFTER BrainCrusher has done its static pass.
    /// </summary>
    public CompressResult Compress(string text, int maxReplacements = MaxDynamicReplacements)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 50)
            return new CompressResult(text, "", 0, 0);

        // Phase 1: Strip attributes
        text = StripAttributes(text);

        // Phase 2: Strip emoji
        text = StripEmoji(text);

        // Phase 3: Find high-value token replacements (word-level)
        var tokenCandidates = FindTokenCandidates(text, maxReplacements / 2);

        // Phase 4: Find phrase-level replacements
        var phraseCandidates = SuffixArray.FindRepeatedPhrases(
            text, MinPhraseLength, MaxPhraseLength, MinPhraseFrequency, maxReplacements / 2);

        // Phase 5: Merge and deduplicate candidates
        var allCandidates = MergeCandidates(tokenCandidates, phraseCandidates, maxReplacements);

        if (allCandidates.Count == 0)
            return new CompressResult(text, "", 0, 0);

        // Phase 6: Generate replacement symbols
        var (patterns, replacements, symbolMap) = GenerateSymbols(allCandidates);

        // Phase 7: Aho-Corasick single-pass replacement
        var ac = new AhoCorasick(patterns);
        var compressed = ac.ReplaceAll(text, replacements);

        // Phase 8: Build legend
        var legend = BuildLegend(symbolMap);

        var totalSaved = allCandidates.Sum(c => c.Savings);
        return new CompressResult(compressed, legend, totalSaved, allCandidates.Count);
    }

    /// <summary>
    /// Gets just the legend header for a pre-scan (without applying compression).
    /// Useful for preview.
    /// </summary>
    public string PreviewLegend(string text, int maxReplacements = MaxDynamicReplacements)
    {
        var result = Compress(text, maxReplacements);
        return result.Legend;
    }

    // =========================================================================
    // Token Frequency Scanner
    // =========================================================================

    private static List<Candidate> FindTokenCandidates(string text, int maxCount)
    {
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        var i = 0;
        var len = text.Length;

        while (i < len)
        {
            // Skip non-identifier chars
            if (!IsIdentStart(text[i]))
            {
                i++;
                continue;
            }

            // Capture identifier
            var start = i;
            while (i < len && IsIdentChar(text[i]))
                i++;

            var token = text.Substring(start, i - start);

            // Only consider tokens >= min length
            if (token.Length >= MinTokenLength)
            {
                freq.TryGetValue(token, out var count);
                freq[token] = count + 1;
            }
        }

        // Score and sort
        var candidates = new List<Candidate>();
        foreach (var kvp in freq)
        {
            var savings = (kvp.Key.Length - 2) * kvp.Value;
            if (savings > 10) // minimum savings threshold
            {
                candidates.Add(new Candidate(kvp.Key, kvp.Value, savings));
            }
        }

        candidates.Sort((a, b) => b.Savings.CompareTo(a.Savings));
        if (candidates.Count > maxCount)
            candidates.Capacity = maxCount;

        return candidates.Take(maxCount).ToList();
    }

    // =========================================================================
    // Merge token + phrase candidates, deduplicate
    // =========================================================================

    private static List<Candidate> MergeCandidates(
        List<Candidate> tokens,
        List<PhraseCandidate> phrases,
        int maxTotal)
    {
        var merged = new List<Candidate>();

        // Add phrase candidates first (higher value)
        foreach (var p in phrases)
        {
            merged.Add(new Candidate(p.Phrase, p.Frequency, p.Savings));
        }

        // Add token candidates that aren't substrings of already-added phrases
        var added = new HashSet<string>(merged.Select(m => m.Token));
        foreach (var t in tokens)
        {
            // Skip if it's a substring of a longer phrase already selected
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

    // =========================================================================
    // Symbol Generation
    // =========================================================================

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

    /// <summary>
    /// Generates shortest possible unique symbols.
    /// Pattern: _a, _b, ..., _z, _A, _B, ..., ~1, ~2, ..., ~99, etc.
    /// </summary>
    private static string GenerateSymbol(int index)
    {
        // Use underscore prefix for first 52, tilde for next 100, then double-char
        if (index < 26)
            return $"_{(char)('a' + index)}";
        if (index < 52)
            return $"_{(char)('A' + index - 26)}";
        if (index < 152)
            return $"~{index - 52}";
        // Double-char: _aa, _ab, ...
        var i = index - 152;
        var c1 = (char)('a' + (i / 26));
        var c2 = (char)('a' + (i % 26));
        return $"_{c1}{c2}";
    }

    // =========================================================================
    // Attribute Stripping
    // =========================================================================

    /// <summary>
    /// Strips C# attributes: [MethodImpl(...)], [Fact], [Obsolete("msg")], etc.
    /// Uses brace/bracket counting to handle nested brackets.
    /// </summary>
    public static string StripAttributes(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length);
        var i = 0;
        var len = text.Length;

        while (i < len)
        {
            // Detect attribute start: '[' followed by identifier char
            if (text[i] == '[' && i + 1 < len && IsIdentStart(text[i + 1]))
            {
                // Skip the entire attribute including nested brackets
                var depth = 1;
                var start = i;
                i++; // skip '['

                while (i < len && depth > 0)
                {
                    if (text[i] == '[') depth++;
                    else if (text[i] == ']') depth--;
                    i++;
                }

                // Replace attribute with single space (avoid merging adjacent tokens)
                sb.Append(' ');
                continue;
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }

    // =========================================================================
    // Emoji Stripping
    // =========================================================================

    /// <summary>
    /// Strips all emoji characters from text. Enforces strict NO EMOJI rule.
    /// </summary>
    public static string StripEmoji(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            // Emoji ranges: U+1F600-U+1F64F (emoticons), U+1F300-U+1F5FF (misc symbols),
            // U+1F680-U+1F6FF (transport), U+1F1E0-U+1F1FF (flags), U+2600-U+26FF,
            // U+2700-U+27BF, U+FE00-U+FE0F (variation selectors), U+1F900-U+1F9FF,
            // U+1FA00-U+1FA6F, U+1FA70-U+1FAFF, U+200D (ZWJ), U+20E3 (combining enclosing)
            var v = rune.Value;
            if (IsEmoji(v)) continue;
            sb.Append(rune);
        }

        return sb.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsEmoji(int codepoint)
    {
        return codepoint is >= 0x1F600 and <= 0x1F64F   // Emoticons
            or >= 0x1F300 and <= 0x1F5FF   // Misc Symbols and Pictographs
            or >= 0x1F680 and <= 0x1F6FF   // Transport and Map
            or >= 0x1F1E0 and <= 0x1F1FF   // Flags
            or >= 0x2600 and <= 0x26FF     // Misc symbols
            or >= 0x2700 and <= 0x27BF     // Dingbats
            or >= 0xFE00 and <= 0xFE0F     // Variation selectors
            or >= 0x1F900 and <= 0x1F9FF   // Supplemental Symbols-A
            or >= 0x1FA00 and <= 0x1FA6F   // Chess Symbols
            or >= 0x1FA70 and <= 0x1FAFF   // Symbols-B
            or 0x200D                       // ZWJ
            or 0x20E3                       // Combining Enclosing Keycap
            or >= 0x2702 and <= 0x27B0      // Additional dingbats
            or >= 0x1F004 and <= 0x1F0CF    // Mahjong, Playing Cards
            or >= 0x1F170 and <= 0x1F251;   // Enclosed Ideographic Supplement
    }

    // =========================================================================
    // Legend Builder
    // =========================================================================

    private static string BuildLegend(List<(string Symbol, string Original)> symbolMap)
    {
        if (symbolMap.Count == 0) return "";

        var sb = new StringBuilder(symbolMap.Count * 40);
        sb.AppendLine("# Dynamic Compression Legend");
        sb.Append("[DICT] ");
        var first = true;
        foreach (var (symbol, original) in symbolMap)
        {
            if (!first) sb.Append(" | ");
            sb.Append($"{symbol}={original}");
            first = false;
        }
        sb.AppendLine();
        sb.AppendLine();

        return sb.ToString();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private readonly record struct Candidate(string Token, int Frequency, int Savings);
}
