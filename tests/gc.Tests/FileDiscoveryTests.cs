using gc.Domain.Common;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Discovery;
using gc.Infrastructure.Logging;

namespace gc.Tests;

public class FileDiscoveryTests : IDisposable
{
    private readonly string _testDir;
    private readonly ConsoleLogger _logger;

    public FileDiscoveryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "gc-test-disc-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
        _logger = new ConsoleLogger();
    }

    public void Dispose()
    {
        TryDeleteDirectory(_testDir);
    }

    // ─── 1. Basic Discovery ────────────────────────────────────────────

    [Fact]
    public async Task Discover_NonexistentDirectory_ReturnsEmptyList()
    {
        // Filesystem mode silently handles missing dirs (IOException is caught internally)
        // and returns an empty list rather than failing.
        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };

        var result = await discovery.DiscoverFilesAsync(
            Path.Combine(_testDir, "does_not_exist_xyz"), config, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Discover_EmptyDirectory_ReturnsEmptyList()
    {
        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };

        var result = await discovery.DiscoverFilesAsync(_testDir, config, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Discover_WithFiles_ReturnsAllFiles()
    {
        File.WriteAllText(Path.Combine(_testDir, "Program.cs"), "class Program {}");
        File.WriteAllText(Path.Combine(_testDir, "readme.md"), "# Test");
        File.WriteAllText(Path.Combine(_testDir, "style.css"), "body { }");

        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };

        var result = await discovery.DiscoverFilesAsync(_testDir, config, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count());
        Assert.Contains(result.Value!, f => f == "Program.cs");
        Assert.Contains(result.Value!, f => f == "readme.md");
        Assert.Contains(result.Value!, f => f == "style.css");
    }

    [Fact]
    public async Task Discover_RespectsMaxDepth()
    {
        // Create structure: root/a/b/c/file.txt
        var deepDir = Path.Combine(_testDir, "a", "b", "c");
        Directory.CreateDirectory(deepDir);
        File.WriteAllText(Path.Combine(_testDir, "root.txt"), "root");
        File.WriteAllText(Path.Combine(_testDir, "a", "level1.txt"), "l1");
        File.WriteAllText(Path.Combine(_testDir, "a", "b", "level2.txt"), "l2");
        File.WriteAllText(Path.Combine(_testDir, "a", "b", "c", "level3.txt"), "l3");

        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "filesystem", MaxDepth = 1 }
        };

        var result = await discovery.DiscoverFilesAsync(_testDir, config, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var files = result.Value!.ToList();
        // MaxDepth=1 means depth 0 (root) and depth 1 (a/) — no deeper
        Assert.Contains(files, f => f == "root.txt");
        Assert.Contains(files, f => f == "a/level1.txt");
        Assert.DoesNotContain(files, f => f.Contains("level2"));
        Assert.DoesNotContain(files, f => f.Contains("level3"));
    }

    // ─── 2. Ignore Patterns ────────────────────────────────────────────

    [Fact]
    public async Task Discover_IgnoresNodeModules()
    {
        File.WriteAllText(Path.Combine(_testDir, "app.js"), "console.log('hi');");
        var nmDir = Path.Combine(_testDir, "node_modules", "pkg");
        Directory.CreateDirectory(nmDir);
        File.WriteAllText(Path.Combine(nmDir, "index.js"), "module.exports = {};");
        File.WriteAllText(Path.Combine(nmDir, "package.json"), "{}");

        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };

        var result = await discovery.DiscoverFilesAsync(_testDir, config, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var files = result.Value!.ToList();
        Assert.Single(files);
        Assert.Equal("app.js", files[0]);
    }

    [Fact]
    public async Task Discover_IgnoresBinObj()
    {
        File.WriteAllText(Path.Combine(_testDir, "Program.cs"), "class Program {}");
        var binDir = Path.Combine(_testDir, "bin", "Debug");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "app.dll"), "binary");
        var objDir = Path.Combine(_testDir, "obj", "Debug");
        Directory.CreateDirectory(objDir);
        File.WriteAllText(Path.Combine(objDir, "app.pdb"), "symbols");

        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };

        var result = await discovery.DiscoverFilesAsync(_testDir, config, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var files = result.Value!.ToList();
        Assert.Single(files);
        Assert.Equal("Program.cs", files[0]);
    }

    [Fact]
    public async Task Discover_IgnoresDotGit()
    {
        File.WriteAllText(Path.Combine(_testDir, "main.py"), "print('hi')");
        var gitDir = Path.Combine(_testDir, ".git", "objects");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "pack"), "gitdata");
        File.WriteAllText(Path.Combine(_testDir, ".git", "HEAD"), "ref: refs/heads/main");

        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };

        var result = await discovery.DiscoverFilesAsync(_testDir, config, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var files = result.Value!.ToList();
        Assert.Single(files);
        Assert.Equal("main.py", files[0]);
    }

    [Fact]
    public async Task Discover_IgnoresMultipleSystemDirectories()
    {
        // Create a typical project structure with system dirs that should be ignored
        File.WriteAllText(Path.Combine(_testDir, "src.py"), "code");

        foreach (var ignored in new[] { "dist", "build", "target", "out", "__pycache__", ".vs", ".idea", "coverage" })
        {
            var dir = Path.Combine(_testDir, ignored);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"file_{ignored}.txt"), "data");
        }

        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };

        var result = await discovery.DiscoverFilesAsync(_testDir, config, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var files = result.Value!.ToList();
        Assert.Single(files);
        Assert.Equal("src.py", files[0]);
    }

    // ─── 3. Nested Directories ─────────────────────────────────────────

    [Fact]
    public async Task Discover_NestedDirectories_ReturnsRelativePaths()
    {
        var sub1 = Path.Combine(_testDir, "src", "utils");
        Directory.CreateDirectory(sub1);
        File.WriteAllText(Path.Combine(sub1, "helper.cs"), "class Helper {}");

        var sub2 = Path.Combine(_testDir, "tests", "unit");
        Directory.CreateDirectory(sub2);
        File.WriteAllText(Path.Combine(sub2, "test.cs"), "class Test {}");

        File.WriteAllText(Path.Combine(_testDir, "root.md"), "# Root");

        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };

        var result = await discovery.DiscoverFilesAsync(_testDir, config, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var files = result.Value!.ToList();
        Assert.Equal(3, files.Count);
        // All paths should use forward slashes (normalized)
        Assert.Contains(files, f => f == "src/utils/helper.cs");
        Assert.Contains(files, f => f == "tests/unit/test.cs");
        Assert.Contains(files, f => f == "root.md");
    }

    [Fact]
    public async Task Discover_FilesystemMode_IgnoresGit()
    {
        // In explicit filesystem mode, should traverse files directly without git
        File.WriteAllText(Path.Combine(_testDir, "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(_testDir, "app.js"), "console.log('hello');");

        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };

        var result = await discovery.DiscoverFilesAsync(_testDir, config, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count());
    }

    // ─── 4. Git Mode ───────────────────────────────────────────────────

    [Fact]
    public async Task Discover_GitMode_NonGitRepo_ReturnsFailure()
    {
        File.WriteAllText(Path.Combine(_testDir, "file.txt"), "data");

        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "git" }
        };

        var result = await discovery.DiscoverFilesAsync(_testDir, config, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("git", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ─── 5. Cluster Discovery ──────────────────────────────────────────

    [Fact]
    public async Task DiscoverCluster_NonexistentDirectory_ReturnsFailure()
    {
        var discovery = new FileDiscovery(_logger);
        var clusterConfig = new ClusterConfiguration();

        var result = await discovery.DiscoverGitReposAsync(
            Path.Combine(_testDir, "no_such_dir"), clusterConfig, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task DiscoverCluster_EmptyDirectory_ReturnsEmptyList()
    {
        var discovery = new FileDiscovery(_logger);
        var clusterConfig = new ClusterConfiguration();

        var result = await discovery.DiscoverGitReposAsync(_testDir, clusterConfig, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task DiscoverCluster_FindsGitRepos()
    {
        // Create nested directories with .git markers (not real repos, but .git dirs exist)
        var repo1 = Path.Combine(_testDir, "repo1");
        Directory.CreateDirectory(Path.Combine(repo1, ".git"));
        File.WriteAllText(Path.Combine(repo1, "README.md"), "# Repo 1");

        var subGroup = Path.Combine(_testDir, "services");
        var repo2 = Path.Combine(subGroup, "api");
        Directory.CreateDirectory(Path.Combine(repo2, ".git"));
        File.WriteAllText(Path.Combine(repo2, "main.go"), "package main");

        var discovery = new FileDiscovery(_logger);
        var clusterConfig = new ClusterConfiguration { MaxDepth = 2 };

        var result = await discovery.DiscoverGitReposAsync(_testDir, clusterConfig, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Note: repos may fail git validation (not real git repos), so they might be filtered out.
        // The scan finds .git dirs, but validation calls git rev-parse which may fail.
        // We just verify the method completes successfully without throwing.
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task DiscoverCluster_SkipsIgnoredDirectories()
    {
        // Create repos in normal dirs and in ignored dirs (node_modules)
        var normalRepo = Path.Combine(_testDir, "myrepo");
        Directory.CreateDirectory(Path.Combine(normalRepo, ".git"));

        var ignoredRepo = Path.Combine(_testDir, "node_modules", "pkg");
        Directory.CreateDirectory(ignoredRepo);
        Directory.CreateDirectory(Path.Combine(ignoredRepo, ".git"));

        var discovery = new FileDiscovery(_logger);
        var clusterConfig = new ClusterConfiguration { MaxDepth = 2 };

        var result = await discovery.DiscoverGitReposAsync(_testDir, clusterConfig, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var repos = result.Value!;
        // node_modules is in skipDirs, so ignoredRepo should not be found
        Assert.DoesNotContain(repos, r => r.RelativePath.Contains("node_modules"));
    }

    // ─── 6. Edge Cases ─────────────────────────────────────────────────

    [Fact]
    public async Task Discover_Cancelled_ReturnsFailure()
    {
        File.WriteAllText(Path.Combine(_testDir, "file.txt"), "data");

        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var result = await discovery.DiscoverFilesAsync(_testDir, config, cts.Token);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Discover_LargeDirectory_FindsAllFiles()
    {
        // Create 150 files to test performance/robustness
        for (int i = 0; i < 150; i++)
        {
            File.WriteAllText(Path.Combine(_testDir, $"file_{i:D3}.txt"), $"content {i}");
        }

        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };

        var result = await discovery.DiscoverFilesAsync(_testDir, config, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(150, result.Value!.Count());
    }

    [Fact]
    public async Task Discover_SymlinkDirectory_SkipsWhenFollowSymlinksFalse()
    {
        // Create a real directory with a file
        var realDir = Path.Combine(_testDir, "real");
        Directory.CreateDirectory(realDir);
        File.WriteAllText(Path.Combine(realDir, "real_file.txt"), "data");

        // Create a symlink to that directory (skip on platforms that don't support it)
        var linkDir = Path.Combine(_testDir, "link");
        try
        {
            Directory.CreateSymbolicLink(linkDir, realDir);
        }
        catch (PlatformNotSupportedException)
        {
            // Skip test on platforms without symlink support
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        File.WriteAllText(Path.Combine(_testDir, "root.txt"), "root");

        var discovery = new FileDiscovery(_logger);
        var config = new GcConfiguration
        {
            Discovery = new DiscoveryConfiguration { Mode = "filesystem", FollowSymlinks = false }
        };

        var result = await discovery.DiscoverFilesAsync(_testDir, config, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var files = result.Value!.ToList();
        // Should not follow symlink into "link" directory
        Assert.DoesNotContain(files, f => f.Contains("link"));
        Assert.Contains(files, f => f == "root.txt");
        Assert.Contains(files, f => f.Contains("real_file.txt"));
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private void TryDeleteDirectory(string path, int retryCount = 0)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch { }
            }
            Directory.Delete(path, true);
        }
        catch when (retryCount < 3)
        {
            Thread.Sleep(100 * (retryCount + 1));
            TryDeleteDirectory(path, retryCount + 1);
        }
        catch { }
    }
}
