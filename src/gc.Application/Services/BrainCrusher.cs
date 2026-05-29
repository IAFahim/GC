using System.Text;
using gc.Domain.Interfaces;

namespace gc.Application.Services;

/// <summary>
///     Phase 1 minifier: strips comments and collapses whitespace.
///     No static keyword dictionary — that was token-pessimal (replacing 1-token
///     keywords like "public" with 2-token "!1"). Dynamic compression is handled
///     by DynamicCompressor using BPE-style SA/LCP analysis.
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
        "md", "markdown"
    };

    // File extensions where # SHOULD be treated as a line comment (including shell scripts).
    private static readonly HashSet<string> HashCommentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "sh", "bash", "zsh", "fish", "ksh", "csh", "tcsh", "shar", "zshrc", "bashrc",
        "profile", "bash_profile", "bash_login", "zprofile", "env", "shell",
        "dockerfile", "makefile", "gemfile", "rakefile", "cfg", "conf", "properties",
        "yaml", "yml", "toml", "ini"
    };

    // File extensions that use // for single-line comments.
    // Markdown/text/YAML should NOT have // treated as a comment.
    private static readonly HashSet<string> DoubleSlashCommentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs", "java", "js", "ts", "jsx", "tsx", "cpp", "cxx", "cc", "h", "hpp",
        "go", "rs", "swift", "kt", "scala", "php", "rust", "dart", "groovy",
        "c", "javascript", "typescript", "csharp", "cpp", "objectivec", "swift"
    };

    private readonly string? _fileExtension;

    public BrainCrusher(string? fileExtension = null)
    {
        _fileExtension = fileExtension;
    }

    public string CrushBlock(string code, string? language = null)
    {
        if (string.IsNullOrEmpty(code)) return code;
        var stripped = StripComments(code, language ?? _fileExtension);
        return CollapseWhitespace(stripped);
    }

    public string Uncrush(string crushed)
    {
        return crushed;
    }

    public string GetDictionaryHeader()
    {
        return string.Empty;
    }

    // Shell shebang patterns - #! at start of file should be preserved
    private static bool IsShebangLine(ReadOnlySpan<char> line)
    {
        return line.Length >= 2 && line[0] == '#' && line[1] == '!';
    }

    public string Crush(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var stripped = StripComments(content, _fileExtension);
        return CollapseWhitespace(stripped);
    }

    // =========================================================================
    // Universal Syntax Minifier
    // Agnostic state machine handles: //, /* */, #, <!-- -->, triple-quotes, --, strings
    // =========================================================================

    internal static string StripComments(string content, string? fileExtension = null)
    {
        var sb = new StringBuilder(content.Length);
        var span = content.AsSpan();
        var i = 0;
        var len = span.Length;

        // Normalize extension (strip leading dot)
        string? normalizedExt = null;
        if (!string.IsNullOrEmpty(fileExtension))
            normalizedExt = fileExtension.StartsWith('.')
                ? fileExtension.Substring(1)
                : fileExtension;

        var shouldStripSql = normalizedExt != null && SqlLikeExtensions.Contains(normalizedExt);
        bool shouldTreatHashAsComment;
        bool shouldStripDoubleSlash;

        if (normalizedExt == null)
        {
            // Null extension (whole-document crush path) - strip both // and # for code
            // URL protection prevents truncating https:// links
            shouldTreatHashAsComment = true;
            shouldStripDoubleSlash = true;
        }
        else
        {
            shouldTreatHashAsComment = !NonHashCommentExtensions.Contains(normalizedExt);
            shouldStripDoubleSlash = DoubleSlashCommentExtensions.Contains(normalizedExt);
        }

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

            // Check for // comments only in languages that use them
            // But NOT if it's part of a URL (e.g., https://, http://, ftp://)
            if (shouldStripDoubleSlash && ch == '/' && i + 1 < len && span[i + 1] == '/')
            {
                // Don't start // comment if preceded by : (URL protocol separator)
                var precededByColon = i > 0 && span[i - 1] == ':';
                if (!precededByColon)
                {
                    inSingleLineComment = true;
                    i += 2;
                    continue;
                }
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
    // String interiors are preserved verbatim to avoid corrupting format strings,
    // SQL queries, regex patterns, and other content where whitespace is semantic.
    // =========================================================================

    internal static string CollapseWhitespace(string stripped)
    {
        var result = new StringBuilder(stripped.Length);
        var i = 0;
        var len = stripped.Length;
        var lastWasSpace = false;
        var lineIsEmpty = true;
        var inString = false;
        var inChar = false;

        while (i < len)
        {
            var ch = stripped[i];

            // Handle string/char state - pass through verbatim
            if (inString)
            {
                result.Append(ch);
                if (ch == '\\' && i + 1 < len)
                {
                    i++;
                    result.Append(stripped[i]);
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
                result.Append(ch);
                if (ch == '\\' && i + 1 < len)
                {
                    i++;
                    result.Append(stripped[i]);
                }
                else if (ch == '\'')
                {
                    inChar = false;
                }

                i++;
                continue;
            }

            // Track string/char start
            if (ch == '"')
            {
                inString = true;
                result.Append(ch);
                i++;
                continue;
            }

            if (ch == '\'')
            {
                inChar = true;
                result.Append(ch);
                i++;
                continue;
            }

            // Newline handling
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

            // Whitespace collapse
            if (ch == ' ' || ch == '\t')
            {
                if (lineIsEmpty)
                    // Preserve leading whitespace for indentation-sensitive languages
                    result.Append(ch);
                else
                    lastWasSpace = true;
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