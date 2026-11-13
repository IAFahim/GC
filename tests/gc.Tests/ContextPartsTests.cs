using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using gc.Application.Services;
using gc.CLI.Services;
using gc.Domain.Common;
using gc.Domain.Constants;
using gc.Domain.Models;

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
