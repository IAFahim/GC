using System.Diagnostics;
using System.Text;

namespace gc.Application.Services;

public sealed class SqzCompressionService
{
    // BOM-less UTF-8: Encoding.UTF8 emits a 3-byte BOM (EF BB BF) on the first write to the
    // child's stdin, which would corrupt the very first token of the payload sqz receives.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public SqzCompressionService()
    {
        IsAvailable = IsSqzInstalled();
    }

    public bool IsAvailable { get; }

    public static string InstallHint =>
        "sqz not found. Please install sqz from the repository: https://github.com/ojuschugh1/sqz";

    public async Task<string> CompressAsync(string markdownContent, bool noCache = false, CancellationToken ct = default)
    {
        if (!IsAvailable) return markdownContent;

        var args = noCache ? "compress --no-cache" : "compress";
        try
        {
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
                    StandardInputEncoding = Utf8NoBom,
                    StandardOutputEncoding = Utf8NoBom
                }
            };

            process.Start();

            // Enforce a generous internal timeout so a hung sqz cannot block the CLI forever.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(120));
            var token = linked.Token;

            // Start reading stdout/stderr BEFORE writing to stdin to avoid deadlock.
            // If sqz streams output while reading input, its stdout pipe (~64KB) can fill
            // and block, causing a deadlock if we're not reading yet.
            // Declared outside the try so the cancellation handler can observe them.
            var compressedTask = process.StandardOutput.ReadToEndAsync(token);
            var stderrTask = process.StandardError.ReadToEndAsync(token);

            try
            {
                try
                {
                    await process.StandardInput.WriteAsync(markdownContent.AsMemory(), token);
                    await process.StandardInput.FlushAsync(token);
                }
                catch (IOException)
                {
                    // sqz exited early and closed its stdin pipe (broken pipe).
                    // Swallow and fall through to read whatever exit code / stderr it produced.
                }
                finally
                {
                    try { process.StandardInput.Close(); } catch (IOException) { }
                }

                await process.WaitForExitAsync(token);

                var compressed = await compressedTask;
                var stderr = await stderrTask;

                if (process.ExitCode != 0)
                {
                    Console.Error.WriteLine($"[gc] sqz error (exit {process.ExitCode}): {stderr.Trim()}");
                    return markdownContent;
                }

                return compressed;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                // Observe the outstanding stdout/stderr reads before the process (and its streams)
                // are disposed, so a read that faults against a disposed stream never surfaces as an
                // UnobservedTaskException.
                try { await Task.WhenAll(compressedTask, stderrTask); } catch { /* drained */ }
                // Distinguish caller cancellation (honor the contract) from internal timeout (fail-open).
                if (ct.IsCancellationRequested) throw;
                Console.Error.WriteLine("[gc] sqz timed out; using uncompressed output");
                return markdownContent;
            }
        }
        catch (OperationCanceledException)
        {
            // Genuine user cancellation propagated from the inner handler.
            throw;
        }
        catch (Exception ex)
        {
            // Fail open: external tool boundary, contract is graceful degradation to uncompressed.
            Console.Error.WriteLine($"[gc] sqz invocation failed: {ex.Message}");
            return markdownContent;
        }
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
            if (p != null)
            {
                if (!p.WaitForExit(2000))
                {
                    try
                    {
                        p.Kill();
                    }
                    catch
                    {
                        // Ignore any exceptions during kill
                    }
                    return false;
                }
                return p.ExitCode == 0;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}