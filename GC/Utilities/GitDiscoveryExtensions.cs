using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using GC.Data;

namespace GC.Utilities;

public static class GitDiscoveryExtensions
{
    public static string[] DiscoverFiles(this CliArguments args)
    {
        using var _ = Logger.TimeOperation("File discovery");

        // Determine discovery mode
        var useGit = ShouldUseGitDiscovery(args);

        if (useGit)
        {
            // Try git-based discovery first
            var gitFiles = TryDiscoverWithGit(args);
            if (gitFiles != null)
            {
                return gitFiles;
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
        int start = 0;
        int position = 0;

        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)  // Null terminator found
                {
                    // Calculate the length of the filename
                    var fileNameLength = position - start;

                    // Create a span from the buffer for this filename
                    var fileNameSpan = buffer.AsSpan(start, fileNameLength);

                    // Convert directly from span - no intermediate allocation
                    var fileName = Encoding.UTF8.GetString(fileNameSpan);
                    files.Add(fileName);

                    // Move to next filename
                    start = i + 1;
                    position = start;
                }
                else
                {
                    position++;
                }
            }

            // Reset position for next buffer read, keep overflow data
            if (start < bytesRead)
            {
                // Copy remaining data to start of buffer
                var remaining = bytesRead - start;
                Array.Copy(buffer, start, buffer, 0, remaining);
                start = 0;
                position = remaining;
            }
            else
            {
                start = 0;
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
        var ignoredPatterns = Constants.SystemIgnoredPatterns.ToList();

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
        var fileName = Path.GetFileName(path).ToLowerInvariant();

        // Check exact matches
        if (ignoredPatterns.Contains(fileName))
        {
            return true;
        }

        // Check pattern matches (simple wildcard support)
        foreach (var pattern in ignoredPatterns)
        {
            if (pattern.StartsWith("*") && fileName.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (pattern.EndsWith("*") && fileName.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}