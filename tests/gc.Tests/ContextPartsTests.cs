using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using gc.Application.Services;
using gc.CLI.Services;
using gc.Domain.Common;
using gc.Domain.Constants;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Discovery;
using gc.Infrastructure.Logging;

namespace gc.Tests;

public sealed class ContextPartsTests
{
    [Theory]
    [InlineData("50", 50000)]
    [InlineData("50KB", 50000)]
    [InlineData("50KiB", 51200)]
    [InlineData("50000B", 50000)]
    [InlineData("0.5MB", 500000)]
    public void SizesAreExplicitFinalByteBudgets(string text, int expected)
    {
        Assert.True(ChunkSize.TryParse(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("255B")]
    [InlineData("-50KB")]
    [InlineData("wat")]
    [InlineData("99999999999999999999MB")]
    public void InvalidBudgetsAreRejected(string text) => Assert.False(ChunkSize.TryParse(text, out _));

    [Theory]
    [InlineData("-k")]
    [InlineData("-k50")]
    [InlineData("--split=50KB")]
    public void ShortSplitCommandsUseFiftyThousandBytes(string flag)
    {
        var parsed = new CliParser().Parse([flag], BuiltInPresets.GetDefaultConfiguration());
        Assert.True(parsed.IsSuccess);
        Assert.Equal(50000, parsed.Value!.SplitBytes);
    }

    [Fact]
    public void UnknownFlagsAreNotSwallowedAsExtensions()
    {
        var parsed = new CliParser().Parse(["-e", "cs", "--splti", "50KB"], BuiltInPresets.GetDefaultConfiguration());
        Assert.False(parsed.IsSuccess);
        Assert.Contains("--splti", parsed.Error);
    }

    [Theory]
    [InlineData("-o", "--split=50KB")]
    [InlineData("--output-dir", "--splti")]
    [InlineData("--root", "--split=50KB")]
    public void FlagsCannotBecomeSingleOptionValues(string option, string next)
    {
        var parsed = new CliParser().Parse([option, next], BuiltInPresets.GetDefaultConfiguration());
        Assert.False(parsed.IsSuccess);
        Assert.Contains("Missing value", parsed.Error);
    }

    [Fact]
    public void DirectFolderAfterExtensionSelectsThatFolder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gc folder বাংলা " + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var parsed = new CliParser().Parse(["-e", "cs", directory, "-k"], BuiltInPresets.GetDefaultConfiguration());
            Assert.True(parsed.IsSuccess);
            Assert.Equal(["cs"], parsed.Value!.Extensions);
            Assert.Equal(directory, InvocationRootResolver.Resolve(parsed.Value, Path.GetTempPath()));
            Assert.Empty(parsed.Value.Paths);
            Assert.Equal(50000, parsed.Value.SplitBytes);
            Assert.Equal(directory, InvocationRootResolver.FindRoot(["-e", "cs", directory], Path.GetTempPath()));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void DirectFileSelectsOnlyThatFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gc-file-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "Some file.cs");
        File.WriteAllText(file, "class Example {}");
        try
        {
            var parsed = new CliParser().Parse([file, "-k"], BuiltInPresets.GetDefaultConfiguration()).Value!;
            Assert.Equal(directory, InvocationRootResolver.Resolve(parsed, Path.GetTempPath()));
            Assert.Equal(["Some file.cs"], parsed.Paths);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DirectFileWorksWithFilesystemDiscoveryAndDoesNotSelectOtherBasenames()
    {
        var root = Directory.CreateTempSubdirectory("gc-direct-file-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "other"));
            var selected = Path.Combine(root, "Example.cs");
            File.WriteAllText(selected, "selected");
            File.WriteAllText(Path.Combine(root, "other", "Example.cs"), "other");
            var misleading = Directory.CreateDirectory(Path.Combine(root, "other", "misleading", "Example.cs"));
            File.WriteAllText(Path.Combine(misleading.FullName, "Hidden.cs"), "must not match a directory with the file's name");
            var config = BuiltInPresets.GetDefaultConfiguration() with
            {
                Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
            };
            var logger = new ConsoleLogger();
            var discovered = await new FileDiscovery(logger).DiscoverFilesAsync(root, config, default);
            Assert.True(discovered.IsSuccess);
            var parsed = new CliParser().Parse([selected, "-f", "-k"], config).Value!;
            Assert.Equal(root, InvocationRootResolver.Resolve(parsed, Path.GetTempPath()));
            var filtered = new FileFilter(logger).FilterFiles(discovered.Value!, config,
                parsed.Paths, [], [], rootPath: root, absoluteRootPath: root);
            Assert.True(filtered.IsSuccess);
            Assert.Equal(selected, Assert.Single(filtered.Value!).AbsolutePath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FilesThatFitOnePartStayWholeWhenTheyCannotShareAPart()
    {
        var root = Path.Combine(Path.GetTempPath(), "gc-whole-files-" + Guid.NewGuid());
        var contents = Enumerable.Range(1, 3).Select(index => new FileContent(
            new FileEntry("", $"File{index}.cs", "cs", "cs", 700), new string('x', 700), 700)).ToArray();
        try
        {
            using (var chunks = new MarkdownChunkStream(new ChunkOutputOptions(1024, root)))
            {
                var result = await new MarkdownGenerator(new ConsoleLogger()).GenerateMarkdownStreamingAsync(
                    contents, chunks, BuiltInPresets.GetDefaultConfiguration());
                Assert.True(result.IsSuccess, result.Error);
                chunks.Complete();
            }
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
            var ranges = json.RootElement.GetProperty("parts").EnumerateArray()
                .SelectMany(part => part.GetProperty("files").EnumerateArray()).ToArray();
            Assert.Equal(3, ranges.Length);
            Assert.All(ranges, range =>
            {
                Assert.False(range.GetProperty("continued").GetBoolean());
                Assert.False(range.GetProperty("continues").GetBoolean());
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ContinuationsHaveAccuratePathsAndOriginalSourceLines(bool inMemory, bool excludeComments)
    {
        var root = Directory.CreateTempSubdirectory("gc-source-lines-").FullName;
        var output = Path.Combine(root, "parts");
        var lines = Enumerable.Range(1, 1200)
            .Select(i => i % 2 == 0 ? $"L{i}: বাংলা 🦊 source line" : "// removed comment").ToArray();
        var source = string.Join("\r\n", lines);
        var renderedContent = excludeComments ? string.Join('\n', lines.Where(line => !line.StartsWith("//"))) : source;
        var originalLines = Enumerable.Range(1, lines.Length).Where(i => !excludeComments || i % 2 == 0).ToArray();
        var contentBytes = Encoding.UTF8.GetBytes(renderedContent);
        var file = Path.Combine(root, "Large.cs");
        File.WriteAllText(file, source, new UTF8Encoding(false));
        var contents = new[] { new FileContent(new FileEntry(root, "Large.cs", "cs", "cs", source.Length),
            inMemory ? source : null, source.Length) };
        var config = BuiltInPresets.GetDefaultConfiguration();
        string[] excludes = excludeComments ? ["//"] : [];
        try
        {
            using var baseline = new MemoryStream();
            var generator = new MarkdownGenerator(new ConsoleLogger());
            var normal = await generator.GenerateMarkdownStreamingAsync(contents, baseline, config, excludes);
            Assert.True(normal.IsSuccess, normal.Error);
            using (var chunks = new MarkdownChunkStream(new ChunkOutputOptions(2048, output)))
            {
                var result = await generator.GenerateMarkdownStreamingAsync(contents, chunks, config, excludes);
                Assert.True(result.IsSuccess, result.Error);
                chunks.Complete();
            }
            var contentStart = Encoding.UTF8.GetByteCount($"## File: Large.cs{Environment.NewLine}````cs{Environment.NewLine}");
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
            using var restored = new MemoryStream();
            var continuations = 0;
            foreach (var part in json.RootElement.GetProperty("parts").EnumerateArray())
            {
                var bytes = File.ReadAllBytes(Path.Combine(output, part.GetProperty("file").GetString()!));
                Assert.InRange(bytes.Length, 1, 2048);
                var payloadOffset = part.GetProperty("payload_offset").GetInt32();
                var payloadBytes = part.GetProperty("payload_bytes").GetInt32();
                var sourceStart = part.GetProperty("source_offset").GetInt64();
                restored.Write(bytes.AsSpan(payloadOffset, payloadBytes));
                foreach (var range in part.GetProperty("files").EnumerateArray())
                {
                    Assert.Equal("Large.cs", range.GetProperty("path").GetString());
                    var start = Math.Max(sourceStart, contentStart) - contentStart;
                    var end = Math.Min(sourceStart + payloadBytes, contentStart + contentBytes.Length) - contentStart;
                    if (start < end)
                    {
                        var first = contentBytes.AsSpan(0, (int)start).Count((byte)'\n');
                        var last = contentBytes.AsSpan(0, (int)end - 1).Count((byte)'\n');
                        Assert.Equal(originalLines[first], range.GetProperty("start_line").GetInt32());
                        Assert.Equal(originalLines[last], range.GetProperty("end_line").GetInt32());
                    }
                    if (range.GetProperty("continued").GetBoolean())
                    {
                        continuations++;
                        Assert.Contains("Source: Large.cs", Encoding.UTF8.GetString(bytes.AsSpan(0, payloadOffset)));
                    }
                }
            }
            Assert.True(continuations > 1);
            Assert.Equal(baseline.ToArray(), restored.ToArray());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeFilesReachTheOutputStreamBeforeGenerationFinishes(bool inMemory)
    {
        var root = Directory.CreateTempSubdirectory("gc-streaming-").FullName;
        try
        {
            var source = new string('x', 2_000_000);
            File.WriteAllText(Path.Combine(root, "Large.cs"), source, new UTF8Encoding(false));
            var contents = new[] { new FileContent(new FileEntry(root, "Large.cs", "cs", "cs", source.Length),
                inMemory ? source : null, source.Length) };
            var generator = new MarkdownGenerator(new ConsoleLogger());
            var emittedAtFirstWrite = -1;
            using var output = new ObservingStream(() => emittedAtFirstWrite = generator.LastEmittedFileCount);
            var config = BuiltInPresets.GetDefaultConfiguration() with
            {
                Limits = new LimitsConfiguration { MaxFileSize = "4MB" }
            };
            var result = await generator.GenerateMarkdownStreamingAsync(contents, output, config);
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(0, emittedAtFirstWrite);
            Assert.True(output.Length >= source.Length);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class ObservingStream(Action onFirstWrite) : Stream
    {
        private long _length;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_length == 0) onFirstWrite();
            _length += buffer.Length;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(4096, 1)]
    [InlineData(50000, 3)]
    [InlineData(50000, 65536)]
    public void PartsPreserveEveryByteAcrossUtf8AndFenceBoundaries(int budget, int writeSize)
    {
        var directory = Path.Combine(Path.GetTempPath(), "gc-parts-" + Guid.NewGuid());
        var text = "```cs\r\n" + string.Concat(Enumerable.Repeat("বাংলা 🦊 中文\r\n", 5000)) +
                   new string('`', 120000) + string.Concat(Enumerable.Repeat("🦊", 30000)) + "\r\n```\n";
        var expected = Encoding.UTF8.GetBytes(text);
        try
        {
            using (var stream = new MarkdownChunkStream(new ChunkOutputOptions(budget, directory)))
            {
                for (var offset = 0; offset < expected.Length; offset += writeSize)
                    stream.Write(expected.AsSpan(offset, Math.Min(writeSize, expected.Length - offset)));
                stream.Complete();
                Assert.True(stream.PartCount > 1);
            }
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
            using var restored = new MemoryStream();
            foreach (var part in manifest.RootElement.GetProperty("parts").EnumerateArray())
            {
                var bytes = File.ReadAllBytes(Path.Combine(directory, part.GetProperty("file").GetString()!));
                Assert.InRange(bytes.Length, 1, budget);
                new UTF8Encoding(false, true).GetString(bytes);
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), part.GetProperty("sha256").GetString());
                var start = part.GetProperty("payload_offset").GetInt32();
                var count = part.GetProperty("payload_bytes").GetInt32();
                var fenceLine = Encoding.UTF8.GetString(bytes.AsSpan(0, start)).Split('\n')[2];
                var fence = fenceLine[..^"markdown".Length];
                Assert.DoesNotContain(fence, Encoding.UTF8.GetString(bytes.AsSpan(start, count)));
                Assert.EndsWith(fence + "\n", Encoding.UTF8.GetString(bytes));
                restored.Write(bytes.AsSpan(start, count));
            }
            Assert.Equal(expected, restored.ToArray());
            Assert.Equal(expected.Length, manifest.RootElement.GetProperty("source_bytes").GetInt64());
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(expected)),
                manifest.RootElement.GetProperty("source_sha256").GetString());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void FailedOrUnfinishedOutputDoesNotPublishPartialFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gc-unfinished-" + Guid.NewGuid());
        using (var stream = new MarkdownChunkStream(new ChunkOutputOptions(1024, directory)))
            stream.Write(Encoding.UTF8.GetBytes(new string('a', 10000)));
        Assert.False(Directory.Exists(directory));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(directory)!, Path.GetFileName(directory) + ".tmp.*"));
    }

    [Fact]
    public void ExistingOutputIsNeverOverwritten()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gc-existing-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "context-001.md");
        File.WriteAllText(file, "keep my export");
        try
        {
            Assert.Throws<IOException>(() => new MarkdownChunkStream(new ChunkOutputOptions(1024, directory)));
            Assert.Equal("keep my export", File.ReadAllText(file));
        }
        finally { Directory.Delete(directory, true); }
    }
}
