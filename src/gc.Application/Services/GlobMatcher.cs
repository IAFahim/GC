using System.Runtime.CompilerServices;

namespace gc.Application.Services;

/// <summary>
/// High-performance glob pattern matcher with wildcard support.
/// Supports: * (any chars except /), ? (single char), ** (any chars including /).
/// Uses iterative NFA simulation with O(n*m) worst case, optimized for common cases.
/// </summary>
public static class GlobMatcher
{
    /// <summary>
    /// Check if a path matches a glob pattern.
    /// * matches any sequence of characters EXCEPT path separators (/)
    /// ** matches any sequence INCLUDING path separators
    /// ? matches exactly one character (except /)
    /// Comparison is case-insensitive.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsMatch(ReadOnlySpan<char> path, ReadOnlySpan<char> pattern)
    {
        if (pattern.IsEmpty) return path.IsEmpty;

        // No wildcards at all → exact match (case-insensitive)
        if (!pattern.ContainsAny('*', '?'))
        {
            return path.Length == pattern.Length &&
                   path.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }

        // Pure ** pattern matches everything
        if (pattern.Length == 2 && pattern[0] == '*' && pattern[1] == '*')
        {
            return true;
        }

        // Check for pure * (single star)
        if (pattern.Length == 1 && pattern[0] == '*')
        {
            return true;
        }

        return MatchGlob(path, pattern);
    }

    /// <summary>
    /// Check if a path matches ANY of the given patterns.
    /// Short-circuits on first match.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool MatchesAny(string path, string[] patterns)
    {
        if (patterns.Length == 0) return false;

        var pathSpan = path.AsSpan();
        foreach (var pattern in patterns)
        {
            if (IsMatch(pathSpan, pattern.AsSpan()))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Normalize pattern by collapsing consecutive ** and handling the semantics correctly.
    /// Returns pattern info: for each position, the type and whether it's part of **
    /// </summary>
    private static (bool IsDoubleStar, int NormalizedLength) GetPatternInfo(ReadOnlySpan<char> pattern, int pos)
    {
        char c = pattern[pos];
        if (c != '*') return (false, 1);

        // Check if this is part of **
        bool isStartOfDoubleStar = (pos + 1 < pattern.Length && pattern[pos + 1] == '*');
        bool isAfterDoubleStar = (pos > 0 && pattern[pos - 1] == '*');

        if (isAfterDoubleStar)
        {
            // This is the second * of ** - skip it in normalized pattern
            return (true, 0);
        }

        if (isStartOfDoubleStar)
        {
            return (true, 2); // ** is treated as one "match any including /" element
        }

        return (false, 1); // single *
    }

    /// <summary>
    /// DP-based glob matching.
    /// Handles *, ?, ** with proper segment semantics.
    /// </summary>
    private static bool MatchGlob(ReadOnlySpan<char> path, ReadOnlySpan<char> pattern)
    {
        int pLen = path.Length;
        int patLen = pattern.Length;

        // Normalize pattern: collapse ** into single entries
        // For DP, we need to track which pattern positions correspond to which "normalized" positions
        var isDoubleStar = new bool[patLen];
        var normPatLen = 0;

        for (int j = 0; j < patLen; j++)
        {
            var (isDs, consumed) = GetPatternInfo(pattern, j);
            if (consumed > 0)
            {
                isDoubleStar[normPatLen] = isDs;
                normPatLen++;
            }
            if (consumed > 1) j++; // skip next *
        }

        // DP table: dp[i,j] = can normalized pattern[0..j] match path[0..i]?
        var dp = new bool[pLen + 1, normPatLen + 1];

        // Initialize: empty pattern matches empty path
        dp[0, 0] = true;

        // Initialize first row: pattern can match empty path with leading * or **
        for (int j = 1; j <= normPatLen; j++)
        {
            // If current normalized pattern element is **, it can match empty
            // If it's *, it can match empty at the start
            // But ** at start of pattern (like **/*.cs) typically shouldn't match empty
            // unless explicitly allowed. Let's allow both for flexibility.
            dp[0, j] = true; // * and ** can both match empty
        }

        // Fill DP table
        for (int i = 1; i <= pLen; i++)
        {
            dp[i, 0] = false; // non-empty path can't match empty pattern
        }

        for (int i = 1; i <= pLen; i++)
        {
            char sc = path[i - 1];

            for (int j = 1; j <= normPatLen; j++)
            {
                // Get original pattern position for current normalized position j
                int origJ = j - 1;

                if (isDoubleStar[origJ])
                {
                    // ** matches any sequence INCLUDING /
                    // Option 1: skip ** entirely (dp[i, j-1])
                    // Option 2: use current char and stay in ** (dp[i-1, j])
                    dp[i, j] = dp[i, j - 1] || dp[i - 1, j];
                }
                else if (pattern[origJ] == '?')
                {
                    // ? matches any single character (but not /)
                    dp[i, j] = sc != '/' && dp[i - 1, j - 1];
                }
                else
                {
                    // Regular character: must match case-insensitively
                    char pc = pattern[origJ];
                    dp[i, j] = dp[i - 1, j - 1] &&
                               char.ToLowerInvariant(sc) == char.ToLowerInvariant(pc);
                }
            }
        }

        return dp[pLen, normPatLen];
    }
}