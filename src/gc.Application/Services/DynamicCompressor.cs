using System.Runtime.InteropServices;
using System.Text;

namespace gc.Application.Services;

/// <summary>
///     Dynamic BPE-style compression for LLM token optimization.
///     Uses frequency analysis for phrase detection, TokenEstimator for BPE-aware scoring,
///     SingleTokenLexicon for 1-token replacement symbols, and iterative greedy
///     replacement to maximize Net Token Savings (NTS).
/// </summary>
public sealed class DynamicCompressor
{
    private const int MaxDynamicReplacements = 100;
    private const int MinPhraseFrequency = 3;
    private const int MinPhraseLength = 11;

    public CompressResult Compress(string text, int maxReplacements = MaxDynamicReplacements)
    {
        if (string.IsNullOrEmpty(text) || text.Length < MinPhraseLength * MinPhraseFrequency || maxReplacements <= 0)
            return new CompressResult(text, "", 0, 0);

        // Phase 1: Context-aware candidate extraction (only from code blocks)
        var codeOnlyText = ExtractCodeOnly(text);
        if (codeOnlyText.Length < MinPhraseLength * MinPhraseFrequency)
            return new CompressResult(text, "", 0, 0);

        var rawCandidates =
            SuffixArray.ExtractMaximalPhrases(codeOnlyText, MinPhraseLength, MinPhraseFrequency, maxReplacements * 3);

        if (rawCandidates.Count == 0)
            return new CompressResult(text, "", 0, 0);

        // Refine candidates: trim whitespace and re-verify ROI
        var refinedMap = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in rawCandidates)
        {
            var trimmed = c.Phrase.Trim();
            if (trimmed.Length < MinPhraseLength) continue;

            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(refinedMap, trimmed, out _);
            count += c.Frequency;
        }

        var finalCandidates = new List<(string Phrase, int Frequency, bool IsIdent, int Tokens)>();
        var symbolMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var symbolIndex = 0;

        // Sort by (length * frequency) descending, then by key for determinism.
        // Dictionary enumeration order is implementation-defined, so we must sort
        // before iterating to ensure byte-identical output across runtimes.
        foreach (var kvp in refinedMap.OrderByDescending(k => (long)k.Key.Length * k.Value)
                     .ThenBy(k => k.Key, StringComparer.Ordinal))
        {
            var phrase = kvp.Key;
            var freq = kvp.Value;

            if (phrase.Contains("```") || phrase.Contains("# ") || phrase.Length > 150)
                continue;

            if (!ContainsLongWord(phrase, 6))
                continue;

            var tokens = TokenEstimator.EstimateTokens(phrase);
            if (tokens <= 1) continue;

            var nts = tokens * freq - (3 + tokens + freq);
            if (nts <= 0) continue;

            var isIdent = IsIdentifier(phrase);
            finalCandidates.Add((phrase, freq, isIdent, tokens));
            symbolMap[phrase] = SingleTokenLexicon.GetSymbol(symbolIndex++);

            if (symbolIndex >= SingleTokenLexicon.Count || symbolIndex >= maxReplacements)
                break;
        }

        if (finalCandidates.Count == 0)
            return new CompressResult(text, "", 0, 0);

        // Phase 2: Context-aware replacement (only in code blocks)
        var output = ReplaceInCodeBlocks(text, finalCandidates, symbolMap);
        var legend = BuildLegend(symbolMap);

        var totalSaved = finalCandidates.Sum(c => c.Tokens * c.Frequency - (3 + c.Tokens + c.Frequency));

