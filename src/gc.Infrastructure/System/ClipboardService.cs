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

            return success
                ? Result.Success()
                : Result.Failure("Failed to copy to clipboard. Clipboard tools may not be available. Use --output file.md to save to a file instead.");
        }
        catch (Exception ex)
        {
            _logger.Error("Clipboard copy failed", ex);
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result> CopyToClipboardAsync(Stream stream, LimitsConfiguration limits, bool append = false, CancellationToken ct = default)
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

            // Read the stream contents
            string newContent;
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }
            using (var reader = new StreamReader(stream, Utf8NoBom, true, -1, true))
            {
                newContent = await reader.ReadToEndAsync(ct);
            }

            if (append)
            {
                var existingContent = await GetClipboardTextAsync(ct);
                if (!string.IsNullOrEmpty(existingContent))
                {
                    newContent = existingContent + Environment.NewLine + newContent;
                }
            }
            
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }
            using var combinedStream = new MemoryStream(Utf8NoBom.GetBytes(newContent));

            bool success;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                success = await CopyToWindowsAsync(combinedStream, ct);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                success = await CopyToMacAsync(combinedStream, ct);
            }
            else
            {
                success = await CopyToLinuxAsync(combinedStream, ct);
            }

            return success
                ? Result.Success()
                : Result.Failure("Failed to copy to clipboard. Clipboard tools may not be available. Use --output file.md to save to a file instead.");
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

    public async Task<Result> CopyToClipboardAsync(string content, LimitsConfiguration limits, bool append = false, CancellationToken ct = default)
    {
        var maxClipboardSize = limits.GetMaxClipboardSizeBytes();
        var contentBytes = Utf8NoBom.GetByteCount(content);
        
        if (contentBytes > maxClipboardSize)
        {
            return Result.Failure($"Content size ({contentBytes} bytes) exceeds maximum clipboard size ({maxClipboardSize} bytes)");
        }

        using var ms = new MemoryStream(Utf8NoBom.GetBytes(content));
        return await CopyToClipboardAsync(ms, limits, append, ct);
    }

    private async Task<string> GetClipboardTextAsync(CancellationToken ct)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await RunClipboardGetProcessAsync("powershell.exe", "-Command \"Get-Clipboard\"", ct);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return await RunClipboardGetProcessAsync("pbpaste", "", ct);
            }
            else
            {
                var wayland = await RunClipboardGetProcessAsync("wl-paste", "", ct);
                if (string.IsNullOrEmpty(wayland))
                {
                    return await RunClipboardGetProcessAsync("xclip", "-selection clipboard -o", ct);
                }
                return wayland;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to get clipboard contents", ex);
            return string.Empty;
        }
    }

    private async Task<string> RunClipboardGetProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        try
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
            if (process == null) return string.Empty;

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            return process.ExitCode == 0 ? output.TrimEnd('\r', '\n') : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<bool> CopyToWindowsAsync(Stream stream, CancellationToken ct)
    {
        // Try clip.exe natively first (much faster and avoids PowerShell truncation)
        var success = await RunClipboardProcessAsync("clip.exe", "", stream, ct);
        if (!success && stream.CanSeek)
        {
            stream.Position = 0; // Reset stream for fallback
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
        if (!success && stream.CanSeek)
        {
            stream.Position = 0; // Reset stream for fallback
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
        catch (Exception ex)
        {
            _logger.Error($"Clipboard tool '{fileName}' not available or failed", ex);
            return false;
        }
    }
}