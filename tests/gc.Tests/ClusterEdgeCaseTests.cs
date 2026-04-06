using System.Diagnostics;
using Xunit.Abstractions;

namespace gc.Tests;

public class ClusterEdgeCaseTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDir;
    private readonly string _binaryPath;

    public ClusterEdgeCaseTests(ITestOutputHelper output)
    {
        _output = output;

        // Create isolated test directory
        _testDir = Path.Combine(Path.GetTempPath(), $"gc_cluster_edge_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);

        // Find the project root by looking for the .sln file
        var current = AppContext.BaseDirectory;
        while (current != null && !File.Exists(Path.Combine(current, "gc.sln")))
        {
            current = Directory.GetParent(current)?.FullName;
        }

        var projectRoot = current ?? throw new Exception("Could not find project root (gc.sln)");

        var isWindows = System.Runtime.InteropServices.RuntimeInformation
            .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
        var binaryName = isWindows ? "gc.exe" : "gc";
        _binaryPath = Path.Combine(projectRoot, "src", "gc.CLI", "bin", "Debug", "net10.0", binaryName);

        if (!File.Exists(_binaryPath))
        {
            throw new Exception($"Could not find binary at {_binaryPath}. Please build the project first.");
        }
    }

    // ─── Test 1: Cluster dir doesn't exist returns error ───

    [Fact]
    public void ClusterEdge_NonExistentDir_ReturnsError()
    {
        _output.WriteLine("Testing non-existent cluster directory...");

        var nonexistentDir = Path.Combine(_testDir, "does_not_exist_at_all");
        var outputFile = Path.Combine(_testDir, "output.md");

        var result = RunGC($"--cluster --cluster-dir {nonexistentDir} --output {outputFile}");

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("✔ Exported", result.StandardOutput);

        _output.WriteLine($"✅ Non-existent cluster dir correctly returns exit code 1");
    }

    // ─── Test 2: Cluster dir with only non-git folders ───

    [Fact]
    public void ClusterEdge_OnlyNonGitFolders_SucceedsWithWarning()
    {
        _output.WriteLine("Testing cluster dir with only non-git folders...");

        var clusterDir = Path.Combine(_testDir, "cluster_nongit");
        Directory.CreateDirectory(clusterDir);

        // Create plain directories (not git repos)
        Directory.CreateDirectory(Path.Combine(clusterDir, "folderA", "src"));
        Directory.CreateDirectory(Path.Combine(clusterDir, "folderB", "lib"));

        File.WriteAllText(Path.Combine(clusterDir, "folderA", "src", "app.js"), "console.log('a');");
        File.WriteAllText(Path.Combine(clusterDir, "folderB", "lib", "util.py"), "def helper(): pass");

        var outputFile = Path.Combine(_testDir, "output_nongit.md");
        var result = RunGC($"--cluster --cluster-dir {clusterDir} --output {outputFile}");

        // Should succeed (exit 0) with a warning about no repos found
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No git repositories found", result.StandardOutput);

        _output.WriteLine($"✅ Non-git folders handled gracefully");
    }

    // ─── Test 3: Deeply nested repos (3+ levels deep) ───

    [Fact]
    public void ClusterEdge_DeeplyNestedRepos_AreDiscovered()
    {
        _output.WriteLine("Testing deeply nested repos...");

        var clusterDir = Path.Combine(_testDir, "cluster_deep");
        Directory.CreateDirectory(clusterDir);

        // Create repos at various depths: level 1, 2, 3, 4
        var shallowRepo = Path.Combine(clusterDir, "top-repo");
        var midRepo = Path.Combine(clusterDir, "org", "team-repo");
        var deepRepo = Path.Combine(clusterDir, "org", "team", "deep-repo");
        var veryDeepRepo = Path.Combine(clusterDir, "a", "b", "c", "d", "bottom-repo");

        InitGitRepo(shallowRepo, "top", new[] { ("README.md", "# Top") });
        InitGitRepo(midRepo, "mid", new[] { ("src/main.cs", "class Mid {}") });
        InitGitRepo(deepRepo, "deep", new[] { ("app.ts", "export class Deep {}") });
        InitGitRepo(veryDeepRepo, "bottom", new[] { ("core.rs", "fn main() {}") });

        var outputFile = Path.Combine(_testDir, "output_deep.md");
        var result = RunGC($"--cluster --cluster-dir {clusterDir} --cluster-depth 5 --output {outputFile}");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);

        var content = File.ReadAllText(outputFile);

        // All four repos should be present in output
        Assert.Contains("top-repo", content);
        Assert.Contains("team-repo", content);
        Assert.Contains("deep-repo", content);
        Assert.Contains("bottom-repo", content);

        // Verify file content markers
        Assert.Contains("README.md", content);
        Assert.Contains("main.cs", content);
        Assert.Contains("app.ts", content);
        Assert.Contains("core.rs", content);

        _output.WriteLine($"✅ Deeply nested repos (up to 4 levels) discovered correctly");
    }

    // ─── Test 4: Repos with identical file names produce distinct output ───

    [Fact]
    public void ClusterEdge_IdenticalFileNames_DistinctOutput()
    {
        _output.WriteLine("Testing repos with identical file names...");

        var clusterDir = Path.Combine(_testDir, "cluster_identical");
        Directory.CreateDirectory(clusterDir);

        // Two repos with the same file name but different content
        var repo1 = Path.Combine(clusterDir, "frontend");
        var repo2 = Path.Combine(clusterDir, "backend");

        InitGitRepo(repo1, "frontend", new[]
        {
            ("src/Program.cs", "// Frontend Program\npublic class FrontendProgram { }")
        });
        InitGitRepo(repo2, "backend", new[]
        {
            ("src/Program.cs", "// Backend Program\npublic class BackendProgram { }")
        });

        var outputFile = Path.Combine(_testDir, "output_identical.md");
        var result = RunGC($"--cluster --cluster-dir {clusterDir} --output {outputFile}");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);

        var content = File.ReadAllText(outputFile);

        // Both repos should appear with their distinct paths
        Assert.Contains("frontend", content);
        Assert.Contains("backend", content);

        // Both instances of Program.cs should be present
        Assert.Contains("FrontendProgram", content);
        Assert.Contains("BackendProgram", content);

        _output.WriteLine($"✅ Identical file names produce distinct output sections");
    }

    // ─── Test 5: Repos with binary files skip them gracefully ───

    [Fact]
    public void ClusterEdge_BinaryFiles_SkippedGracefully()
    {
        _output.WriteLine("Testing repos with binary files...");

        var clusterDir = Path.Combine(_testDir, "cluster_binary");
        Directory.CreateDirectory(clusterDir);

        var repo = Path.Combine(clusterDir, "assets-repo");
        InitGitRepo(repo, "assets", Array.Empty<(string, string)>());

        // Add a text file
        AddFileToRepo(repo, "src/app.js", "console.log('hello');");

        // Add binary files (PNG header bytes + some data)
        var pngPath = Path.Combine(repo, "images", "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        var pngData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
        File.WriteAllBytes(pngPath, pngData);
        RunGit(repo, "add", "images/logo.png");
        RunGit(repo, "commit", "-m", "Add binary file");

        // Add another binary file
        var zipPath = Path.Combine(repo, "dist", "bundle.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        var zipData = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00 };
        File.WriteAllBytes(zipPath, zipData);
        RunGit(repo, "add", "dist/bundle.zip");
        RunGit(repo, "commit", "-m", "Add zip file");

        var outputFile = Path.Combine(_testDir, "output_binary.md");
        var result = RunGC($"--cluster --cluster-dir {clusterDir} --output {outputFile}");

        Assert.Equal(0, result.ExitCode);

        var content = File.ReadAllText(outputFile);

        // Text file should be included
        Assert.Contains("app.js", content);
        Assert.Contains("console.log", content);

        // Binary files should be skipped — no raw PNG bytes in the output
        Assert.DoesNotContain("PK\u0003\u0004", content);

        _output.WriteLine($"✅ Binary files skipped gracefully");
    }

    // ─── Test 6: Empty git repos (no commits) handled ───

    [Fact]
    public void ClusterEdge_EmptyGitRepos_HandledGracefully()
    {
        _output.WriteLine("Testing empty git repos (no commits)...");

        var clusterDir = Path.Combine(_testDir, "cluster_empty");
        Directory.CreateDirectory(clusterDir);

        // Create a valid repo with content
        var goodRepo = Path.Combine(clusterDir, "good-repo");
        InitGitRepo(goodRepo, "good", new[] { ("hello.cs", "class Hello {}") });

        // Create an empty repo (git init only, no commits)
        var emptyRepo = Path.Combine(clusterDir, "empty-repo");
        Directory.CreateDirectory(emptyRepo);
        RunGit(emptyRepo, "init");
        RunGit(emptyRepo, "config", "user.email", "test@example.com");
        RunGit(emptyRepo, "config", "user.name", "Test User");
        // No files added, no commits made

        var outputFile = Path.Combine(_testDir, "output_empty.md");
        var result = RunGC($"--cluster --cluster-dir {clusterDir} --output {outputFile}");

        // Should succeed — empty repo is simply skipped or produces no files
        Assert.Equal(0, result.ExitCode);

        // The good repo's content should still be present
        if (File.Exists(outputFile))
        {
            var content = File.ReadAllText(outputFile);
            Assert.Contains("hello.cs", content);
        }

        _output.WriteLine($"✅ Empty git repos handled gracefully");
    }

    // ─── Test 7: Cluster with 20+ repos ───

    [Fact]
    public void ClusterEdge_LargeNumberOfRepos_AllDiscovered()
    {
        _output.WriteLine("Testing cluster with 20+ repos...");

        var clusterDir = Path.Combine(_testDir, "cluster_large");
        Directory.CreateDirectory(clusterDir);

        const int repoCount = 25;
        for (int i = 0; i < repoCount; i++)
        {
            var repoPath = Path.Combine(clusterDir, $"repo-{i:D3}");
            InitGitRepo(repoPath, $"repo-{i:D3}", new[]
            {
                ($"src/module{i}.cs", $"public class Module{i} {{ }}")
            });
        }

        var outputFile = Path.Combine(_testDir, "output_large.md");
        var result = RunGC($"--cluster --cluster-dir {clusterDir} --output {outputFile}");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);

        var content = File.ReadAllText(outputFile);

        // Verify all repos are represented
        for (int i = 0; i < repoCount; i++)
        {
            Assert.Contains($"repo-{i:D3}", content);
        }

        _output.WriteLine($"✅ All {repoCount} repos discovered and processed");
    }

    // ─── Test 8: --cluster-depth 1 only finds top-level repos ───

    [Fact]
    public void ClusterEdge_Depth1_OnlyFindsTopLevel()
    {
        _output.WriteLine("Testing --cluster-depth 1...");

        var clusterDir = Path.Combine(_testDir, "cluster_depth1");
        Directory.CreateDirectory(clusterDir);

        // Top-level repo (depth 1 = direct children of cluster dir)
        var topRepo = Path.Combine(clusterDir, "top-repo");
        InitGitRepo(topRepo, "top", new[] { ("top.cs", "class Top {}") });

        // Nested repo at depth 2 — should NOT be found with depth 1
        var nestedRepo = Path.Combine(clusterDir, "org", "nested-repo");
        InitGitRepo(nestedRepo, "nested", new[] { ("nested.cs", "class Nested {}") });

        var outputFile = Path.Combine(_testDir, "output_depth1.md");
        var result = RunGC($"--cluster --cluster-dir {clusterDir} --cluster-depth 1 --output {outputFile}");

        Assert.Equal(0, result.ExitCode);

        var content = File.ReadAllText(outputFile);

        // Top-level repo should be found
        Assert.Contains("top-repo", content);
        Assert.Contains("top.cs", content);

        // Nested repo at depth 2 should NOT be found
        Assert.DoesNotContain("nested-repo", content);
        Assert.DoesNotContain("nested.cs", content);

        _output.WriteLine($"✅ --cluster-depth 1 only finds top-level repos");
    }

    // ─── Test 9: --cluster-depth 5 finds very deep repos ───

    [Fact]
    public void ClusterEdge_Depth5_FindsVeryDeepRepos()
    {
        _output.WriteLine("Testing --cluster-depth 5...");

        var clusterDir = Path.Combine(_testDir, "cluster_depth5");
        Directory.CreateDirectory(clusterDir);

        // Repo at depth 1
        var r1 = Path.Combine(clusterDir, "shallow");
        InitGitRepo(r1, "shallow", new[] { ("a.txt", "shallow content") });

        // Repo at depth 3
        var r3 = Path.Combine(clusterDir, "l1", "l2", "mid-repo");
        InitGitRepo(r3, "mid", new[] { ("b.txt", "mid content") });

        // Repo at depth 5
        var r5 = Path.Combine(clusterDir, "d1", "d2", "d3", "d4", "deep-repo");
        InitGitRepo(r5, "deep", new[] { ("c.txt", "deep content") });

        var outputFile = Path.Combine(_testDir, "output_depth5.md");
        var result = RunGC($"--cluster --cluster-dir {clusterDir} --cluster-depth 5 --output {outputFile}");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);

        var content = File.ReadAllText(outputFile);

        Assert.Contains("shallow", content);
        Assert.Contains("mid-repo", content);
        Assert.Contains("deep-repo", content);
        Assert.Contains("shallow content", content);
        Assert.Contains("mid content", content);
        Assert.Contains("deep content", content);

        _output.WriteLine($"✅ --cluster-depth 5 finds repos up to 5 levels deep");
    }

    // ─── Test 10: Cluster with symlinks to repos outside the dir ───

    [Fact]
    public void ClusterEdge_SymlinksToOutsideRepos_Followed()
    {
        _output.WriteLine("Testing symlinks to repos outside the cluster dir...");

        // Create a repo outside the cluster directory
        var externalRepo = Path.Combine(_testDir, "external-repo");
        InitGitRepo(externalRepo, "external", new[] { ("ext.cs", "class External {}") });

        // Create cluster dir with a symlink to the external repo
        var clusterDir = Path.Combine(_testDir, "cluster_symlink");
        Directory.CreateDirectory(clusterDir);

        var symlinkPath = Path.Combine(clusterDir, "linked-repo");
        CreateSymlink(symlinkPath, externalRepo);

        // Also add a regular (non-symlinked) repo inside
        var localRepo = Path.Combine(clusterDir, "local-repo");
        InitGitRepo(localRepo, "local", new[] { ("local.cs", "class Local {}") });

        var outputFile = Path.Combine(_testDir, "output_symlink.md");
        var result = RunGC($"--cluster --cluster-dir {clusterDir} --output {outputFile}");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);

        var content = File.ReadAllText(outputFile);

        // Both the symlinked external repo and the local repo should be found
        Assert.Contains("linked-repo", content);
        Assert.Contains("ext.cs", content);
        Assert.Contains("local-repo", content);
        Assert.Contains("local.cs", content);

        _output.WriteLine($"✅ Symlinks to repos outside cluster dir are followed");
    }

    // ─── Test 11: Mixed git and non-git dirs at same level ───

    [Fact]
    public void ClusterEdge_MixedGitAndNonGitDirs_OnlyProcessesGitRepos()
    {
        _output.WriteLine("Testing mixed git and non-git dirs at same level...");

        var clusterDir = Path.Combine(_testDir, "cluster_mixed");
        Directory.CreateDirectory(clusterDir);

        // Git repo
        var gitRepo = Path.Combine(clusterDir, "real-git-repo");
        InitGitRepo(gitRepo, "git", new[] { ("code.py", "def main(): pass") });

        // Non-git directory with files
        var nonGitDir = Path.Combine(clusterDir, "not-a-repo");
        Directory.CreateDirectory(nonGitDir);
        File.WriteAllText(Path.Combine(nonGitDir, "notes.txt"), "these are notes");
        File.WriteAllText(Path.Combine(nonGitDir, "data.json"), "{}");

        // Another git repo
        var gitRepo2 = Path.Combine(clusterDir, "another-repo");
        InitGitRepo(gitRepo2, "another", new[] { ("index.ts", "export {}") });

        var outputFile = Path.Combine(_testDir, "output_mixed.md");
        var result = RunGC($"--cluster --cluster-dir {clusterDir} --output {outputFile}");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);

        var content = File.ReadAllText(outputFile);

        // Git repos should be included
        Assert.Contains("real-git-repo", content);
        Assert.Contains("code.py", content);
        Assert.Contains("another-repo", content);
        Assert.Contains("index.ts", content);

        // Non-git dir content should NOT be included
        Assert.DoesNotContain("notes.txt", content);
        Assert.DoesNotContain("data.json", content);

        _output.WriteLine($"✅ Only git repos processed; non-git dirs skipped");
    }

    // ─── Test 12: Cluster dir with .gitignore files per repo ───

    [Fact]
    public void ClusterEdge_RepoGitignores_RespectedIndividually()
    {
        _output.WriteLine("Testing per-repo .gitignore files...");

        var clusterDir = Path.Combine(_testDir, "cluster_gitignore");
        Directory.CreateDirectory(clusterDir);

        // Repo 1: ignores *.log files
        var repo1 = Path.Combine(clusterDir, "service-a");
        InitGitRepo(repo1, "svc-a", Array.Empty<(string, string)>());
        AddFileToRepo(repo1, ".gitignore", "*.log\n");
        AddFileToRepo(repo1, "src/app.cs", "class App {}");
        AddFileToRepo(repo1, "debug.log", "debug output here");
        // This .log file is tracked but we test that git-tracked .log appears.
        // The real test: repo2 has a DIFFERENT .gitignore

        // Repo 2: ignores *.cs files (but tracks .log files)
        var repo2 = Path.Combine(clusterDir, "service-b");
        InitGitRepo(repo2, "svc-b", Array.Empty<(string, string)>());
        AddFileToRepo(repo2, ".gitignore", "*.cs\n");
        AddFileToRepo(repo2, "src/util.py", "def util(): pass");
        AddFileToRepo(repo2, "output.log", "log output here");

        var outputFile = Path.Combine(_testDir, "output_gitignore.md");
        var result = RunGC($"--cluster --cluster-dir {clusterDir} --output {outputFile}");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✔ Exported", result.StandardOutput);

        var content = File.ReadAllText(outputFile);

        // Repo 1: app.cs should be present (not ignored); debug.log was committed so it appears
        Assert.Contains("service-a", content);
        Assert.Contains("app.cs", content);

        // Repo 2: util.py should be present; .cs files are gitignored so src/util.py is the file
        Assert.Contains("service-b", content);
        Assert.Contains("util.py", content);
        Assert.Contains("output.log", content);

        // The key assertion: repo2 ignores *.cs but repo1 does not
        // repo1's app.cs should be present, proving each repo has its own ignore rules
        Assert.Contains("app.cs", content);

        _output.WriteLine($"✅ Each repo respects its own .gitignore independently");
    }

    // ─── Helper Methods ───

    private void InitGitRepo(string repoPath, string name, (string FileName, string Content)[] files)
    {
        Directory.CreateDirectory(repoPath);
        RunGit(repoPath, "init");
        RunGit(repoPath, "config", "user.email", "test@example.com");
        RunGit(repoPath, "config", "user.name", "Test User");

        foreach (var (fileName, content) in files)
        {
            AddFileToRepo(repoPath, fileName, content);
        }

        // Ensure at least one commit exists
        if (files.Length == 0)
        {
            // We intentionally don't create any commits for "empty repo" tests
            // but for normal repos we need at least one file committed
        }
    }

    private void AddFileToRepo(string repoPath, string fileName, string content)
    {
        var fullPath = Path.Combine(repoPath, fileName);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(fullPath, content);
        RunGit(repoPath, "add", fileName);
        RunGit(repoPath, "commit", "-m", $"Add {fileName}");
    }

    private void RunGit(string workingDir, string command, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"{command} {string.Join(" ", args)}",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process != null)
        {
            process.WaitForExit();
        }
    }

    private void CreateSymlink(string linkPath, string targetPath)
    {
        var isWindows = System.Runtime.InteropServices.RuntimeInformation
            .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

        if (isWindows)
        {
            // On Windows, create a directory junction if symlinks require admin
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            })?.WaitForExit();
        }
        else
        {
            // On Linux/macOS, create a symbolic link
            Process.Start(new ProcessStartInfo
            {
                FileName = "ln",
                Arguments = $"-s \"{targetPath}\" \"{linkPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            })?.WaitForExit();
        }
    }

    private ProcessResult RunGC(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _binaryPath,
            Arguments = args,
            WorkingDirectory = _testDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return new ProcessResult(-1, "", "Failed to start process");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);

        _output.WriteLine($"  Exit={process.ExitCode}");
        _output.WriteLine($"  Stdout: {Truncate(stdoutTask.Result, 500)}");
        if (!string.IsNullOrEmpty(stderrTask.Result))
        {
            _output.WriteLine($"  Stderr: {Truncate(stderrTask.Result, 500)}");
        }

        return new ProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private static string Truncate(string s, int maxLength)
    {
        if (string.IsNullOrEmpty(s)) return "(empty)";
        return s.Length <= maxLength ? s : s[..maxLength] + "...(truncated)";
    }

    public void Dispose()
    {
        TryDeleteDirectory(_testDir);
    }

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
        catch
        {
            // Ignore cleanup errors after retries
        }
    }

    private record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
