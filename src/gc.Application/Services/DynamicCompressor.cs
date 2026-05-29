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

        var finalCandidates = new List<(string Phrase, int Frequency, bool IsIdent)>();
        var symbolMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var symbolIndex = 0;

        // Sort by (length * frequency) descending, then by key for determinism.
        // Dictionary enumeration order is implementation-defined, so we must sort
        // before iterating to ensure byte-identical output across runtimes.
        foreach (var kvp in refinedMap.OrderByDescending(k => k.Key.Length * k.Value)
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
            finalCandidates.Add((phrase, freq, isIdent));
            symbolMap[phrase] = SingleTokenLexicon.GetSymbol(symbolIndex++);

            if (symbolIndex >= SingleTokenLexicon.Count || symbolIndex >= maxReplacements)
                break;
        }

        if (finalCandidates.Count == 0)
            return new CompressResult(text, "", 0, 0);

        // Phase 2: Context-aware replacement (only in code blocks)
        var output = ReplaceInCodeBlocks(text, finalCandidates, symbolMap);
        var legend = BuildLegend(symbolMap);

        var totalSaved = finalCandidates.Sum(c =>
        {
            var tokens = TokenEstimator.EstimateTokens(c.Phrase);
            return tokens * c.Frequency - (3 + tokens + c.Frequency);
        });

        return new CompressResult(output, legend, totalSaved, finalCandidates.Count);
    }

    private static bool IsIdentifier(string s)
    {
        return s.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    private static string ExtractCodeOnly(string text)
    {
        if (!text.Contains("```")) return text;

        var sb = new StringBuilder(text.Length / 2);
        var lines = text.Split('\n');
        var inCodeBlock = false;
        var isSourceCode = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("```"))
            {
                if (!inCodeBlock)
                {
                    var lang = line.Substring(3).Trim().ToLowerInvariant();
                    isSourceCode = IsSourceCodeLanguage(lang);
                }

                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock && isSourceCode) sb.AppendLine(line);
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
        List<(string Phrase, int Frequency, bool IsIdent)> candidates, Dictionary<string, string> symbolMap)
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
        var lines = text.Split('\n');
        var inCodeBlock = false;

        // Sort by length descending to ensure greedy matching
        var sortedList = candidates.OrderByDescending(c => c.Phrase.Length).ToList();

        for (var l = 0; l < lines.Length; l++)
        {
            var line = lines[l];

            if (line.StartsWith("```"))
            {
                // Toggle code block state
                inCodeBlock = !inCodeBlock;
                sb.Append(line);
            }
            else if (inCodeBlock)
            {
                // Only apply replacements in code blocks
                var processedLine = line;
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

            if (l < lines.Length - 1)
                sb.Append('\n');
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
                sb.Append(line[i..idx]);
                sb.Append(newStr);
                i = idx + oldStr.Length;
            }
            else
            {
                sb.Append(line[i..(idx + 1)]);
                i = idx + 1;
            }
        }

        sb.Append(line[i..]);
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