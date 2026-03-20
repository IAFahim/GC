using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using gc.Data;

namespace gc.Utilities;

public static class ClipboardExtensions
{
    public static void HandleOutput(this string markdown, in CliArguments args, FileContent[] contents)
    {
        if (markdown == null) throw new ArgumentNullException(nameof(markdown));
        if (contents == null) throw new ArgumentNullException(nameof(contents));

        using var _ = Logger.TimeOperation("Output handling");

        var markdownSize = Encoding.UTF8.GetByteCount(markdown);
        var maxClipboardSize = args.Configuration?.Limits?.GetMaxClipboardSizeBytes() ?? 10485760;

        Logger.LogVerbose($"Output size: {Formatting.FormatSize(markdownSize)}");

        if (string.IsNullOrEmpty(args.OutputFile) && markdownSize > maxClipboardSize)
        {
            var sizeStr = markdownSize < 1024 ? $"{markdownSize} B" :
                markdownSize < 1048576 ? $"{(markdownSize / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} KB" :
                $"{(markdownSize / 1048576.0).ToString("F2", CultureInfo.InvariantCulture)} MB";
            var maxSizeStr = $"{(maxClipboardSize / 1048576.0).ToString("F2", CultureInfo.InvariantCulture)} MB";

            Logger.LogError($"Output size exceeds clipboard limit: {sizeStr} > {maxSizeStr}");
            Console.WriteLine("Use -o <file> to save to a file instead.");
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
            Console.WriteLine("    Ubuntu/Debian: sudo apt install wl-clipboard xclip");
            Console.WriteLine("    Fedora/RHEL: sudo dnf install wl-clipboard xclip");
            Console.WriteLine("    Arch: sudo pacman -S wl-clipboard xclip");
            Environment.Exit(1);
        }
    }

    private static bool CopyToClipboard(this string markdown)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return CopyToClipboardWindows(markdown);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return CopyToClipboardMac(markdown);

        return CopyToClipboardLinux(markdown);
    }

    private static bool CopyToClipboardWindows(string markdown)
    {
        try
        {
            Logger.LogDebug("Copying to clipboard via PowerShell Set-Clipboard");

            var psi = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = $"-Command Set-Clipboard -Value @(\"{markdown.Replace("\"", "`\"").Replace("$", "`$")}\")",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                Logger.LogDebug("PowerShell 7+ not found, trying Windows PowerShell");
                return CopyToClipboardWindowsLegacy(markdown);
            }

            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                Logger.LogDebug("PowerShell 7+ clipboard copy successful");
                return true;
            }

            Logger.LogDebug("PowerShell 7+ clipboard copy failed, trying Windows PowerShell");
            return CopyToClipboardWindowsLegacy(markdown);
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"PowerShell clipboard copy failed: {ex.Message}, trying legacy method");
            return CopyToClipboardWindowsLegacy(markdown);
        }
    }

    private static bool CopyToClipboardWindowsLegacy(string markdown)
    {
        try
        {
            Logger.LogDebug("Copying to clipboard via Windows PowerShell (powershell.exe)");

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command Set-Clipboard -Value @(\"{markdown.Replace("\"", "`\"").Replace("$", "`$")}\")",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                Logger.LogDebug("Windows PowerShell not found, trying clip.exe with chcp 65001");
                return CopyToClipboardWindowsClipExe(markdown);
            }

            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                Logger.LogDebug("Windows PowerShell clipboard copy successful");
                return true;
            }

            Logger.LogDebug("Windows PowerShell clipboard copy failed, trying clip.exe with chcp 65001");
            return CopyToClipboardWindowsClipExe(markdown);
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"Windows PowerShell clipboard copy failed: {ex.Message}, trying clip.exe with chcp 65001");
            return CopyToClipboardWindowsClipExe(markdown);
        }
    }

    private static bool CopyToClipboardWindowsClipExe(string markdown)
    {
        try
        {
            Logger.LogDebug("Copying to clipboard via clip.exe with UTF-8 encoding");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments =
                    $"/c chcp 65001 >nul 2>&1 && echo \"{markdown.Replace("\"", "^\"").Replace("&", "^&").Replace("<", "^<").Replace(">", "^>").Replace("|", "^|").Replace("%", "^%")}\" | clip.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"clip.exe clipboard copy failed: {ex.Message}");
            return false;
        }
    }

    private static bool CopyToClipboardMac(string markdown)
    {
        try
        {
            Logger.LogDebug("Copying to clipboard via pbcopy");

            var psi = new ProcessStartInfo
            {
                FileName = "pbcopy",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            using var writer = process.StandardInput;
            writer.Write(markdown);
            writer.Close();

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"pbcopy clipboard copy failed: {ex.Message}");
            return false;
        }
    }

    private static bool CopyToClipboardLinux(string markdown)
    {
        var result = TryCopyToClipboardLinux(markdown, "wl-copy", "Wayland");
        if (result) return true;

        Logger.LogDebug("Wayland clipboard not available, trying xclip");
        return TryCopyToClipboardLinux(markdown, "xclip", "X11", "-selection", "clipboard");
    }

    private static bool TryCopyToClipboardLinux(string markdown, string tool, string toolName,
        params string[] extraArgs)
    {
        try
        {
            Logger.LogDebug($"Copying to clipboard via {toolName}");

            var psi = new ProcessStartInfo
            {
                FileName = tool,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in extraArgs) psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process == null) return false;

            using var writer = process.StandardInput;
            writer.Write(markdown);
            writer.Close();

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"{toolName} clipboard copy failed: {ex.Message}");
            return false;
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

        foreach (var argument in arguments) psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi);
        if (process == null) return -1;

        process.WaitForExit();
        return process.ExitCode;
    }


    private static void PrintStats(this string _, string target, FileContent[] contents)
    {
        long totalBytes = 0;
        for (var i = 0; i < contents.Length; i++) totalBytes += contents[i].Size;

        var tokens = totalBytes / 4;
        var sizeStr = totalBytes < 1024 ? $"{totalBytes} B" :
            totalBytes < 1048576 ? $"{(totalBytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} KB" :
            $"{(totalBytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture)} MB";

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[OK] ");
        Console.ResetColor();
        Console.WriteLine($"Exported to {target}: {contents.Length} files | Size: {sizeStr} | Tokens: ~{tokens}");
    }
}