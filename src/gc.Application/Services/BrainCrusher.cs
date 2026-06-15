using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using gc.Domain.Interfaces;
using gc.Domain.Language;

namespace gc.Application.Services;

/// <summary>
///     Phase 1 minifier: strips comments and collapses whitespace.
///     No static keyword dictionary — that was token-pessimal (replacing 1-token
///     keywords like "public" with 2-token "!1"). Dynamic compression is handled
///     by DynamicCompressor using BPE-style SA/LCP analysis.
/// </summary>
public sealed class BrainCrusher : IBrainCrusher
{
    private readonly string? _fileExtension;

    public BrainCrusher(string? fileExtension = null)
    {
        _fileExtension = fileExtension;
    }

    public string CrushBlock(string code, string? language = null)
    {
        if (string.IsNullOrEmpty(code)) return code;
        return CrushFused(code, language ?? _fileExtension);
    }

    public string Uncrush(string crushed)
    {
        return crushed;
    }

    public string GetDictionaryHeader()
    {
        return string.Empty;
    }

    public string Crush(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        return CrushFused(content, _fileExtension);
    }

    // =========================================================================
    // Fused minifier: strips comments AND collapses whitespace in ONE pass.
    // Carries a single StringMode state machine plus the whitespace-collapse
    // state (lastWasSpace / lineIsEmpty), eliminating the intermediate string
    // and the second full re-lex that the old two-pass design required.
    //
    // Output is byte-identical to the old StripComments -> CollapseWhitespace
    // pipeline (verified over a corpus before the legacy methods were removed).
    //
    // Routing rules (mirroring the old two-pass sequencing):
    //   * In a string/comment-open state: append verbatim, and set
    //     lineIsEmpty=false for any non-newline content so quoted whitespace is
    //     never collapsed.
    //   * Outside strings: route every emitted char through the collapse logic
    //     (newline -> blank-line suppression; ' '/'\t' -> pending space unless
    //     leading indentation; other -> flush pending space then append).
    //   * Block-comment close emits a collapsible space (old line 240).
    //   * Embedded/terminating comment newlines route through the newline branch.
    //   * The shebang first line skips comment detection but is still collapsed
    //     and string-scanned, exactly as the old pass 2 reprocessed it.
    // =========================================================================
    internal static string CrushFused(string content, string? fileExtension = null)
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

        var profile = LanguageProfiles.For(normalizedExt);

        var hasBlockComment = profile.BlockComment.Length == 2;
        var blockOpen = hasBlockComment ? profile.BlockComment[0] : "";
        var blockClose = hasBlockComment ? profile.BlockComment[1] : "";

        var shouldTreatHashAsComment = profile.HashComment;
        var shouldStripDoubleSlash = profile.LineComment.Contains("//");
        var shouldStripSql = profile.SqlComment;

        var isCSharp = normalizedExt != null && (normalizedExt.Equals("cs", StringComparison.OrdinalIgnoreCase) || normalizedExt.Equals("csharp", StringComparison.OrdinalIgnoreCase));
        var isPython = normalizedExt != null && (normalizedExt.Equals("py", StringComparison.OrdinalIgnoreCase) || normalizedExt.Equals("python", StringComparison.OrdinalIgnoreCase));

        var stringMode = StringMode.None;
        var csharpRawQuoteCount = 0;
        var inMultiLineComment = false;
        var inSingleLineComment = false;

        // Whitespace-collapse state (matches old CollapseWhitespace).
        var lastWasSpace = false;
        var lineIsEmpty = true;

        // Shebang first-line state: suppress comment detection for the very first
        // line if it starts with "#!", but still collapse and string-scan it.
        var inShebang = i == 0 && len >= 2 && span[0] == '#' && span[1] == '!';

