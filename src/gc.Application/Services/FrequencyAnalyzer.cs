using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace gc.Application.Services;

public static class FrequencyAnalyzer
{
    private const int BUFFER_SIZE = 4096;

    public static Dictionary<string, int> BuildFrequencyMap(string rootPath, string searchPattern = "*.cs", CancellationToken ct = default)
    {
        var files = Directory.EnumerateFiles(rootPath, searchPattern, SearchOption.AllDirectories);
        var threadLocalMaps = new ThreadLocal<Dictionary<string, int>>(() => new Dictionary<string, int>(StringComparer.Ordinal), trackAllValues: true);

        try
        {
            Parallel.ForEach(files, new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, file =>
            {
                if (ct.IsCancellationRequested) return;
                var localMap = threadLocalMaps.Value!;
                try
                {
                    var content = File.ReadAllText(file);
                    var lexer = new CodeLexer(content.AsSpan());
                    lexer.Enumerate(identSpan =>
                    {
                        var id = new string(identSpan);
                        if (localMap.TryGetValue(id, out int existing))
                            localMap[id] = existing + 1;
                        else
                            localMap[id] = 1;
                    });
                }
                catch { }
            });

            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var localMap in threadLocalMaps.Values)
            {
                foreach (var kvp in localMap)
                {
                    if (result.TryGetValue(kvp.Key, out int existing))
                        result[kvp.Key] = existing + kvp.Value;
                    else
                        result[kvp.Key] = kvp.Value;
                }
            }
            return result;
        }
        finally
        {
            threadLocalMaps.Dispose();
        }
    }

    public static List<IdentifierRankedEntry> Analyze(string rootPath, string searchPattern = "*.cs", int minLength = 6)
    {
        var frequencyMap = BuildFrequencyMap(rootPath, searchPattern);
        var scoredItems = ComputeSavingsScores(frequencyMap);
        var rankedEntries = scoredItems.Where(ie => ie.Identifier.Length >= minLength).ToList();
        rankedEntries.Sort((a, b) => b.Score.CompareTo(a.Score));
        return rankedEntries;
    }

    public static List<IdentifierRankedEntry> ComputeSavingsScores(Dictionary<string, int> frequencyMap)
    {
        var results = new List<IdentifierRankedEntry>();
        foreach (var kvp in frequencyMap)
        {
            long savedPerOccurrence = kvp.Key.Length - 1;
            if (savedPerOccurrence <= 0) continue;
            results.Add(new IdentifierRankedEntry(kvp.Key, kvp.Value, savedPerOccurrence * kvp.Value));
        }
        return results;
    }
}

public class IdentifierRankedEntry
{
    public string Identifier { get; }
    public int Frequency { get; }
    public long Score { get; }

    public IdentifierRankedEntry(string identifier, int frequency, long score)
    {
        Identifier = identifier;
        Frequency = frequency;
        Score = score;
    }
}

public static class IdentifierRanker
{
    public static IEnumerable<T> RankByScore<T>(IEnumerable<T> items, Func<T, long> scoreSelector)
    {
        return items.OrderByDescending(item => scoreSelector(item));
    }
}