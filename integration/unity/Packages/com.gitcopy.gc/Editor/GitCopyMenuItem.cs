using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GitCopy.Editor
{
    public static class GitCopyMenuItem
    {
        private const string GcMenuRoot = "Assets/";

        [MenuItem(GcMenuRoot + "Git Copy", false, 100)]
        private static void CopyToClipboard()
        {
            var projectPath = GetProjectPath();
            var selectedPath = GetSelectedPath();

            if (string.IsNullOrEmpty(selectedPath))
            {
                Debug.LogWarning("GitCopy: Please select a folder or file in the Project window first.");
                return;
            }

            if (!File.Exists(projectPath + "/.git/config"))
            {
                Debug.LogError("GitCopy: Not a git repository. Please initialize git first.");
                return;
            }

            var gcPath = FindGcExecutable();

            if (string.IsNullOrEmpty(gcPath))
            {
                Debug.LogError("GitCopy: gc CLI tool not found. Install from: https://github.com/IAFahim/gc");
                return;
            }

            try
            {
                var relativePath = GetRelativePath(selectedPath, projectPath);
                var arguments = $"--paths \"{relativePath}\"";

                var processInfo = new ProcessStartInfo
                {
                    FileName = gcPath,
                    Arguments = arguments,
                    WorkingDirectory = projectPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);

                if (process == null)
                {
                    Debug.LogError("GitCopy: Failed to start gc process");
                    return;
                }

                process.WaitForExit();

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                if (process.ExitCode == 0)
                {
                    var folderName = Path.GetFileName(selectedPath);
                    Debug.Log($"GitCopy: '{folderName}' copied to clipboard!");
                    if (!string.IsNullOrEmpty(output))
                        Debug.Log($"GitCopy output:\n{output}");
                }
                else
                {
                    Debug.LogError($"GitCopy: gc failed with exit code {process.ExitCode}\n{error}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"GitCopy: Exception occurred: {ex.Message}\n{ex.StackTrace}");
            }
        }

        [MenuItem(GcMenuRoot + "Git Copy", true)]
        private static bool ValidateGitCopy()
        {
            return IsGitRepository() && Selection.activeObject != null;
        }

        private static string GetSelectedPath()
        {
            var selectedObject = Selection.activeObject;

            if (selectedObject == null)
                return null;

            var assetPath = AssetDatabase.GetAssetPath(selectedObject);

            if (string.IsNullOrEmpty(assetPath))
                return null;

            var fullPath = Path.Combine(GetProjectPath(), assetPath);
            return fullPath;
        }

        private static string GetRelativePath(string fullPath, string projectPath)
        {
            if (!fullPath.StartsWith(projectPath))
                return fullPath;

            var relativePath = fullPath.Substring(projectPath.Length);
            if (relativePath.StartsWith("/") || relativePath.StartsWith("\\"))
                relativePath = relativePath.Substring(1);

            return relativePath;
        }

        private static string GetProjectPath()
        {
            return Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        }

        private static bool IsGitRepository()
        {
            var projectPath = GetProjectPath();
            return Directory.Exists(projectPath + "/.git");
        }

        private static string FindGcExecutable()
        {
            var projectPath = GetProjectPath();
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (IsRunningOnLinuxOrMac())
            {
                var paths = new[]
                {
                    "/usr/local/bin/gc",
                    "/usr/bin/gc",
                    Path.Combine(homeDir, ".local", "bin", "gc")
                };

                foreach (var path in paths)
                {
                    if (File.Exists(path))
                        return path;
                }

                try
                {
                    var whichResult = RunCommand("which gc 2>/dev/null");
                    if (!string.IsNullOrEmpty(whichResult))
                    {
                        var trimmedPath = whichResult.Trim();
                        if (File.Exists(trimmedPath))
                            return trimmedPath;
                    }
                }
                catch
                {
                }
            }
            else
            {
                var exePath = Path.Combine(projectPath, "Tools", "gc.exe");
                if (File.Exists(exePath))
                    return exePath;

                exePath = Path.Combine(homeDir, "AppData", "Local", "gc", "gc.exe");
                if (File.Exists(exePath))
                    return exePath;
            }

            return null;
        }

        private static bool IsRunningOnLinuxOrMac()
        {
            return Application.platform == RuntimePlatform.LinuxPlayer ||
                   Application.platform == RuntimePlatform.OSXEditor ||
                   Application.platform == RuntimePlatform.LinuxEditor;
        }

        private static string RunCommand(string command)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{command}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                process?.WaitForExit();
                return process?.StandardOutput.ReadToEnd() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
