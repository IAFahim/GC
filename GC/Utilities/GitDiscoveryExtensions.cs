using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using GC.Data;

namespace GC.Utilities;

public static class GitDiscoveryExtensions
{
    public static string[] DiscoverFiles(this CliArguments args)
    {
        using var _ = Logger.TimeOperation("Git file discovery");

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
                Logger.LogError("Failed to start git process");
                Environment.Exit(1);
                return[];
            }

            checkGitProcess.WaitForExit();

            if (checkGitProcess.ExitCode != 0)
            {
                Logger.LogError("Git not found. Please install git from https://git-scm.com");
                Console.Error.WriteLine("Git is required to use this tool.");
                Console.Error.WriteLine("Install from: https://git-scm.com/downloads");
                Environment.Exit(1);
                return[];
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
                Logger.LogError("Failed to start git process");
                Environment.Exit(1);
                return[];
            }

            checkRepoProcess.WaitForExit();

            if (checkRepoProcess.ExitCode != 0)
            {
                Logger.LogError("Not a git repository");
                Console.Error.WriteLine("This tool must be run from inside a git repository.");
                Console.Error.WriteLine("Current directory is not a git repository.");
                Environment.Exit(1);
                return[];
            }
        }

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
            return[];
        }

        var files = new List<string>(1024);
        var buffer = new byte[4096];
        var currentFile = new List<byte>(256);

        using var stream = process.StandardOutput.BaseStream;
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                {
                    files.Add(Encoding.UTF8.GetString(currentFile.ToArray()));
                    currentFile.Clear();
                }
                else
                {
                    currentFile.Add(buffer[i]);
                }
            }
        }

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            Logger.LogError($"Git command failed with exit code {process.ExitCode}", new Exception(error));
            return[];
        }

        Logger.LogVerbose($"Discovered {files.Count} files from git");
        Logger.LogDebug($"Git discovery completed. Exit code: {process.ExitCode}");

        return files.ToArray();
    }
}