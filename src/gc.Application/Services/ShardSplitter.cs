using gc.Domain.Interfaces;
using gc.Domain.Models;

namespace gc.Application.Services;

/// <summary>
/// Splits a list of file entries into N shards and returns the requested slice.
/// Smart grouping: groups by top-level module/folder first, then falls back to
/// size-based sorting if too few distinct groups.
/// </summary>
public sealed class ShardSplitter
{
    /// <summary>
    /// Group files into shards using smart grouping by module/folder first,
    /// then by size when not enough distinct groups.</summary>
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

        var groups = GroupByModule(entries);

        if (groups.Count >= totalShards)
        {
            return AssignByGroup(groups, totalShards, desiredShardSlice, logger);
        }

        // Not enough groups: merge small groups and re-split by size
        logger?.Debug($"Shard: only {groups.Count} module groups for {totalShards} shards — using size-based splitting");

        var merged = MergeSmallGroups(groups);
        return AssignBySize(merged, totalShards, desiredShardSlice, logger);
    }

    /// <summary>
    /// Group entries by their top-level module (first path segment).
    /// </summary>
    private Dictionary<string, List<FileEntry>> GroupByModule(IReadOnlyList<FileEntry> entries)
    {
        var groups = new Dictionary<string, List<FileEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var path = entry.RelativePath;
            string key;

            var firstSep = path.IndexOfAny(['/', '\\']);
            if (firstSep < 0)
            {
                key = "_root";
            }
            else
            {
                key = path[..firstSep].ToLowerInvariant();
                if (key == ".") key = "_root";
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

    private List<List<FileEntry>> AssignByGroup(
        Dictionary<string, List<FileEntry>> groups,
        int totalShards,
        int desiredShardSlice,
        ILogger? logger)
    {
        // Sort groups by total size (largest first) for stable splitting
        var sortedGroups = groups
            .OrderByDescending(g => g.Value.Sum(e => e.Size))
            .Select(g => g.Value)
            .ToList();

        // Spread groups across shards to balance size
        var shardBuckets = new List<FileEntry>[totalShards];
        for (int i = 0; i < totalShards; i++) shardBuckets[i] = new List<FileEntry>();

        foreach (var group in sortedGroups)
        {
            // Find the shard with the least total size so far
            int smallestIdx = 0;
            long smallestSize = shardBuckets[0].Sum(e => e.Size);
            for (int i = 1; i < totalShards; i++)
            {
                long size = shardBuckets[i].Sum(e => e.Size);
                if (size < smallestSize) { smallestSize = size; smallestIdx = i; }
            }
            shardBuckets[smallestIdx].AddRange(group);
        }

        logger?.Debug($"Shard {desiredShardSlice}/{totalShards}: {shardBuckets[desiredShardSlice - 1].Count} files");

        return shardBuckets.ToList();
    }

    private static List<List<FileEntry>> MergeSmallGroups(Dictionary<string, List<FileEntry>> groups)
    {
        // Merge groups with < 2 files into "_misc" to avoid tiny shards
        var sorted = groups
            .OrderByDescending(g => g.Value.Sum(e => e.Size))
            .ToList();

        var merged = new List<List<FileEntry>>();
        var misc = new List<FileEntry>();

        foreach (var (group, entries) in sorted)
        {
            if ("_root".Equals(group) || entries.Count < 2)
            {
                misc.AddRange(entries);
            }
            else
            {
                merged.Add(entries);
            }
        }

        if (misc.Count > 0) merged.Insert(0, misc);
        return merged;
    }

    private List<List<FileEntry>> AssignBySize(
        List<List<FileEntry>> mergedGroups,
        int totalShards,
        int desiredShardSlice,
        ILogger? logger)
    {
        // Sort by total size descending
        var sorted = mergedGroups
            .OrderByDescending(g => g.Sum(e => e.Size))
            .ToList();

        // Sort files within each group by size descending
        var withSizes = sorted
            .SelectMany(g => g.Select(e => (Entry: e, Size: e.Size)))
            .OrderByDescending(x => x.Size)
            .Select(x => x.Entry)
            .ToList();

        // Split sorted list into N roughly-equal shards
        var shardBuckets = new List<FileEntry>[totalShards];
        for (int i = 0; i < totalShards; i++) shardBuckets[i] = new List<FileEntry>();

        foreach (var entry in withSizes)
        {
            // Assign to shard with least accumulated size
            int smallestIdx = 0;
            long smallestSize = shardBuckets[0].Sum(e => e.Size);
            for (int i = 1; i < totalShards; i++)
            {
                long size = shardBuckets[i].Sum(e => e.Size);
                if (size < smallestSize) { smallestSize = size; smallestIdx = i; }
            }
            shardBuckets[smallestIdx].Add(entry);
        }

        logger?.Info($"Shard {desiredShardSlice}/{totalShards}: {shardBuckets[desiredShardSlice - 1].Count} files ({shardBuckets[desiredShardSlice - 1].Sum(e => e.Size):N0} bytes)");

        return shardBuckets.ToList();
    }

    /// <summary>
    /// Returns a preview of all shards — for --list output showing what each shard contains.
    /// </summary>
    public List<(int Slice, List<FileEntry> Files)> GetAllShardsPreview(
        IReadOnlyList<FileEntry> entries,
        int totalShards,
        ILogger? logger = null)
    {
        if (totalShards < 1) totalShards = 1;

        if (entries.Count == 0) return new List<(int Slice, List<FileEntry> Files)>();

        // Compute all shard buckets once by requesting slice 1 (we don't use the logger
        // output anyway — we just need the partitioned bucket lists)
        var allBuckets = SplitIntoShards(entries, totalShards, 1, null);

        var results = new List<(int Slice, List<FileEntry> Files)>();
        for (int i = 0; i < allBuckets.Count; i++)
        {
            results.Add((i + 1, allBuckets[i]));
        }
        return results;
    }
}
