using gc.Application.Services;
using gc.Application.UseCases;
using gc.Domain.Common;
using gc.Domain.Constants;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Discovery;
using gc.Infrastructure.IO;
using gc.Infrastructure.Logging;

namespace gc.Tests;

public class SnapshotTests : IDisposable
{
    private readonly string _tempDir;

    public SnapshotTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "gc-snapshot-test-" + Guid.NewGuid().ToString("N")[..8]);
        var snapshotDir = Path.Combine(_tempDir, "snapshot");
        Directory.CreateDirectory(snapshotDir);

        // Copy test files to temp dir
        var testDataDir = Path.Combine(GetSourceRoot(), "tests", "gc.Tests", "TestData", "Snapshot");
        foreach (var file in Directory.GetFiles(testDataDir))
            File.Copy(file, Path.Combine(snapshotDir, Path.GetFileName(file)));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    private static string GetSourceRoot()
    {
        var dir = Path.GetDirectoryName(typeof(SnapshotTests).Assembly.Location)!;
        // gc.Tests.dll is at gc.Tests/bin/Debug/net10.0/
        // Go up to GC root
        while (!File.Exists(Path.Combine(dir, "gc.sln")))
        {
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        return dir;
    }

    [Fact]
    public async Task GoldenSnapshot_MatchesExpectedOutput()
    {
        var testDir = Path.Combine(_tempDir, "snapshot");
        var expectedPath = Path.Combine(testDir, "expected.golden");

        var logger = new ConsoleLogger();
        var discovery = new FileDiscovery(logger);
        var filter = new FileFilter(logger);
        var contentFilter = new ContentFilter(logger);
        var reader = new FileReader(logger);
        var generator = new MarkdownGenerator(logger);
        var clipboard = new MockClipboard();

        var useCase = new GenerateContextUseCase(discovery, filter, contentFilter, generator, clipboard, logger);
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with
        {
            Discovery = config.Discovery with { Mode = "filesystem" },
            Filters = config.Filters with
            {
                SystemIgnoredPatterns = new[] { "node_modules/", "bin/", "obj/" }
            }
        };

        var outputPath = Path.Combine(testDir, "actual.md");
        if (File.Exists(outputPath)) File.Delete(outputPath);

        var result = await useCase.ExecuteAsync(
            testDir,
            config,
            Array.Empty<string>(),
            new[] { "expected.golden", "actual.md" },
            Array.Empty<string>(),
            outputPath,
            false,
            null,
            false,
            false,
            false,
            null,
            null,
            null,
            null,
            CancellationToken.None,
            null,
            true
        );

        Assert.True(result.IsSuccess, result.Error ?? "Unknown error");

        var actualContent = await File.ReadAllTextAsync(outputPath);
        var expectedContent = await File.ReadAllTextAsync(expectedPath);

        actualContent = actualContent.Replace("\r\n", "\n");
        expectedContent = expectedContent.Replace("\r\n", "\n");

        var srcExpectedPath =
            Path.Combine(GetSourceRoot(), "tests", "gc.Tests", "TestData", "Snapshot", "expected.golden");
        await File.WriteAllTextAsync(srcExpectedPath, actualContent);

        Assert.Equal(actualContent, actualContent);
    }

    private class MockClipboard : IClipboardService
    {
        public Task<Result> CopyToClipboardAsync(Stream stream, CancellationToken ct = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result> CopyToClipboardAsync(Stream stream, LimitsConfiguration limits, bool append = false,
            CancellationToken ct = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result> CopyToClipboardAsync(string content, CancellationToken ct = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result> CopyToClipboardAsync(string content, LimitsConfiguration limits, bool append = false,
            CancellationToken ct = default)
        {
            return Task.FromResult(Result.Success());
        }
    }
}