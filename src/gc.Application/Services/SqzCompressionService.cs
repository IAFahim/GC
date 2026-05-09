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
            Console.Error.WriteLine(
                "[gc] sqz not found. Install it: curl -fsSL https://raw.githubusercontent.com/" +
                "ojuschugh1/sqz/main/install.sh | sh\n" +
                "[gc] Falling back to uncompressed output.");
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

        await process.StandardInput.WriteAsync(markdownContent);
        process.StandardInput.Close();

        var compressed = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"[gc] sqz exited with code {process.ExitCode}: {stderr.Trim()}");
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
}
