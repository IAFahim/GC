using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text;

namespace gc.Application.Services;

/// <summary>
/// Brain Mode tokenizer — squeezes code for LLM context windows.
/// Strips comments, collapses whitespace, maps common keywords to short tokens.
/// Targets ~40% token reduction for C#-heavy codebases.
/// </summary>
public sealed class BrainCrusher
{
    /// <summary>
    /// The token dictionary mapping common C# keywords to short replacements.
    /// Using non-alphanumeric prefixes to avoid collisions with real code.
    /// </summary>
    private static readonly FrozenDictionary<string, string> TokenMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Access modifiers
        ["public"] = "!1",
        ["private"] = "!2",
        ["protected"] = "!3",
        ["internal"] = "!4",

        // Types
        ["static"] = "!5",
        ["readonly"] = "!6",
        ["sealed"] = "!7",
        ["abstract"] = "!8",
        ["virtual"] = "!9",
        ["override"] = "!a",
        ["partial"] = "!b",
        ["const"] = "!c",
        ["volatile"] = "!d",

        // Keywords
        ["class"] = "!e",
        ["struct"] = "!f",
        ["record"] = "!g",
        ["interface"] = "!h",
        ["enum"] = "!i",
        ["namespace"] = "!j",
        ["using"] = "!k",
        ["void"] = "!l",
        ["return"] = "!m",
        ["new"] = "!n",
        ["this"] = "!o",
        ["base"] = "!p",
        ["true"] = "!q",
        ["false"] = "!r",
        ["null"] = "!s",

        // Control flow
        ["if"] = "#1",
        ["else"] = "#2",
        ["for"] = "#3",
        ["foreach"] = "#4",
        ["while"] = "#5",
        ["do"] = "#6",
        ["switch"] = "#7",
        ["case"] = "#8",
        ["break"] = "#9",
        ["continue"] = "#a",
        ["try"] = "#b",
        ["catch"] = "#c",
        ["finally"] = "#d",
        ["throw"] = "#e",
        ["yield"] = "#f",

        // Async/LINQ
        ["async"] = "$1",
        ["await"] = "$2",
        ["Task"] = "$3",
        ["var"] = "$4",
        ["get"] = "$5",
        ["set"] = "$6",
        ["init"] = "$7",
        ["where"] = "$8",
        ["select"] = "$9",

        // Types
        ["string"] = "%1",
        ["int"] = "%2",
        ["bool"] = "%3",
        ["long"] = "%4",
        ["double"] = "%5",
        ["float"] = "%6",
        ["byte"] = "%7",
        ["object"] = "%8",
        ["List"] = "%9",
        ["Dictionary"] = "%a",
        ["IEnumerable"] = "%b",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Reverse map for decoding (useful for debugging / display).
    /// </summary>
    private static readonly FrozenDictionary<string, string> ReverseMap = TokenMap
        .ToDictionary(kvp => kvp.Value, kvp => kvp.Key)
        .ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Compresses source code content for LLM consumption.
    /// 1. Strips single-line comments (// ...)
    /// 2. Strips multi-line comments (/* ... */)
    /// 3. Strips XML doc comments (/// ...)
    /// 4. Collapses consecutive whitespace to single space
    /// 5. Strips leading/trailing whitespace per line
    /// 6. Removes blank lines
    /// 7. Applies token dictionary mapping (whole-word only)
    /// </summary>
    public string Crush(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;

        var sb = new StringBuilder(content.Length);
        var span = content.AsSpan();
        var i = 0;
        var len = span.Length;

        // State machine: track whether we're inside a string literal or char literal
        var inString = false;
        var inChar = false;
        var inSingleLineComment = false;
        var inMultiLineComment = false;

        while (i < len)
        {
            var ch = span[i];

            // Multi-line comment end
            if (inMultiLineComment)
            {
                if (ch == '*' && i + 1 < len && span[i + 1] == '/')
                {
                    inMultiLineComment = false;
                    i += 2;
                    // Replace comment with single space
                    sb.Append(' ');
                    continue;
                }
                // Replace newlines inside multi-line comments with actual newlines
                if (ch == '\n')
                {
                    sb.Append('\n');
                }
                i++;
                continue;
            }

            // Single-line comment end
            if (inSingleLineComment)
            {
                if (ch == '\n')
                {
                    inSingleLineComment = false;
                    sb.Append('\n');
                }
                i++;
                continue;
            }

            // String literal — pass through verbatim
            if (inString)
            {
                sb.Append(ch);
                if (ch == '\\' && i + 1 < len)
                {
                    // Escaped character — pass next char through
                    i++;
                    sb.Append(span[i]);
                }
                else if (ch == '"')
                {
                    inString = false;
                }
                i++;
                continue;
            }

            // Char literal — pass through verbatim
            if (inChar)
            {
                sb.Append(ch);
                if (ch == '\\' && i + 1 < len)
                {
                    i++;
                    sb.Append(span[i]);
                }
                else if (ch == '\'')
                {
                    inChar = false;
                }
                i++;
                continue;
            }

            // Detect comment starts
            if (ch == '/' && i + 1 < len)
            {
                var next = span[i + 1];
                if (next == '/')
                {
                    inSingleLineComment = true;
                    i += 2;
                    continue;
                }
                if (next == '*')
                {
                    inMultiLineComment = true;
                    i += 2;
                    continue;
                }
            }

            // Detect string/char starts
            if (ch == '"')
            {
                inString = true;
                sb.Append(ch);
                i++;
                continue;
            }
            if (ch == '\'')
            {
                inChar = true;
                sb.Append(ch);
                i++;
                continue;
            }

            // Normal character — pass through
            sb.Append(ch);
            i++;
        }

        // Now collapse whitespace and apply token mapping
        return CollapseAndMap(sb);
    }

