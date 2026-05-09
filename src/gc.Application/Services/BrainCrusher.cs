using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text;
using gc.Domain.Interfaces;

namespace gc.Application.Services;

public sealed class BrainCrusher : IBrainCrusher
{
    private sealed class TrieNode
    {
        public Dictionary<char, TrieNode> Children = new();
        public string? Token;
    }

    private readonly TrieNode _trieRoot;
    private readonly FrozenDictionary<string, string> _tokenMap;
    private readonly FrozenDictionary<string, string> _reverseMap;

    public BrainCrusher()
    {
        var map = BuildTokenMap();
        _tokenMap = map.ToFrozenDictionary(StringComparer.Ordinal);
        _reverseMap = map.ToDictionary(kvp => kvp.Value, kvp => kvp.Key).ToFrozenDictionary(StringComparer.Ordinal);
        _trieRoot = BuildTrie(map);
    }

    public string CrushBlock(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;
        var stripped = StripComments(code);
        return CollapseAndMap(stripped);
    }

    public string Crush(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var stripped = StripComments(content);
        return CollapseAndMap(stripped);
    }

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

            // Try 2-char tokens: prefix ! # $ % &
            if (i + 1 < len && (ch == '!' || ch == '#' || ch == '$' || ch == '%' || ch == '&'))
            {
                var candidate = crushed.Substring(i, 2);
                if (_reverseMap.TryGetValue(candidate, out var keyword))
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

    public string GetDictionaryHeader()
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine("# DICT");
        foreach (var kvp in _tokenMap)
        {
            sb.AppendLine($"{kvp.Value}={kvp.Key}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    public IReadOnlyDictionary<string, string> GetTokenMap() => _tokenMap;

    // =========================================================================
    // Phase 1: Universal Syntax Minifier
    // Agnostic state machine handles: //, /* */, #, <!-- -->, triple-quotes, --, strings
    // =========================================================================

    private static string StripComments(string content)
    {
        var sb = new StringBuilder(content.Length);
        var span = content.AsSpan();
        var i = 0;
        var len = span.Length;

        var inString = false;
        var inChar = false;
        var inSingleLineComment = false;
        var inMultiLineComment = false;
        var inHashComment = false;
        var inHtmlComment = false;
        var inTripleQuote = false;
        var inSqlComment = false;

        while (i < len)
        {
            var ch = span[i];

            // --- Triple-quote string (Python """ or ''' docstrings) ---
            if (inTripleQuote)
            {
                if (ch == '"' && i + 2 < len && span[i + 1] == '"' && span[i + 2] == '"')
                {
                    sb.Append("\"\"\"");
                    i += 3;
                    inTripleQuote = false;
                    continue;
                }
                if (ch == '\'' && i + 2 < len && span[i + 1] == '\'' && span[i + 2] == '\'')
                {
                    sb.Append("'''");
                    i += 3;
                    inTripleQuote = false;
                    continue;
                }
                sb.Append(ch);
                if (ch == '\\' && i + 1 < len)
                {
                    i++;
                    sb.Append(span[i]);
                }
                i++;
                continue;
            }

            // --- SQL single-line comment (--) ---
            if (inSqlComment)
            {
                if (ch == '\n')
                {
                    inSqlComment = false;
                    sb.Append('\n');
                }
                i++;
                continue;
            }

            // --- HTML comment (<!-- -->) ---
            if (inHtmlComment)
            {
                if (ch == '-' && i + 2 < len && span[i + 1] == '-' && span[i + 2] == '>')
                {
                    inHtmlComment = false;
                    i += 3;
                    sb.Append(' ');
                    continue;
                }
                if (ch == '\n') sb.Append('\n');
                i++;
                continue;
            }

            // --- Hash comment (# ... \n) for Python, Ruby, Shell, YAML, TOML ---
            if (inHashComment)
            {
                if (ch == '\n')
                {
                    inHashComment = false;
                    sb.Append('\n');
                }
                i++;
                continue;
            }

            // --- C-style multi-line comment (/* ... */) ---
            if (inMultiLineComment)
            {
                if (ch == '*' && i + 1 < len && span[i + 1] == '/')
                {
                    inMultiLineComment = false;
                    i += 2;
                    sb.Append(' ');
                    continue;
                }
                if (ch == '\n') sb.Append('\n');
                i++;
                continue;
            }

            // --- C-style / JS / Java single-line comment (// ... \n) ---
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

            // --- Double-quoted string ---
            if (inString)
            {
                sb.Append(ch);
                if (ch == '\\' && i + 1 < len)
                {
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

            // --- Single-quoted char/string ---
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

            // ========== Not inside any string/comment — detect patterns ==========

            // Triple-quote start: """ or '''
            if (ch == '"' && i + 2 < len && span[i + 1] == '"' && span[i + 2] == '"')
            {
                inTripleQuote = true;
                sb.Append("\"\"\"");
                i += 3;
                continue;
            }
            if (ch == '\'' && i + 2 < len && span[i + 1] == '\'' && span[i + 2] == '\'')
            {
                inTripleQuote = true;
                sb.Append("'''");
                i += 3;
                continue;
            }

            // SQL comment -- (must check before string detection)
            if (ch == '-' && i + 1 < len && span[i + 1] == '-')
            {
                inSqlComment = true;
                i += 2;
                continue;
            }

            // HTML comment <!--
            if (ch == '<' && i + 3 < len && span[i + 1] == '!' && span[i + 2] == '-' && span[i + 3] == '-')
            {
                inHtmlComment = true;
                i += 4;
                continue;
            }

            // C-style // comment
            if (ch == '/' && i + 1 < len && span[i + 1] == '/')
            {
                inSingleLineComment = true;
                i += 2;
                continue;
            }

            // C-style /* comment
            if (ch == '/' && i + 1 < len && span[i + 1] == '*')
            {
                inMultiLineComment = true;
                i += 2;
                continue;
            }

            // Hash comment: # outside of string context
            // Be careful: # in C# is preprocessor directives, not comments
            // We strip them anyway since they're noise for LLM compression
            if (ch == '#')
            {
                inHashComment = true;
                i++;
                continue;
            }

            // String start
            if (ch == '"')
            {
                inString = true;
                sb.Append(ch);
                i++;
                continue;
            }

            // Char start
            if (ch == '\'')
            {
                inChar = true;
                sb.Append(ch);
                i++;
                continue;
            }

            sb.Append(ch);
            i++;
        }

        return sb.ToString();
    }

    // =========================================================================
    // Whitespace collapse + trie-based token replacement in single pass
    // =========================================================================

    private string CollapseAndMap(string stripped)
    {
        var result = new StringBuilder(stripped.Length);
        var i = 0;
        var len = stripped.Length;
        var lastWasSpace = false;
        var lineIsEmpty = true;

        while (i < len)
        {
            var ch = stripped[i];

            if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < len && stripped[i + 1] == '\n')
                {
                    i++;
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

            if (lastWasSpace)
            {
                result.Append(' ');
                lastWasSpace = false;
            }

            if (TryMatchTrie(stripped, i, len, out var replacement, out var advance))
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
    private bool TryMatchTrie(string source, int start, int len, out string replacement, out int advance)
    {
        replacement = string.Empty;
        advance = 0;

        if (start > 0 && IsIdentifierChar(source[start - 1]))
            return false;

        var node = _trieRoot;
        var bestMatch = -1;
        var bestToken = string.Empty;
        var pos = start;

        while (pos < len)
        {
            var ch = source[pos];
            if (!node.Children.TryGetValue(ch, out var next))
                break;

            node = next;
            pos++;

            if (node.Token != null)
            {
                if (pos >= len || !IsIdentifierChar(source[pos]))
                {
                    bestMatch = pos;
                    bestToken = node.Token;
                }
            }
        }

        if (bestMatch > 0)
        {
            replacement = bestToken;
            advance = bestMatch - start;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierChar(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_';
    }

    // =========================================================================
    // Phase 2: Universal Static Dictionary
    // Top keywords from C#, JS/TS, Python, Go, Rust, Java, C++, Ruby, SQL
    // Mapped to rare Unicode 1-char tokens for LLM-friendliness
    // Prefix groups: ! (access/modifiers), # (control flow), $ (async/misc),
    //                % (types/collections), & (multi-language)
    // =========================================================================

    private static Dictionary<string, string> BuildTokenMap() => new(StringComparer.Ordinal)
    {
        // --- C# access modifiers → !1-!9 ---
        ["public"] = "!1",
        ["private"] = "!2",
        ["protected"] = "!3",
        ["internal"] = "!4",

        // --- C# type modifiers → !5-!d ---
        ["static"] = "!5",
        ["readonly"] = "!6",
        ["sealed"] = "!7",
        ["abstract"] = "!8",
        ["virtual"] = "!9",
        ["override"] = "!a",
        ["partial"] = "!b",
        ["const"] = "!c",
        ["volatile"] = "!d",

        // --- C# type keywords → !e-!s ---
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

        // --- Control flow → #1-#f ---
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

        // --- Async / LINQ / misc C# → $1-$9 ---
        ["async"] = "$1",
        ["await"] = "$2",
        ["Task"] = "$3",
        ["var"] = "$4",
        ["get"] = "$5",
        ["set"] = "$6",
        ["init"] = "$7",
        ["where"] = "$8",
        ["select"] = "$9",

        // --- C# common types → %1-%b ---
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

        // --- Multi-language universal keywords → &1-&9, &a-&f ---
        // JavaScript / TypeScript
        ["function"] = "&1",
        ["let"] = "&2",
        ["import"] = "&4",
        ["export"] = "&5",
        ["default"] = "&6",
        ["typeof"] = "&7",
        ["instanceof"] = "&8",

        // Python
        ["def"] = "&9",
        ["lambda"] = "&a",
        ["with"] = "&b",
        ["as"] = "&c",
        ["pass"] = "&d",
        ["from"] = "&e",
        ["raise"] = "&f",

        // Go
        ["func"] = "&g",
        ["go"] = "&h",
        ["defer"] = "&i",
        ["chan"] = "&j",
        ["package"] = "&k",
        ["range"] = "&l",
        ["map"] = "&m",

        // Rust
        ["fn"] = "&n",
        ["mut"] = "&p",
        ["impl"] = "&q",
        ["pub"] = "&r",
        ["crate"] = "&s",
        ["mod"] = "&t",
        ["use"] = "&u",
        ["match"] = "&v",
        ["loop"] = "&w",
        ["self"] = "&x",
        ["super"] = "&y",
        ["trait"] = "&z",
    };

    private static TrieNode BuildTrie(Dictionary<string, string> map)
    {
        var root = new TrieNode();
        foreach (var kvp in map)
        {
            var node = root;
            foreach (var ch in kvp.Key)
            {
                if (!node.Children.TryGetValue(ch, out var child))
                {
                    child = new TrieNode();
                    node.Children[ch] = child;
                }
                node = child;
            }
            node.Token = kvp.Value;
        }
        return root;
    }
}
