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

    private const int MaxDynamicReplacements = 100;
    private const int MinPhraseFrequency = 3;
    private const int MinPhraseLength = 10;

    public CompressResult Compress(string text, int maxReplacements = MaxDynamicReplacements)
    {
        if (string.IsNullOrEmpty(text) || text.Length < MinPhraseLength * MinPhraseFrequency || maxReplacements <= 0)
            return new CompressResult(text, "", 0, 0);

        // Phase 1: Context-aware candidate extraction (only from code blocks)
        var codeOnlyText = ExtractCodeOnly(text);
        if (codeOnlyText.Length < MinPhraseLength * MinPhraseFrequency)
            return new CompressResult(text, "", 0, 0);

        var rawCandidates = SuffixArray.ExtractMaximalPhrases(codeOnlyText, MinPhraseLength, MinPhraseFrequency, maxReplacements * 3);
        
        if (rawCandidates.Count == 0)
            return new CompressResult(text, "", 0, 0);

        // Refine candidates: trim whitespace and re-verify ROI
        var refinedMap = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in rawCandidates)
        {
            var trimmed = c.Phrase.Trim();
            if (trimmed.Length < MinPhraseLength) continue;
            
            ref var count = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(refinedMap, trimmed, out _);
            count += c.Frequency;
        }

        var finalCandidates = new List<(string Phrase, int Frequency, bool IsIdent)>();
        var symbolMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var symbolIndex = 0;

        foreach (var kvp in refinedMap.OrderByDescending(k => k.Key.Length * k.Value))
        {
            var phrase = kvp.Key;
            var freq = kvp.Value;

            if (phrase.Contains("```") || phrase.Contains("# ") || phrase.Length > 150)
                continue;

            var tokens = TokenEstimator.EstimateTokens(phrase);
            if (tokens <= 2) continue;

            var nts = (tokens * freq) - (3 + tokens + freq);
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
        
        var totalSaved = finalCandidates.Sum(c => {
            var tokens = TokenEstimator.EstimateTokens(c.Phrase);
            return (tokens * c.Frequency) - (3 + tokens + c.Frequency);
        });

        return new CompressResult(output, legend, totalSaved, finalCandidates.Sum(c => c.Frequency));
    }

    private static bool IsIdentifier(string s) => s.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static string ExtractCodeOnly(string text)
    {
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

            if (inCodeBlock && isSourceCode)
            {
                sb.AppendLine(line);
            }
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

    private static string ReplaceInCodeBlocks(string text, List<(string Phrase, int Frequency, bool IsIdent)> candidates, Dictionary<string, string> symbolMap)
    {
        var sb = new StringBuilder(text.Length);
        var lines = text.Split('\n');
        var inCodeBlock = false;
        var inProjectStructure = false;

        // Sort by length descending to ensure greedy matching
        var sorted = candidates.OrderByDescending(c => c.Phrase.Length).ToList();

        for (int l = 0; l < lines.Length; l++)
        {
            var line = lines[l];

            if (line.Contains("Project Structure", StringComparison.OrdinalIgnoreCase))
                inProjectStructure = true;

            if (line.StartsWith("```"))
            {
                inCodeBlock = !inCodeBlock;
                sb.Append(line);
            }
            else if (inCodeBlock && !inProjectStructure)
            {
                var processedLine = line;
                foreach (var c in sorted)
                {
                    if (c.IsIdent)
                        processedLine = ReplaceWordBoundary(processedLine, c.Phrase, symbolMap[c.Phrase]);
                    else
                        processedLine = processedLine.Replace(c.Phrase, symbolMap[c.Phrase]);
                }
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

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string BuildLegend(Dictionary<string, string> symbolMap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# GC_DICT");
        foreach (var (phrase, symbol) in symbolMap)
        {
            sb.AppendLine($"{symbol}={phrase}");
        }
        sb.AppendLine();
        return sb.ToString();
    }
}
