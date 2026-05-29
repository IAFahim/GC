using gc.Application.Services;
using gc.Domain.Models;

namespace gc.Tests.FeatureTests;

public class ShardSplitterTests
{
    private ShardSplitter CreateSplitter()
    {
        return new ShardSplitter();
    }

    private FileEntry MakeEntry(string relativePath, long size = 100)
    {
        return new FileEntry("", relativePath, "cs", "csharp", size);
    }

    private List<FileEntry> MakeEntries(params (string path, long size)[] items)
    {
        return items.Select(i => MakeEntry(i.path, i.size)).ToList();
    }

    [Fact]
    public void SplitIntoShards_GroupByModule_BalancesSize()
    {
        var entries = MakeEntries(
            ("src/file1.cs", 1000),
            ("src/file2.cs", 500),
            ("lib/file3.cs", 800),
            ("lib/file4.cs", 200),
            ("test/file5.cs", 300),
            ("test/file6.cs", 150)
        );

        var splitter = CreateSplitter();
        var shards = splitter.SplitIntoShards(entries, 2, 1);

        Assert.Equal(2, shards.Count);
        Assert.True(shards[0].Count > 0 || shards[1].Count > 0);

        // All files should be present
        var allFiles = shards.SelectMany(s => s).ToList();
        Assert.Equal(entries.Count, allFiles.Count);
    }

    [Fact]
    public void SplitIntoShards_SliceOutOfBounds_FallsBack()
    {
        var entries = MakeEntries(("a.cs", 100), ("b.cs", 100));

        var splitter = CreateSplitter();
        var shards = splitter.SplitIntoShards(entries, 3, 99); // invalid slice

        // Should still return something
        Assert.NotEmpty(shards);
    }

    [Fact]
    public void SplitIntoShards_TooFewModules_UsesSizeBasedSplit()
    {
        // Only 2 module groups for 3 shards -> size-based fallback
        var entries = MakeEntries(
            ("moduleA/file1.cs", 500),
            ("moduleA/file2.cs", 300),
            ("moduleB/file3.cs", 800)
        );

        var splitter = CreateSplitter();
        var shards = splitter.SplitIntoShards(entries, 3, 1);

        Assert.Equal(3, shards.Count);
        var allFiles = shards.SelectMany(s => s).ToList();
        Assert.Equal(entries.Count, allFiles.Count);
    }

    [Fact]
    public void SplitIntoShards_EmptyInput_ReturnsEmptyShards()
    {
        var splitter = CreateSplitter();
        var shards = splitter.SplitIntoShards(new List<FileEntry>(), 3, 1);

        Assert.Empty(shards);
    }

    [Fact]
    public void SplitIntoShards_SingleShard_ReturnsAllFiles()
    {
        var entries = MakeEntries(("a.cs", 100), ("b.cs", 200), ("c.cs", 150));

        var splitter = CreateSplitter();
        var shards = splitter.SplitIntoShards(entries, 1, 1);

        Assert.Single(shards);
        Assert.Equal(entries.Count, shards[0].Count);
    }

    [Fact]
    public void SplitIntoShards_FiveShards_CoversAllFiles()
    {
        var entries = MakeEntries(
            ("src/file1.cs", 100),
            ("src/file2.cs", 100),
            ("lib/file3.cs", 100),
            ("test/file4.cs", 100),
            ("docs/file5.cs", 100)
        );

        var splitter = CreateSplitter();

        // Gather all files across all 5 shards
        var allShards = splitter.SplitIntoShards(entries, 5, 1);
        Assert.Equal(5, allShards.Count);

        var allFiles = allShards.SelectMany(s => s).ToList();
        Assert.Equal(entries.Count, allFiles.Count);
    }

    [Fact]
    public void SplitIntoShards_FilesInRootFolder_GroupedUnderRoot()
    {
        var entries = new List<FileEntry>
        {
            MakeEntry("rootfile.cs"),
            MakeEntry("src/file.cs", 200),
            MakeEntry("README.md", 50),
            MakeEntry("src/utils/file.cs", 150)
        };

        var splitter = CreateSplitter();
        var shards = splitter.SplitIntoShards(entries, 2, 1);

        Assert.Equal(2, shards.Count);
        var allFiles = shards.SelectMany(s => s).ToList();
        Assert.Equal(entries.Count, allFiles.Count);
    }

    [Fact]
    public void SplitIntoShards_LargeTotalShards_CoversAllFiles()
    {
        var entries = MakeEntries(
            ("a.cs", 10),
            ("b.cs", 20),
            ("c.cs", 30),
            ("d.cs", 40),
            ("e.cs", 50)
        );

        var splitter = CreateSplitter();
        var allShards = splitter.SplitIntoShards(entries, 10, 1);

        Assert.Equal(10, allShards.Count);
        var allFiles = allShards.SelectMany(s => s).ToList();
        Assert.Equal(entries.Count, allFiles.Count);
    }

    [Fact]
    public void GetAllShardsPreview_ReturnsAllSlices()
    {
        var entries = MakeEntries(("a.cs", 100), ("b.cs", 100), ("c.cs", 100));

        var splitter = CreateSplitter();
        var previews = splitter.GetAllShardsPreview(entries, 3);

        Assert.Equal(3, previews.Count);
        Assert.Equal(1, previews[0].Slice);
        Assert.Equal(2, previews[1].Slice);
        Assert.Equal(3, previews[2].Slice);

        // All previews together should cover all files
        var allFiles = previews.SelectMany(p => p.Files).ToList();
        Assert.Equal(entries.Count, allFiles.Count);
    }
}