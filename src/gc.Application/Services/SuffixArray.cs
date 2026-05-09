using System.Runtime.CompilerServices;
using System.Text;

namespace gc.Application.Services;

public static class SuffixArray
{
    public static List<PhraseCandidate> FindRepeatedPhrases(
        string text,
        int minPhraseLength = 6,
        int maxPhraseLength = 80,
        int minFrequency = 2,
        int maxCandidates = 50)
    {
        if (text.Length < minPhraseLength * 2)
            return [];

        // Phase 3: BPE-inspired word-frequency approach
        // Instead of O(N^2 log N) suffix array, use O(N) frequency scan + heap

        var candidates = new List<PhraseCandidate>();

        // 1. Word frequency map — split by non-alphanumeric, count occurrences
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        var i = 0;
        var len = text.Length;

        while (i < len)
        {
            if (!IsIdentStart(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < len && IsIdentChar(text[i]))
                i++;

            var word = text.Substring(start, i - start);
            if (word.Length >= minPhraseLength)
            {
                freq.TryGetValue(word, out var count);
                freq[word] = count + 1;
            }
        }

        // Also detect repeated multi-word phrases (simple n-gram approach)
        // Scan for repeated substrings using rolling hash on lines
        var lines = text.Split('\n');
        var lineHashFreq = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length >= minPhraseLength && trimmed.Length <= maxPhraseLength)
            {
                lineHashFreq.TryGetValue(trimmed, out var count);
                lineHashFreq[trimmed] = count + 1;
            }
        }

        // Add repeated lines as phrase candidates
        foreach (var kvp in lineHashFreq)
        {
            if (kvp.Value >= minFrequency)
            {
                var savings = (kvp.Key.Length - 2) * kvp.Value;
                if (savings > 0)
                {
                    candidates.Add(new PhraseCandidate(kvp.Key, kvp.Value, savings));
                }
            }
        }

        // 2. Score word candidates: savings = (len - 2) * count
        foreach (var kvp in freq)
        {
            if (kvp.Value >= minFrequency)
            {
                var savings = (kvp.Key.Length - 2) * kvp.Value;
                if (savings > 0)
                {
                    candidates.Add(new PhraseCandidate(kvp.Key, kvp.Value, savings));
                }
            }
        }

        // 3. Sort by savings (max-heap equivalent), deduplicate substrings
        candidates.Sort((a, b) => b.Savings.CompareTo(a.Savings));

        var filtered = new List<PhraseCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in candidates)
        {
            if (!seen.Add(c.Phrase)) continue;

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

    // Keep the Build method for backward compatibility with tests
    public static int[] Build(string text)
    {
        var n = text.Length;
        if (n == 0) return [];

        var sa = new int[n];
        var rank = new int[n];
        var tmp = new int[n];

        for (var i = 0; i < n; i++)
        {
            sa[i] = i;
            rank[i] = text[i];
        }

        for (var k = 1; k < n; k *= 2)
        {
            Array.Sort(sa, (a, b) =>
            {
                var cmp = rank[a].CompareTo(rank[b]);
                if (cmp != 0) return cmp;
                var ra = a + k < n ? rank[a + k] : -1;
                var rb = b + k < n ? rank[b + k] : -1;
                return ra.CompareTo(rb);
            });

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

            if (rank[sa[n - 1]] == n - 1) break;
        }

        return sa;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

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

public readonly record struct PhraseCandidate(string Phrase, int Frequency, int Savings);
