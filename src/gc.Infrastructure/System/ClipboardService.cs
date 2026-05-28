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

    private enum OsKind { Windows, Mac, Linux }
    private readonly OsKind _platform;

    public ClipboardService(ILogger logger)
    {
        _logger = logger;
        _platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OsKind.Windows
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OsKind.Mac
            : OsKind.Linux;
    }

    public async Task<Result> CopyToClipboardAsync(Stream stream, CancellationToken ct = default)
    {
        try
        {
            bool success = _platform switch
            {
                OsKind.Windows => await CopyToWindowsAsync(stream, ct),
                OsKind.Mac      => await CopyToMacAsync(stream, ct),
                OsKind.Linux    => await CopyToLinuxAsync(stream, ct),
                _               => false
            };

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

            // Read new content first
            if (stream.CanSeek) stream.Position = 0;
            string newContent;
            using (var reader = new StreamReader(stream, Utf8NoBom, true, -1, true))
            {
                newContent = await reader.ReadToEndAsync(ct);
            }

            string finalContent;
            if (append)
            {
                var existingContent = await GetClipboardTextAsync(ct);
                if (!string.IsNullOrEmpty(existingContent))
                {
                    finalContent = existingContent + Environment.NewLine + newContent;
                }
                else
                {
                    finalContent = newContent;
                }
            }
            else
            {
                finalContent = newContent;
            }

            // Enforce size limit on COMBINED content before allocating
            var finalBytes = Utf8NoBom.GetByteCount(finalContent);
            if (finalBytes > maxClipboardSize)
            {
                return Result.Failure($"Combined content size ({finalBytes} bytes) exceeds maximum clipboard size ({maxClipboardSize} bytes)");
            }

            using var combinedStream = new MemoryStream(Utf8NoBom.GetBytes(finalContent));
            bool success = _platform switch
            {
                OsKind.Windows => await CopyToWindowsAsync(combinedStream, ct),
                OsKind.Mac      => await CopyToMacAsync(combinedStream, ct),
                OsKind.Linux    => await CopyToLinuxAsync(combinedStream, ct),
                _               => false
            };

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
            return _platform switch
            {
                OsKind.Windows => await RunClipboardGetProcessAsync("powershell.exe", "-Command \"Get-Clipboard\"", ct),
                OsKind.Mac      => await RunClipboardGetProcessAsync("pbpaste", "", ct),
                OsKind.Linux    => await GetLinuxClipboardTextAsync(ct),
                _               => string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to get clipboard contents", ex);
            return string.Empty;
        }
    }

    private async Task<string> GetLinuxClipboardTextAsync(CancellationToken ct)
    {
        var wayland = await RunClipboardGetProcessAsync("wl-paste", "", ct);
        if (!string.IsNullOrEmpty(wayland)) return wayland;
        return await RunClipboardGetProcessAsync("xclip", "-selection clipboard -o", ct);
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
        var success = await RunClipboardProcessAsync("clip.exe", "", stream, ct);
        if (!success && stream.CanSeek)
        {
            stream.Position = 0;
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
        var success = await RunClipboardProcessAsync("wl-copy", "", stream, ct);
        if (!success && stream.CanSeek)
        {
            stream.Position = 0;
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

            await inputStream.CopyToAsync(process.StandardInput.BaseStream, ct);
            await process.StandardInput.BaseStream.FlushAsync(ct);
            process.StandardInput.Close();

            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch
        {
            // Fail silently so the caller can try a fallback tool without polluting logs
            return false;
        }
    }
}