        return new CompressResult(output, legend, totalSaved, finalCandidates.Count);
    }

    private static bool IsIdentifier(string s)
    {
        foreach (var c in s)
            if (!(char.IsLetterOrDigit(c) || c == '_'))
                return false;
        return true;
    }

    private static string ExtractCodeOnly(string text)
    {
        if (!text.Contains("```")) return text;

        var sb = new StringBuilder(text.Length / 2);
        var span = text.AsSpan();
        var inCodeBlock = false;
        var isSourceCode = false;

        // Walk lines over the span (splitting only on '\n', exactly like the old Split('\n'))
        // to avoid allocating a string[] of every line plus a string per harvested line.
        var start = 0;
        while (start <= span.Length)
        {
            var rel = span.Slice(start).IndexOf('\n');
            var line = rel < 0 ? span.Slice(start) : span.Slice(start, rel);

            if (line.StartsWith("```"))
            {
                if (!inCodeBlock)
                {
                    var lang = line[3..].Trim().ToString().ToLowerInvariant();
                    isSourceCode = IsSourceCodeLanguage(lang);
                }

                inCodeBlock = !inCodeBlock;
            }
            else if (inCodeBlock && isSourceCode)
            {
                sb.Append(line);
                sb.Append('\n'); // matches the old AppendLine on the (Linux) build/test platform
            }

            if (rel < 0) break;
            start += rel + 1;
        }

        return sb.ToString();
    }

    private static bool IsSourceCodeLanguage(string lang)
    {
        if (string.IsNullOrEmpty(lang)) return true; // Assume code if unknown

        // Languages we want to harvest identifiers from
        return lang switch
        {
            "cs" or "csharp" or "js" or "javascript" or "ts" or "typescript" or
                "py" or "python" or "rs" or "rust" or "go" or "java" or "c" or "cpp" or
                "h" or "hpp" or "swift" or "kt" or "kotlin" or "rb" or "ruby" or
                "php" or "sh" or "bash" or "ps1" or "lua" => true,

            // Skip data-heavy or highly repetitive structural languages
            "xml" or "json" or "yaml" or "yml" or "log" or "txt" or "csv" or "md" or "sql" => false,

            _ => true
        };
    }

    private static string ReplaceInCodeBlocks(string text,
        List<(string Phrase, int Frequency, bool IsIdent, int Tokens)> candidates,
        Dictionary<string, string> symbolMap)
    {
        if (!text.Contains("```"))
        {
            var processedText = text;
            var sorted = candidates.OrderByDescending(c => c.Phrase.Length).ToList();
            foreach (var c in sorted)
                if (c.IsIdent)
                    processedText = ReplaceWordBoundary(processedText, c.Phrase, symbolMap[c.Phrase]);
                else
                    processedText = processedText.Replace(c.Phrase, symbolMap[c.Phrase]);
            return processedText;
        }

        var sb = new StringBuilder(text.Length);
        var span = text.AsSpan();
        var inCodeBlock = false;
        var isSourceCode = false;

        // Sort by length descending to ensure greedy matching
        var sortedList = candidates.OrderByDescending(c => c.Phrase.Length).ToList();

        // Walk lines over the span (splitting only on '\n'), rejoining with '\n'. This is a
        // lossless reconstruction of the original text (Split('\n')+Join('\n')) with replacements
        // applied only inside source-code fences, and avoids the per-line string[] allocation.
        // Only actually-replaced code lines still materialize a string (the Replace helpers need one).
        var start = 0;
        var segIdx = 0;
        while (start <= span.Length)
        {
            var rel = span.Slice(start).IndexOf('\n');
            var line = rel < 0 ? span.Slice(start) : span.Slice(start, rel);

            if (segIdx > 0) sb.Append('\n'); // separator between segments == old `if (l < lines.Length-1)`

            if (line.StartsWith("```"))
            {
                if (!inCodeBlock)
                {
                    var lang = line[3..].Trim().ToString().ToLowerInvariant();
                    isSourceCode = IsSourceCodeLanguage(lang);
                }
                // Toggle code block state
                inCodeBlock = !inCodeBlock;
                sb.Append(line);
            }
            else if (inCodeBlock && isSourceCode)
            {
                // Only apply replacements in code blocks
                var processedLine = line.ToString();
                foreach (var c in sortedList)
                    if (c.IsIdent)
                        processedLine = ReplaceWordBoundary(processedLine, c.Phrase, symbolMap[c.Phrase]);
                    else
                        processedLine = processedLine.Replace(c.Phrase, symbolMap[c.Phrase]);
                sb.Append(processedLine);
            }
            else
            {
                sb.Append(line);
            }

            segIdx++;
            if (rel < 0) break;
            start += rel + 1;
        }

        return sb.ToString();
    }

    private static string ReplaceWordBoundary(string line, string oldStr, string newStr)
    {
        var sb = new StringBuilder(line.Length);
        var i = 0;
        while (i <= line.Length - oldStr.Length)
        {
            var idx = line.IndexOf(oldStr, i, StringComparison.Ordinal);
            if (idx < 0) break;

            var leftOk = idx == 0 || !IsIdentChar(line[idx - 1]);
            var rightOk = idx + oldStr.Length >= line.Length || !IsIdentChar(line[idx + oldStr.Length]);

            if (leftOk && rightOk)
            {
                sb.Append(line.AsSpan(i, idx - i));
                sb.Append(newStr);
                i = idx + oldStr.Length;
            }
            else
            {
                sb.Append(line.AsSpan(i, idx + 1 - i));
                i = idx + 1;
            }
        }

        sb.Append(line.AsSpan(i));
        return sb.ToString();
    }

    private static bool IsIdentChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private static string BuildLegend(Dictionary<string, string> symbolMap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# GC_DICT");
        // Sort by symbol for deterministic output - dictionary iteration is not ordered
        foreach (var kvp in symbolMap.OrderBy(k => k.Value, StringComparer.Ordinal))
            sb.AppendLine($"{kvp.Key}={kvp.Value}");
        sb.AppendLine();
        return sb.ToString();
    }

    private static bool ContainsLongWord(string s, int minWordLength)
    {
        var currentLength = 0;
        foreach (var c in s)
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                currentLength++;
                if (currentLength >= minWordLength)
                    return true;
            }
            else
            {
                currentLength = 0;
            }

        return false;
    }

    public readonly record struct CompressResult(
        string Output,
        string Legend,
        int TokensSaved,
        int ReplacementCount);
}