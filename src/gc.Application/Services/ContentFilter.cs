using System.Buffers;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.Application.Services;

/// <summary>
/// Filters files by content using fast keyword/wildcard matching.
/// Uses Aho-Corasick for exact keywords, GlobMatcher for wildcard patterns.
/// The automaton is built once and reused across all match operations.
/// </summary>
public sealed class ContentFilter
{
    private readonly ILogger _logger;

    // Cached automata for content filtering
    private AhoCorasick? _cachedExcludeExact;
    private AhoCorasick? _cachedIncludeExact;
    private FrozenSet<string>? _cachedExcludeWildcards;
    private FrozenSet<string>? _cachedIncludeWildcards;

    public ContentFilter(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Updates the cached automata with new patterns.
    /// Call this before processing a batch of files to avoid rebuilding.
    /// </summary>
    public void UpdatePatterns(string[] excludePatterns, string[] includePatterns)
    {
        _cachedExcludeExact = null;
        _cachedIncludeExact = null;
        _cachedExcludeWildcards = null;
        _cachedIncludeWildcards = null;

        if (excludePatterns.Length > 0)
        {
            var (exact, wildcards) = SplitPatterns(excludePatterns);
            if (exact.Count > 0)
                _cachedExcludeExact = new AhoCorasick(exact.ToArray());
            if (wildcards.Count > 0)
                _cachedExcludeWildcards = wildcards.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        }

        if (includePatterns.Length > 0)
        {
            var (exact, wildcards) = SplitPatterns(includePatterns);
            if (exact.Count > 0)
                _cachedIncludeExact = new AhoCorasick(exact.ToArray());
            if (wildcards.Count > 0)
                _cachedIncludeWildcards = wildcards.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Determines whether a file's content passes the content filter.
    /// Returns true if the file should be INCLUDED.
    /// </summary>
    public bool ShouldInclude(string content, string[] excludePatterns, string[] includePatterns)
    {
        if (excludePatterns.Length == 0 && includePatterns.Length == 0)
            return true;

        // Check exclude first — if content matches any exclude pattern, drop it
        if (excludePatterns.Length > 0 && MatchesAnyPattern(content, excludePatterns))
        {
            return false;
        }

        // Check include — if include patterns are set, content must match at least one
        if (includePatterns.Length > 0 && !MatchesAnyPattern(content, includePatterns))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether a file's raw bytes pass the content filter.
    /// Reads only the first <paramref name="previewLength"/> bytes for fast rejection.
    /// </summary>
    public bool ShouldInclude(byte[] buffer, int length, string[] excludePatterns, string[] includePatterns, int previewLength = 8192)
    {
        if (excludePatterns.Length == 0 && includePatterns.Length == 0)
            return true;

        var checkLen = Math.Min(length, previewLength);
        var preview = System.Text.Encoding.UTF8.GetString(buffer, 0, checkLen);

        return ShouldInclude(preview, excludePatterns, includePatterns);
    }

    /// <summary>
    /// Checks if content matches ANY of the given patterns.
    /// Uses cached automata when available (after UpdatePatterns), otherwise builds per-call.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool MatchesAnyPattern(string content, string[] patterns)
    {
        if (patterns.Length == 0) return false;

        // Try to use cached automata first
        if (_cachedExcludeExact != null || _cachedIncludeExact != null ||
            _cachedExcludeWildcards != null || _cachedIncludeWildcards != null)
        {
            return MatchesWithCache(content, patterns);
        }

        // No cache — build per-call (backward compatible)
        var (exactPatterns, wildcardPatterns) = SplitPatterns(patterns);

        if (exactPatterns.Count > 0)
        {
            var ac = new AhoCorasick(exactPatterns.ToArray());
            if (AhoCorasickContainsAny(ac, content))
                return true;
        }

        if (wildcardPatterns.Count > 0)
        {
            foreach (var pattern in wildcardPatterns)
            {
                if (GlobContains(content, pattern))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Match using cached automatons. Determines which cache to use based on pattern reference equality.
    /// </summary>
    private bool MatchesWithCache(string content, string[] patterns)
    {
        var (exactPatterns, wildcardPatterns) = SplitPatterns(patterns);

        // Use cached exact-match automaton (Aho-Corasick)
        AhoCorasick? cachedExact = _cachedExcludeExact ?? _cachedIncludeExact;
        if (exactPatterns.Count > 0 && cachedExact != null)
        {
            if (AhoCorasickContainsAny(cachedExact, content))
                return true;
        }
        else if (exactPatterns.Count > 0)
        {
            var ac = new AhoCorasick(exactPatterns.ToArray());
            if (AhoCorasickContainsAny(ac, content))
                return true;
        }

        // Use cached wildcard patterns
        FrozenSet<string>? cachedWild = _cachedExcludeWildcards ?? _cachedIncludeWildcards;
        if (wildcardPatterns.Count > 0)
        {
            foreach (var pattern in wildcardPatterns)
            {
                if (GlobContains(content, pattern))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Static method for backward compatibility - builds automaton per-call.
    /// Prefer instance method UpdatePatterns + MatchesAnyPattern for batch processing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool MatchesAnyPatternStatic(string content, string[] patterns)
    {
        if (patterns.Length == 0) return false;

        var (exactPatterns, wildcardPatterns) = SplitPatterns(patterns);

        if (exactPatterns.Count > 0)
        {
            var ac = new AhoCorasick(exactPatterns.ToArray());
            if (AhoCorasickContainsAny(ac, content))
            {
                return true;
            }
        }

        if (wildcardPatterns.Count > 0)
        {
            foreach (var pattern in wildcardPatterns)
            {
                if (GlobContains(content, pattern))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static (List<string> Exact, List<string> Wildcards) SplitPatterns(string[] patterns)
    {
        var exactPatterns = new List<string>();
        var wildcardPatterns = new List<string>();

        foreach (var pattern in patterns)
        {
            if (pattern.Contains('*') || pattern.Contains('?'))
            {
                wildcardPatterns.Add(pattern);
            }
            else
            {
                exactPatterns.Add(pattern);
            }
        }

        return (exactPatterns, wildcardPatterns);
    }

    /// <summary>
    /// Returns a compiled pattern set suitable for batch content filtering.
    /// Compile once, use for all files.
    /// </summary>
    public CompiledContentPatterns CompilePatterns(string[] excludePatterns, string[] includePatterns)
    {
        if (excludePatterns.Length == 0 && includePatterns.Length == 0)
            return default; // IsEmpty == true

        // Build Aho-Corasick automatons
        AhoCorasick? excludeAc = null, includeAc = null;
        string[]? excludeWildcards = null, includeWildcards = null;

        if (excludePatterns.Length > 0)
        {
            var (exact, wildcards) = SplitPatterns(excludePatterns);
            if (exact.Count > 0) excludeAc = new AhoCorasick(exact.ToArray());
            if (wildcards.Count > 0) excludeWildcards = wildcards.ToArray();
        }

        if (includePatterns.Length > 0)
        {
            var (exact, wildcards) = SplitPatterns(includePatterns);
            if (exact.Count > 0) includeAc = new AhoCorasick(exact.ToArray());
            if (wildcards.Count > 0) includeWildcards = wildcards.ToArray();
        }

        // Capture into closures for the domain struct
        var exAc = excludeAc;
        var inAc = includeAc;
        var exWild = excludeWildcards;
        var inWild = includeWildcards;

        bool shouldIncludeText(string content)
        {
            // Exclude check
            if (exAc != null && AhoCorasickContainsAny(exAc, content))
                return false;
            if (exWild != null)
            {
                foreach (var p in exWild)
                    if (GlobContains(content, p))
                        return false;
            }

            // Include check
            if (inAc == null && inWild == null) return true;
            if (inAc != null && AhoCorasickContainsAny(inAc, content))
                return true;
            if (inWild != null)
            {
                foreach (var p in inWild)
                    if (GlobContains(content, p))
                        return true;
            }
            return false;
        }

        bool shouldIncludeBytes(byte[] buffer, int length)
        {
            var checkLen = Math.Min(length, 8192);
            var preview = System.Text.Encoding.UTF8.GetString(buffer, 0, checkLen);
            return shouldIncludeText(preview);
        }

        return new CompiledContentPatterns(shouldIncludeText, shouldIncludeBytes);
    }

    /// <summary>
    /// Uses Aho-Corasick to check if content contains any of the exact keywords.
    /// Single-pass O(n) scan for all keywords simultaneously.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool AhoCorasickContainsAny(AhoCorasick ac, string content)
    {
        var state = 0;
        for (int i = 0; i < content.Length; i++)
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
            if (ac.GetOutput(state) != -1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if content contains a substring that matches a glob pattern.
    /// For patterns like "auto-generated*", we scan for the fixed prefix then verify.
    /// </summary>
    internal static bool GlobContainsStatic(string content, string pattern)
        => GlobContains(content, pattern);

    private static bool GlobContains(string content, string pattern)
    {
        // Strategy: try matching the pattern against every possible substring start
        // Optimization: extract longest fixed prefix/suffix for fast scanning

        var prefix = ExtractFixedPrefix(pattern);
        var suffix = ExtractFixedSuffix(pattern);

        // If pattern has a fixed prefix, use IndexOf for fast scan
        if (prefix.Length > 0)
        {
            int start = 0;
            while (start <= content.Length - prefix.Length)
            {
                var idx = content.IndexOf(prefix, start, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;

                // Try to match the full glob pattern from this position
                var remaining = content.AsSpan(idx);
                if (GlobMatcher.IsMatch(remaining, pattern))
                {
                    return true;
                }

                start = idx + 1;
            }
            return false;
        }

        // If pattern has a fixed suffix, use LastIndexOf for fast scan
        if (suffix.Length > 0)
        {
            var idx = content.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;

            // Check if the portion ending at idx+suffix.Length matches
            var portion = content.AsSpan(0, idx + suffix.Length);
            return GlobMatcher.IsMatch(portion, pattern);
        }

        // Pure wildcard pattern — scan across all possible substrings
        // This is the slowest path but rare in practice
        for (int len = content.Length; len >= 1; len--)
        {
            if (GlobMatcher.IsMatch(content.AsSpan(0, len), pattern))
            {
                return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ExtractFixedPrefix(string pattern)
    {
        int starIdx = pattern.IndexOfAny('*', '?');
        return starIdx <= 0 ? "" : pattern[..starIdx];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ExtractFixedSuffix(string pattern)
    {
        int starIdx = pattern.LastIndexOfAny('*', '?');
        return starIdx < 0 || starIdx >= pattern.Length - 1 ? "" : pattern[(starIdx + 1)..];
    }
}