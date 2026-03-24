using System.Diagnostics;
using System.Text;
using gc.Domain.Common;
using gc.Domain.Interfaces;
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

            if (mode == "auto" && await IsGitRepositoryAsync(rootPath, ct))
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

            var files = new List<string>();
            var stream = process.StandardOutput.BaseStream;
            var buffer = new byte[4096];
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
                        var fileName = Encoding.UTF8.GetString(buffer, start, i - start);
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            if (config.MaxDepth.HasValue)
                            {
                                var depth = fileName.Count(c => c == '/' || c == '\\');
                                if (depth <= config.MaxDepth.Value)
                                {
                                    files.Add(fileName);
                                }
                            }
                            else
                            {
                                files.Add(fileName);
                            }
                        }
                        start = i + 1;
                    }
                }

                if (start < totalBytes)
                {
                    var remaining = totalBytes - start;
                    Array.Copy(buffer, start, buffer, 0, remaining);
                    position = remaining;
                }
                else
                {
                    position = 0;
                }
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
                    files.Add(Path.GetRelativePath(rootPath, file).Replace('\\', '/'));
                }

                if (!config.MaxDepth.HasValue || depth < config.MaxDepth.Value)
                {
                    foreach (var dir in Directory.EnumerateDirectories(currentDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (!ignoredDirs.Contains(dirName))
                        {
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