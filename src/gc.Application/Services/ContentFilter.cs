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
            var checkLen = Math.Min(length, 8192);
            var preview = Encoding.UTF8.GetString(buffer, 0, checkLen);
            return shouldIncludeText(preview);
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

            while (true)
            {
                var next = ac.GetGoto(state, ci);
                if (next != -1)
                {
                    state = next;
                    break;
                }

                if (state == 0) break;
                state = ac.GetFail(state);
            }

            // Check output chain
            if (ac.GetOutput(state) != -1) return true;
        }

        return false;
    }
}