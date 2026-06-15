using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;

namespace gc.Application.Services;

/// <summary>
///     Splits a list of file entries into N shards and returns the requested slice.
///     Uses greedy round-robin by size for balanced shards, preferring to keep
///     files from the same module together when they don't unbalance the shards.
/// </summary>
public sealed class ShardSplitter
{
    /// <summary>
    ///     Split files into shards using greedy size-balancing.
    ///     Files from the same module are kept together as long as the group
    ///     fits within the target shard size; otherwise the group is split.
    /// </summary>
    public List<List<FileEntry>> SplitIntoShards(
        IReadOnlyList<FileEntry> entries,
        int totalShards,
        int desiredShardSlice,
        ILogger? logger = null)
    {
        if (entries.Count == 0) return new List<List<FileEntry>>();
        if (totalShards < 1) totalShards = 1;
        if (desiredShardSlice < 1) desiredShardSlice = 1;
        if (desiredShardSlice > totalShards) desiredShardSlice = totalShards;

        var shardBuckets = new List<FileEntry>[totalShards];
        var shardSizes = new long[totalShards];
        for (var i = 0; i < totalShards; i++) shardBuckets[i] = new List<FileEntry>();

        // Group by module for locality
        var groups = GroupByModule(entries);

        // Sort groups largest-first (greedy fits large groups before small ones)
        var sortedGroups = groups
            .OrderByDescending(g => g.Value.Sum(e => e.Size))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        // A group is kept whole if it doesn't exceed 1.5× the average shard budget.
        // Groups larger than that get split file-by-file for balance.
        var targetSize = (long)(entries.Sum(e => (long)e.Size) / (double)totalShards * 1.5);

        foreach (var (_, groupEntries) in sortedGroups)
        {
            var groupSize = groupEntries.Sum(e => (long)e.Size);
            var groupFiles = groupEntries.OrderByDescending(e => e.Size).ToList();

            // If the group fits within a shard budget, assign as a unit
            if (groupSize <= targetSize || groupFiles.Count == 1)
            {
                var idx = FindSmallestShard(shardSizes);
                shardBuckets[idx].AddRange(groupFiles);
                shardSizes[idx] += groupSize;
            }
            else
            {
                // Group is too large — split it file-by-file into the least-full shards
                foreach (var entry in groupFiles)
                {
                    var idx = FindSmallestShard(shardSizes);
                    shardBuckets[idx].Add(entry);
                    shardSizes[idx] += entry.Size;
                }
            }
        }

        logger?.Info(
            $"Shard {desiredShardSlice}/{totalShards}: {shardBuckets[desiredShardSlice - 1].Count} files " +
            $"({Formatting.FormatSize(shardSizes[desiredShardSlice - 1])})");

        return shardBuckets.ToList();
    }

    private static int FindSmallestShard(long[] shardSizes)
    {
        var idx = 0;
        var smallest = shardSizes[0];
        for (var i = 1; i < shardSizes.Length; i++)
        {
            if (shardSizes[i] < smallest)
            {
                smallest = shardSizes[i];
                idx = i;
            }
        }

        return idx;
    }

    /// <summary>
    ///     Group entries by their top-level module (first path segment).
    /// </summary>
    private static Dictionary<string, List<FileEntry>> GroupByModule(IReadOnlyList<FileEntry> entries)
    {
        var groups = new Dictionary<string, List<FileEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var path = entry.RelativePath;
            string key;

            var span = path.AsSpan();
            var firstSep = span.IndexOfAny('/', '\\');
            if (firstSep < 0 || span[..firstSep].SequenceEqual("."))
            {
                key = "_root";
            }
            else
            {
                key = path[..firstSep];
            }

            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<FileEntry>();
                groups[key] = list;
            }

            list.Add(entry);
        }

        return groups;
    }

    /// <summary>
    ///     Returns a preview of all shards — for --list output showing what each shard contains.
    /// </summary>
    public List<(int Slice, List<FileEntry> Files)> GetAllShardsPreview(
        IReadOnlyList<FileEntry> entries,
        int totalShards,
        ILogger? logger = null)
    {
        if (totalShards < 1) totalShards = 1;
        if (entries.Count == 0) return new List<(int Slice, List<FileEntry> Files)>();

        var allBuckets = SplitIntoShards(entries, totalShards, 1);

        var results = new List<(int Slice, List<FileEntry> Files)>();
        for (var i = 0; i < allBuckets.Count; i++) results.Add((i + 1, allBuckets[i]));
        return results;
    }
}
