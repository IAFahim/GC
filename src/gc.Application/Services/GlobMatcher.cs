using System.Runtime.CompilerServices;

namespace gc.Application.Services;

/// <summary>
/// High-performance glob pattern matcher with wildcard support.
/// Supports: * (any chars except /), ? (single char), ** (any chars including /).
/// Uses backtracking simulation with O(n*m) worst case, optimized for common cases.
/// Includes a backtracking budget to prevent exponential blowup on adversarial patterns.
/// </summary>
public static class GlobMatcher
{
    // Maximum backtracking iterations to prevent O(2^n) exponential blowup.
    // Pattern *a*a*a*a*b against a string of a's would normally be O(2^n).
    // After this budget is exhausted, the match fails rather than hang.
    private const int DefaultMaxBacktrackIterations = 1_000_000;

    /// <summary>
    /// Check if a path matches a glob pattern.
    /// * matches any sequence of characters EXCEPT path separators (/)
    /// ** matches any sequence INCLUDING path separators
    /// ? matches exactly one character (except /)
    /// Comparison is case-insensitive.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsMatch(ReadOnlySpan<char> path, ReadOnlySpan<char> pattern, int maxBacktrackIterations = DefaultMaxBacktrackIterations)
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

        // If the pattern has no '/', it can match against either the entire path or the filename
        if (!pattern.Contains('/'))
        {
            if (MatchInternal(path, pattern, ref maxBacktrackIterations))
                return true;

            int lastSlash = path.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                var remaining = path.Slice(lastSlash + 1);
                var budget = maxBacktrackIterations;
                if (MatchInternal(remaining, pattern, ref budget))
                    return true;
            }

            return false;
        }

        // If the pattern starts with "**/", it can match starting after the "**/".
        // E.g., "**/boost/**" can match "boost/algorithm.hpp".
        if (pattern.Length >= 3 && pattern[0] == '*' && pattern[1] == '*' && pattern[2] == '/')
        {
            var remaining = pattern.Slice(3);
            var budget = maxBacktrackIterations;
            if (IsMatch(path, remaining, budget))
                return true;
        }

        var finalBudget = maxBacktrackIterations;
        return MatchInternal(path, pattern, ref finalBudget);
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
    /// Core backtracking glob matching algorithm with backtrack budget.
    /// Returns false if the backtracking budget is exhausted to prevent DoS.
    /// </summary>
    private static bool MatchInternal(ReadOnlySpan<char> path, ReadOnlySpan<char> pattern, ref int remainingIterations)
    {
        int pathIdx = 0;
        int patIdx = 0;

        while (patIdx < pattern.Length)
        {
            // Check budget before each major operation
            if (--remainingIterations <= 0)
            {
                return false; // Budget exhausted - prevent exponential blowup
            }

            char patChar = pattern[patIdx];

            if (patChar == '*')
            {
                // Check if it is a double star '**'
                bool isDoubleStar = (patIdx + 1 < pattern.Length && pattern[patIdx + 1] == '*');
                int starLen = isDoubleStar ? 2 : 1;

                // Merge consecutive stars
                while (patIdx + starLen < pattern.Length && pattern[patIdx + starLen] == '*')
                {
                    if (patIdx + starLen + 1 < pattern.Length && pattern[patIdx + starLen + 1] == '*')
                    {
                        isDoubleStar = true;
                        starLen += 2;
                    }
                    else
                    {
                        starLen += 1;
                    }
                }

                ReadOnlySpan<char> nextPattern = pattern.Slice(patIdx + starLen);

                // If this is the end of the pattern
                if (nextPattern.IsEmpty)
                {
                    if (isDoubleStar)
                    {
                        return true;
                    }
                    else
                    {
                        // Single star at end: must not contain '/' in the remaining path
                        return !path.Slice(pathIdx).Contains('/');
                    }
                }

                // Optimization: if next character in pattern is not a wildcard, we can scan for it
                char nextChar = nextPattern[0];
                bool nextIsWildcard = (nextChar == '*' || nextChar == '?');

                for (int i = pathIdx; i <= path.Length; i++)
                {
                    // Check budget for each iteration
                    if (--remainingIterations <= 0)
                    {
                        return false;
                    }

                    // If it is a single star, it cannot cross '/'
                    if (!isDoubleStar && i > pathIdx && path[i - 1] == '/')
                    {
                        break;
                    }

                    // Optimization: if next char is not wildcard, skip paths that don't match nextChar
                    if (!nextIsWildcard && i < path.Length)
                    {
                        if (char.ToLowerInvariant(path[i]) != char.ToLowerInvariant(nextChar))
                        {
                            continue;
                        }
                    }

                    var remainingPath = path.Slice(i);
                    var budget = remainingIterations;
                    if (MatchInternal(remainingPath, nextPattern, ref budget))
                    {
                        remainingIterations = budget;
                        return true;
                    }
                    remainingIterations = budget;
                }

                return false;
            }
            else if (patChar == '?')
            {
                if (pathIdx >= path.Length || path[pathIdx] == '/')
                {
                    return false;
                }
                pathIdx++;
                patIdx++;
            }
            else
            {
                if (pathIdx >= path.Length)
                {
                    return false;
                }

                char pc = patChar;
                char sc = path[pathIdx];

                if (char.ToLowerInvariant(pc) != char.ToLowerInvariant(sc))
                {
                    return false;
                }

                pathIdx++;
                patIdx++;
            }
        }

        return pathIdx == path.Length;
    }
}