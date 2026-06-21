using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClassifyAsciiByte(byte b)
    {
        // Caller guarantees b < 128; Class.Length == 128, so the access is always in range.
        return Unsafe.Add(ref MemoryMarshal.GetReference(Class), b);
    }

    /// <summary>
    ///     Estimates LLM tokens DIRECTLY over UTF-8 bytes, producing a result byte-identical to
    ///     <see cref="EstimateTokens(ReadOnlySpan{char})"/> applied to the decoded text — but with no
    ///     UTF-16 decode and no intermediate char buffer. ASCII bytes (&lt; 0x80) classify exactly as
    ///     their char value would; a fresh non-ASCII run is scored by the UTF-16 code-unit count the
    ///     .NET decoder would emit (via <see cref="Rune.DecodeFromUtf8"/>, which uses the same
    ///     maximal-subpart U+FFFD substitution as UTF8Encoding), and a non-ASCII char absorbed inside
    ///     an ASCII word contributes no token and resets the CamelCase predecessor class to None —
    ///     mirroring the char path where Classify of a non-ASCII char is 0.
    /// </summary>
    public static int EstimateTokensUtf8(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return 0;

        var tokens = 0;
        var i = 0;
        var n = bytes.Length;

        while (i < n)
        {
            var c = bytes[i];

            if (c < 0x80)
            {
                var k = ClassifyAsciiByte(c);

                if ((k & (byte)CharClass.Whitespace) != 0)
                {
                    i++;
                    continue;
                }

                if ((k & (byte)CharClass.Punct) != 0)
                {
                    tokens++;
                    i++;
                    continue;
                }

                // ASCII alphanumeric word: mirror the EstimateTokens(char) inner state machine
                // exactly, tracking the predecessor class explicitly (== Classify(text[i-1])).
                tokens++;
                i++;
                var prevClass = k;

                while (i < n)
                {
                    var cur = bytes[i];

                    if (cur < 0x80)
                    {
                        var ck = ClassifyAsciiByte(cur);

                        if ((ck & (byte)(CharClass.Whitespace | CharClass.Punct)) != 0)
                            break;

                        if (cur == (byte)'_')
                        {
                            i++;
                            prevClass = ck; // Class['_'] == 0, matching Classify('_')
                            continue;
                        }

                        const byte upper = (byte)CharClass.Upper;
                        const byte alpha = (byte)(CharClass.Upper | CharClass.Lower);
                        const byte digit = (byte)CharClass.Digit;

                        var currentUpper = (ck & upper) != 0;
                        var prevUpper = (prevClass & upper) != 0;

                        if (currentUpper && !prevUpper)
                        {
                            tokens++;
                            i++;
                            prevClass = ck;
                            continue;
                        }

                        if (currentUpper && prevUpper && i + 1 < n && bytes[i + 1] < 0x80 &&
                            (ClassifyAsciiByte(bytes[i + 1]) & (byte)CharClass.Lower) != 0)
                        {
                            tokens++;
                            i++;
                            prevClass = ck;
                            continue;
                        }

                        if (((ck & alpha) != 0 && (prevClass & digit) != 0) ||
                            ((ck & digit) != 0 && (prevClass & alpha) != 0))
                        {
                            tokens++;
                            i++;
                            prevClass = ck;
                            continue;
                        }

                        i++;
                        prevClass = ck;
                        continue;
                    }

                    // Non-ASCII char absorbed mid-word: no token, predecessor class becomes None,
                    // and the whole UTF-8 sequence (or maximal invalid subpart) is consumed.
                    Rune.DecodeFromUtf8(bytes[i..], out _, out var midConsumed);
                    i += midConsumed;
                    prevClass = 0;
                }

                continue;
            }

            // Fresh non-ASCII run at a token boundary: sum the UTF-16 code units the decoder would
            // emit, then approximate BPE granularity as ceil(units / 1.5) — identical to the char path.
            var units = 0;
            while (i < n && bytes[i] >= 0x80)
            {
                var status = Rune.DecodeFromUtf8(bytes[i..], out var rune, out var consumed);
                units += status == OperationStatus.Done ? rune.Utf16SequenceLength : 1; // invalid -> one U+FFFD
                i += consumed;
            }

            tokens += (units * 2 + 2) / 3;
        }

        return tokens;
    }

    /// <summary>
    ///     Stateful streaming estimator for content decoded in chunks. Calling
    ///     <see cref="EstimateTokens(ReadOnlySpan{char})"/> once per chunk over-counts whenever a word
    ///     straddles a chunk boundary (each side starts a fresh token run). This carries the trailing
    ///     partial word across chunks and only estimates up to a *safe* boundary — a whitespace or ASCII
    ///     punctuation char, which always breaks a token run on both sides — so the total matches a
    ///     single whole-content estimate exactly.
    /// </summary>
    public struct StreamingTokenEstimator
    {
        // Bound the carried partial word so a pathological boundary-free run cannot grow it without
        // limit; a run longer than this reverts to the harmless per-chunk behaviour (text never has
        // 64K-char tokens, so this valve essentially never fires).
        private const int MaxCarry = 1 << 16;

        private char[]? _carry;
        private int _carryLen;
        private int _tokens;

        public readonly int Tokens => _tokens;

        public void Append(ReadOnlySpan<char> chunk)
        {
            if (chunk.IsEmpty) return;

            // Index of the last safe boundary (whitespace / ASCII punctuation). Everything after it is
            // a partial word that may continue into the next chunk, so it is deferred to the carry.
            var lastBoundary = -1;
            for (var i = chunk.Length - 1; i >= 0; i--)
                if (!IsWordChar(chunk[i]))
                {
                    lastBoundary = i;
                    break;
                }

            if (lastBoundary < 0)
            {
                // Whole chunk is one unbroken run: accumulate and wait for a boundary.
                AppendCarry(chunk);
                if (_carryLen >= MaxCarry) FlushCarry();
                return;
            }

            var head = chunk[..(lastBoundary + 1)];
            if (_carryLen > 0)
            {
                EnsureCarry(_carryLen + head.Length);
                head.CopyTo(_carry!.AsSpan(_carryLen));
                _tokens += EstimateTokens(_carry.AsSpan(0, _carryLen + head.Length));
                _carryLen = 0;
            }
            else
            {
                _tokens += EstimateTokens(head);
            }

            AppendCarry(chunk[(lastBoundary + 1)..]);
        }

        public void Flush() => FlushCarry();

        private void FlushCarry()
        {
            if (_carryLen > 0)
            {
                _tokens += EstimateTokens(_carry!.AsSpan(0, _carryLen));
                _carryLen = 0;
            }
        }

        private void AppendCarry(ReadOnlySpan<char> span)
        {
            if (span.IsEmpty) return;
            EnsureCarry(_carryLen + span.Length);
            span.CopyTo(_carry!.AsSpan(_carryLen));
            _carryLen += span.Length;
        }

        private void EnsureCarry(int needed)
        {
            if (_carry == null)
            {
                _carry = new char[Math.Max(needed, 256)];
            }
            else if (_carry.Length < needed)
            {
                Array.Resize(ref _carry, Math.Max(needed, _carry.Length * 2));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsWordChar(char c)
    {
        // A "word char" is anything that does NOT break a token run: i.e. not whitespace and not
        // ASCII punctuation. Non-ASCII chars are treated as word characters (absorbed into runs).
        if (IsWhitespace(c)) return false;
        return c >= 128 || (Class[c] & (byte)CharClass.Punct) == 0;
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

    // Compile-time classification table, embedded as RVA data in the binary (no static ctor,
    // no startup allocation). Generated to byte-match the Is* classifier helpers below over c in
    // [0,128): Whitespace=1, Punct=2, Upper=4, Lower=8, Digit=16.
    private static ReadOnlySpan<byte> Class =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 2, 2, 2, 2, 2, 2,
        2, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
        4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 2, 2, 2, 2, 0,
        2, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 2, 2, 2, 2, 0,
    ];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Classify(char c)
    {
        // c is provably in [0,128) inside the branch and Class.Length == 128, so the bounds
        // check is redundant; elide it with a direct ref read into the RVA table.
        return c < 128
            ? Unsafe.Add(ref MemoryMarshal.GetReference(Class), c)
            : (byte)0;
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