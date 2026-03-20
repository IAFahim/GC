using System.Diagnostics;
using System.Text;
using gc.Data;

namespace gc.Utilities;

public static class GitDiscoveryExtensions
{
    public static string[] DiscoverFiles(this CliArguments args)
    {
        using var _ = Logger.TimeOperation("File discovery");

        // Determine discovery mode
        var useGit = ShouldUseGitDiscovery(args);

        if (useGit)
        {
            // Try git-based discovery
            var gitFiles = TryDiscoverWithGit(args);
            if (gitFiles != null)
            {
                return gitFiles;
            }

            // If we specifically requested git mode and it failed, throw or return empty
            if (args.DiscoveryMode == DiscoveryMode.Git)
            {
                Logger.LogError("Git discovery failed but was explicitly requested.");
                return Array.Empty<string>();
            }
        }

        // Fallback to file system discovery
        Logger.LogVerbose("Using file system discovery");
        return DiscoverWithFileSystem(args);
    }

    private static bool ShouldUseGitDiscovery(CliArguments args)
    {
        return args.DiscoveryMode switch
        {
            DiscoveryMode.Git => true,
            DiscoveryMode.FileSystem => false,
            DiscoveryMode.Auto => true, // Try git first, fallback to filesystem
            _ => true
        };
    }

    private static string[]? TryDiscoverWithGit(CliArguments args)
    {
        try
        {
            // First check if git is available
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

            // Check if we're in a git repository
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

            // Use git to discover files
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
        var gitArgs = "ls-files -z --cached --others --exclude-standard";
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
        int position = 0;

        while ((bytesRead = stream.Read(buffer, position, buffer.Length - position)) > 0)
        {
            int totalBytes = position + bytesRead;
            int start = 0;

            for (var i = position; i < totalBytes; i++)
            {
                if (buffer[i] == 0)  // Null terminator found
                {
                    // Calculate the length of the filename
                    var fileNameLength = i - start;

                    // Create a span from the buffer for this filename
                    var fileNameSpan = buffer.AsSpan(start, fileNameLength);

                    // Convert directly from span - no intermediate allocation
                    var fileName = Encoding.UTF8.GetString(fileNameSpan);
                    files.Add(fileName);

                    // Move to next filename
                    start = i + 1;
                }
            }

            // Reset position for next buffer read, keep overflow data
            if (start < totalBytes)
            {
                var remaining = totalBytes - start;
                
                // If the remaining string is equal to the buffer length, we need a bigger buffer
                if (remaining == buffer.Length)
                {
                    Array.Resize(ref buffer, buffer.Length * 2);
                }
                
                // Copy remaining data to start of buffer
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

        // Try to read .gitignore
        var gitignorePath = Path.Combine(rootPath, ".gitignore");
        if (File.Exists(gitignorePath))
        {
            try
            {
                var lines = File.ReadAllLines(gitignorePath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                    
                    // Simple conversion from gitignore to our ignore format
                    if (trimmed.StartsWith("/")) trimmed = trimmed.Substring(1);
                    ignoredPatterns.Add(trimmed);
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"Failed to read .gitignore: {ex.Message}");
            }
        }

        // Add common build/directory patterns
        var ignoredDirs = new[]
        {
            "node_modules", ".git", ".svn", ".hg", "bin", "obj", "dist", "build",
            "target", "out", ".vs", ".idea", "coverage", ".next", ".nuxt",
            "__pycache__", "venv", ".env", ".env.local", "node_modules_cache"
        };

        try
        {
            // Enumerate all files recursively
            foreach (var file in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(rootPath, file);

                // Skip if in ignored directory
                var pathParts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (pathParts.Any(part => ignoredDirs.Contains(part, StringComparer.OrdinalIgnoreCase)))
                {
                    Logger.LogDebug($"Skipping ignored directory: {relativePath}");
                    continue;
                }

                // Skip if matches ignored patterns
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
        // Normalize path to use forward slashes and lowercase
        var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();

        foreach (var pattern in ignoredPatterns)
        {
            var normalizedPattern = pattern.Replace('\\', '/').ToLowerInvariant();

            // Directory pattern (ends with /)
            if (normalizedPattern.EndsWith("/"))
            {
                // Check if any directory in the path matches the pattern
                var dirPattern = normalizedPattern.TrimEnd('/');
                var pathParts = normalizedPath.Split('/');

                if (pathParts.Contains(dirPattern))
                {
                    return true;
                }
            }
            // File pattern
            else
            {
                var fileName = Path.GetFileName(normalizedPath);

                // Exact filename match
                if (normalizedPattern == fileName)
                {
                    return true;
                }

                // Wildcard patterns
                if (normalizedPattern.StartsWith("*") && fileName.EndsWith(normalizedPattern.Substring(1)))
                {
                    return true;
                }

                if (normalizedPattern.EndsWith("*") && fileName.StartsWith(normalizedPattern.Substring(0, normalizedPattern.Length - 1)))
                {
                    return true;
                }

                // Extension pattern (e.g., *.log)
                if (normalizedPattern.StartsWith("*.") && fileName.EndsWith(normalizedPattern.Substring(1)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}