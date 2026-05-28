using System.Runtime.CompilerServices;

namespace gc.Application.Services;

/// <summary>
/// Options for CodeLexer comment handling.
/// Allows language-aware filtering to avoid false positives
/// (e.g., SQL -- comments in C# files, hash comments in non-shell files).
/// </summary>
public readonly struct CodeLexerOptions
{
    /// <summary>
    /// Enable C-style single-line comments (// ...).
    /// </summary>
    public bool EnableCStyleComment { get; init; }

    /// <summary>
    /// Enable C-style multi-line comments (/* ... */).
    /// </summary>
    public bool EnableCMultiComment { get; init; }

    /// <summary>
    /// Enable HTML comments (&lt;!-- ... --&gt;).
    /// </summary>
    public bool EnableHtmlComment { get; init; }

    /// <summary>
    /// Enable SQL-style comments (-- ...).
    /// Enable only for SQL-like files to avoid false positives.
    /// </summary>
    public bool EnableSqlComment { get; init; }

    /// <summary>
    /// Enable hash (# ...) comments.
    /// Enable only for shell-like files to avoid stripping C# preprocessor directives.
    /// </summary>
    public bool EnableHashComment { get; init; }

    /// <summary>
    /// Enable triple-quoted strings (""" ... """, ''' ... ''').
    /// </summary>
    public bool EnableTripleQuote { get; init; }

    public static CodeLexerOptions ForCSharp => new()
    {
        EnableCStyleComment = true,
        EnableCMultiComment = true,
        EnableHtmlComment = true,
        EnableSqlComment = false,
        EnableHashComment = false,
        EnableTripleQuote = true
    };

    public static CodeLexerOptions ForSql => new()
    {
        EnableCStyleComment = true,
        EnableCMultiComment = true,
        EnableHtmlComment = true,
        EnableSqlComment = true,
        EnableHashComment = false,
        EnableTripleQuote = true
    };

    public static CodeLexerOptions ForShell => new()
    {
        EnableCStyleComment = true,
        EnableCMultiComment = true,
        EnableHtmlComment = true,
        EnableSqlComment = false,
        EnableHashComment = true,
        EnableTripleQuote = true
    };

    public static CodeLexerOptions ForPython => new()
    {
        EnableCStyleComment = true,
        EnableCMultiComment = true,
        EnableHtmlComment = true,
        EnableSqlComment = false,
        EnableHashComment = true,
        EnableTripleQuote = true
    };

    /// <summary>
    /// Default: C-style comments only, no SQL/hash. Safe for most languages.
    /// </summary>
    public static CodeLexerOptions Default => new()
    {
        EnableCStyleComment = true,
        EnableCMultiComment = true,
        EnableHtmlComment = true,
        EnableSqlComment = false,
        EnableHashComment = false,
        EnableTripleQuote = true
    };

    /// <summary>
    /// Returns options for a given file extension/language.
    /// </summary>
    public static CodeLexerOptions ForLanguage(string? language)
    {
        if (string.IsNullOrEmpty(language)) return Default;

        var lang = language.ToLowerInvariant();
        return lang switch
        {
            "cs" or "csharp" => ForCSharp,
            "sql" or "pgsql" or "mysql" or "sqlite" or "tsql" or "plsql" => ForSql,
            "py" or "python" or "rb" or "ruby" or "sh" or "bash" or "zsh" or "shell" => ForShell,
            _ => Default
        };
    }
}

