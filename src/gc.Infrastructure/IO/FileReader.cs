using System.Text;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;

namespace gc.Infrastructure.IO;

public sealed class FileReader : IFileReader
{
    private readonly ILogger _logger;

    public FileReader(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<Result<Stream>> ReadStreamingAsync(string path, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Result<Stream>.Failure($"File not found: {path}");
            }

            // Using FileShare.ReadWrite to avoid locking issues as much as possible
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
            return Result<Stream>.Success(stream);
        }
        catch (IOException ex)
        {
            _logger.Error($"Failed to open stream for {path} (file may be locked)", ex);
            return Result<Stream>.Failure(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error($"Access denied to {path}", ex);
            return Result<Stream>.Failure(ex.Message);
        }
    }

    public async Task<Result<FileContent>> ReadAsync(FileEntry entry, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(entry.Path))
            {
                return Result<FileContent>.Failure($"File not found: {entry.Path}");
            }

            // Check if binary before reading fully
            var isBinary = await IsBinaryFileAsync(entry.Path, ct);
            if (isBinary)
            {
                return Result<FileContent>.Failure($"Skipping binary file: {entry.Path}");
            }

            using var stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = await reader.ReadToEndAsync(ct);
            
            return Result<FileContent>.Success(new FileContent(entry, content, stream.Length));
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

    private async Task<bool> IsBinaryFileAsync(string path, CancellationToken ct)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
            var length = (int)Math.Min(4096, stream.Length);
            if (length == 0) return false;

            var buffer = new byte[length];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, length), ct);

            var consecutiveNulls = 0;
            var nonPrintableCount = 0;

            for (var i = 0; i < bytesRead; i++)
            {
                var b = buffer[i];

                if (b == 0x00)
                {
                    consecutiveNulls++;
                    if (consecutiveNulls >= 3) return true;
                }
                else
                {
                    consecutiveNulls = 0;
                }

                if (b < 32 && b != 9 && b != 10 && b != 13 && b != 0x00) nonPrintableCount++;
            }

            if (bytesRead > 0 && (double)nonPrintableCount / bytesRead > 0.1) return true;

            return false;
        }
        catch
        {
            return false; // Assume not binary if we can't check
        }
    }
}