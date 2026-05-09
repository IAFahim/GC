using System.Text;

namespace gc.Application.Services;

/// <summary>
/// Dynamic BPE-style compression for LLM token optimization.
/// Uses frequency analysis for phrase detection, TokenEstimator for BPE-aware scoring,
/// SingleTokenLexicon for 1-token replacement symbols, and iterative greedy
/// replacement to maximize Net Token Savings (NTS).
/// </summary>
public sealed class DynamicCompressor
{
    public readonly record struct CompressResult(
        string Output,
        string Legend,
        int TokensSaved,
        int ReplacementCount);

    private const int MaxDynamicReplacements = 50;
    private const int MinPhraseFrequency = 2;
    private const int MinPhraseLength = 10;

    public CompressResult Compress(string text, int maxReplacements = MaxDynamicReplacements)
    {
        if (string.IsNullOrEmpty(text) || text.Length < MinPhraseLength * 2)
            return new CompressResult(text, "", 0, 0);

        text = StripAttributes(text);
        text = StripEmoji(text);

        var symbolMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var reverseMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var symbolIndex = 0;
        var totalTokensSaved = 0;
        var totalReplacements = 0;
        var currentText = text;

        // Iterative BPE-style loop: find best phrase, replace, repeat
        for (var iteration = 0; iteration < maxReplacements; iteration++)
        {
            var bestCandidate = FindBestCandidate(currentText, reverseMap);
            if (bestCandidate == null || bestCandidate.Value.NTS <= 0)
                break;

            var phrase = bestCandidate.Value.Phrase;
            var freq = bestCandidate.Value.Frequency;
            var nts = bestCandidate.Value.NTS;

            var symbol = SingleTokenLexicon.GetSymbol(symbolIndex++);
            symbolMap[phrase] = symbol;
            reverseMap[symbol] = phrase;

            // Replace all non-overlapping occurrences
            currentText = ReplaceAll(currentText, phrase, symbol);

            totalTokensSaved += nts;
            totalReplacements += freq;
        }

        if (totalReplacements == 0)
            return new CompressResult(text, "", 0, 0);

        var legend = BuildLegend(symbolMap);
        return new CompressResult(currentText, legend, totalTokensSaved, totalReplacements);
    }

    private readonly record struct Candidate(string Phrase, int Frequency, int PhraseTokens, int NTS);

    /// <summary>
    /// Fast candidate detection using word-frequency + substring scanning.
    /// Avoids O(N log² N) suffix array rebuild per iteration.
    /// </summary>
    private Candidate? FindBestCandidate(string text, Dictionary<string, string> reverseMap)
    {
        // Phase 1: Build frequency map of all "words" (identifiers, operators, phrases)
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        var i = 0;
        while (i < text.Length)
        {
            // Skip whitespace
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;

            if (i >= text.Length)
                break;

            // Extract a "token" — identifier, keyword, or short phrase
            var start = i;

            // Try to grab a multi-word phrase (up to 6 words) for longer candidates
            var wordCount = 0;
            var end = i;
            while (end < text.Length && wordCount < 6)
            {
                // Skip non-whitespace
                while (end < text.Length && !char.IsWhiteSpace(text[end]))
                    end++;
                wordCount++;

                var phrase = text[start..end].TrimEnd();
                if (phrase.Length >= MinPhraseLength)
                {
                    // Skip if already replaced
                    if (!reverseMap.ContainsKey(phrase))
                    {
                        ref var count = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(freq, phrase, out _);
                        count++;
                    }
                }

                // Skip whitespace between words
                while (end < text.Length && char.IsWhiteSpace(text[end]))
                    end++;
            }

            // Advance past first word to avoid O(n²) scanning of every start position
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
                i++;
        }

        // Phase 2: Score candidates by Net Token Savings
        Candidate? best = null;

        foreach (var (phrase, count) in freq)
        {
            if (count < MinPhraseFrequency)
                continue;

            var phraseTokens = TokenEstimator.EstimateTokens(phrase);

            // Legend overhead: "Δ=phrase\n" ≈ 4 + phrase.Length tokens for the header line
            var legendTokens = 3 + TokenEstimator.EstimateTokens(phrase);

            // Replacement symbol is 1 token (single-token Unicode)
            var nts = (phraseTokens * count) - (legendTokens + count);

            if (nts > (best?.NTS ?? 0))
                best = new Candidate(phrase, count, phraseTokens, nts);
        }

        return best;
    }

