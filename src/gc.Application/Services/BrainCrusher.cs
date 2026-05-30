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
        var stripped = StripComments(code, language ?? _fileExtension);
        return CollapseWhitespace(stripped, language ?? _fileExtension);
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
        var stripped = StripComments(content, _fileExtension);
        return CollapseWhitespace(stripped, _fileExtension);
    }

    // =========================================================================
    // Universal Syntax Minifier
    // Driven by LanguageProfiles state machine
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

        // Get language profile
        var profile = LanguageProfiles.For(normalizedExt);

        var hasBlockComment = profile.BlockComment.Length == 2;
        var blockOpen = hasBlockComment ? profile.BlockComment[0] : "";
        var blockClose = hasBlockComment ? profile.BlockComment[1] : "";

        var shouldTreatHashAsComment = profile.HashComment;
        var shouldStripDoubleSlash = profile.LineComment.Contains("//");
        var shouldStripSql = profile.SqlComment;

        // Preserve shebang line if present at the very beginning of the file
        if (i == 0 && len >= 2 && span[0] == '#' && span[1] == '!')
        {
            while (i < len && span[i] != '\n' && span[i] != '\r')
            {
                sb.Append(span[i]);
                i++;
            }
        }

        var isCSharp = normalizedExt != null && (normalizedExt.Equals("cs", StringComparison.OrdinalIgnoreCase) || normalizedExt.Equals("csharp", StringComparison.OrdinalIgnoreCase));
        var isPython = normalizedExt != null && (normalizedExt.Equals("py", StringComparison.OrdinalIgnoreCase) || normalizedExt.Equals("python", StringComparison.OrdinalIgnoreCase));

        var stringMode = StringMode.None;
        var csharpRawQuoteCount = 0;
        var inMultiLineComment = false;
        var inSingleLineComment = false;

        while (i < len)
        {
            var ch = span[i];

            // 1. Handle String Modes
            if (stringMode != StringMode.None)
            {
                sb.Append(ch);

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
                        if (ch == '\\' && i + 1 < len)
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
                        if (ch == '\\' && i + 1 < len)
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
                        // Count trailing quotes to check if raw string ends
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

            // 2. Handle Multi-line Block Comments
            if (inMultiLineComment)
            {
                if (span[i..].StartsWith(blockClose.AsSpan()))
                {
                    inMultiLineComment = false;
                    i += blockClose.Length;
                    sb.Append(' ');
                    continue;
                }

                if (ch == '\n') sb.Append('\n');
                i++;
                continue;
            }

            // 3. Handle Single-line Comments
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

            // 4. Start Comment Checks

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

            // 5. Start String Checks

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

            // Regular character
            sb.Append(ch);
            i++;
        }

        return sb.ToString();
    }

    // =========================================================================
    // Whitespace collapse: multiple spaces → single, blank lines removed
    // =========================================================================
    internal static string CollapseWhitespace(string stripped, string? fileExtension = null)
    {
        var result = new StringBuilder(stripped.Length);
        var span = stripped.AsSpan();
        var i = 0;
        var len = span.Length;
        var lastWasSpace = false;
        var lineIsEmpty = true;

        // Normalize extension (strip leading dot)
        string? normalizedExt = null;
        if (!string.IsNullOrEmpty(fileExtension))
            normalizedExt = fileExtension.StartsWith('.')
                ? fileExtension.Substring(1)
                : fileExtension;

        var isCSharp = normalizedExt != null && (normalizedExt.Equals("cs", StringComparison.OrdinalIgnoreCase) || normalizedExt.Equals("csharp", StringComparison.OrdinalIgnoreCase));
        var isPython = normalizedExt != null && (normalizedExt.Equals("py", StringComparison.OrdinalIgnoreCase) || normalizedExt.Equals("python", StringComparison.OrdinalIgnoreCase));

        var stringMode = StringMode.None;
        var csharpRawQuoteCount = 0;

        while (i < len)
        {
            var ch = span[i];

            if (stringMode != StringMode.None)
            {
                result.Append(ch);
                lineIsEmpty = false;

                switch (stringMode)
                {
                    case StringMode.JsTemplateLiteral:
                        if (ch == '\\' && i + 1 < len)
                        {
                            i++;
                            result.Append(span[i]);
                        }
                        else if (ch == '`')
                        {
                            stringMode = StringMode.None;
                        }
                        break;

                    case StringMode.NormalDouble:
                        if (ch == '\\' && i + 1 < len)
                        {
                            i++;
                            result.Append(span[i]);
                        }
                        else if (ch == '"')
                        {
                            stringMode = StringMode.None;
                        }
                        break;

                    case StringMode.NormalSingle:
                        if (ch == '\\' && i + 1 < len)
                        {
                            i++;
                            result.Append(span[i]);
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
                                result.Append('"');
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
                                for (var q = 1; q < quotes; q++) result.Append('"');
                                i += quotes - 1;
                                stringMode = StringMode.None;
                            }
                        }
                        break;

                    case StringMode.PythonRawDouble:
                        if (ch == '\\' && i + 1 < len && span[i + 1] == '"')
                        {
                            i++;
                            result.Append(span[i]);
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
                            result.Append(span[i]);
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
                            result.Append(span[i]);
                        }
                        else if (ch == '"' && i + 2 < len && span[i + 1] == '"' && span[i + 2] == '"')
                        {
                            result.Append("\"\"");
                            i += 2;
                            stringMode = StringMode.None;
                        }
                        break;

                    case StringMode.TripleQuoteSingle:
                        if (ch == '\\' && i + 1 < len)
                        {
                            i++;
                            result.Append(span[i]);
                        }
                        else if (ch == '\'' && i + 2 < len && span[i + 1] == '\'' && span[i + 2] == '\'')
                        {
                            result.Append("''");
                            i += 2;
                            stringMode = StringMode.None;
                        }
                        break;
                }

                i++;
                continue;
            }

            // String start checks
            if (isPython && (ch == 'r' || ch == 'R') && i + 1 < len)
            {
                var next = span[i + 1];
                if (next == '"')
                {
                    if (i + 3 < len && span[i + 2] == '"' && span[i + 3] == '"')
                    {
                        stringMode = StringMode.TripleQuoteDouble;
                        result.Append("r\"\"\"");
                        i += 4;
                        continue;
                    }
                    stringMode = StringMode.PythonRawDouble;
                    result.Append("r\"");
                    i += 2;
                    continue;
                }
                if (next == '\'')
                {
                    if (i + 3 < len && span[i + 2] == '\'' && span[i + 3] == '\'')
                    {
                        stringMode = StringMode.TripleQuoteSingle;
                        result.Append("r'''");
                        i += 4;
                        continue;
                    }
                    stringMode = StringMode.PythonRawSingle;
                    result.Append("r'");
                    i += 2;
                    continue;
                }
            }

            if (isPython && ch == '"' && i + 2 < len && span[i + 1] == '"' && span[i + 2] == '"')
            {
                stringMode = StringMode.TripleQuoteDouble;
                result.Append("\"\"\"");
                i += 3;
                continue;
            }
            if (isPython && ch == '\'' && i + 2 < len && span[i + 1] == '\'' && span[i + 2] == '\'')
            {
                stringMode = StringMode.TripleQuoteSingle;
                result.Append("'''");
                i += 3;
                continue;
            }

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
                    for (var q = 0; q < quotes; q++) result.Append('"');
                    i += quotes;
                    continue;
                }
            }
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
                result.Append('$');
                for (var q = 0; q < quotes; q++) result.Append('"');
                i += 1 + quotes;
                continue;
            }

            if (isCSharp && ch == '@' && i + 1 < len && span[i + 1] == '"')
            {
                stringMode = StringMode.CSharpVerbatim;
                result.Append("@\"");
                i += 2;
                continue;
            }
            if (isCSharp && ch == '$' && i + 2 < len && span[i + 1] == '@' && span[i + 2] == '"')
            {
                stringMode = StringMode.CSharpVerbatim;
                result.Append("$@\"");
                i += 3;
                continue;
            }
            if (isCSharp && ch == '@' && i + 2 < len && span[i + 1] == '$' && span[i + 2] == '"')
            {
                stringMode = StringMode.CSharpVerbatim;
                result.Append("@$\"");
                i += 3;
                continue;
            }

            if (ch == '`')
            {
                stringMode = StringMode.JsTemplateLiteral;
                result.Append('`');
                i++;
                continue;
            }

            if (ch == '"')
            {
                stringMode = StringMode.NormalDouble;
                result.Append('"');
                i++;
                continue;
            }

            if (ch == '\'')
            {
                stringMode = StringMode.NormalSingle;
                result.Append('\'');
                i++;
                continue;
            }

            // Newline handling
            if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < len && span[i + 1] == '\n')
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