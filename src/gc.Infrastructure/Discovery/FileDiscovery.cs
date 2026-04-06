using System.Buffers;
using System.Diagnostics;
using System.Text;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.Infrastructure.Discovery;

public sealed class FileDiscovery : IFileDiscovery
{
    private readonly ILogger _logger;

    public FileDiscovery(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<Result<IEnumerable<string>>> DiscoverFilesAsync(string rootPath, GcConfiguration config, CancellationToken ct = default)
    {
        try
        {
            var discoveryConfig = config.Discovery ?? new DiscoveryConfiguration();
            var mode = discoveryConfig.Mode.ToLowerInvariant();

            if (mode == "git")
            {
                if (!await IsGitRepositoryAsync(rootPath, ct))
                {
                    return Result<IEnumerable<string>>.Failure("Not a git repository. git discovery mode requires a git repository.");
                }

                return await DiscoverWithGitAsync(rootPath, discoveryConfig, ct);
            }

            // Phase 0.2: In auto mode, skip IsGitRepositoryAsync entirely.
            // Just try git ls-files directly — if it fails, fall back to filesystem.
            // This eliminates one process spawn (~2-5ms) on every run.
            if (mode == "auto")
            {
                var gitFiles = await DiscoverWithGitAsync(rootPath, discoveryConfig, ct);
                if (gitFiles.IsSuccess)
                {
                    return gitFiles;
                }
            }

            return await DiscoverWithFileSystemAsync(rootPath, discoveryConfig, ct);
        }
        catch (Exception ex)
        {
            _logger.Error("File discovery failed", ex);
            return Result<IEnumerable<string>>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Discovers all git repositories within a cluster directory.
    /// Scans the directory tree looking for .git folders/submodules and returns info about each repo.
    /// Handles edge cases: nested repos, submodules, permission errors, empty dirs, symlinks.
    /// </summary>
    public async Task<Result<IReadOnlyList<RepoInfo>>> DiscoverGitReposAsync(string clusterRoot, ClusterConfiguration clusterConfig, CancellationToken ct = default)
    {
        try
        {
            if (!Directory.Exists(clusterRoot))
            {
                return Result<IReadOnlyList<RepoInfo>>.Failure($"Cluster directory does not exist: {clusterRoot}");
            }

            var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "node_modules", ".git", ".svn", ".hg", "bin", "obj", "dist", "build",
                "target", "out", ".vs", ".idea", "coverage", ".next", ".nuxt",
                "__pycache__", "venv", ".env"
            };

            // Add user-specified skip directories
            if (clusterConfig.SkipDirectories != null)
            {
                foreach (var skip in clusterConfig.SkipDirectories)
                {
                    if (!string.IsNullOrWhiteSpace(skip))
                        skipDirs.Add(skip);
                }
            }

            var maxDepth = clusterConfig.MaxDepth > 0 ? clusterConfig.MaxDepth : 2;
            var repos = new List<RepoInfo>();
            var visitedRealPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await ScanForGitReposAsync(clusterRoot, clusterRoot, skipDirs, maxDepth, repos, visitedRealPaths, ct);

            if (repos.Count == 0)
            {
                _logger.Warning($"No git repositories found in {clusterRoot}");
                return Result<IReadOnlyList<RepoInfo>>.Success(repos);
            }

            // Validate each discovered repo
            for (int i = 0; i < repos.Count; i++)
            {
                var repo = repos[i];
                if (!repo.IsValid)
                {
                    // Already marked invalid during scan
                    continue;
                }

                var isValid = await IsGitRepositoryAsync(repo.RootPath, ct);
                repos[i] = repo with { IsValid = isValid, Error = isValid ? null : "Git validation failed" };
            }

            // Filter to only valid repos
            var validRepos = repos.Where(r => r.IsValid).ToList();
            var invalidCount = repos.Count - validRepos.Count;

            if (invalidCount > 0)
            {
                _logger.Warning($"{invalidCount} repo(s) failed validation and were skipped");
                foreach (var invalid in repos.Where(r => !r.IsValid))
                {
                    _logger.Debug($"  Skipped: {invalid.RelativePath} - {invalid.Error}");
                }
            }

            _logger.Success($"Discovered {validRepos.Count} git repos in cluster directory");
            return Result<IReadOnlyList<RepoInfo>>.Success(validRepos);
        }
        catch (OperationCanceledException)
        {
            return Result<IReadOnlyList<RepoInfo>>.Failure("Operation cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error("Cluster discovery failed", ex);
            return Result<IReadOnlyList<RepoInfo>>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Recursively scans a directory for git repositories.
    /// Handles edge cases: symlink cycles, permission errors, nested git repos.
    /// Stops descending into a directory once a .git is found (it's a repo root, not a parent).
    /// </summary>
    private async Task ScanForGitReposAsync(
        string currentDir,
        string clusterRoot,
        HashSet<string> skipDirs,
        int maxDepth,
        List<RepoInfo> repos,
        HashSet<string> visitedRealPaths,
        CancellationToken ct,
        int currentDepth = 0)
    {
        ct.ThrowIfCancellationRequested();

        // Resolve real path to detect symlink cycles
        string realPath;
        try
        {
            realPath = Path.GetFullPath(currentDir);
            var dirInfo = new DirectoryInfo(currentDir);
            var linkTarget = dirInfo.ResolveLinkTarget(returnFinalTarget: true);
            if (linkTarget != null)
            {
                realPath = linkTarget.FullName;
            }
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Debug($"Access denied scanning: {currentDir}");
            return;
        }
        catch (IOException ex)
        {
            _logger.Debug($"I/O error scanning: {currentDir} - {ex.Message}");
            return;
        }

        if (!visitedRealPaths.Add(realPath))
        {
            // Symlink cycle detected
            _logger.Debug($"Skipping already-visited path (possible symlink cycle): {currentDir}");
            return;
        }

        // Check if this directory is itself a git repo
        bool isGitRepo = false;
        try
        {
            // Check for .git directory (standard repo) or .git file (submodule/worktree)
            var gitPath = Path.Combine(currentDir, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                isGitRepo = true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Debug($"Access denied checking .git in: {currentDir}");
            return;
        }

        if (isGitRepo)
        {
            var relativePath = Path.GetRelativePath(clusterRoot, currentDir).Replace('\\', '/');
            var name = Path.GetFileName(currentDir);
            repos.Add(new RepoInfo
            {
                RootPath = currentDir,
                RelativePath = relativePath,
                Name = name,
                IsValid = true // Will be validated later
            });
            _logger.Debug($"Found git repo: {relativePath}");

            // IMPORTANT: Do NOT descend into a git repo's subdirectories looking for more repos
            // UNLESS we want to support nested repos (submodules). For submodules, .git is a file
            // pointing to ../.git/modules/..., and the submodule's own submodules would be in .git/modules/.
            // We skip descending to avoid duplicate discovery of parent repos.
            return;
        }

        // Not a git repo — scan subdirectories if we haven't exceeded max depth
        if (currentDepth >= maxDepth)
        {
            return;
        }

        string[] subdirectories;
        try
        {
            subdirectories = Directory.GetDirectories(currentDir);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Debug($"Access denied listing directory: {currentDir}");
            return;
        }
        catch (IOException ex)
        {
            _logger.Debug($"I/O error listing directory: {currentDir} - {ex.Message}");
            return;
        }

        foreach (var subdir in subdirectories)
        {
            ct.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(subdir);

            // Skip ignored directories
            if (skipDirs.Contains(dirName))
            {
                continue;
            }

            // Skip hidden directories (start with .) unless they might be project roots
            // e.g., don't skip .config, .github repos
            if (dirName.StartsWith('.') && dirName != ".github")
            {
                // Still scan .github directories but skip most hidden dirs
                if (dirName.Length > 1 && !dirName.Equals(".github", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            // Skip symlink directories when FollowSymlinks is false
            try
            {
                var subdirInfo = new DirectoryInfo(subdir);
                if ((subdirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    // Symlink — still scan it (cycle detection is handled above)
                    // but log it for debug visibility
                    _logger.Debug($"Following symlink: {subdir}");
                }
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            await ScanForGitReposAsync(subdir, clusterRoot, skipDirs, maxDepth, repos, visitedRealPaths, ct, currentDepth + 1);
        }
    }

    private async Task<bool> IsGitRepositoryAsync(string rootPath, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --is-inside-work-tree",
                WorkingDirectory = rootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch (IOException ex)
        {
            _logger.Error("Git executable not found or failed to start", ex);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error($"Access denied when checking git repository in {rootPath}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error checking if {rootPath} is a git repository", ex);
            return false;
        }
    }

    private async Task<Result<IEnumerable<string>>> DiscoverWithGitAsync(string rootPath, DiscoveryConfiguration config, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "ls-files -z --cached --others --exclude-standard",
                WorkingDirectory = rootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return Result<IEnumerable<string>>.Failure("Failed to start git process");

            // Phase 1.3: Pre-size list to avoid repeated resizing (typical repos have 50-500 files)
            var files = new List<string>(256);
            var stream = process.StandardOutput.BaseStream;
            // Phase 0.5: 64KB buffer instead of 4KB — reduces read syscalls by 16x
            var buffer = ArrayPool<byte>.Shared.Rent(65536);
            try
            {
            int bytesRead;
            var position = 0;

            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(position, buffer.Length - position), ct)) > 0)
            {
                var totalBytes = position + bytesRead;
                var start = 0;

                for (var i = 0; i < totalBytes; i++)
                {
                    if (buffer[i] == 0)
                    {
                        var span = buffer.AsSpan(start, i - start);
                        if (span.Length > 0)
                        {
                            bool shouldAdd = true;
                            if (config.MaxDepth.HasValue)
                            {
                                var depth = span.Count((byte)'/') + span.Count((byte)'\\');
                                if (depth > config.MaxDepth.Value)
                                {
                                    shouldAdd = false;
                                }
                            }

                            if (shouldAdd)
                            {
                                files.Add(Encoding.UTF8.GetString(span));
                            }
                        }
                        start = i + 1;
                    }
                }

                // Handle remaining partial data
                if (start < totalBytes)
                {
                    var remaining = totalBytes - start;
                    // Prevent buffer overflow: if remaining data is too large, we have a problem
                    if (remaining >= buffer.Length)
                    {
                        // This should never happen, but guard against it
                        _logger.Error($"Buffer overflow detected in git ls-files parsing. Remaining: {remaining}, Buffer: {buffer.Length}");
                        // CRITICAL: Must kill process before breaking to avoid pipe deadlock
                        try { process.Kill(true); } catch { }
                        break;
                    }
                    Array.Copy(buffer, start, buffer, 0, remaining);
                    position = remaining;
                }
                else
                {
                    position = 0;
                }
            }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(ct);
                return Result<IEnumerable<string>>.Failure($"Git command failed: {error}");
            }

            return Result<IEnumerable<string>>.Success(files);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<string>>.Failure(ex.Message);
        }
    }

    private async Task<Result<IEnumerable<string>>> DiscoverWithFileSystemAsync(string rootPath, DiscoveryConfiguration config, CancellationToken ct)
    {
        var files = new List<string>();
        var ignoredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", ".svn", ".hg", "bin", "obj", "dist", "build",
            "target", "out", ".vs", ".idea", "coverage", ".next", ".nuxt",
            "__pycache__", "venv", ".env", ".env.local", "node_modules_cache"
        };

        var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((rootPath, 0));

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (currentDir, depth) = queue.Dequeue();
            
            var realPath = Path.GetFullPath(currentDir);
            if (config.FollowSymlinks)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(currentDir);
                    var linkTarget = dirInfo.ResolveLinkTarget(returnFinalTarget: true);
                    if (linkTarget != null)
                    {
                        realPath = linkTarget.FullName;
                    }
                }
                catch { }
            }

            if (!visitedPaths.Add(realPath)) continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(currentDir, "*", SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();
                    files.Add(Path.GetRelativePath(rootPath, file).Replace('\\', '/'));
                }

                if (!config.MaxDepth.HasValue || depth < config.MaxDepth.Value)
                {
                    foreach (var dir in Directory.EnumerateDirectories(currentDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        ct.ThrowIfCancellationRequested();
                        var dirName = Path.GetFileName(dir);
                        if (!ignoredDirs.Contains(dirName))
                        {
                            // Skip symlinks when FollowSymlinks is false to prevent infinite loops
                            if (!config.FollowSymlinks)
                            {
                                try
                                {
                                    var dirInfo = new DirectoryInfo(dir);
                                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                                    {
                                        // This is a symlink, skip it
                                        continue;
                                    }
                                }
                                catch { }
                            }
                            
                            queue.Enqueue((dir, depth + 1));
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return Result<IEnumerable<string>>.Success(files);
    }
}