        while (i < len)
        {
            var ch = span[i];

            // 1. Handle String Modes (append verbatim; mark line non-empty).
            // Mirrors old CollapseWhitespace: lineIsEmpty is set false unconditionally
            // for every char consumed in string mode (including raw newlines inside
            // multi-line literals), so the fused output matches the old pass 2 exactly.
            if (stringMode != StringMode.None)
            {
                sb.Append(ch);
                lineIsEmpty = false;

                switch (stringMode)
                {
                    case StringMode.JsTemplateLiteral:
                        if (ch == '\\' && i + 1 < len)
                        {
                            i++;
                            sb.Append(span[i]);
                        }
                        else if (ch == '`')
                        {
                            stringMode = StringMode.None;
                        }
                        break;

                    case StringMode.NormalDouble:
                        // Confined to a single line (FIX 1): a normal double-quoted
                        // literal never spans a raw newline in any supported language.
                        if (ch == '\n' || ch == '\r')
                        {
                            stringMode = StringMode.None;
                        }
                        else if (ch == '\\' && i + 1 < len)
                        {
                            i++;
                            sb.Append(span[i]);
                        }
                        else if (ch == '"')
                        {
                            stringMode = StringMode.None;
                        }
                        break;

                    case StringMode.NormalSingle:
                        if (ch == '\n' || ch == '\r')
                        {
                            stringMode = StringMode.None;
                        }
                        else if (ch == '\\' && i + 1 < len)
                        {
                            i++;
                            sb.Append(span[i]);
                        }
                        else if (ch == '\'')
                        {
                            stringMode = StringMode.None;
                        }
                        break;

                    case StringMode.CSharpVerbatim:
                        if (ch == '"')
                        {
                            if (i + 1 < len && span[i + 1] == '"')
                            {
                                sb.Append('"');
                                i++; // Skip the escaped quote
                            }
                            else
                            {
                                stringMode = StringMode.None;
                            }
                        }
                        break;

                    case StringMode.CSharpRaw:
                        if (ch == '"')
                        {
                            var quotes = 0;
                            while (i + quotes < len && span[i + quotes] == '"')
                            {
                                quotes++;
                            }
                            if (quotes >= csharpRawQuoteCount)
                            {
                                for (var q = 1; q < quotes; q++) sb.Append('"');
                                i += quotes - 1;
                                stringMode = StringMode.None;
                            }
                        }
                        break;

                    case StringMode.PythonRawDouble:
                        if (ch == '\\' && i + 1 < len && span[i + 1] == '"')
                        {
                            i++;
                            sb.Append(span[i]);
                        }
                        else if (ch == '"')
                        {
                            stringMode = StringMode.None;
                        }
                        break;

                    case StringMode.PythonRawSingle:
                        if (ch == '\\' && i + 1 < len && span[i + 1] == '\'')
                        {
                            i++;
                            sb.Append(span[i]);
                        }
                        else if (ch == '\'')
                        {
                            stringMode = StringMode.None;
                        }
                        break;

                    case StringMode.TripleQuoteDouble:
                        if (ch == '\\' && i + 1 < len)
                        {
                            i++;
                            sb.Append(span[i]);
                        }
                        else if (ch == '"' && i + 2 < len && span[i + 1] == '"' && span[i + 2] == '"')
                        {
                            sb.Append("\"\"");
                            i += 2;
                            stringMode = StringMode.None;
                        }
                        break;

                    case StringMode.TripleQuoteSingle:
                        if (ch == '\\' && i + 1 < len)
                        {
                            i++;
                            sb.Append(span[i]);
                        }
                        else if (ch == '\'' && i + 2 < len && span[i + 1] == '\'' && span[i + 2] == '\'')
                        {
                            sb.Append("''");
                            i += 2;
                            stringMode = StringMode.None;
                        }
                        break;
                }

                i++;
                continue;
            }

            // 2. Handle Multi-line Block Comments.
            if (inMultiLineComment)
            {
                if (span[i..].StartsWith(blockClose.AsSpan()))
                {
                    inMultiLineComment = false;
                    i += blockClose.Length;
                    // Old pass 1 emitted a single ' ' here, which old pass 2 then ran
                    // through its space branch. Reproduce that branch exactly: a space
                    // at line start is preserved as indentation; otherwise it becomes a
                    // pending collapsed space.
                    if (lineIsEmpty)
                        sb.Append(' ');
                    else
                        lastWasSpace = true;
                    continue;
                }

                if (ch == '\n')
                {
                    // Old pass 1 emitted '\n' here; old pass 2 ran it through the
                    // newline branch (blank-line suppression).
                    if (!lineIsEmpty)
                    {
                        sb.Append('\n');
                        lineIsEmpty = true;
                        lastWasSpace = false;
                    }
                }
                i++;
                continue;
            }

            // 3. Handle Single-line Comments.
            if (inSingleLineComment)
            {
                if (ch == '\n')
                {
                    inSingleLineComment = false;
                    // Old pass 1 emitted '\n'; route through the newline branch.
                    if (!lineIsEmpty)
                    {
                        sb.Append('\n');
                        lineIsEmpty = true;
                        lastWasSpace = false;
                    }
                }

                i++;
                continue;
            }

            // 4. Start Comment Checks (suppressed on the shebang line).
            if (!inShebang)
            {
                // Start block comment check
                if (hasBlockComment && span[i..].StartsWith(blockOpen.AsSpan()))
                {
                    inMultiLineComment = true;
                    i += blockOpen.Length;
                    continue;
                }

                // Start double slash line comment check
                if (shouldStripDoubleSlash && ch == '/' && i + 1 < len && span[i + 1] == '/')
                {
                    var precededByColon = i > 0 && span[i - 1] == ':';
                    if (!precededByColon)
                    {
                        inSingleLineComment = true;
                        i += 2;
                        continue;
                    }
                }

                // Start SQL line comment check
                if (shouldStripSql && ch == '-' && i + 1 < len && span[i + 1] == '-')
                {
                    inSingleLineComment = true;
                    i += 2;
                    continue;
                }

                // Start Hash comment check
                if (shouldTreatHashAsComment && ch == '#')
                {
                    inSingleLineComment = true;
                    i++;
                    continue;
                }
            }

            // 5. Start String Checks (run on the shebang line too, matching old pass 2).
            //
            // IMPORTANT byte-identity note: the old pass 2 appended string openers
            // DIRECTLY (no pending-space flush, no lineIsEmpty mutation). So if a
            // string opener immediately follows collapsed spaces, that pending space
            // is intentionally dropped (e.g. `x   "y"` -> `x"y"`). We reproduce that
            // exactly: append openers raw; the first in-string char then sets
            // lineIsEmpty=false on the next iteration.

            // Python raw triple/single/double quote check
            if (isPython && (ch == 'r' || ch == 'R') && i + 1 < len)
            {
                var next = span[i + 1];
                if (next == '"')
                {
                    if (i + 3 < len && span[i + 2] == '"' && span[i + 3] == '"')
                    {
                        stringMode = StringMode.TripleQuoteDouble;
                        sb.Append("r\"\"\"");
                        i += 4;
                        continue;
                    }
                    stringMode = StringMode.PythonRawDouble;
                    sb.Append("r\"");
                    i += 2;
                    continue;
                }
                if (next == '\'')
                {
                    if (i + 3 < len && span[i + 2] == '\'' && span[i + 3] == '\'')
                    {
                        stringMode = StringMode.TripleQuoteSingle;
                        sb.Append("r'''");
                        i += 4;
                        continue;
                    }
                    stringMode = StringMode.PythonRawSingle;
                    sb.Append("r'");
                    i += 2;
                    continue;
                }
            }

            // Python triple quote check (non-raw)
            if (isPython && ch == '"' && i + 2 < len && span[i + 1] == '"' && span[i + 2] == '"')
            {
                stringMode = StringMode.TripleQuoteDouble;
                sb.Append("\"\"\"");
                i += 3;
                continue;
            }
            if (isPython && ch == '\'' && i + 2 < len && span[i + 1] == '\'' && span[i + 2] == '\'')
            {
                stringMode = StringMode.TripleQuoteSingle;
                sb.Append("'''");
                i += 3;
                continue;
            }

            // C# raw string literal check
            if (isCSharp && ch == '"')
            {
                var quotes = 0;
                while (i + quotes < len && span[i + quotes] == '"')
                {
                    quotes++;
                }
                if (quotes >= 3)
                {
                    stringMode = StringMode.CSharpRaw;
                    csharpRawQuoteCount = quotes;
                    for (var q = 0; q < quotes; q++) sb.Append('"');
                    i += quotes;
                    continue;
                }
            }
            // C# raw string literal with $ check
            if (isCSharp && ch == '$' && i + 3 < len && span[i + 1] == '"' && span[i + 2] == '"' && span[i + 3] == '"')
            {
                var idxTemp = i + 1;
                var quotes = 0;
                while (idxTemp < len && span[idxTemp] == '"')
                {
                    quotes++;
                    idxTemp++;
                }
                stringMode = StringMode.CSharpRaw;
                csharpRawQuoteCount = quotes;
                sb.Append('$');
                for (var q = 0; q < quotes; q++) sb.Append('"');
                i += 1 + quotes;
                continue;
            }

            // C# verbatim string check
            if (isCSharp && ch == '@' && i + 1 < len && span[i + 1] == '"')
            {
                stringMode = StringMode.CSharpVerbatim;
                sb.Append("@\"");
                i += 2;
                continue;
            }
            if (isCSharp && ch == '$' && i + 2 < len && span[i + 1] == '@' && span[i + 2] == '"')
            {
                stringMode = StringMode.CSharpVerbatim;
                sb.Append("$@\"");
                i += 3;
                continue;
            }
            if (isCSharp && ch == '@' && i + 2 < len && span[i + 1] == '$' && span[i + 2] == '"')
            {
                stringMode = StringMode.CSharpVerbatim;
                sb.Append("@$\"");
                i += 3;
                continue;
            }

            // JS/TS template literal check
            if (ch == '`')
            {
                stringMode = StringMode.JsTemplateLiteral;
                sb.Append('`');
                i++;
                continue;
            }

            // Standard double quote string
            if (ch == '"')
            {
                stringMode = StringMode.NormalDouble;
                sb.Append('"');
                i++;
                continue;
            }

            // Standard single quote/char
            if (ch == '\'')
            {
                stringMode = StringMode.NormalSingle;
                sb.Append('\'');
                i++;
                continue;
            }

            // 6. Whitespace collapse (outside any string/comment).

            // Newline handling
            if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < len && span[i + 1] == '\n')
                    i++;

                if (!lineIsEmpty)
                {
                    sb.Append('\n');
                    lineIsEmpty = true;
                    lastWasSpace = false;
                }

                inShebang = false;
                i++;
                continue;
            }

            if (ch == ' ' || ch == '\t')
            {
                if (lineIsEmpty)
                    sb.Append(ch);
                else
                    lastWasSpace = true;
                i++;
                continue;
            }

            if (lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = false;
            }

            sb.Append(ch);
            lineIsEmpty = false;
            i++;
        }

        return sb.ToString();
    }

    private enum StringMode
    {
        None,
        JsTemplateLiteral,
        NormalDouble,
        NormalSingle,
        CSharpVerbatim,
        CSharpRaw,
        PythonRawDouble,
        PythonRawSingle,
        TripleQuoteDouble,
        TripleQuoteSingle
    }
}