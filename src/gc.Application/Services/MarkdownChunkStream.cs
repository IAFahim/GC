using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using gc.Domain.Models;

namespace gc.Application.Services;

/// <summary>Streams UTF-8 Markdown into bounded, independently framed files and an integrity manifest.</summary>
public sealed class MarkdownChunkStream : Stream
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ChunkOutputOptions _options;
    private readonly string _stagingDirectory;
    private readonly bool _hadEmptyDirectory;
    private readonly IncrementalHash _sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly List<Part> _parts = [];
    private byte[] _buffer;
    private int _buffered;
    private long _sourceBytes;
    private bool _completed;
    private bool _disposed;

    private sealed record Part(string File, int Bytes, string Sha256, int PayloadOffset,
        int PayloadBytes, long SourceOffset);

    /// <summary>Starts an isolated output transaction; existing nonempty directories are preserved.</summary>
    public MarkdownChunkStream(ChunkOutputOptions options)
    {
        if (options.MaxBytes is < 256 or > 1_000_000_000)
            throw new ArgumentOutOfRangeException(nameof(options), "Part size must be between 256B and 1GB.");
        if (string.IsNullOrWhiteSpace(options.Prefix) || options.Prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            options.Prefix.Contains('/') || options.Prefix.Contains('\\'))
            throw new ArgumentException("Invalid part filename prefix.", nameof(options));
        _options = options;
        OutputDirectory = Path.GetFullPath(options.Directory ??
            Path.Combine(Path.GetTempPath(), "gc-" + Guid.NewGuid().ToString("N")[..12]));
        if (File.Exists(OutputDirectory)) throw new IOException("Output directory is a file: " + OutputDirectory);
        _hadEmptyDirectory = Directory.Exists(OutputDirectory);
        if (_hadEmptyDirectory && Directory.EnumerateFileSystemEntries(OutputDirectory).Any())
            throw new IOException("Output directory must be empty: " + OutputDirectory);
        _stagingDirectory = OutputDirectory + ".tmp." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(_stagingDirectory);
        _buffer = new byte[Math.Min(options.MaxBytes, 65_536)];
    }

    /// <summary>The published folder, available after <see cref="Complete"/> succeeds.</summary>
    public string OutputDirectory { get; }

    /// <summary>The number of Markdown parts written.</summary>
    public int PartCount => _parts.Count;

    /// <summary>Total bytes in the framed Markdown files.</summary>
    public long OutputBytes { get; private set; }

    /// <inheritdoc/>
    public override bool CanRead => false;
    /// <inheritdoc/>
    public override bool CanSeek => false;
    /// <inheritdoc/>
    public override bool CanWrite => !_disposed && !_completed;
    /// <inheritdoc/>
    public override long Length => _sourceBytes + _buffered;
    /// <inheritdoc/>
    public override long Position { get => Length; set => throw new NotSupportedException(); }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) throw new InvalidOperationException("Output has already been completed.");
        while (!data.IsEmpty)
        {
            if (_buffered == _buffer.Length)
            {
                if (_buffer.Length < _options.MaxBytes)
                    Array.Resize(ref _buffer, (int)Math.Min((long)_buffer.Length * 2, _options.MaxBytes));
                else
                    EmitPart();
            }
            var count = Math.Min(data.Length, _buffer.Length - _buffered);
            data[..count].CopyTo(_buffer.AsSpan(_buffered));
            _buffered += count;
            data = data[count..];
        }
    }

    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private void EmitPart()
    {
        var count = _buffered;
        byte[] prefix;
        byte[] suffix;
        while (true)
        {
            count = SafePayloadLength(_buffer.AsSpan(0, _buffered), count);
            if (count == 0) throw new DecoderFallbackException("Output ended inside a UTF-8 character.");
            var payload = _buffer.AsSpan(0, count);
            var fence = new string('`', Math.Max(4, LongestBacktickRun(payload) + 1));
            prefix = Encoding.UTF8.GetBytes($"# Context part {_parts.Count + 1:D3}\n\n{fence}markdown\n");
            suffix = Encoding.UTF8.GetBytes((payload[^1] == '\n' ? "" : "\n") + fence + "\n");
            var size = (long)prefix.Length + count + suffix.Length;
            if (size <= _options.MaxBytes) break;
            count = (int)Math.Min(count - 1L, (long)count * _options.MaxBytes / size);
            if (count <= 0) throw new IOException("Part limit is too small for Markdown framing.");
        }

        var content = _buffer.AsSpan(0, count);
        StrictUtf8.GetCharCount(content);
        var filename = $"{_options.Prefix}-{_parts.Count + 1:D3}.md";
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var file = new FileStream(Path.Combine(_stagingDirectory, filename), FileMode.CreateNew,
                   FileAccess.Write, FileShare.None, 65_536))
        {
            file.Write(prefix);
            file.Write(content);
            file.Write(suffix);
        }
        hash.AppendData(prefix);
        hash.AppendData(content);
        hash.AppendData(suffix);
        var bytes = prefix.Length + count + suffix.Length;
        _parts.Add(new Part(filename, bytes, Convert.ToHexStringLower(hash.GetHashAndReset()),
            prefix.Length, count, _sourceBytes));
        _sourceHash.AppendData(content);
        _sourceBytes += count;
        OutputBytes += bytes;
        _buffer.AsSpan(count, _buffered - count).CopyTo(_buffer);
        _buffered -= count;
    }

    private static int SafePayloadLength(ReadOnlySpan<byte> buffer, int limit)
    {
        var newline = buffer[..limit].LastIndexOf((byte)'\n');
        if (newline >= 0) return newline + 1;
        var leader = limit - 1;
        while (leader > 0 && (buffer[leader] & 0xC0) == 0x80) leader--;
        var value = buffer[leader];
        var width = value < 0x80 ? 1 : value < 0xE0 ? 2 : value < 0xF0 ? 3 : 4;
        return limit - leader < width ? leader : limit;
    }

    private static int LongestBacktickRun(ReadOnlySpan<byte> payload)
    {
        var longest = 0;
        var current = 0;
        foreach (var value in payload)
        {
            current = value == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }
        return longest;
    }

    /// <summary>Finishes the manifest and atomically publishes all parts together.</summary>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) throw new InvalidOperationException("Output has already been completed.");
        while (_buffered > 0) EmitPart();
        using (var file = File.Create(Path.Combine(_stagingDirectory, "manifest.json")))
        using (var json = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("max_bytes", _options.MaxBytes);
            json.WriteNumber("source_bytes", _sourceBytes);
            json.WriteString("source_sha256", Convert.ToHexStringLower(_sourceHash.GetHashAndReset()));
            json.WriteStartArray("parts");
            foreach (var part in _parts)
            {
                json.WriteStartObject();
                json.WriteString("file", part.File);
                json.WriteNumber("bytes", part.Bytes);
                json.WriteString("sha256", part.Sha256);
                json.WriteNumber("payload_offset", part.PayloadOffset);
                json.WriteNumber("payload_bytes", part.PayloadBytes);
                json.WriteNumber("source_offset", part.SourceOffset);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        if (_hadEmptyDirectory) Directory.Delete(OutputDirectory, false);
        Directory.Move(_stagingDirectory, OutputDirectory);
        _completed = true;
    }

    /// <inheritdoc/>
    public override void Flush() { }
    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _sourceHash.Dispose();
            if (!_completed && Directory.Exists(_stagingDirectory)) Directory.Delete(_stagingDirectory, true);
            if (!_completed && _hadEmptyDirectory && !Directory.Exists(OutputDirectory))
                Directory.CreateDirectory(OutputDirectory);
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
