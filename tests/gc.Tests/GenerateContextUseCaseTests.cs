using gc.Application.Services;
using gc.Application.UseCases;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.Tests;

public class GenerateContextUseCaseTests
{
    // ─── Mock implementations ───────────────────────────────────────────────

    private class MockFileDiscovery : IFileDiscovery
    {
        public Dictionary<string, List<string>> FilesPerDirectory { get; set; } = new();
        public List<RepoInfo> ReposToReturn { get; set; } = new();
        public HashSet<string> FailDirectories { get; set; } = new();
        public bool DiscoverShouldFail { get; set; }
        public bool DiscoverReposShouldFail { get; set; }
        public bool RespectCancellation { get; set; }

        public Task<Result<IEnumerable<string>>> DiscoverFilesAsync(
            string rootPath, GcConfiguration config, CancellationToken ct)
        {
            if (RespectCancellation && ct.IsCancellationRequested)
                return Task.FromResult(Result<IEnumerable<string>>.Failure("Operation cancelled"));
            if (DiscoverShouldFail)
                return Task.FromResult(Result<IEnumerable<string>>.Failure("Discovery failed"));
            if (FailDirectories.Contains(rootPath))
                return Task.FromResult(Result<IEnumerable<string>>.Failure($"Discovery failed for {rootPath}"));
            if (FilesPerDirectory.TryGetValue(rootPath, out var files))
                return Task.FromResult(Result<IEnumerable<string>>.Success(files));
            return Task.FromResult(Result<IEnumerable<string>>.Success(Array.Empty<string>()));
        }

        public Task<Result<IReadOnlyList<RepoInfo>>> DiscoverGitReposAsync(
            string clusterRoot, ClusterConfiguration clusterConfig, CancellationToken ct)
        {
            if (RespectCancellation && ct.IsCancellationRequested)
                return Task.FromResult(Result<IReadOnlyList<RepoInfo>>.Failure("Operation cancelled"));
            if (DiscoverReposShouldFail)
                return Task.FromResult(Result<IReadOnlyList<RepoInfo>>.Failure("Repo discovery failed"));
            return Task.FromResult(Result<IReadOnlyList<RepoInfo>>.Success(ReposToReturn.AsReadOnly()));
        }
    }

    private class MockFileReader : IFileReader
    {
        public Task<Result<Stream>> ReadStreamingAsync(string path, CancellationToken ct)
            => Task.FromResult(Result<Stream>.Success(new MemoryStream()));

        public Task<Result<FileContent>> ReadAsync(FileEntry entry, CancellationToken ct)
            => Task.FromResult(Result<FileContent>.Success(new FileContent(entry, "", entry.Size)));

        public Task<bool> IsBinaryFileAsync(string path, CancellationToken ct)
            => Task.FromResult(false);
    }

    private class MockMarkdownGenerator : IMarkdownGenerator
    {
        public List<FileContent> ProcessedContents { get; } = new();
        public long ReturnSize { get; set; } = 100;
        public bool ShouldFail { get; set; }
        public IEnumerable<string>? CapturedExcludeLineIfStart { get; private set; }

        public Task<Result<long>> GenerateMarkdownStreamingAsync(
            IEnumerable<FileContent> contents, Stream outputStream,
            GcConfiguration config, IEnumerable<string>? excludeLineIfStart,
            CancellationToken ct)
        {
            ProcessedContents.AddRange(contents);
            CapturedExcludeLineIfStart = excludeLineIfStart;
            if (ShouldFail)
                return Task.FromResult(Result<long>.Failure("Generator failed"));
            outputStream.WriteByte((byte)'#');
            return Task.FromResult(Result<long>.Success(ReturnSize));
        }
    }

    private class MockClipboardService : IClipboardService
    {
        public bool ShouldFail { get; set; }
        public int CopyCallCount { get; private set; }

