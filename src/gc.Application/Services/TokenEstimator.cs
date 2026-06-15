using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace gc.Application.Services;

/// <summary>
///     Fast, zero-allocation heuristic estimator for LLM token counts (o200k_base / cl100k_base).
///     <para>
///         The heuristic counts: alphanumeric word boundaries + CamelCase transitions + punctuation symbols.
///         For example: "_configurationValidator" = _ + configuration + Validator = 3 tokens.
///         "public async Task&lt;Result&gt;" = public + async + Task + &lt; + Result + &gt; = 6 tokens.
///         "IConfigurationValidator" = I + Configuration + Validator = 3 tokens.
///     </para>
/// </summary>
public static class TokenEstimator
{
    /// <summary>
    ///     Estimates the number of LLM tokens for the given text using a BPE-aware heuristic.
    /// </summary>
    /// <param name="text">The input text to estimate tokens for.</param>
    /// <returns>An estimated token count.</returns>
    public static int EstimateTokens(ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
            return 0;

        var tokens = 0;
        var i = 0;
        var length = text.Length;

        while (i < length)
        {
            var c = text[i];
            var k = Classify(c);

            // Whitespace: skip, acts as a boundary separator
            if ((k & (byte)CharClass.Whitespace) != 0)
            {
                i++;
                continue;
            }

            // Punctuation: each punctuation character is its own token (e.g., '<', '>', ',', '.', '(', ')')
            if ((k & (byte)CharClass.Punct) != 0)
            {
                tokens++;
                i++;
                continue;
            }

            // Non-ASCII text (CJK, accented Latin, etc.): the ASCII classifiers cannot detect
            // boundaries within such a run, so approximate BPE granularity (~1.5 chars/token)
            // instead of collapsing the entire run into a single token.
            if (!IsAscii(c))
            {
                var start = i;
                while (i < length && !IsAscii(text[i]) && !IsWhitespace(text[i]))
                    i++;
                var runLen = i - start;
                tokens += (runLen * 2 + 2) / 3; // ceil(runLen / 1.5), at least 1 for a non-empty run
                continue;
            }

            // Alphanumeric word: consume a run of alphanumeric chars, counting CamelCase transitions
            tokens++;
            i++;

            while (i < length)
            {
                var current = text[i];
                var ck = Classify(current);

                // Stop at whitespace or punctuation
                if ((ck & (byte)(CharClass.Whitespace | CharClass.Punct)) != 0)
                    break;

                // Underscore acts as a boundary separator
                if (current == '_')
                {
                    i++;
                    continue;
                }

                // CamelCase transition: lowercase-to-uppercase or digit-to-alpha or alpha-to-digit
                Debug.Assert(i > 0);
                var pk = Classify(text[i - 1]);

                const byte upper = (byte)CharClass.Upper;
                const byte alpha = (byte)(CharClass.Upper | CharClass.Lower);
                const byte digit = (byte)CharClass.Digit;

                var currentUpper = (ck & upper) != 0;
                var prevUpper = (pk & upper) != 0;

                // lowercase/digit followed by uppercase => new token boundary
                // e.g., "configurationValidator" — the 'V' after 'n' triggers a new token
                if (currentUpper && !prevUpper)
                {
                    tokens++;
                    i++;
                    continue;
                }

                // Two uppercase letters followed by a lowercase => the second uppercase starts a new token
                // e.g., "XMLParser" — the 'P' after 'M' with 'L' before triggers a new token
                // We peek ahead to confirm
                if (currentUpper && prevUpper && i + 1 < length && (Classify(text[i + 1]) & (byte)CharClass.Lower) != 0)
                {
                    tokens++;
                    i++;
                    continue;
                }

                // alpha-to-digit or digit-to-alpha transition
                if (((ck & alpha) != 0 && (pk & digit) != 0) || ((ck & digit) != 0 && (pk & alpha) != 0))
                {
                    tokens++;
                    i++;
                    continue;
                }

                i++;
            }
        }

        return tokens;
    }

    /// <summary>
    ///     Estimates the number of LLM tokens for the given text using a BPE-aware heuristic.
    /// </summary>
    /// <param name="text">The input text to estimate tokens for.</param>
    /// <returns>An estimated token count.</returns>
    public static int EstimateTokens(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : EstimateTokens(text.AsSpan());
    }

    // Combined ASCII classification table: one array load + one mask classifies a char
    // into all categories at once, replacing per-char chains of '||' comparisons.
    // Non-ASCII chars (>= 128) classify as None and are handled by the dedicated run path.

    [Flags]
    private enum CharClass : byte
    {
        None = 0,
        Whitespace = 1,
        Punct = 2,
        Upper = 4,
        Lower = 8,
        Digit = 16
    }

    private static readonly byte[] Class = BuildTable();

    private static byte[] BuildTable()
    {
        var t = new byte[128];
        for (var c = 0; c < 128; c++)
        {
            byte k = 0;
            var ch = (char)c;
            if (IsWhitespace(ch)) k |= (byte)CharClass.Whitespace;
            if (IsPunctuation(ch)) k |= (byte)CharClass.Punct;
            if (IsUpper(ch)) k |= (byte)CharClass.Upper;
            if (IsLower(ch)) k |= (byte)CharClass.Lower;
            if (IsDigit(ch)) k |= (byte)CharClass.Digit;
            t[c] = k;
        }

        return t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Classify(char c)
    {
        return c < 128 ? Class[c] : (byte)0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAscii(char c)
    {
        return c < 128;
    }

    // Range-check based char classification for speed (avoids char.IsXyz virtual calls)
    // Retained as the source of truth used to build the combined Class table above.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsUpper(char c)
    {
        return (uint)(c - 'A') <= 'Z' - 'A';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLower(char c)
    {
        return (uint)(c - 'a') <= 'z' - 'a';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDigit(char c)
    {
        return (uint)(c - '0') <= '9' - '0';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAlpha(char c)
    {
        return IsUpper(c) || IsLower(c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsWhitespace(char c)
    {
        return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\v' || c == '\f';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPunctuation(char c)
    {
        // Covers common punctuation and symbols that BPE tends to split on
        // ASCII ranges: 33-47 (!-/), 58-64 (:-@), 91-96 ([-`), 123-126 ({-~)
        return c == '!' || c == '"' || c == '#' || c == '$' || c == '%' || c == '&' ||
               c == '\'' || c == '(' || c == ')' || c == '*' || c == '+' || c == ',' ||
               c == '-' || c == '.' || c == '/' || c == ':' || c == ';' || c == '<' ||
               c == '=' || c == '>' || c == '?' || c == '@' || c == '[' || c == '\\' ||
               c == ']' || c == '^' || c == '`' || c == '{' || c == '|' || c == '}' || c == '~';
    }
}