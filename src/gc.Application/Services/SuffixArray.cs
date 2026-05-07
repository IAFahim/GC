using System.Runtime.CompilerServices;
using System.Text;

namespace gc.Application.Services;

/// <summary>
/// Suffix Array + LCP array for finding longest repeated substrings (phrases).
/// O(N log N) construction via prefix-doubling with radix sort.
/// Used by DynamicCompressor for phrase-level compression.
/// </summary>
public static class SuffixArray
{
    /// <summary>
    /// Finds repeated substrings of minimum length that appear at least minFrequency times.
    /// Returns candidates sorted by savings (length * frequency) descending.
    /// </summary>
    public static List<PhraseCandidate> FindRepeatedPhrases(
        string text,
        int minPhraseLength = 6,
        int maxPhraseLength = 80,
        int minFrequency = 2,
        int maxCandidates = 50)
    {
        if (text.Length < minPhraseLength * 2)
            return [];

        // Build suffix array
        var sa = Build(text);
        var n = sa.Length;

        // Build LCP array using Kasai's algorithm
        var rank = new int[n];
        for (var i = 0; i < n; i++) rank[sa[i]] = i;

        var lcp = new int[n];
        var h = 0;
        for (var i = 0; i < n; i++)
        {
            if (rank[i] > 0)
            {
                var j = sa[rank[i] - 1];
                while (i + h < n && j + h < n && text[i + h] == text[j + h])
                    h++;
                lcp[rank[i]] = h;
                if (h > 0) h--;
            }
        }

        // Extract candidate phrases from LCP peaks
        var seen = new HashSet<string>();
        var candidates = new List<PhraseCandidate>();

        for (var i = 1; i < n; i++)
        {
            if (lcp[i] < minPhraseLength) continue;

            var phraseLen = Math.Min(lcp[i], maxPhraseLength);
            var phrase = text.Substring(sa[i], phraseLen);

            // Clean up: trim to word boundaries
            phrase = TrimToWordBoundary(phrase, text, sa[i]);
            if (phrase.Length < minPhraseLength) continue;

            // Skip phrases that are mostly whitespace or punctuation
            if (!IsUsefulPhrase(phrase)) continue;

            if (!seen.Add(phrase)) continue;

            // Count actual occurrences using simple search
            var freq = CountOccurrences(text, phrase);
            if (freq < minFrequency) continue;

            var savings = (phrase.Length - 2) * freq; // -2 for the replacement symbol cost
            if (savings > 0)
            {
                candidates.Add(new PhraseCandidate(phrase, freq, savings));
            }
        }

        // Deduplicate: remove substrings of longer phrases with higher savings
        candidates.Sort((a, b) => b.Savings.CompareTo(a.Savings));

        var filtered = new List<PhraseCandidate>();
        foreach (var c in candidates)
        {
            // Skip if this phrase is a substring of an already-selected phrase with higher savings
            var isSubstr = false;
            foreach (var f in filtered)
            {
                if (f.Phrase.Contains(c.Phrase, StringComparison.Ordinal))
                {
                    isSubstr = true;
                    break;
                }
            }
            if (!isSubstr)
            {
                filtered.Add(c);
                if (filtered.Count >= maxCandidates) break;
            }
        }

        return filtered;
    }

    /// <summary>
    /// Build suffix array using prefix-doubling with radix sort. O(N log N).
    /// </summary>
    public static int[] Build(string text)
    {
        var n = text.Length;
        if (n == 0) return [];

        var sa = new int[n];
        var rank = new int[n];
        var tmp = new int[n];

        // Initial ranking based on character
        for (var i = 0; i < n; i++)
        {
            sa[i] = i;
            rank[i] = text[i];
        }

        for (var k = 1; k < n; k *= 2)
        {
            // Sort by (rank[i], rank[i+k])
            Array.Sort(sa, (a, b) =>
            {
                var cmp = rank[a].CompareTo(rank[b]);
                if (cmp != 0) return cmp;
                var ra = a + k < n ? rank[a + k] : -1;
                var rb = b + k < n ? rank[b + k] : -1;
                return ra.CompareTo(rb);
            });

            // Compute new ranks
            tmp[sa[0]] = 0;
            for (var i = 1; i < n; i++)
            {
                tmp[sa[i]] = tmp[sa[i - 1]];
                var sameRank = rank[sa[i]] == rank[sa[i - 1]];
                var ra = sa[i] + k < n ? rank[sa[i] + k] : -1;
                var rb = sa[i - 1] + k < n ? rank[sa[i - 1] + k] : -1;
                if (!sameRank || ra != rb)
                    tmp[sa[i]]++;
            }

            Array.Copy(tmp, rank, n);

            // All ranks unique — early termination
            if (rank[sa[n - 1]] == n - 1) break;
        }

        return sa;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string TrimToWordBoundary(string phrase, string text, int start)
    {
        // Trim trailing partial words
        var end = phrase.Length;
        while (end > 1 && char.IsLetterOrDigit(phrase[end - 1]) && end < phrase.Length)
            end--;

        // Also trim if the next char in text is also an identifier char (partial word)
        if (start + end < text.Length && char.IsLetterOrDigit(text[start + end]))
        {
            // Back up to last word boundary
            while (end > 0 && char.IsLetterOrDigit(phrase[end - 1]))
                end--;
        }

        return phrase[..end].TrimEnd();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsUsefulPhrase(string phrase)
    {
        var alphaCount = 0;
        foreach (var c in phrase)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
                alphaCount++;
        }
        // At least 50% should be meaningful characters
        return alphaCount >= phrase.Length * 0.5;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountOccurrences(string text, string phrase)
    {
        var count = 0;
        var idx = 0;
        while (idx <= text.Length - phrase.Length)
        {
            var found = text.IndexOf(phrase, idx, StringComparison.Ordinal);
            if (found == -1) break;
            count++;
            idx = found + phrase.Length;
        }
        return count;
    }
}

/// <summary>
/// A candidate phrase for dynamic compression.
/// </summary>
public readonly record struct PhraseCandidate(string Phrase, int Frequency, int Savings);