        public Task<Result> CopyToClipboardAsync(Stream stream, CancellationToken ct)
            => CopyToClipboardAsync(stream, new LimitsConfiguration(), false, ct);

        public Task<Result> CopyToClipboardAsync(Stream stream, LimitsConfiguration limits, bool append, CancellationToken ct)
        {
            CopyCallCount++;
            return ShouldFail
                ? Task.FromResult(Result.Failure("Clipboard failed"))
                : Task.FromResult(Result.Success());
        }

        public Task<Result> CopyToClipboardAsync(string content, CancellationToken ct)
            => Task.FromResult(Result.Success());

        public Task<Result> CopyToClipboardAsync(string content, LimitsConfiguration limits, bool append, CancellationToken ct)
            => Task.FromResult(Result.Success());
    }

    private class MockLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = new();

        public void Log(LogLevel level, string message, Exception? ex = null)
        {
            Messages.Add((level, message));
        }
    }

    // ─── Factory ─────────────────────────────────────────────────────────────

    private static (GenerateContextUseCase UseCase, MockFileDiscovery Discovery,
        MockMarkdownGenerator Generator, MockClipboardService Clipboard, MockLogger Logger)
        CreateUseCase()
    {
        var logger = new MockLogger();
        var discovery = new MockFileDiscovery();
        var filter = new FileFilter(logger);
        var reader = new MockFileReader();
        var generator = new MockMarkdownGenerator();
        var clipboard = new MockClipboardService();
        var useCase = new GenerateContextUseCase(discovery, filter, reader, generator, clipboard, logger);
        return (useCase, discovery, generator, clipboard, logger);
    }

    private static GcConfiguration DefaultConfig() => new()
    {
        Limits = new LimitsConfiguration(),
        Discovery = new DiscoveryConfiguration { Cluster = new ClusterConfiguration() },
        Filters = new FiltersConfiguration(),
    };

    // ─── ExecuteAsync tests ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithValidFiles_ReturnsSuccess()
    {
        var (useCase, discovery, generator, clipboard, _) = CreateUseCase();
        discovery.FilesPerDirectory["/repo"] = ["src/file1.cs", "src/file2.cs"];
        var config = DefaultConfig();

        var result = await useCase.ExecuteAsync("/repo", config, [], [], [], null);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, generator.ProcessedContents.Count);
        Assert.Equal(1, clipboard.CopyCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_NoFiles_ReturnsSuccessWithZeroFiles()
    {
        var (useCase, discovery, generator, _, logger) = CreateUseCase();
        discovery.FilesPerDirectory["/repo"] = [];
        var config = DefaultConfig();

        var result = await useCase.ExecuteAsync("/repo", config, [], [], [], null);

        Assert.True(result.IsSuccess);
        Assert.Empty(generator.ProcessedContents);
        Assert.Contains(logger.Messages, m => m.Message.Contains("No files match"));
    }

    [Fact]
    public async Task ExecuteAsync_OperationCancelled_ReturnsFailure()
    {
        var (useCase, discovery, _, _, _) = CreateUseCase();
        discovery.RespectCancellation = true;
        var config = DefaultConfig();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await useCase.ExecuteAsync("/repo", config, [], [], [], null, ct: cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("cancelled", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_DiscoveryFails_ReturnsFailure()
    {
        var (useCase, discovery, _, _, _) = CreateUseCase();
        discovery.DiscoverShouldFail = true;
        var config = DefaultConfig();

        var result = await useCase.ExecuteAsync("/repo", config, [], [], [], null);

        Assert.False(result.IsSuccess);
        Assert.Equal("Discovery failed", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_GeneratorFails_ReturnsFailure()
    {
        var (useCase, discovery, generator, _, _) = CreateUseCase();
        discovery.FilesPerDirectory["/repo"] = ["file1.cs"];
        generator.ShouldFail = true;
        var config = DefaultConfig();

        var result = await useCase.ExecuteAsync("/repo", config, [], [], [], null);

        Assert.False(result.IsSuccess);
        Assert.Equal("Generator failed", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_ClipboardFails_ReturnsFailure()
    {
        var (useCase, discovery, _, clipboard, _) = CreateUseCase();
        discovery.FilesPerDirectory["/repo"] = ["file1.cs"];
        clipboard.ShouldFail = true;
        var config = DefaultConfig();

        var result = await useCase.ExecuteAsync("/repo", config, [], [], [], null);

        Assert.False(result.IsSuccess);
        Assert.Equal("Clipboard failed", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputFile_WritesAndReturnsSuccess()
    {
        var (useCase, discovery, _, clipboard, _) = CreateUseCase();
        discovery.FilesPerDirectory["/repo"] = ["file1.cs"];
        var config = DefaultConfig();
        var tempFile = Path.Combine(Path.GetTempPath(), $"gc-test-{Guid.NewGuid()}.md");
        try
        {
            var result = await useCase.ExecuteAsync("/repo", config, [], [], [], tempFile);

            Assert.True(result.IsSuccess);
            Assert.Equal(0, clipboard.CopyCallCount);
            Assert.True(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithExcludeLineIfStart_PassesToGenerator()
    {
        var (useCase, discovery, generator, _, _) = CreateUseCase();
        discovery.FilesPerDirectory["/repo"] = ["file1.cs"];
        var config = DefaultConfig();
        var excludePatterns = new[] { "//", "/*" };

        var result = await useCase.ExecuteAsync("/repo", config, [], [], [], null,
            excludeLineIfStart: excludePatterns);

        Assert.True(result.IsSuccess);
        Assert.NotNull(generator.CapturedExcludeLineIfStart);
        Assert.Equal(excludePatterns, generator.CapturedExcludeLineIfStart);
    }

    [Fact]
    public async Task ExecuteAsync_WithMaxFiles_LimitsResults()
    {
        var (useCase, discovery, generator, _, _) = CreateUseCase();
        discovery.FilesPerDirectory["/repo"] =
            ["file1.cs", "file2.cs", "file3.cs", "file4.cs", "file5.cs"];
        var config = DefaultConfig() with
        {
            Limits = new LimitsConfiguration { MaxFiles = 3 }
        };

        var result = await useCase.ExecuteAsync("/repo", config, [], [], [], null);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, generator.ProcessedContents.Count);
    }

    // ─── ExecuteClusterAsync tests ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteClusterAsync_NoGitRepos_ReturnsSuccess()
    {
        var (useCase, discovery, generator, _, logger) = CreateUseCase();
        discovery.ReposToReturn = [];
        var config = DefaultConfig();

        var result = await useCase.ExecuteClusterAsync("/cluster", config, [], [], [], null);

        Assert.True(result.IsSuccess);
        Assert.Empty(generator.ProcessedContents);
        Assert.Contains(logger.Messages,
            m => m.Level == LogLevel.Warning && m.Message.Contains("No git repositories"));
    }

    [Fact]
    public async Task ExecuteClusterAsync_WithRepos_ReturnsSuccess()
    {
        var (useCase, discovery, generator, clipboard, _) = CreateUseCase();
        discovery.ReposToReturn =
        [
            new() { RootPath = "/cluster/repo1", RelativePath = "repo1", Name = "repo1", IsValid = true },
            new() { RootPath = "/cluster/repo2", RelativePath = "repo2", Name = "repo2", IsValid = true },
        ];
        discovery.FilesPerDirectory["/cluster/repo1"] = ["file1.cs"];
        discovery.FilesPerDirectory["/cluster/repo2"] = ["file2.cs"];
        var config = DefaultConfig();

        var result = await useCase.ExecuteClusterAsync("/cluster", config, [], [], [], null);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, generator.ProcessedContents.Count); // 2 headers + 2 files
        Assert.Equal(1, clipboard.CopyCallCount);
    }

    [Fact]
    public async Task ExecuteClusterAsync_OperationCancelled_ReturnsFailure()
    {
        var (useCase, discovery, _, _, _) = CreateUseCase();
        discovery.RespectCancellation = true;
        var config = DefaultConfig();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await useCase.ExecuteClusterAsync("/cluster", config, [], [], [], null,
            ct: cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("cancelled", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteClusterAsync_GeneratorFails_ReturnsFailure()
    {
        var (useCase, discovery, generator, _, _) = CreateUseCase();
        discovery.ReposToReturn =
        [
            new() { RootPath = "/cluster/repo1", RelativePath = "repo1", Name = "repo1", IsValid = true },
        ];
        discovery.FilesPerDirectory["/cluster/repo1"] = ["file1.cs"];
        generator.ShouldFail = true;
        var config = DefaultConfig();

        var result = await useCase.ExecuteClusterAsync("/cluster", config, [], [], [], null);

        Assert.False(result.IsSuccess);
        Assert.Equal("Generator failed", result.Error);
    }

    [Fact]
    public async Task ExecuteClusterAsync_WithRepoHeaders_IncludesHeaders()
    {
        var (useCase, discovery, generator, _, _) = CreateUseCase();
        discovery.ReposToReturn =
        [
            new() { RootPath = "/cluster/alpha", RelativePath = "alpha", Name = "alpha", IsValid = true },
            new() { RootPath = "/cluster/beta", RelativePath = "beta", Name = "beta", IsValid = true },
        ];
        discovery.FilesPerDirectory["/cluster/alpha"] = ["a.cs"];
        discovery.FilesPerDirectory["/cluster/beta"] = ["b.cs"];
        var config = DefaultConfig() with
        {
            Discovery = new DiscoveryConfiguration
            {
                Cluster = new ClusterConfiguration { IncludeRepoHeader = true }
            }
        };

        var result = await useCase.ExecuteClusterAsync("/cluster", config, [], [], [], null);

        Assert.True(result.IsSuccess);
        // alpha header + a.cs + beta separator/header + b.cs = 4
        Assert.Equal(4, generator.ProcessedContents.Count);
        // First item is a header with non-null Content
        Assert.NotNull(generator.ProcessedContents[0].Content);
        Assert.Contains("alpha", generator.ProcessedContents[0].Content);
        // Third item is a separator/header for beta
        Assert.NotNull(generator.ProcessedContents[2].Content);
        Assert.Contains("beta", generator.ProcessedContents[2].Content);
    }

    [Fact]
    public async Task ExecuteClusterAsync_EmptyRepos_SkipsInOutput()
    {
        var (useCase, discovery, generator, _, _) = CreateUseCase();
        discovery.ReposToReturn =
        [
            new() { RootPath = "/cluster/repo1", RelativePath = "repo1", Name = "repo1", IsValid = true },
            new() { RootPath = "/cluster/repo2", RelativePath = "repo2", Name = "repo2", IsValid = true },
        ];
        discovery.FilesPerDirectory["/cluster/repo1"] = ["file1.cs"];
        discovery.FilesPerDirectory["/cluster/repo2"] = []; // empty
        var config = DefaultConfig() with
        {
            Discovery = new DiscoveryConfiguration
            {
                Cluster = new ClusterConfiguration { IncludeRepoHeader = true }
            }
        };

        var result = await useCase.ExecuteClusterAsync("/cluster", config, [], [], [], null);

        Assert.True(result.IsSuccess);
        // Only repo1 contributes: 1 header + 1 file = 2 (repo2 skipped)
        Assert.Equal(2, generator.ProcessedContents.Count);
    }

    [Fact]
    public async Task ExecuteClusterAsync_DiscoveryFailsForRepo_ContinuesWithOthers()
    {
        var (useCase, discovery, generator, _, logger) = CreateUseCase();
        discovery.ReposToReturn =
        [
            new() { RootPath = "/cluster/good", RelativePath = "good", Name = "good", IsValid = true },
            new() { RootPath = "/cluster/bad", RelativePath = "bad", Name = "bad", IsValid = true },
        ];
        discovery.FilesPerDirectory["/cluster/good"] = ["file1.cs"];
        discovery.FailDirectories.Add("/cluster/bad");
        var config = DefaultConfig();

        var result = await useCase.ExecuteClusterAsync("/cluster", config, [], [], [], null);

        Assert.True(result.IsSuccess);
        // good repo contributes its files
        Assert.True(generator.ProcessedContents.Count >= 1);
        // A warning about the bad repo should be logged
        Assert.Contains(logger.Messages,
            m => m.Level == LogLevel.Warning && m.Message.Contains("bad"));
    }

    // ─── BuildClusterContents (indirect via ExecuteClusterAsync) ─────────────

    [Fact]
    public async Task BuildClusterContents_YieldsHeaderEntries()
    {
        var (useCase, discovery, generator, _, _) = CreateUseCase();
        discovery.ReposToReturn =
        [
            new() { RootPath = "/c/r1", RelativePath = "r1", Name = "r1", IsValid = true },
        ];
        discovery.FilesPerDirectory["/c/r1"] = ["a.cs"];
        var config = DefaultConfig() with
        {
            Discovery = new DiscoveryConfiguration
            {
                Cluster = new ClusterConfiguration { IncludeRepoHeader = true }
            }
        };

        await useCase.ExecuteClusterAsync("/c", config, [], [], [], null);

        // 1 header + 1 file = 2
        Assert.Equal(2, generator.ProcessedContents.Count);
        // Header entry has Content
        var header = generator.ProcessedContents[0];
        Assert.NotNull(header.Content);
        Assert.Contains("r1", header.Content);
    }

    [Fact]
    public async Task BuildClusterContents_YieldsFileEntriesAsStreaming()
    {
        var (useCase, discovery, generator, _, _) = CreateUseCase();
        discovery.ReposToReturn =
        [
            new() { RootPath = "/c/r1", RelativePath = "r1", Name = "r1", IsValid = true },
        ];
        discovery.FilesPerDirectory["/c/r1"] = ["code.cs"];
        var config = DefaultConfig() with
        {
            Discovery = new DiscoveryConfiguration
            {
                Cluster = new ClusterConfiguration { IncludeRepoHeader = false }
            }
        };

        await useCase.ExecuteClusterAsync("/c", config, [], [], [], null);

        Assert.Single(generator.ProcessedContents);
        var fileItem = generator.ProcessedContents[0];
        // Streaming files have Content == null
        Assert.Null(fileItem.Content);
        Assert.True(fileItem.IsStreaming);
    }

    [Fact]
    public async Task BuildClusterContents_SkipsEmptyRepos()
    {
        var (useCase, discovery, generator, _, _) = CreateUseCase();
        discovery.ReposToReturn =
        [
            new() { RootPath = "/c/blank", RelativePath = "blank", Name = "blank", IsValid = true },
            new() { RootPath = "/c/active", RelativePath = "active", Name = "active", IsValid = true },
        ];
        discovery.FilesPerDirectory["/c/blank"] = [];
        discovery.FilesPerDirectory["/c/active"] = ["x.cs"];
        var config = DefaultConfig() with
        {
            Discovery = new DiscoveryConfiguration
            {
                Cluster = new ClusterConfiguration { IncludeRepoHeader = true }
            }
        };

        await useCase.ExecuteClusterAsync("/c", config, [], [], [], null);

        // Only active repo: 1 header + 1 file = 2
        Assert.Equal(2, generator.ProcessedContents.Count);
        // No entries from the blank repo (it had no files)
        Assert.All(generator.ProcessedContents, c =>
            Assert.DoesNotContain("blank", c.Entry.Path));
    }
}
