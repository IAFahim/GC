using System.Runtime.CompilerServices;

namespace gc.Application.Services;

/// <summary>
///     High-performance glob pattern matcher with wildcard support.
///     Supports: * (any chars except /), ? (single char), ** (any chars including /).
///     Uses backtracking simulation with O(n*m) worst case, optimized for common cases.
///     Includes a backtracking budget to prevent exponential blowup on adversarial patterns.
/// </summary>
public static class GlobMatcher
{
    // Maximum backtracking iterations to prevent O(2^n) exponential blowup.
    // Pattern *a*a*a*a*b against a string of a's would normally be O(2^n).
    // After this budget is exhausted, the match fails rather than hang.
    private const int DefaultMaxBacktrackIterations = 1_000_000;

    /// <summary>
    ///     Check if a path matches a glob pattern.
    ///     * matches any sequence of characters EXCEPT path separators (/)
    ///     ** matches any sequence INCLUDING path separators
    ///     ? matches exactly one character (except /)
    ///     Comparison is case-insensitive.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsMatch(ReadOnlySpan<char> path, ReadOnlySpan<char> pattern,
        int maxBacktrackIterations = DefaultMaxBacktrackIterations)
    {
        var budget = maxBacktrackIterations;
        return IsMatchInternal(path, pattern, ref budget);
    }

    // Single backtracking budget threaded through the whole match — the segment loop, the "**/"
    // recursion, and the final MatchInternal all share and decrement one counter, so total work is
    // capped once rather than reset to the full budget per segment/branch (the documented DoS bound).
    private static bool IsMatchInternal(ReadOnlySpan<char> path, ReadOnlySpan<char> pattern, ref int budget)
    {
        if (pattern.IsEmpty) return path.IsEmpty;

        // No wildcards at all
        if (!pattern.ContainsAny('*', '?'))
        {
            // 1. Suffix/Extension matching (e.g. ".bin")
            if (pattern[0] == '.')
            {
                var lastSep = path.LastIndexOfAny('/', '\\');
                var fileName = lastSep >= 0 ? path[(lastSep + 1)..] : path;
                if (fileName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Check if pattern has internal slash (excluding trailing slash)
            var patternTrimmed = pattern;
            if (!patternTrimmed.IsEmpty && (patternTrimmed[^1] == '/' || patternTrimmed[^1] == '\\'))
            {
                patternTrimmed = patternTrimmed[..^1];
            }
            var hasSlashInternal = patternTrimmed.ContainsAny('/', '\\');

            // 2. Anchored path matching (e.g. "src/generated" or "src/temp/")
            if (hasSlashInternal)
            {
                var endsWithSlash = pattern[^1] == '/' || pattern[^1] == '\\';
                if (endsWithSlash)
                {
                    if (PathStartsWith(path, pattern))
                        return true;
                }
                else
                {
                    if (PathEquals(path, pattern))
                        return true;
                    if (path.Length > pattern.Length && (path[pattern.Length] == '/' || path[pattern.Length] == '\\') && PathStartsWith(path, pattern))
                        return true;
                }
                return false;
            }

            // 3. Segment matching (e.g. "generated" or "temp/")
            var searchPattern = pattern;
            if (!searchPattern.IsEmpty && (searchPattern[^1] == '/' || searchPattern[^1] == '\\'))
            {
                searchPattern = searchPattern[..^1];
            }
            var isDirOnly = pattern[^1] == '/' || pattern[^1] == '\\';

            var tempPath = path;
            while (true)
            {
                var slashIdx = tempPath.IndexOfAny('/', '\\');
                var segment = slashIdx >= 0 ? tempPath.Slice(0, slashIdx) : tempPath;
                var isDirectorySegment = slashIdx >= 0;

                if (isDirOnly)
                {
                    if (isDirectorySegment && PathEquals(segment, searchPattern))
                        return true;
                }
                else
                {
                    if (PathEquals(segment, searchPattern))
                        return true;
                }

                if (slashIdx < 0) break;
                tempPath = tempPath.Slice(slashIdx + 1);
            }

            return false;
        }

        // Pure ** pattern matches everything
        if (pattern.Length == 2 && pattern[0] == '*' && pattern[1] == '*') return true;

        // Check for pure * (single star)
        if (pattern.Length == 1 && pattern[0] == '*') return true;

        // Fast path for *.extension
        if (pattern.Length >= 2 && pattern[0] == '*' && pattern[1] == '.' && !pattern.Contains('/'))
        {
            var extSpan = pattern.Slice(2);
            if (!extSpan.ContainsAny('*', '?'))
                if (path.EndsWith(pattern.Slice(1), StringComparison.OrdinalIgnoreCase))
                    return true;
            // If it doesn't end with the extension, it might still match a directory name
            // so we fall through to MatchInternal.
        }

        // Fast path for directory prefix: prefix/*
        if (pattern.Length >= 2 && pattern[^1] == '*' && pattern[^2] == '/')
        {
            var prefixSpan = pattern.Slice(0, pattern.Length - 2);
            if (!prefixSpan.ContainsAny('*', '?'))
                if (path.StartsWith(prefixSpan, StringComparison.OrdinalIgnoreCase) &&
                    path.Length > prefixSpan.Length &&
                    path[prefixSpan.Length] == '/')
                {
                    var remainder = path.Slice(prefixSpan.Length + 1);
                    if (!remainder.Contains('/'))
                        return true;
                }
        }

        // If the pattern has no '/', it can match against either the entire path or any segment (directory or filename)
        if (!pattern.Contains('/'))
        {
            var tempPath = path;
            while (true)
            {
                var slashIdx = tempPath.IndexOf('/');
                var segment = slashIdx >= 0 ? tempPath.Slice(0, slashIdx) : tempPath;

                if (MatchInternal(segment, pattern, ref budget))
                    return true;

                if (slashIdx < 0) break;
                tempPath = tempPath.Slice(slashIdx + 1);
            }

            return false;
        }

        // If the pattern starts with "**/", it can match starting after the "**/".
        // E.g., "**/boost/**" can match "boost/algorithm.hpp".
        if (pattern.Length >= 3 && pattern[0] == '*' && pattern[1] == '*' && pattern[2] == '/')
        {
            var remaining = pattern.Slice(3);
            if (IsMatchInternal(path, remaining, ref budget))
                return true;
        }

        return MatchInternal(path, pattern, ref budget);
    }

    /// <summary>
    ///     Check if a path matches ANY of the given patterns.
    ///     Short-circuits on first match.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool MatchesAny(string path, string[] patterns)
    {
        if (patterns.Length == 0) return false;

        var pathSpan = path.AsSpan();
        foreach (var pattern in patterns)
            if (IsMatch(pathSpan, pattern.AsSpan()))
                return true;
        return false;
    }

    /// <summary>
    ///     Core backtracking glob matching algorithm with backtrack budget.
    ///     Returns false if the backtracking budget is exhausted to prevent DoS.
    /// </summary>
    private static bool MatchInternal(ReadOnlySpan<char> path, ReadOnlySpan<char> pattern, ref int remainingIterations)
    {
        var pathIdx = 0;
        var patIdx = 0;

        while (patIdx < pattern.Length)
        {
            // Check budget before each major operation
            if (--remainingIterations <= 0) return false; // Budget exhausted - prevent exponential blowup

            var patChar = pattern[patIdx];

            if (patChar == '*')
            {
                // Check if it is a double star '**'
                var isDoubleStar = patIdx + 1 < pattern.Length && pattern[patIdx + 1] == '*';
                var starLen = isDoubleStar ? 2 : 1;

                // Merge consecutive stars
                while (patIdx + starLen < pattern.Length && pattern[patIdx + starLen] == '*')
                    if (patIdx + starLen + 1 < pattern.Length && pattern[patIdx + starLen + 1] == '*')
                    {
                        isDoubleStar = true;
                        starLen += 2;
                    }
                    else
                    {
                        starLen += 1;
                    }

                var nextPattern = pattern.Slice(patIdx + starLen);

                // If this is the end of the pattern
                if (nextPattern.IsEmpty)
                {
                    if (isDoubleStar) return true;

                    // Single star at end: must not contain '/' in the remaining path
                    return !path.Slice(pathIdx).Contains('/');
                }

                // Optimization: if next character in pattern is not a wildcard, we can scan for it
                var nextChar = nextPattern[0];
                var nextIsWildcard = nextChar == '*' || nextChar == '?';
                var nextLower = !nextIsWildcard ? char.ToLowerInvariant(nextChar) : '\0';

                for (var i = pathIdx; i <= path.Length; i++)
                {
                    // Check budget for each iteration
                    if (--remainingIterations <= 0) return false;

                    // If it is a single star, it cannot cross '/'
                    if (!isDoubleStar && i > pathIdx && path[i - 1] == '/') break;

                    // Optimization: if next char is not wildcard, skip paths that don't match nextChar
                    if (!nextIsWildcard && i < path.Length)
                        if (char.ToLowerInvariant(path[i]) != nextLower)
                            continue;

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

            if (patChar == '?')
            {
                if (pathIdx >= path.Length || path[pathIdx] == '/') return false;
                pathIdx++;
                patIdx++;
            }
            else
            {
                if (pathIdx >= path.Length) return false;

                var pc = patChar;
                var sc = path[pathIdx];

                if (char.ToLowerInvariant(pc) != char.ToLowerInvariant(sc)) return false;

                pathIdx++;
                patIdx++;
            }
        }

        return pathIdx == path.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PathEquals(ReadOnlySpan<char> path, ReadOnlySpan<char> pattern)
    {
        if (path.Length != pattern.Length) return false;
        for (var i = 0; i < path.Length; i++)
        {
            var pc = pattern[i];
            var sc = path[i];
            if (pc == '\\') pc = '/';
            if (sc == '\\') sc = '/';
            if (char.ToLowerInvariant(pc) != char.ToLowerInvariant(sc)) return false;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PathStartsWith(ReadOnlySpan<char> path, ReadOnlySpan<char> pattern)
    {
        if (path.Length < pattern.Length) return false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var pc = pattern[i];
            var sc = path[i];
            if (pc == '\\') pc = '/';
            if (sc == '\\') sc = '/';
            if (char.ToLowerInvariant(pc) != char.ToLowerInvariant(sc)) return false;
        }
        return true;
    }
}