    /// <summary>
    /// Decodes a crushed string back to readable form (for debugging).
    /// </summary>
    public string Uncrush(string crushed)
    {
        if (string.IsNullOrEmpty(crushed)) return crushed;

        var sb = new StringBuilder(crushed.Length);
        var i = 0;
        var len = crushed.Length;

        while (i < len)
        {
            var ch = crushed[i];
            var matched = false;

            // Try to match a token prefix (!, #, $, %)
            if (i + 1 < len && (ch == '!' || ch == '#' || ch == '$' || ch == '%'))
            {
                var candidate = crushed.Substring(i, 2);
                if (ReverseMap.TryGetValue(candidate, out var keyword))
                {
                    sb.Append(keyword);
                    i += 2;
                    matched = true;
                }
            }

            if (!matched)
            {
                sb.Append(ch);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the dictionary header to prepend to crushed output so LLMs can decode.
    /// </summary>
    public string GetDictionaryHeader()
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("# Brain Mode Token Dictionary");
        foreach (var kvp in TokenMap)
        {
            sb.AppendLine($"# {kvp.Key} = {kvp.Value}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static string CollapseAndMap(StringBuilder sb)
    {
        var result = new StringBuilder(sb.Length);
        var i = 0;
        var len = sb.Length;
        var lastWasSpace = false;
        var lineIsEmpty = true;

        while (i < len)
        {
            var ch = sb[i];

            if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < len && sb[i + 1] == '\n')
                {
                    i++; // Skip \r in \r\n
                }

                if (!lineIsEmpty)
                {
                    result.Append('\n');
                    lineIsEmpty = true;
                    lastWasSpace = false;
                }
                i++;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!lineIsEmpty)
                {
                    lastWasSpace = true;
                }
                i++;
                continue;
            }

            // Non-whitespace character
            if (lastWasSpace)
            {
                result.Append(' ');
                lastWasSpace = false;
            }

            // Try to match a keyword at this position (whole-word)
            var matched = TryMatchKeyword(sb, i, len, out var replacement, out var advance);

            if (matched)
            {
                result.Append(replacement);
                i += advance;
            }
            else
            {
                result.Append(ch);
                i++;
            }

            lineIsEmpty = false;
        }

        return result.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryMatchKeyword(StringBuilder sb, int start, int len, out string replacement, out int advance)
    {
        replacement = string.Empty;
        advance = 0;

        // Only try to match at word boundaries — preceded by non-identifier char or start
        if (start > 0)
        {
            var prev = sb[start - 1];
            if (IsIdentifierChar(prev)) return false;
        }

        // Find the end of the current word
        var wordEnd = start;
        while (wordEnd < len && IsIdentifierChar(sb[wordEnd]))
        {
            wordEnd++;
        }

        var wordLen = wordEnd - start;
        if (wordLen == 0) return false;

        // Check that the character after the word is a non-identifier (word boundary)
        if (wordEnd < len && IsIdentifierChar(sb[wordEnd])) return false;

        // Extract the word and look up in token map
        // Use span-based comparison for performance
        foreach (var kvp in TokenMap)
        {
            if (kvp.Key.Length != wordLen) continue;

            var match = true;
            for (int j = 0; j < wordLen; j++)
            {
                if (sb[start + j] != kvp.Key[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                replacement = kvp.Value;
                advance = wordLen;
                return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierChar(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_';
    }
}
