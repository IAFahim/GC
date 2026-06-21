using System.Buffers;
using System.Text;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.Infrastructure.IO;

public sealed class FileReader : IFileReader
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly SearchValues<byte> NullByte = SearchValues.Create((byte)0);
    private readonly ILogger _logger;

    public FileReader(ILogger logger)
    {
        _logger = logger;
    }

    public Task<Result<Stream>> ReadStreamingAsync(string path, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(path)) return Task.FromResult(Result<Stream>.Failure($"File not found: {path}"));

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
            return Task.FromResult(Result<Stream>.Success(stream));
        }
        catch (IOException ex)
        {
            _logger.Error($"Failed to open stream for {path} (file may be locked)", ex);
            return Task.FromResult(Result<Stream>.Failure(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error($"Access denied to {path}", ex);
            return Task.FromResult(Result<Stream>.Failure(ex.Message));
        }
    }

    public async Task<Result<FileContent>> ReadAsync(FileEntry entry, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(entry.Path)) return Result<FileContent>.Failure($"File not found: {entry.Path}");

            using var stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096,
                true);

            return await ReadDecodedAsync(entry, stream, stream.Length, ct);
        }
        catch (IOException ex)
        {
            _logger.Error($"Failed to read {entry.Path} (file may be locked)", ex);
            return Result<FileContent>.Failure(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error($"Access denied to {entry.Path}", ex);
            return Result<FileContent>.Failure(ex.Message);
        }
    }

    private async Task<Result<FileContent>> ReadDecodedAsync(FileEntry entry, Stream stream, long len,
        CancellationToken ct)
    {
        if (len == 0)
            return Result<FileContent>.Success(new FileContent(entry, string.Empty, 0));

        // Files are read whole into a single pooled buffer; a >2 GB length would overflow the int
        // cast (negative -> ArrayPool.Rent throws) or silently truncate, so guard before renting.
        if (len > int.MaxValue)
            return Result<FileContent>.Failure($"File too large to read into memory ({len} bytes): {entry.Path}");

        var capacity = (int)len;
        var buffer = ArrayPool<byte>.Shared.Rent(capacity);
        try
        {
            var read = 0;
            while (read < capacity)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read, capacity - read), ct);
                if (n == 0) break;
                read += n;
            }

            if (IsBinaryBytes(buffer.AsSpan(0, read)))
                return Result<FileContent>.Failure($"Skipping binary file: {entry.Path}");

            // The whole file is already in a contiguous pooled buffer, so decode it
            // directly instead of wrapping it in a MemoryStream+StreamReader (which would
            // allocate a stream, a reader, an internal char/byte buffer and a Decoder, plus
            // an async state machine for a purely in-memory transform). StreamReader strips a
            // leading UTF-8 BOM; replicate that to stay byte-identical. (UTF-16/32 BOM files
            // contain null bytes and are already rejected by IsBinaryBytes above.)
            var span = buffer.AsSpan(0, read);
            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
                span = span[3..];
            var content = Utf8NoBom.GetString(span);

            // Report bytes actually read, not the declared length: a concurrent truncation
            // (FileShare.ReadWrite) can shorten the file between Length capture and read.
            return Result<FileContent>.Success(new FileContent(entry, content, read));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<bool> IsBinaryFileAsync(string path, CancellationToken ct)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
            var length = (int)Math.Min(4096, stream.Length);
            if (length == 0) return false;

            var buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, length), ct);

                var nonPrintableCount = 0;

                for (var i = 0; i < bytesRead; i++)
                {
                    var b = buffer[i];

                    if (b == 0x00) return true;

                    if (b < 32 && b != 9 && b != 10 && b != 13) nonPrintableCount++;
                }

                if (bytesRead > 0 && (double)nonPrintableCount / bytesRead > 0.1) return true;

                return false;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (IOException ex)
        {
            _logger.Error($"Failed to check if {path} is binary (file may be locked)", ex);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error($"Access denied when checking if {path} is binary", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error checking if {path} is binary", ex);
            return false;
        }
    }

    public Task<Result<Stream>> ReadStreamingAsync(string path, LimitsConfiguration limits,
        CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(path)) return Task.FromResult(Result<Stream>.Failure($"File not found: {path}"));

            var fileInfo = new FileInfo(path);
            var maxFileSize = limits.GetMaxFileSizeBytes();
            if (fileInfo.Length > maxFileSize)
                return Task.FromResult(Result<Stream>.Failure(
                    $"File size ({fileInfo.Length} bytes) exceeds maximum allowed size ({maxFileSize} bytes): {path}"));

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
            return Task.FromResult(Result<Stream>.Success(stream));
        }
        catch (IOException ex)
        {
            _logger.Error($"Failed to open stream for {path} (file may be locked)", ex);
            return Task.FromResult(Result<Stream>.Failure(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error($"Access denied to {path}", ex);
            return Task.FromResult(Result<Stream>.Failure(ex.Message));
        }
    }

    public async Task<Result<FileContent>> ReadAsync(FileEntry entry, LimitsConfiguration limits,
        CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(entry.Path)) return Result<FileContent>.Failure($"File not found: {entry.Path}");

            var fileInfo = new FileInfo(entry.Path);
            var maxFileSize = limits.GetMaxFileSizeBytes();
            if (fileInfo.Length > maxFileSize)
                return Result<FileContent>.Failure(
                    $"File size ({fileInfo.Length} bytes) exceeds maximum allowed size ({maxFileSize} bytes): {entry.Path}");

            using var stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096,
                true);

            return await ReadDecodedAsync(entry, stream, stream.Length, ct);
        }
        catch (IOException ex)
        {
            _logger.Error($"Failed to read {entry.Path} (file may be locked)", ex);
            return Result<FileContent>.Failure(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error($"Access denied to {entry.Path}", ex);
            return Result<FileContent>.Failure(ex.Message);
        }
    }

    private static bool IsBinaryBytes(ReadOnlySpan<byte> bytes)
    {
        var window = bytes.Length > 4096 ? bytes[..4096] : bytes;
        if (window.Length == 0) return false;

        // Vectorized first pass for the most common positive signal (a NUL byte).
        if (window.ContainsAny(NullByte)) return true;

        var nonPrintableCount = 0;

        for (var i = 0; i < window.Length; i++)
        {
            var b = window[i];

            if (b < 32 && b != 9 && b != 10 && b != 13) nonPrintableCount++;
        }

        if ((double)nonPrintableCount / window.Length > 0.1) return true;

        return false;
    }
}