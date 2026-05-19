using System.Text;
using gc.Domain.Interfaces;

namespace gc.Application.Services;

/// <summary>
/// Phase 1 minifier: strips comments and collapses whitespace.
/// No static keyword dictionary — that was token-pessimal (replacing 1-token
/// keywords like "public" with 2-token "!1"). Dynamic compression is handled
/// by DynamicCompressor using BPE-style SA/LCP analysis.
/// </summary>
public sealed class BrainCrusher : IBrainCrusher
{
    public string CrushBlock(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;
        var stripped = StripComments(code);
        return CollapseWhitespace(stripped);
    }

    public string Crush(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var stripped = StripComments(content);
        return CollapseWhitespace(stripped);
    }

    public string Uncrush(string crushed) => crushed;

    public string GetDictionaryHeader() => string.Empty;

    // =========================================================================
    // Universal Syntax Minifier
    // Agnostic state machine handles: //, /* */, #, <!-- -->, triple-quotes, --, strings
    // =========================================================================

    internal static string StripComments(string content)
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

            // Triple-quote start
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

            if (ch == '-' && i + 1 < len && span[i + 1] == '-')
            {
                inSqlComment = true;
                i += 2;
                continue;
            }

            if (ch == '<' && i + 3 < len && span[i + 1] == '!' && span[i + 2] == '-' && span[i + 3] == '-')
            {
                inHtmlComment = true;
                i += 4;
                continue;
            }

            if (ch == '/' && i + 1 < len && span[i + 1] == '/')
            {
                inSingleLineComment = true;
                i += 2;
                continue;
            }

            if (ch == '/' && i + 1 < len && span[i + 1] == '*')
            {
                inMultiLineComment = true;
                i += 2;
                continue;
            }

            if (ch == '#')
            {
                inHashComment = true;
                i++;
                continue;
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

    // =========================================================================
    // Whitespace collapse: multiple spaces → single, blank lines removed
    // =========================================================================

    internal static string CollapseWhitespace(string stripped)
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
                    i++;

                if (!lineIsEmpty)
                {
                    result.Append('\n');
                    lineIsEmpty = true;
                    lastWasSpace = false;
                }
                i++;
                continue;
            }

            if (ch == ' ' || ch == '\t')
            {
                if (lineIsEmpty)
                {
                    // Preserve leading whitespace for indentation-sensitive languages
                    result.Append(ch);
                }
                else
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

            result.Append(ch);
            lineIsEmpty = false;
            i++;
        }

        return result.ToString();
    }
}
