using gc.Infrastructure.IO;
using gc.Infrastructure.Logging;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Domain.Common;

namespace gc.Tests;

public class FileReaderTests
{
    private static FileReader CreateReader() => new(new ConsoleLogger());

    // ─── ReadStreamingAsync basic ─────────────────────────────────────────

    [Fact]
    public async Task ReadStreaming_NonexistentFile_ReturnsFailure()
    {
        var reader = CreateReader();
        var result = await reader.ReadStreamingAsync("/nonexistent/path/to/file.txt", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadStreaming_ExistingFile_ReturnsStream()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "hello world");
            var reader = CreateReader();
            var result = await reader.ReadStreamingAsync(tempFile, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.True(result.Value!.Length > 0);
            await result.Value.DisposeAsync();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadStreaming_StreamContainsCorrectContent()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            const string expected = "test content for stream";
            File.WriteAllText(tempFile, expected);
            var reader = CreateReader();
            var result = await reader.ReadStreamingAsync(tempFile, CancellationToken.None);

            Assert.True(result.IsSuccess);
            using var sr = new StreamReader(result.Value!);
            var content = await sr.ReadToEndAsync();
            Assert.Equal(expected, content);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ─── ReadStreamingAsync with limits ───────────────────────────────────

    [Fact]
    public async Task ReadStreaming_WithLimits_UnderLimit_ReturnsStream()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "small content");
            var limits = new LimitsConfiguration { MaxFileSize = "1MB" };
            var reader = CreateReader();
            var result = await reader.ReadStreamingAsync(tempFile, limits, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            await result.Value!.DisposeAsync();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadStreaming_WithLimits_OverLimit_ReturnsFailure()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // Write content larger than our limit
            File.WriteAllText(tempFile, "this is more than one byte of content");
            var limits = new LimitsConfiguration { MaxFileSize = "1B" }; // 1 byte limit
            var reader = CreateReader();
            var result = await reader.ReadStreamingAsync(tempFile, limits, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains("exceeds maximum", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadStreaming_WithLimits_ExactSize_ReturnsStream()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var data = new byte[] { 0x41, 0x42, 0x43 }; // exactly 3 bytes
            File.WriteAllBytes(tempFile, data);
            var limits = new LimitsConfiguration { MaxFileSize = "3B" }; // exact limit
            var reader = CreateReader();
            var result = await reader.ReadStreamingAsync(tempFile, limits, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            await result.Value!.DisposeAsync();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ─── ReadAsync basic ──────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_NonexistentFile_ReturnsFailure()
    {
        var reader = CreateReader();
        var entry = new FileEntry("/nonexistent/file.txt", "txt", "text", 0);
        var result = await reader.ReadAsync(entry, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ExistingFile_ReturnsContent()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            const string expected = "file content here";
            File.WriteAllText(tempFile, expected);
            var entry = new FileEntry(tempFile, "txt", "text", new FileInfo(tempFile).Length);
            var reader = CreateReader();
            var result = await reader.ReadAsync(entry, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(expected, result.Value!.Content);
            Assert.Equal(tempFile, result.Value!.Entry.Path);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadAsync_BinaryFile_ReturnsFailure()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // 3+ consecutive nulls triggers binary detection
            File.WriteAllBytes(tempFile, new byte[] { 0x00, 0x00, 0x00, 0x41 });
            var entry = new FileEntry(tempFile, "bin", "binary", new FileInfo(tempFile).Length);
            var reader = CreateReader();
            var result = await reader.ReadAsync(entry, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains("binary", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ─── ReadAsync with limits ────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_WithLimits_UnderLimit_ReturnsContent()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            const string expected = "content within limit";
            File.WriteAllText(tempFile, expected);
            var limits = new LimitsConfiguration { MaxFileSize = "1MB" };
            var entry = new FileEntry(tempFile, "txt", "text", new FileInfo(tempFile).Length);
            var reader = CreateReader();
            var result = await reader.ReadAsync(entry, limits, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(expected, result.Value!.Content);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadAsync_WithLimits_OverLimit_ReturnsFailure()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "this content is definitely more than one byte");
            var limits = new LimitsConfiguration { MaxFileSize = "1B" };
            var entry = new FileEntry(tempFile, "txt", "text", new FileInfo(tempFile).Length);
            var reader = CreateReader();
            var result = await reader.ReadAsync(entry, limits, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains("exceeds maximum", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ─── IsBinaryFileAsync ────────────────────────────────────────────────

    [Fact]
    public async Task IsBinary_TextFile_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "Hello, this is a normal text file.");
            var reader = CreateReader();
            var result = await reader.IsBinaryFileAsync(tempFile, CancellationToken.None);

            Assert.False(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task IsBinary_BinaryFile_ReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // 3+ consecutive nulls triggers binary detection
            File.WriteAllBytes(tempFile, new byte[] { 0x00, 0x00, 0x00, 0x41 });
            var reader = CreateReader();
            var result = await reader.IsBinaryFileAsync(tempFile, CancellationToken.None);

            Assert.True(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task IsBinary_MixedContent_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // Text with a few non-printable chars but <10% ratio and no consecutive nulls
            var content = "Hello" + (char)0x01 + "World" + (char)0x02 + "Normal text content here";
            File.WriteAllText(tempFile, content);
            var reader = CreateReader();
            var result = await reader.IsBinaryFileAsync(tempFile, CancellationToken.None);

            Assert.False(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task IsBinary_EmptyFile_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "");
            var reader = CreateReader();
            var result = await reader.IsBinaryFileAsync(tempFile, CancellationToken.None);

            Assert.False(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task IsBinary_HighNonPrintableRatio_ReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // Create a buffer where >10% bytes are non-printable (not null, not tab/newline/cr)
            // 50 bytes total, 10 non-printable (20%) — no consecutive nulls
            var bytes = new byte[50];
            for (var i = 0; i < 50; i++) bytes[i] = 0x41; // 'A'
            // Place non-printable control chars (not 0x00, 0x09, 0x0A, 0x0D)
            bytes[0] = 0x01;
            bytes[5] = 0x02;
            bytes[10] = 0x03;
            bytes[15] = 0x04;
            bytes[20] = 0x05;
            bytes[25] = 0x06;
            bytes[30] = 0x07;
            File.WriteAllBytes(tempFile, bytes);
            var reader = CreateReader();
            var result = await reader.IsBinaryFileAsync(tempFile, CancellationToken.None);

            Assert.True(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task IsBinary_NonexistentFile_ReturnsFalse()
    {
        var reader = CreateReader();
        var result = await reader.IsBinaryFileAsync("/nonexistent/file.bin", CancellationToken.None);

        // Exception caught internally, returns false
        Assert.False(result);
    }

    // ─── Edge cases ───────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_LockedFile_HandledGracefully()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "locked content");
            var entry = new FileEntry(tempFile, "txt", "text", new FileInfo(tempFile).Length);
            var reader = CreateReader();

            // Open with FileShare.None to simulate a locked file.
            // The FileReader uses FileShare.ReadWrite, so the lock should still cause an IOException
            // because the exclusive handle denies sharing.
            await using var lockStream = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var result = await reader.ReadAsync(entry, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadAsync_EntryWithPathOnly_NoExtension()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            const string expected = "no extension content";
            File.WriteAllText(tempFile, expected);

            // FileEntry with empty extension
            var entry = new FileEntry(tempFile, "", "", new FileInfo(tempFile).Length);
            var reader = CreateReader();
            var result = await reader.ReadAsync(entry, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(expected, result.Value!.Content);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadStreaming_LockedFile_ReturnsFailure()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "locked streaming content");
            var reader = CreateReader();

            // Open with FileShare.None to lock the file
            await using var lockStream = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var result = await reader.ReadStreamingAsync(tempFile, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