    private static string ReplaceAll(string text, string phrase, string symbol)
    {
        // Word-boundary-aware replacement
        return ReplaceWordBoundary(text, phrase, symbol);
    }

    private static string ReplaceWordBoundary(string text, string oldStr, string newStr)
    {
        if (string.IsNullOrEmpty(oldStr))
            return text;

        var sb = new StringBuilder(text.Length);
        var i = 0;
        var replacements = 0;

        while (i <= text.Length - oldStr.Length)
        {
            var matchIndex = text.IndexOf(oldStr, i, StringComparison.Ordinal);
            if (matchIndex < 0)
                break;

            // Check word boundaries
            var leftOk = matchIndex == 0 || !IsIdentChar(text[matchIndex - 1]);
            var rightOk = matchIndex + oldStr.Length >= text.Length ||
                          !IsIdentChar(text[matchIndex + oldStr.Length]);

            if (leftOk && rightOk)
            {
                sb.Append(text[i..matchIndex]);
                sb.Append(newStr);
                i = matchIndex + oldStr.Length;
                replacements++;
            }
            else
            {
                // Copy up to just past the match start and continue searching
                sb.Append(text[i..(matchIndex + 1)]);
                i = matchIndex + 1;
            }
        }

        sb.Append(text[i..]);
        return sb.ToString();
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string BuildLegend(Dictionary<string, string> symbolMap)
    {
        if (symbolMap.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("# GC_DICT");
        foreach (var (phrase, symbol) in symbolMap)
        {
            sb.AppendLine($"{symbol}={phrase}");
        }

        return sb.ToString();
    }

    // =========================================================================
    // Static utility methods (preserved from v1)
    // =========================================================================

    public static string StripAttributes(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder(input.Length);
        var i = 0;
        while (i < input.Length)
        {
            // Detect attribute: '[' preceded by whitespace/start-of-line, followed by ident start
            if (input[i] == '[' && IsAttributeStart(input, i))
            {
                var depth = 1;
                i++;
                while (i < input.Length && depth > 0)
                {
                    if (input[i] == '[') depth++;
                    else if (input[i] == ']') depth--;
                    i++;
                }
                continue;
            }

            sb.Append(input[i]);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Heuristic: '[' is an attribute bracket if:
    /// - preceded by whitespace, newline, or start of string
    /// - followed by an identifier start char or '[' (nested attribute) or '(' (for [Obsolete(...)])
    /// This avoids stripping array indexers like arr[0].
    /// </summary>
    private static bool IsAttributeStart(string input, int bracketIndex)
    {
        // Check what follows '['
        var nextIdx = bracketIndex + 1;
        if (nextIdx >= input.Length)
            return false;

        var nextChar = input[nextIdx];
        if (!IsIdentStart(nextChar) && nextChar != '[' && nextChar != '(')
            return false;

        // Check what precedes '[' — should be whitespace/start-of-line
        if (bracketIndex == 0)
            return true;

        var prevChar = input[bracketIndex - 1];
        return char.IsWhiteSpace(prevChar) || prevChar == '\n' || prevChar == '\r';
    }

    public static string StripEmoji(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder(input.Length);
        var i = 0;
        while (i < input.Length)
        {
            var code = char.ConvertToUtf32(input, i);
            var isEmoji = code >= 0x1F600 && code <= 0x1F64F   // Emoticons
                       || code >= 0x1F300 && code <= 0x1F5FF   // Misc Symbols
                       || code >= 0x1F680 && code <= 0x1F6FF   // Transport
                       || code >= 0x1F1E0 && code <= 0x1F1FF   // Flags
                       || code >= 0x2600 && code <= 0x26FF     // Misc symbols
                       || code >= 0x2700 && code <= 0x27BF     // Dingbats
                       || code >= 0xFE00 && code <= 0xFE0F     // Variation selectors
                       || code >= 0x1F900 && code <= 0x1F9FF   // Supp symbols
                       || code >= 0x1FA00 && code <= 0x1FA6F   // Chess symbols
                       || code >= 0x1FA70 && code <= 0x1FAFF   // Symbols extended-A
                       || code >= 0x2702 && code <= 0x27B0;    // Dingbats extended

            if (!isEmoji)
                sb.Append(input[i]);
            else if (char.IsSurrogatePair(input, i))
                i++; // skip the high surrogate; the low surrogate will be skipped by i++ below

            i++;
        }

        return sb.ToString();
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
}
