using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;

namespace gc.Infrastructure.System;

public sealed class ClipboardService : IClipboardService
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly ILogger _logger;
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
        if (IsRunningInTest()) return Result.Success();
        try
        {
            var success = _platform switch
            {
                OsKind.Windows => await CopyToWindowsAsync(stream, ct),
                OsKind.Mac => await CopyToMacAsync(stream, ct),
                OsKind.Linux => await CopyToLinuxAsync(stream, ct),
                _ => false
            };

            return success
                ? Result.Success()
                : Result.Failure(
                    "Failed to copy to clipboard. Clipboard tools may not be available. Use --output file.md to save to a file instead.");
        }
        catch (Exception ex)
        {
            _logger.Error("Clipboard copy failed", ex);
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result> CopyToClipboardAsync(Stream stream, LimitsConfiguration limits, bool append = false,
        CancellationToken ct = default)
    {
        try
        {
            var maxClipboardSize = limits.GetMaxClipboardSizeBytes();

            if (!append)
            {
                // If not appending, check stream length if available, then copy stream directly
                if (stream.CanSeek)
                {
                    if (stream.Length > maxClipboardSize)
                        return Result.Failure(
                            $"Content size ({stream.Length} bytes) exceeds maximum clipboard size ({maxClipboardSize} bytes)");
                    stream.Position = 0;
                }

                return await CopyToClipboardAsync(stream, ct);
            }

            // Read new content first
            if (stream.CanSeek)
            {
                // New content alone exceeding the cap guarantees the combined (existing + new) does too,
                // so reject before buffering the whole stream into a managed string.
                if (stream.Length > maxClipboardSize)
                    return Result.Failure(
                        $"Content size ({stream.Length} bytes) exceeds maximum clipboard size ({maxClipboardSize} bytes)");
                stream.Position = 0;
            }
            string newContent;
            using (var reader = new StreamReader(stream, Utf8NoBom, true, -1, true))
            {
                newContent = await reader.ReadToEndAsync(ct);
            }

            return await CopyToClipboardStringInternalAsync(newContent, maxClipboardSize, append, ct);
        }
        catch (Exception ex)
        {
            _logger.Error("Clipboard copy failed", ex);
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result> CopyToClipboardAsync(string content, CancellationToken ct = default)
    {
        var finalBytes = Utf8NoBom.GetBytes(content);
        using var ms = new MemoryStream(finalBytes);
        return await CopyToClipboardAsync(ms, ct);
    }

    public async Task<Result> CopyToClipboardAsync(string content, LimitsConfiguration limits, bool append = false,
        CancellationToken ct = default)
    {
        var maxClipboardSize = limits.GetMaxClipboardSizeBytes();
        return await CopyToClipboardStringInternalAsync(content, maxClipboardSize, append, ct);
    }

    private async Task<Result> CopyToClipboardStringInternalAsync(string newContent, long maxClipboardSize, bool append,
        CancellationToken ct)
    {
        try
        {
            string finalContent;
            if (append)
            {
                var existingContent = await GetClipboardTextAsync(ct);
                if (!string.IsNullOrEmpty(existingContent))
                    finalContent = existingContent + Environment.NewLine + newContent;
                else
                    finalContent = newContent;
            }
            else
            {
                finalContent = newContent;
            }

            var finalBytes = Utf8NoBom.GetBytes(finalContent);
            if (finalBytes.Length > maxClipboardSize)
                return Result.Failure(
                    $"Combined content size ({finalBytes.Length} bytes) exceeds maximum clipboard size ({maxClipboardSize} bytes)");

            using var combinedStream = new MemoryStream(finalBytes);
            return await CopyToClipboardAsync(combinedStream, ct);
        }
        catch (Exception ex)
        {
            _logger.Error("Clipboard copy failed", ex);
            return Result.Failure(ex.Message);
        }
    }

    private async Task<string> GetClipboardTextAsync(CancellationToken ct)
    {
        if (IsRunningInTest()) return string.Empty;
        try
        {
            return _platform switch
            {
                OsKind.Windows => await RunClipboardGetProcessAsync("powershell.exe", "-Command \"Get-Clipboard\"", ct),
                OsKind.Mac => await RunClipboardGetProcessAsync("pbpaste", "", ct),
                OsKind.Linux => await GetLinuxClipboardTextAsync(ct),
                _ => string.Empty
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
        catch (OperationCanceledException)
        {
            throw; // Honor cancellation rather than masking it as an empty read.
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<bool> CopyToWindowsAsync(Stream stream, CancellationToken ct)
    {
        // Prefer PowerShell's Set-Clipboard for UTF-8/UTF-16 safe handling.
        // clip.exe truncates/garbles non-ANSI text (including PUA symbols from brain mode).
        var success = await RunClipboardProcessAsync("powershell.exe",
            "-NoProfile -Command \"[Console]::InputEncoding=[System.Text.UTF8Encoding]::new($false); Set-Clipboard -Value ([Console]::In.ReadToEnd())\"",
            stream, ct);
        if (success) return true;

        // Fallback to clip.exe only if PowerShell fails
        if (stream.CanSeek)
        {
            stream.Position = 0;
            success = await RunClipboardProcessAsync("clip.exe", "", stream, ct);
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

    private async Task<bool> RunClipboardProcessAsync(string fileName, string arguments, Stream inputStream,
        CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
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
        catch (OperationCanceledException)
        {
            // Honor cancellation instead of treating it as a tool failure that triggers a fallback.
            // The `using` disposes the child process, closing its stdin so it drains and exits.
            throw;
        }
        catch
        {
            // Fail silently so the caller can try a fallback tool without polluting logs
            return false;
        }
    }

    private enum OsKind
    {
        Windows,
        Mac,
        Linux
    }

    private static bool IsRunningInTest()
    {
        if (Environment.GetEnvironmentVariable("GC_TEST_MODE") == "true") return true;

        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                var name = assemblies[i].FullName;
                if (name != null && (name.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                                     name.Contains("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase) ||
                                     name.Contains("testhost", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore reflection errors
        }

        return false;
    }
}