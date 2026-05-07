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

            if (i + 1 < len && (ch == '!' || ch == '#' || ch == '$' || ch == '%'))
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
        var sb = new StringBuilder(512);
        sb.AppendLine("# Brain Mode Token Dictionary");
        foreach (var kvp in _tokenMap)
        {
            sb.AppendLine($"# {kvp.Key} = {kvp.Value}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    public IReadOnlyDictionary<string, string> GetTokenMap() => _tokenMap;


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

        while (i < len)
        {
            var ch = span[i];

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

            sb.Append(ch);
            i++;
        }

        return sb.ToString();
    }


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


    private static Dictionary<string, string> BuildTokenMap() => new(StringComparer.Ordinal)
    {
        ["public"] = "!1",
        ["private"] = "!2",
        ["protected"] = "!3",
        ["internal"] = "!4",

        ["static"] = "!5",
        ["readonly"] = "!6",
        ["sealed"] = "!7",
        ["abstract"] = "!8",
        ["virtual"] = "!9",
        ["override"] = "!a",
        ["partial"] = "!b",
        ["const"] = "!c",
        ["volatile"] = "!d",

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

        ["async"] = "$1",
        ["await"] = "$2",
        ["Task"] = "$3",
        ["var"] = "$4",
        ["get"] = "$5",
        ["set"] = "$6",
        ["init"] = "$7",
        ["where"] = "$8",
        ["select"] = "$9",

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
