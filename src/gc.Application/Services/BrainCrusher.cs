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
    // File extensions that may legitimately contain SQL-style comments.
    // Restricting to these prevents false positives in YAML, Markdown, shell scripts.
    private static readonly HashSet<string> SqlLikeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "sql", "pgsql", "psql", "mysql", "mariadb", "sqlite", "mssql", "oracle", "plsql",
        "pl/pgsql", "duckdb", "bigquery", "snowflake", "transactsql", "tsql"
    };

    // File extensions where # should NOT be treated as a line comment.
    // These files commonly have # at line starts that are not comments.
    private static readonly HashSet<string> NonHashCommentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "md", "markdown", "yaml", "yml", "toml", "ini", "cfg", "conf", "properties",
        "dockerfile", "makefile", "gemfile", "rakefile", "lock", "json"
    };

    private string? _fileExtension;

    public BrainCrusher(string? fileExtension = null)
    {
        _fileExtension = fileExtension;
    }

    public string CrushBlock(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;
        var stripped = StripComments(code, _fileExtension);
        return CollapseWhitespace(stripped);
    }

    public string Crush(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var stripped = StripComments(content, _fileExtension);
        return CollapseWhitespace(stripped);
    }

    public string Uncrush(string crushed) => crushed;

    public string GetDictionaryHeader() => string.Empty;

    // =========================================================================
    // Universal Syntax Minifier
    // Agnostic state machine handles: //, /* */, #, <!-- -->, triple-quotes, --, strings
    // =========================================================================

    internal static string StripComments(string content, string? fileExtension = null)
    {
        var sb = new StringBuilder(content.Length);
        var span = content.AsSpan();
        int i = 0;
        int len = span.Length;

        bool shouldStripSql = fileExtension != null && fileExtension.StartsWith('.') &&
            SqlLikeExtensions.Contains(fileExtension.Substring(1));
        bool shouldTreatHashAsComment = fileExtension == null ||
            !NonHashCommentExtensions.Contains(fileExtension.StartsWith('.') ? fileExtension.Substring(1) : fileExtension);

        bool inString = false;
        bool inChar = false;
        bool inSingleLineComment = false;
        bool inMultiLineComment = false;
        bool inHashComment = false;
        bool inHtmlComment = false;
        bool inTripleQuote = false;
        bool inSqlComment = false;

        while (i < len)
        {
            char ch = span[i];

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

            // SQL comment (--): only strip for SQL files to avoid false positives
            // in CLI flags (--verbose), YAML separators (---), and Markdown headers
            if (shouldStripSql && ch == '-' && i + 1 < len && span[i + 1] == '-')
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

            if (ch == '#' && shouldTreatHashAsComment)
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
        int i = 0;
        int len = stripped.Length;
        bool lastWasSpace = false;
        bool lineIsEmpty = true;

        while (i < len)
        {
            char ch = stripped[i];

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