/// <summary>
/// Zero-allocation lexer optimized for throughput.
/// Walks a <see cref="ReadOnlySpan{char}"/> and reports identifiers
/// via a callback, avoiding all string allocations during lexing.
///
/// Performance design:
/// - No heap allocations in the hot path (ref struct)
/// - Branch-friendly state machine with predictable patterns
/// - Identifier chars checked via bitmask instead of Char methods
/// - Caller decides when/if to allocate (via callback)
/// </summary>
public ref struct CodeLexer
{
    private readonly ReadOnlySpan<char> _span;
    private int _len;

    // State flags
    private bool _inString;
    private bool _inChar;
    private bool _inSingleComment;
    private bool _inMultiComment;
    private bool _inHashComment;
    private bool _inHtmlComment;
    private bool _inTripleQuote;
    private bool _inSqlComment;

    private readonly CodeLexerOptions _options;
    private const int DefaultMinIdentifierLength = 6;
    private readonly int _minIdentifierLength;

    public CodeLexer(ReadOnlySpan<char> span, int minIdentifierLength = DefaultMinIdentifierLength)
        : this(span, CodeLexerOptions.Default, minIdentifierLength) { }

    public CodeLexer(ReadOnlySpan<char> span, CodeLexerOptions options, int minIdentifierLength = DefaultMinIdentifierLength)
    {
        _span = span;
        _len = span.Length;
        _options = options;
        _minIdentifierLength = minIdentifierLength;
    }

    /// <summary>
    /// Creates a lexer configured for the given language.
    /// </summary>
    public CodeLexer(ReadOnlySpan<char> span, string language, int minIdentifierLength = DefaultMinIdentifierLength)
        : this(span, CodeLexerOptions.ForLanguage(language), minIdentifierLength) { }

    /// <summary>
    /// Invokes <paramref name="onIdentifier"/> for each identifier found.
    /// The span is valid only during the callback (ref struct lifetime).
    /// Returns the total count of identifiers reported.
    /// </summary>
    public int Enumerate(Action<ReadOnlySpan<char>> onIdentifier)
    {
        int count = 0;
        var span = _span;
        int len = _len;
        int pos = 0;

        while ((uint)pos < (uint)len)
        {
            char ch = span[pos];

            // --- Fast path: check state flags in priority order ---

            if (_inTripleQuote)
            {
                if (ch == '"' && pos + 2 < len && span[pos + 1] == '"' && span[pos + 2] == '"')
                {
                    pos += 3;
                    _inTripleQuote = false;
                }
                else if (ch == '\'' && pos + 2 < len && span[pos + 1] == '\'' && span[pos + 2] == '\'')
                {
                    pos += 3;
                    _inTripleQuote = false;
                }
                else
                {
                    pos++;
                }
                continue;
            }

            if (_inSqlComment)
            {
                if (ch == '\n') _inSqlComment = false;
                pos++;
                continue;
            }

            if (_inHtmlComment)
            {
                if (ch == '-' && pos + 2 < len && span[pos + 1] == '-' && span[pos + 2] == '>')
                {
                    pos += 3;
                    _inHtmlComment = false;
                }
                else
                {
                    pos++;
                }
                continue;
            }

            if (_inHashComment)
            {
                if (ch == '\n') _inHashComment = false;
                pos++;
                continue;
            }

            if (_inMultiComment)
            {
                if (ch == '*' && pos + 1 < len && span[pos + 1] == '/')
                {
                    pos += 2;
                    _inMultiComment = false;
                }
                else
                {
                    pos++;
                }
                continue;
            }

            if (_inSingleComment)
            {
                if (ch == '\n') _inSingleComment = false;
                pos++;
                continue;
            }

            if (_inString)
            {
                if (ch == '\\' && pos + 1 < len)
                {
                    pos += 2;
                }
                else if (ch == '"')
                {
                    _inString = false;
                    pos++;
                }
                else
                {
                    pos++;
                }
                continue;
            }

            if (_inChar)
            {
                if (ch == '\\' && pos + 1 < len)
                {
                    pos += 2;
                }
                else if (ch == '\'')
                {
                    _inChar = false;
                    pos++;
                }
                else
                {
                    pos++;
                }
                continue;
            }

            // --- Not inside any string/comment: detect transitions ---

            // Triple-quote open
            if (_options.EnableTripleQuote &&
                ch == '"' && pos + 2 < len && span[pos + 1] == '"' && span[pos + 2] == '"')
            {
                _inTripleQuote = true;
                pos += 3;
                continue;
            }
            if (_options.EnableTripleQuote &&
                ch == '\'' && pos + 2 < len && span[pos + 1] == '\'' && span[pos + 2] == '\'')
            {
                _inTripleQuote = true;
                pos += 3;
                continue;
            }

            // SQL comment (only for SQL-like languages)
            if (_options.EnableSqlComment && ch == '-' && pos + 1 < len && span[pos + 1] == '-')
            {
                _inSqlComment = true;
                pos += 2;
                continue;
            }

            // HTML comment
            if (_options.EnableHtmlComment &&
                ch == '<' && pos + 3 < len && span[pos + 1] == '!' && span[pos + 2] == '-' && span[pos + 3] == '-')
            {
                _inHtmlComment = true;
                pos += 4;
                continue;
            }

            // C-style // comment
            if (_options.EnableCStyleComment && ch == '/' && pos + 1 < len && span[pos + 1] == '/')
            {
                _inSingleComment = true;
                pos += 2;
                continue;
            }

            // C-style /* comment
            if (_options.EnableCMultiComment && ch == '/' && pos + 1 < len && span[pos + 1] == '*')
            {
                _inMultiComment = true;
                pos += 2;
                continue;
            }

            // Hash comment (only for shell-like languages)
            if (_options.EnableHashComment && ch == '#')
            {
                _inHashComment = true;
                pos++;
                continue;
            }

            // String literal open
            if (ch == '"')
            {
                _inString = true;
                pos++;
                continue;
            }

            // Char literal open
            if (ch == '\'')
            {
                _inChar = true;
                pos++;
                continue;
            }

            // --- Identifier detection with fast char classification ---
            if (IsIdStart(ch))
            {
                int start = pos;
                pos++;
                // Hot loop: use bitmask check instead of Char methods
                while ((uint)pos < (uint)len && IsIdPart(span[pos]))
                    pos++;

                int identLen = pos - start;
                if (identLen >= _minIdentifierLength)
                {
                    onIdentifier(span.Slice(start, identLen));
                    count++;
                }
                continue;
            }

            pos++;
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdStart(char c)
    {
        // a-z, A-Z, _
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdPart(char c)
    {
        // a-z, A-Z, 0-9, _
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
    }
}