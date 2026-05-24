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

            for (int i = 0; i < repos.Count; i++)
            {
                var repo = repos[i];
                if (!repo.IsValid)
                {
                    continue;
                }

                var isValid = await IsGitRepositoryAsync(repo.RootPath, ct);
                repos[i] = repo with { IsValid = isValid, Error = isValid ? null : "Git validation failed" };
            }

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
            _logger.Debug($"Skipping already-visited path (possible symlink cycle): {currentDir}");
            return;
        }

        bool isGitRepo = false;
        try
        {
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
                IsValid = true
            });
            _logger.Debug($"Found git repo: {relativePath}");

            return;
        }

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

            if (skipDirs.Contains(dirName))
            {
                continue;
            }

            if (dirName.StartsWith('.') && dirName != ".github")
            {
                if (dirName.Length > 1 && !dirName.Equals(".github", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            try
            {
                var subdirInfo = new DirectoryInfo(subdir);
                if ((subdirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
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

            var files = new List<string>(256);
            var stream = process.StandardOutput.BaseStream;
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
                                    int depth = 0;
                                    foreach (var b in span)
                                    {
                                        if (b == (byte)'/' || b == (byte)'\\')
                                        {
                                            depth++;
                                        }
                                    }
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

                    if (start < totalBytes)
                    {
                        var remaining = totalBytes - start;
                        if (remaining >= buffer.Length)
                        {
                            _logger.Error($"Buffer overflow detected in git ls-files parsing. Remaining: {remaining}, Buffer: {buffer.Length}");
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
                            if (!config.FollowSymlinks)
                            {
                                try
                                {
                                    var dirInfo = new DirectoryInfo(dir);
                                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                                    {
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
