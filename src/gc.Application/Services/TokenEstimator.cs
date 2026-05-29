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

            // Whitespace: skip, acts as a boundary separator
            if (IsWhitespace(c))
            {
                i++;
                continue;
            }

            // Punctuation: each punctuation character is its own token (e.g., '<', '>', ',', '.', '(', ')')
            if (IsPunctuation(c))
            {
                tokens++;
                i++;
                continue;
            }

            // Alphanumeric word: consume a run of alphanumeric chars, counting CamelCase transitions
            tokens++;
            i++;

            while (i < length)
            {
                var current = text[i];

                // Stop at whitespace or punctuation
                if (IsWhitespace(current) || IsPunctuation(current))
                    break;

                // Underscore acts as a boundary separator
                if (current == '_')
                {
                    i++;
                    continue;
                }

                // CamelCase transition: lowercase-to-uppercase or digit-to-alpha or alpha-to-digit
                if (i > 0)
                {
                    var prev = text[i - 1];

                    // lowercase/digit followed by uppercase => new token boundary
                    // e.g., "configurationValidator" — the 'V' after 'n' triggers a new token
                    if (IsUpper(current) && !IsUpper(prev))
                    {
                        tokens++;
                        i++;
                        continue;
                    }

                    // Two uppercase letters followed by a lowercase => the second uppercase starts a new token
                    // e.g., "XMLParser" — the 'P' after 'M' with 'L' before triggers a new token
                    // We peek ahead to confirm
                    if (IsUpper(current) && IsUpper(prev) && i + 1 < length && IsLower(text[i + 1]))
                    {
                        tokens++;
                        i++;
                        continue;
                    }

                    // alpha-to-digit or digit-to-alpha transition
                    if ((IsAlpha(current) && IsDigit(prev)) || (IsDigit(current) && IsAlpha(prev)))
                    {
                        tokens++;
                        i++;
                        continue;
                    }
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

    // Range-check based char classification for speed (avoids char.IsXyz virtual calls)

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