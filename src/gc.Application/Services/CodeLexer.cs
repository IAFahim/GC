using System;
using System.Runtime.CompilerServices;

namespace gc.Application.Services;

/// <summary>
/// Zero-allocation lexer optimized for throughput.
/// Walks a <see cref="ReadOnlySpan{char}"/> and reports identifiers
/// via a callback, avoiding all string allocations during lexing.
///
/// Performance design:
/// - No heap allocations in the hot path (ref struct)
/// - Branch-friendly state machine with predictable patterns
/// - Identifier chars checked via bitmask instead of Char.IsLetterOrDigit
/// - Caller decides when/if to allocate (via callback)
/// </summary>
public ref struct CodeLexer
{
    private readonly ReadOnlySpan<char> _span;
    private int _len;

    // State flags - use separate bools for clarity and JIT optimization
    private bool _inString;
    private bool _inChar;
    private bool _inSingleComment;
    private bool _inMultiComment;
    private bool _inHashComment;
    private bool _inHtmlComment;
    private bool _inTripleQuote;
    private bool _inSqlComment;

    public CodeLexer(ReadOnlySpan<char> span)
    {
        _span = span;
        _len = span.Length;
    }

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
            if (ch == '"' && pos + 2 < len && span[pos + 1] == '"' && span[pos + 2] == '"')
            {
                _inTripleQuote = true;
                pos += 3;
                continue;
            }
            if (ch == '\'' && pos + 2 < len && span[pos + 1] == '\'' && span[pos + 2] == '\'')
            {
                _inTripleQuote = true;
                pos += 3;
                continue;
            }

            // SQL comment
            if (ch == '-' && pos + 1 < len && span[pos + 1] == '-')
            {
                _inSqlComment = true;
                pos += 2;
                continue;
            }

            // HTML comment
            if (ch == '<' && pos + 3 < len && span[pos + 1] == '!' && span[pos + 2] == '-' && span[pos + 3] == '-')
            {
                _inHtmlComment = true;
                pos += 4;
                continue;
            }

            // C-style // comment
            if (ch == '/' && pos + 1 < len && span[pos + 1] == '/')
            {
                _inSingleComment = true;
                pos += 2;
                continue;
            }

            // C-style /* comment
            if (ch == '/' && pos + 1 < len && span[pos + 1] == '*')
            {
                _inMultiComment = true;
                pos += 2;
                continue;
            }

            // Hash comment
            if (ch == '#')
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
                if (identLen >= 6)
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