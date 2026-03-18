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
        if (!string.IsNullOrEmpty(args.OutputFile))
        {
            File.WriteAllText(args.OutputFile, markdown);
            markdown.PrintStats(args.OutputFile, contents);
            return;
        }

        markdown.CopyToClipboard();
        markdown.PrintStats("Clipboard", contents);
    }

    private static void CopyToClipboard(this string markdown)
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, markdown);

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                "powershell".RunProcess($"-NoProfile -ExecutionPolicy Bypass -Command \"Set-Clipboard -Value (Get-Content '{tempFile}' -Raw -Encoding UTF8)\"");
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                "bash".RunProcess($"-c \"pbcopy < '{tempFile}'\"");
                return;
            }

            if ("bash".RunProcess("-c \"command -v wl-copy\"") == 0)
            {
                "bash".RunProcess($"-c \"wl-copy < '{tempFile}'\"");
                return;
            }

            "bash".RunProcess($"-c \"xclip -selection clipboard < '{tempFile}'\"");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static int RunProcess(this string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return -1;
        }

        process.WaitForExit();
        return process.ExitCode;
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