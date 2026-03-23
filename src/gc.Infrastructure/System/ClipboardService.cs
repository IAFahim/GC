using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;

namespace gc.Infrastructure.System;

public sealed class ClipboardService : IClipboardService
{
    private readonly ILogger _logger;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public ClipboardService(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<Result> CopyToClipboardAsync(Stream stream, CancellationToken ct = default)
    {
        try
        {
            bool success;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                success = await CopyToWindowsAsync(stream, ct);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                success = await CopyToMacAsync(stream, ct);
            }
            else
            {
                success = await CopyToLinuxAsync(stream, ct);
            }

            return success ? Result.Success() : Result.Failure("Failed to copy to clipboard.");
        }
        catch (Exception ex)
        {
            _logger.Error("Clipboard copy failed", ex);
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result> CopyToClipboardAsync(Stream stream, LimitsConfiguration limits, CancellationToken ct = default)
    {
        try
        {
            var maxClipboardSize = limits.GetMaxClipboardSizeBytes();
            
            if (stream.CanSeek)
            {
                if (stream.Length > maxClipboardSize)
                {
                    return Result.Failure($"Content size ({stream.Length} bytes) exceeds maximum clipboard size ({maxClipboardSize} bytes)");
                }
            }

            bool success;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                success = await CopyToWindowsAsync(stream, ct);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                success = await CopyToMacAsync(stream, ct);
            }
            else
            {
                success = await CopyToLinuxAsync(stream, ct);
            }

            return success ? Result.Success() : Result.Failure("Failed to copy to clipboard.");
        }
        catch (Exception ex)
        {
            _logger.Error("Clipboard copy failed", ex);
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result> CopyToClipboardAsync(string content, CancellationToken ct = default)
    {
        using var ms = new MemoryStream(Utf8NoBom.GetBytes(content));
        return await CopyToClipboardAsync(ms, ct);
    }

    public async Task<Result> CopyToClipboardAsync(string content, LimitsConfiguration limits, CancellationToken ct = default)
    {
        var maxClipboardSize = limits.GetMaxClipboardSizeBytes();
        var contentBytes = Utf8NoBom.GetByteCount(content);
        
        if (contentBytes > maxClipboardSize)
        {
            return Result.Failure($"Content size ({contentBytes} bytes) exceeds maximum clipboard size ({maxClipboardSize} bytes)");
        }

        using var ms = new MemoryStream(Utf8NoBom.GetBytes(content));
        return await CopyToClipboardAsync(ms, ct);
    }

    private async Task<bool> CopyToWindowsAsync(Stream stream, CancellationToken ct)
    {
        // Try pwsh first, then powershell
        var success = await RunClipboardProcessAsync("pwsh", "-Command \"$input | Out-String | Set-Clipboard\"", stream, ct);
        if (!success)
        {
            success = await RunClipboardProcessAsync("powershell.exe", "-Command \"$input | Out-String | Set-Clipboard\"", stream, ct);
        }
        return success;
    }

    private async Task<bool> CopyToMacAsync(Stream stream, CancellationToken ct)
    {
        return await RunClipboardProcessAsync("pbcopy", "", stream, ct);
    }

    private async Task<bool> CopyToLinuxAsync(Stream stream, CancellationToken ct)
    {
        // Try wl-copy (Wayland), then xclip (X11)
        var success = await RunClipboardProcessAsync("wl-copy", "", stream, ct);
        if (!success)
        {
            success = await RunClipboardProcessAsync("xclip", "-selection clipboard", stream, ct);
        }
        return success;
    }

    private async Task<bool> RunClipboardProcessAsync(string fileName, string arguments, Stream inputStream, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            // Copy stream to standard input
            await inputStream.CopyToAsync(process.StandardInput.BaseStream, ct);
            await process.StandardInput.BaseStream.FlushAsync(ct);
            process.StandardInput.Close();

            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}