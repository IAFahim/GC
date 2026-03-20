using System.Diagnostics;
using System.Text;
using gc.Data;

namespace gc.Utilities;

public static class GitDiscoveryExtensions
{
    public static string[] DiscoverFiles(this CliArguments args)
    {
        using var _ = Logger.TimeOperation("File discovery");

        var useGit = ShouldUseGitDiscovery(args);

        if (useGit)
        {
            var gitFiles = TryDiscoverWithGit(args);
            if (gitFiles != null) return gitFiles;

            if (args.DiscoveryMode == DiscoveryMode.Git)
            {
                Logger.LogError("Git discovery failed but was explicitly requested.");
                return Array.Empty<string>();
            }
        }

        Logger.LogVerbose("Using file system discovery");
        return DiscoverWithFileSystem(args);
    }

    private static bool ShouldUseGitDiscovery(CliArguments args)
    {
        return args.DiscoveryMode switch
        {
            DiscoveryMode.Git => true,
            DiscoveryMode.FileSystem => false,
            DiscoveryMode.Auto => true,
            _ => true
        };
    }

    private static string[]? TryDiscoverWithGit(CliArguments args)
    {
        try
        {
            Logger.LogDebug("Checking if git is installed...");
            var checkGitPsi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var checkGitProcess = Process.Start(checkGitPsi))
            {
                if (checkGitProcess == null)
                {
                    Logger.LogDebug("Failed to start git process");
                    return null;
                }

                checkGitProcess.WaitForExit();

                if (checkGitProcess.ExitCode != 0)
                {
                    Logger.LogDebug("Git not found");
                    return null;
                }
            }

            Logger.LogDebug("Checking if current directory is a git repository...");
            var checkRepoPsi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --git-dir",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var checkRepoProcess = Process.Start(checkRepoPsi))
            {
                if (checkRepoProcess == null)
                {
                    Logger.LogDebug("Failed to start git process");
                    return null;
                }

                checkRepoProcess.WaitForExit();

                if (checkRepoProcess.ExitCode != 0)
                {
                    Logger.LogDebug("Not a git repository");
                    return null;
                }
            }

            return DiscoverWithGitInternal(args);
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"Git discovery failed: {ex.Message}");
            return null;
        }
    }

    private static string[] DiscoverWithGitInternal(CliArguments args)
    {
        var gitArgs = "ls-files -z --cached";
        Logger.LogDebug($"Executing: git {gitArgs}");

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = gitArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            Logger.LogError("Failed to start git process");
            return Array.Empty<string>();
        }

        var files = new List<string>(1024);
        var buffer = new byte[4096];

        using var stream = process.StandardOutput.BaseStream;
        int bytesRead;
        var position = 0;

        while ((bytesRead = stream.Read(buffer, position, buffer.Length - position)) > 0)
        {
            var totalBytes = position + bytesRead;
            var start = 0;

            for (var i = position; i < totalBytes; i++)
                if (buffer[i] == 0)
                {
                    var fileNameLength = i - start;

                    var fileNameSpan = buffer.AsSpan(start, fileNameLength);

                    var fileName = Encoding.UTF8.GetString(fileNameSpan);
                    files.Add(fileName);

                    start = i + 1;
                }

            if (start < totalBytes)
            {
                var remaining = totalBytes - start;

                if (remaining == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);

                Array.Copy(buffer, start, buffer, 0, remaining);
                position = remaining;
            }
            else
            {
                position = 0;
            }
        }

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            Logger.LogError($"Git command failed with exit code {process.ExitCode}", new Exception(error));
            return Array.Empty<string>();
        }

        Logger.LogVerbose($"Discovered {files.Count} files from git");
        Logger.LogDebug($"Git discovery completed. Exit code: {process.ExitCode}");

        return files.ToArray();
    }

    private static string[] DiscoverWithFileSystem(CliArguments args)
    {
        Logger.LogVerbose("Using file system discovery...");

        var currentDir = Directory.GetCurrentDirectory();
        var allFiles = EnumerateFiles(currentDir);

        Logger.LogVerbose($"Discovered {allFiles.Length} files from file system");

        return allFiles;
    }

    private static string[] EnumerateFiles(string rootPath)
    {
        var files = new List<string>(1024);
        var ignoredPatterns = BuiltInPresets.SystemIgnoredPatterns.ToList();

        var gitignorePath = Path.Combine(rootPath, ".gitignore");
        if (File.Exists(gitignorePath))
            try
            {
                var lines = File.ReadAllLines(gitignorePath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                    if (trimmed.StartsWith("/")) trimmed = trimmed.Substring(1);
                    ignoredPatterns.Add(trimmed);
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"Failed to read .gitignore: {ex.Message}");
            }

        var ignoredDirs = new[]
        {
            "node_modules", ".git", ".svn", ".hg", "bin", "obj", "dist", "build",
            "target", "out", ".vs", ".idea", "coverage", ".next", ".nuxt",
            "__pycache__", "venv", ".env", ".env.local", "node_modules_cache"
        };

        try
        {
            foreach (var file in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(rootPath, file);

                var pathParts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (pathParts.Any(part => ignoredDirs.Contains(part, StringComparer.OrdinalIgnoreCase)))
                {
                    Logger.LogDebug($"Skipping ignored directory: {relativePath}");
                    continue;
                }

                if (ShouldIgnoreFile(relativePath, ignoredPatterns))
                {
                    Logger.LogDebug($"Skipping ignored file: {relativePath}");
                    continue;
                }

                files.Add(relativePath.Replace('\\', '/'));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error enumerating files: {ex.Message}");
        }

        return files.ToArray();
    }

    private static bool ShouldIgnoreFile(string path, List<string> ignoredPatterns)
    {
        var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();

        foreach (var pattern in ignoredPatterns)
        {
            var normalizedPattern = pattern.Replace('\\', '/').ToLowerInvariant();

            if (normalizedPattern.EndsWith("/"))
            {
                var dirPattern = normalizedPattern.TrimEnd('/');
                var pathParts = normalizedPath.Split('/');

                if (pathParts.Contains(dirPattern)) return true;
            }
            else
            {
                var fileName = Path.GetFileName(normalizedPath);

                if (normalizedPattern == fileName) return true;

                if (normalizedPattern.StartsWith("*") && fileName.EndsWith(normalizedPattern.Substring(1))) return true;

                if (normalizedPattern.EndsWith("*") &&
                    fileName.StartsWith(normalizedPattern.Substring(0, normalizedPattern.Length - 1))) return true;

                if (normalizedPattern.StartsWith("*.") && fileName.EndsWith(normalizedPattern.Substring(1)))
                    return true;
            }
        }

        return false;
    }
}