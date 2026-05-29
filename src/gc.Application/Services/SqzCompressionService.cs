using System.Diagnostics;
using System.Text;

namespace gc.Application.Services;

public sealed class SqzCompressionService
{
    private readonly bool _sqzAvailable;

    public SqzCompressionService()
    {
        _sqzAvailable = IsSqzInstalled();
    }

    public bool IsAvailable => _sqzAvailable;

    public async Task<string> CompressAsync(string markdownContent, bool noCache = false)
    {
        if (!_sqzAvailable)
        {
            return markdownContent;
        }

        var args = noCache ? "compress --no-cache" : "compress";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "sqz",
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
            }
        };

        process.Start();

        // Start reading stdout/stderr BEFORE writing to stdin to avoid deadlock.
        // If sqz streams output while reading input, its stdout pipe (~64KB) can fill
        // and block, causing a deadlock if we're not reading yet.
        var compressedTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.StandardInput.WriteAsync(markdownContent);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        await process.WaitForExitAsync();

        var compressed = await compressedTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"[gc] sqz error (exit {process.ExitCode}): {stderr.Trim()}");
            return markdownContent;
        }

        return compressed;
    }

    private static bool IsSqzInstalled()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "sqz",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            p?.WaitForExit(2000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static string InstallHint =>
        "sqz not found. Install: curl -fsSL https://raw.githubusercontent.com/ojuschugh1/sqz/main/install.sh | sh" +
        "\nSee: https://github.com/ojuschugh1/sqz";
}
