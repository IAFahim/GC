using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using gc.Domain.Models;

namespace gc.Application.Services;

// The generator flushes at these boundaries, so positions refer to rendered UTF-8 bytes.
internal interface IMarkdownBoundarySink
{
    void BeginFile(string? path, long renderedBytes, int longestFence);
    void BeginContent(long bytes, IReadOnlyList<int>? sourceLines);
    void EndContent();
    void EndFile();
}

/// <summary>Streams UTF-8 Markdown into bounded, independently framed files and an integrity manifest.</summary>
public sealed class MarkdownChunkStream : Stream, IMarkdownBoundarySink
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ChunkOutputOptions _options;
    private readonly string _stagingDirectory;
    private readonly bool _hadEmptyDirectory;
    private readonly IncrementalHash _sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly List<Part> _parts = [];
    private readonly List<FileRegion> _pendingFiles = [];
    private FileRegion? _activeFile;
    private bool _inContent;
    private int _longestRun;
    private int _trailingRun;
    private byte[] _buffer;
    private int _buffered;
    private long _sourceBytes;
    private bool _completed;
    private bool _disposed;

    private sealed record Part(string File, int Bytes, string Sha256, int PayloadOffset,
        int PayloadBytes, long SourceOffset, FileRange[] Files);

    private sealed record FileRange(string Path, int? StartLine, int? EndLine, bool Continued, bool Continues);

    private sealed class FileRegion(string? path, long start, long end, bool whole)
    {
        public string? Path { get; } = path;
        public long Start { get; } = start;
        public long End { get; } = end;
        public bool Whole { get; } = whole;
        public long ContentStart { get; set; } = -1;
        public long ContentEnd { get; set; } = -1;
        public List<long> LineStarts { get; } = [];
        public IReadOnlyList<int>? SourceLines { get; set; }
        public bool PreviousWasCr { get; set; }

        public int LineAt(long offset)
        {
            var index = LineStarts.BinarySearch(offset);
            if (index < 0) index = ~index - 1;
            return SourceLines != null && index < SourceLines.Count
                ? SourceLines[Math.Max(0, index)] : Math.Max(0, index) + 1;
        }
    }

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

    void IMarkdownBoundarySink.BeginFile(string? path, long renderedBytes, int longestFence)
    {
        if (_activeFile != null) throw new InvalidOperationException("A Markdown file is already open.");
        var whole = FitsWhole(renderedBytes, longestFence);
        // Large files get their own parts. Whole files share a part when the complete block fits.
        if (!whole || !FitsWhole(_buffered + renderedBytes, Math.Max(_longestRun, longestFence)))
            while (_buffered > 0) EmitPart();
        _activeFile = new FileRegion(path, Length, Length + renderedBytes, whole);
        _pendingFiles.Add(_activeFile);
    }

    void IMarkdownBoundarySink.BeginContent(long bytes, IReadOnlyList<int>? sourceLines)
    {
        var file = _activeFile ?? throw new InvalidOperationException("No Markdown file is open.");
        file.ContentStart = Length;
        file.ContentEnd = Length + bytes;
        file.SourceLines = sourceLines;
        file.LineStarts.Add(Length);
        _inContent = true;
    }

    void IMarkdownBoundarySink.EndContent() => _inContent = false;

    void IMarkdownBoundarySink.EndFile()
    {
        var file = _activeFile ?? throw new InvalidOperationException("No Markdown file is open.");
        if (Length != file.End) throw new InvalidOperationException("Markdown file byte count did not match its boundary.");
        _inContent = false;
        _activeFile = null;
        if (!file.Whole) while (_buffered > 0) EmitPart();
    }

    private bool FitsWhole(long bytes, int longestFence)
    {
        var fenceBytes = Math.Max(4, longestFence + 1);
        var prefixBytes = Encoding.UTF8.GetByteCount($"# Context part {_parts.Count + 1:D3}\n\n") + fenceBytes + 9;
        return bytes + prefixBytes + fenceBytes + 2 <= _options.MaxBytes;
    }

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
            TrackContent(data[..count], Length);
            data[..count].CopyTo(_buffer.AsSpan(_buffered));
            _buffered += count;
            data = data[count..];
        }
    }

    private void TrackContent(ReadOnlySpan<byte> data, long offset)
    {
        foreach (var value in data)
        {
            _trailingRun = value == '`' ? _trailingRun + 1 : 0;
            _longestRun = Math.Max(_longestRun, _trailingRun);
            if (_inContent && _activeFile is { Path: not null } file)
            {
                if (value == '\n' && file.PreviousWasCr)
                    file.LineStarts[^1] = offset + 1;
                else if (value is (byte)'\r' or (byte)'\n')
                    file.LineStarts.Add(offset + 1);
                file.PreviousWasCr = value == '\r';
            }
            offset++;
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
            var continuation = GetRanges(_sourceBytes, _sourceBytes + count).FirstOrDefault(f => f.Continued);
            var label = continuation == null ? "" : ContinuationLabel(continuation);
            prefix = Encoding.UTF8.GetBytes($"# Context part {_parts.Count + 1:D3}\n\n{label}{fence}markdown\n");
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
            prefix.Length, count, _sourceBytes, GetRanges(_sourceBytes, _sourceBytes + count)));
        _sourceHash.AppendData(content);
        _sourceBytes += count;
        OutputBytes += bytes;
        _buffer.AsSpan(count, _buffered - count).CopyTo(_buffer);
        _buffered -= count;
        _pendingFiles.RemoveAll(f => f.End <= _sourceBytes);
        _longestRun = LongestBacktickRun(_buffer.AsSpan(0, _buffered));
        _trailingRun = 0;
        for (var i = _buffered - 1; i >= 0 && _buffer[i] == '`'; i--) _trailingRun++;
    }

    private FileRange[] GetRanges(long start, long end)
    {
        var ranges = new List<FileRange>();
        foreach (var file in _pendingFiles)
        {
            if (file.Path == null || file.Start >= end || file.End <= start) continue;
            var contentStart = Math.Max(start, file.ContentStart);
            var contentEnd = Math.Min(end, file.ContentEnd);
            var hasContent = file.ContentStart >= 0 && contentStart < contentEnd;
            ranges.Add(new FileRange(file.Path, hasContent ? file.LineAt(contentStart) : null,
                hasContent ? file.LineAt(contentEnd - 1) : null, file.Start < start, file.End > end));
        }
        return ranges.ToArray();
    }

    private string ContinuationLabel(FileRange file)
    {
        var path = file.Path;
        // Keep tiny budgets usable with long paths; the manifest always retains the complete path.
        var pathLimit = Math.Max(16, _options.MaxBytes / 4);
        if (Encoding.UTF8.GetByteCount(path) > pathLimit)
        {
            var encoded = Encoding.UTF8.GetBytes(path);
            var count = pathLimit - 3;
            while (count > 0 && (encoded[count] & 0xC0) == 0x80) count--;
            path = StrictUtf8.GetString(encoded.AsSpan(0, count)) + "…";
        }
        return file.StartLine.HasValue
            ? $"Source: {path} (lines {file.StartLine}-{file.EndLine}, continued)\n\n"
            : $"Source: {path} (continued)\n\n";
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
                json.WriteStartArray("files");
                foreach (var range in part.Files)
                {
                    json.WriteStartObject();
                    json.WriteString("path", range.Path);
                    if (range.StartLine.HasValue) json.WriteNumber("start_line", range.StartLine.Value);
                    else json.WriteNull("start_line");
                    if (range.EndLine.HasValue) json.WriteNumber("end_line", range.EndLine.Value);
                    else json.WriteNull("end_line");
                    json.WriteBoolean("continued", range.Continued);
                    json.WriteBoolean("continues", range.Continues);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
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
