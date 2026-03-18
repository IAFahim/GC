using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using GC.Data;

namespace GC.Utilities;

public static class ClipboardExtensions
{
    public static void HandleOutput(this string markdown, in CliArguments args, FileContent[] contents)
    {
        if (markdown == null) throw new ArgumentNullException(nameof(markdown));
        if (contents == null) throw new ArgumentNullException(nameof(contents));

        using var _ = Logger.TimeOperation("Output handling");

        // Check clipboard size before copying
        var markdownSize = System.Text.Encoding.UTF8.GetByteCount(markdown);
        const long maxClipboardSize = 10485760; // 10MB typical clipboard limit

        Logger.LogVerbose($"Output size: {FormatSize(markdownSize)}");

        if (string.IsNullOrEmpty(args.OutputFile) && markdownSize > maxClipboardSize)
        {
            var sizeStr = markdownSize < 1024 ? $"{markdownSize} B" :
                markdownSize < 1048576 ? $"{markdownSize / 1024.0:F2} KB" :
                $"{markdownSize / 1048576.0:F2} MB";
            var maxSizeStr = $"{maxClipboardSize / 1048576.0:F2} MB";

            Logger.LogError($"Output size exceeds clipboard limit: {sizeStr} > {maxSizeStr}");
            Console.WriteLine($"Use -o <file> to save to a file instead.");
            Environment.Exit(1);
            return;
        }

        if (!string.IsNullOrEmpty(args.OutputFile))
        {
            Logger.LogDebug($"Writing to file: {args.OutputFile}");
            File.WriteAllText(args.OutputFile, markdown);
            markdown.PrintStats(args.OutputFile, contents);
            return;
        }

        Logger.LogDebug("Copying to clipboard...");
        var success = markdown.CopyToClipboard();
        if (success)
        {
            Logger.LogDebug("Clipboard copy successful");
            markdown.PrintStats("Clipboard", contents);
        }
        else
        {
            Logger.LogError("Failed to copy to clipboard. Please ensure clipboard tools are installed:");
            Console.WriteLine("  - Windows: PowerShell (usually pre-installed)");
            Console.WriteLine("  - macOS: pbcopy (usually pre-installed)");
            Console.WriteLine("  - Linux: Install wl-copy (wayland) or xclip (X11)");
            Console.WriteLine($"    Ubuntu/Debian: sudo apt install wl-clipboard xclip");
            Console.WriteLine($"    Fedora/RHEL: sudo dnf install wl-clipboard xclip");
            Console.WriteLine($"    Arch: sudo pacman -S wl-clipboard xclip");
            Environment.Exit(1);
        }
    }

    private static bool CopyToClipboard(this string markdown)
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, markdown);

        try
        {
            int exitCode;
            string command;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                command = "powershell -NoProfile -ExecutionPolicy Bypass -Command \"Set-Clipboard -Value (Get-Content '{tempFile}' -Raw -Encoding UTF8)\"";
                Logger.LogDebug($"Executing: {command}");
                exitCode = "powershell".RunProcess(
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    $"Set-Clipboard -Value (Get-Content '{tempFile}' -Raw -Encoding UTF8)"
                );
                return exitCode == 0;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                command = $"pbcopy < '{tempFile}'";
                Logger.LogDebug($"Executing: {command}");
                exitCode = "bash".RunProcess(
                    "-c",
                    command
                );
                return exitCode == 0;
            }

            // Linux: try wl-copy first (Wayland), then xclip (X11)
            if ("bash".RunProcess("-c", "command -v wl-copy") == 0)
            {
                command = $"wl-copy < '{tempFile}'";
                Logger.LogDebug($"Trying Wayland clipboard: {command}");
                exitCode = "bash".RunProcess(
                    "-c",
                    command
                );
                if (exitCode == 0)
                {
                    return true;
                }
                Logger.LogDebug("Wayland clipboard failed, falling back to X11");
            }

            command = $"xclip -selection clipboard < '{tempFile}'";
            Logger.LogDebug($"Trying X11 clipboard: {command}");
            exitCode = "bash".RunProcess(
                "-c",
                command
            );
            return exitCode == 0;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static int RunProcess(this string fileName, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi);
        if (process == null)
        {
            return -1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static string FormatSize(long bytes)
    {
        return bytes < 1024 ? $"{bytes} B" :
            bytes < 1048576 ? $"{bytes / 1024.0:F2} KB" :
            $"{bytes / 1048576.0:F2} MB";
    }

    private static void PrintStats(this string _, string target, FileContent[] contents)
    {
        long totalBytes = 0;
        for (var i = 0; i < contents.Length; i++)
        {
            totalBytes += contents[i].Size;
        }

        var tokens = totalBytes / 4;
        var sizeStr = totalBytes < 1024 ? $"{totalBytes} B" :
            totalBytes < 1048576 ? $"{totalBytes / 1024.0:F2} KB" :
            $"{totalBytes / 1048576.0:F2} MB";

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[OK] ");
        Console.ResetColor();
        Console.WriteLine($"Exported to {target}: {contents.Length} files | Size: {sizeStr} | Tokens: ~{tokens}");
    }
}