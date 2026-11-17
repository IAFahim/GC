using System.Runtime.CompilerServices;
using System.Text;
using gc.Domain.Interfaces;

namespace gc.Application.Services;

/// <summary>
///     Filters files by content using fast keyword matching.
///     Plain keywords use Aho-Corasick; a keyword containing * (any text) or ? (any one character)
///     is matched as a wildcard pattern. The automaton is built once and reused across all files.
/// </summary>
public sealed class ContentFilter
{
    private readonly ILogger _logger;

    public ContentFilter(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Returns a compiled pattern set suitable for batch content filtering.
    ///     Compile once, use for all files.
    /// </summary>
    public CompiledContentPatterns CompilePatterns(string[] excludePatterns, string[] includePatterns)
    {
        if (excludePatterns.Length == 0 && includePatterns.Length == 0)
            return default; // IsEmpty == true

        var (excludeLiterals, excludeWildcards) = SplitWildcards(excludePatterns);
        var (includeLiterals, includeWildcards) = SplitWildcards(includePatterns);

        // Build Aho-Corasick automatons for the plain keywords
        AhoCorasick? exAc = excludeLiterals.Length > 0 ? new AhoCorasick(excludeLiterals) : null;
        AhoCorasick? inAc = includeLiterals.Length > 0 ? new AhoCorasick(includeLiterals) : null;
        var hasInclude = includePatterns.Length > 0;
        var hasWildcards = excludeWildcards.Length > 0 || includeWildcards.Length > 0;

        // A null automaton means "no patterns of this kind" -> treat as ASCII-safe.
        var excludeAscii = exAc?.IsAsciiOnly ?? true;
        var includeAscii = inAc?.IsAsciiOnly ?? true;

        bool shouldIncludeText(string content)
        {
            // Exclude check
            if (exAc != null && AhoCorasickContainsAny(exAc, content)) return false;
            if (MatchesAny(excludeWildcards, content)) return false;

            // Include check
            if (!hasInclude) return true;
            if (inAc != null && AhoCorasickContainsAny(inAc, content)) return true;
            return MatchesAny(includeWildcards, content);
        }

        bool shouldIncludeBytes(byte[] buffer, int length)
        {
            // Allocation-free fast path: when every pattern char is ASCII (and none is a wildcard),
            // each ASCII byte equals its UTF-16 char, so we can scan the raw bytes directly.
            if (!hasWildcards && excludeAscii && includeAscii)
            {
                var span = buffer.AsSpan(0, length);
                if (exAc != null && AhoCorasickContainsAnyAscii(exAc, span))
                    return false;
                if (inAc == null) return true;
                return AhoCorasickContainsAnyAscii(inAc, span);
            }

            // Wildcards and non-ASCII patterns need the decoded text.
            return shouldIncludeText(Encoding.UTF8.GetString(buffer, 0, length));
        }

        return new CompiledContentPatterns(shouldIncludeText, shouldIncludeBytes);
    }

    /// <summary>Combines patterns from the configuration file with the ones typed on the command line.</summary>
    public static string[] Merge(string[]? fromConfig, string[]? fromCommandLine)
    {
        if ((fromConfig == null || fromConfig.Length == 0) && (fromCommandLine == null || fromCommandLine.Length == 0))
            return [];
        return [.. fromConfig ?? [], .. fromCommandLine ?? []];
    }

    private static (string[] Literals, WildcardPattern[] Wildcards) SplitWildcards(string[] patterns)
    {
        var literals = new List<string>();
        var wildcards = new List<WildcardPattern>();
        foreach (var pattern in patterns)
        {
            if (pattern.AsSpan().IndexOfAny('*', '?') < 0) literals.Add(pattern);
            else wildcards.Add(new WildcardPattern(pattern));
        }

        return (literals.ToArray(), wildcards.ToArray());
    }

    private static bool MatchesAny(WildcardPattern[] patterns, string content)
    {
        foreach (var pattern in patterns)
            if (pattern.IsContainedIn(content))
                return true;
        return false;
    }

    /// <summary>
    ///     Uses Aho-Corasick to check if content contains any of the exact keywords.
    ///     Single-pass O(n) scan for all keywords simultaneously.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool AhoCorasickContainsAny(AhoCorasick ac, string content)
    {
        var state = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (!ac.TryGetCharIndex(content[i], out var ci))
            {
                state = 0;
                continue;
            }

            // Single lookup into the delta automaton — no fail-link walk.
            state = ac.Next(state, ci);
            if (ac.GetOutput(state) != -1) return true;
        }

        return false;
    }

    /// <summary>
    ///     ASCII-only variant of <see cref="AhoCorasickContainsAny" /> that scans raw bytes
    ///     directly, avoiding a per-file string allocation. Safe ONLY when every pattern char
    ///     is ASCII: any byte >= 128 is a UTF-8 lead/continuation byte that no ASCII pattern
    ///     can match, so it is treated as a non-matching boundary (state reset).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool AhoCorasickContainsAnyAscii(AhoCorasick ac, ReadOnlySpan<byte> bytes)
    {
        var state = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            if (b >= 128 || !ac.TryGetCharIndex((char)b, out var ci))
            {
                state = 0;
                continue;
            }

            // Single lookup into the delta automaton — no fail-link walk.
            state = ac.Next(state, ci);
            if (ac.GetOutput(state) != -1) return true;
        }

        return false;
    }
}

/// <summary>
///     A "contains" wildcard: <c>*</c> is any text (including line breaks), <c>?</c> any one character.
///     Hand-written (no Regex) so it is trimming/AOT-safe and never backtracks catastrophically:
///     the text between stars is found left to right, taking the earliest match each time.
/// </summary>
internal sealed class WildcardPattern
{
    private readonly string[] _segments;

    public WildcardPattern(string pattern)
    {
        _segments = pattern.Split('*', StringSplitOptions.RemoveEmptyEntries);
    }

    public bool IsContainedIn(string content)
    {
        var position = 0;
        foreach (var segment in _segments)
        {
            var found = IndexOf(content, segment, position);
            if (found < 0) return false;
            position = found + segment.Length;
        }

        return true;
    }

    private static int IndexOf(string text, string segment, int start)
    {
        if (segment.AsSpan().IndexOf('?') < 0) return text.IndexOf(segment, start, StringComparison.Ordinal);
        for (var i = start; i + segment.Length <= text.Length; i++)
        {
            var matches = true;
            for (var k = 0; k < segment.Length; k++)
                if (segment[k] != '?' && segment[k] != text[i + k])
                {
                    matches = false;
                    break;
                }

            if (matches) return i;
        }

        return -1;
    }
}
