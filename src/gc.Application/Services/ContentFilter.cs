using System.Runtime.CompilerServices;
using System.Text;
using gc.Domain.Interfaces;

namespace gc.Application.Services;

/// <summary>
///     Filters files by content using fast keyword matching.
///     Uses Aho-Corasick for exact keywords.
///     The automaton is built once and reused across all match operations.
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

        // Build Aho-Corasick automatons
        AhoCorasick? excludeAc = null, includeAc = null;

        if (excludePatterns.Length > 0) excludeAc = new AhoCorasick(excludePatterns);

        if (includePatterns.Length > 0) includeAc = new AhoCorasick(includePatterns);

        // Capture into closures for the domain struct
        var exAc = excludeAc;
        var inAc = includeAc;

        // A null automaton means "no patterns of this kind" -> treat as ASCII-safe.
        var excludeAscii = exAc?.IsAsciiOnly ?? true;
        var includeAscii = inAc?.IsAsciiOnly ?? true;

        bool shouldIncludeText(string content)
        {
            // Exclude check
            if (exAc != null && AhoCorasickContainsAny(exAc, content))
                return false;

            // Include check
            if (inAc == null) return true;
            if (AhoCorasickContainsAny(inAc, content))
                return true;

            return false;
        }

        bool shouldIncludeBytes(byte[] buffer, int length)
        {
            // Allocation-free fast path: when every pattern char is ASCII, each ASCII
            // byte equals its UTF-16 char, so we can scan the raw bytes directly.
            if (excludeAscii && includeAscii)
            {
                var span = buffer.AsSpan(0, length);
                if (exAc != null && AhoCorasickContainsAnyAscii(exAc, span))
                    return false;
                if (inAc == null) return true;
                return AhoCorasickContainsAnyAscii(inAc, span);
            }

            // Fallback preserves exact behavior for non-ASCII patterns.
            return shouldIncludeText(Encoding.UTF8.GetString(buffer, 0, length));
        }

        return new CompiledContentPatterns(shouldIncludeText, shouldIncludeBytes);
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