using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace gc.Application.Services;

/// <summary>The non-Markdown output formats selected by <c>output.defaultFormat</c> or <c>--format</c>.</summary>
public enum OutputFormat
{
    Markdown,

    /// <summary>Each file as <c>==> path &lt;==</c>, its content, and a blank line. No fences, no file listing.</summary>
    Plain,

    /// <summary>One JSON document: <c>{ "fileCount": N, "files": [ { "path": ..., "content": ... } ] }</c>.</summary>
    Json
}

/// <summary>
///     Receives the generator's Markdown, keeps only each file's exact content (using the same boundary
///     notifications that split Markdown parts) and writes the chosen format to the destination on
///     <see cref="Complete" />. Fences, headers and the project-structure block are dropped.
/// </summary>
internal sealed class StructuredOutputStream : Stream, IMarkdownBoundarySink
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly Stream _destination;
    private readonly OutputFormat _format;
    private readonly List<(string Path, MemoryStream Content)> _files = [];
    private MemoryStream? _current;
    private string? _currentPath;
    private bool _inContent;

    public StructuredOutputStream(Stream destination, OutputFormat format)
    {
        _destination = destination;
        _format = format;
    }

    /// <summary>Number of files whose content was captured.</summary>
    public int FileCount => _files.Count;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    void IMarkdownBoundarySink.BeginFile(string? path, long renderedBytes, int longestFence)
    {
        _currentPath = path;
        _current = null;
        _inContent = false;
    }

    void IMarkdownBoundarySink.BeginContent(long bytes, IReadOnlyList<int>? sourceLines)
    {
        // Synthetic entries (cluster separators, error and skip notes) carry no path and are not files.
        if (_currentPath == null) return;
        _current = new MemoryStream((int)Math.Min(bytes, int.MaxValue));
        _inContent = true;
    }

    void IMarkdownBoundarySink.EndContent() => _inContent = false;

    void IMarkdownBoundarySink.EndFile()
    {
        if (_current != null && _currentPath != null) _files.Add((_currentPath, _current));
        _current = null;
        _currentPath = null;
        _inContent = false;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_inContent) _current!.Write(buffer);
    }

    public override void Flush()
    {
    }

    /// <summary>Writes the collected files to the destination in the chosen format.</summary>
    public void Complete()
    {
        if (_format == OutputFormat.Json) WriteJson();
        else WritePlain();
        _destination.Flush();
    }

    private void WritePlain()
    {
        foreach (var (path, content) in _files)
        {
            _destination.Write(Utf8.GetBytes($"==> {path} <==\n"));
            _destination.Write(content.GetBuffer(), 0, (int)content.Length);
            _destination.Write("\n\n"u8);
        }
    }

    private void WriteJson()
    {
        using var writer = new Utf8JsonWriter(_destination, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        writer.WriteStartObject();
        writer.WriteNumber("fileCount", _files.Count);
        writer.WriteStartArray("files");
        foreach (var (path, content) in _files)
        {
            writer.WriteStartObject();
            writer.WriteString("path", path);
            writer.WriteString("content", Utf8.GetString(content.GetBuffer(), 0, (int)content.Length));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
