using System.Runtime.CompilerServices;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.Application.Services;

/// <summary>
/// Filters files by content using fast keyword/wildcard matching.
/// Uses Aho-Corasick for exact keywords, GlobMatcher for wildcard patterns.
/// Applies AFTER file reading, BEFORE markdown generation.
/// </summary>
public sealed class ContentFilter
{
    private readonly ILogger _logger;

    public ContentFilter(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines whether a file's content passes the content filter.
    /// Returns true if the file should be INCLUDED.
    /// </summary>
    /// <param name="content">The file content (first N bytes is sufficient for performance)</param>
    /// <param name="excludePatterns">Exclude files whose content matches any of these patterns</param>
    /// <param name="includePatterns">When set, ONLY include files whose content matches at least one pattern</param>
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
    /// Separates exact keywords (Aho-Corasick) from wildcard patterns (GlobMatcher).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool MatchesAnyPattern(string content, string[] patterns)
    {
        if (patterns.Length == 0) return false;

        // Fast path: check if any pattern is exact (no wildcards)
        // Use Aho-Corasick for exact patterns, GlobMatcher for wildcards
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

        // Aho-Corasick for exact keyword search — O(n) for all keywords simultaneously
        if (exactPatterns.Count > 0)
        {
            var ac = new AhoCorasick(exactPatterns.ToArray());
            var dummy = new string[exactPatterns.Count];
            Array.Fill(dummy, "");

            // Use ReplaceAll but we just need to know if ANY match exists
            // For performance, we use a simpler approach: scan with Aho-Corasick automaton
            if (AhoCorasickContainsAny(content, exactPatterns.ToArray()))
            {
                return true;
            }
        }

        // Wildcard patterns — GlobMatcher line-by-line for speed
        if (wildcardPatterns.Count > 0)
        {
            // For wildcard matching, we do a substring search with glob support
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

    /// <summary>
    /// Uses Aho-Corasick to check if content contains any of the exact keywords.
    /// Single-pass O(n) scan for all keywords simultaneously.
    /// </summary>
    private static bool AhoCorasickContainsAny(string content, string[] keywords)
    {
        var ac = new AhoCorasick(keywords);

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
