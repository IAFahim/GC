using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using gc.Application.Services;
using gc.Application.UseCases;
using gc.Domain.Common;
using gc.Domain.Constants;
using gc.Domain.Interfaces;
using gc.Infrastructure.Discovery;
using gc.Infrastructure.Logging;
using gc.Infrastructure.System;
using Xunit;

namespace gc.Tests;

public class SnapshotTests
{
    private class MockClipboard : IClipboardService
    {
        public Task<Result> CopyToClipboardAsync(Stream stream, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> CopyToClipboardAsync(Stream stream, gc.Domain.Models.Configuration.LimitsConfiguration limits, bool append = false, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> CopyToClipboardAsync(string content, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> CopyToClipboardAsync(string content, gc.Domain.Models.Configuration.LimitsConfiguration limits, bool append = false, CancellationToken ct = default) => Task.FromResult(Result.Success());
    }

    [Fact]
    public async Task GoldenSnapshot_MatchesExpectedOutput()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData", "Snapshot");
        var expectedPath = Path.Combine(testDir, "expected.golden");
        
        var logger = new ConsoleLogger(); // no MinimumLevel property
        var discovery = new FileDiscovery(logger);
        var filter = new FileFilter(logger);
        var contentFilter = new ContentFilter(logger);
        var reader = new gc.Infrastructure.IO.FileReader(logger);
        var generator = new MarkdownGenerator(logger, reader);
        var clipboard = new MockClipboard();

        var useCase = new GenerateContextUseCase(discovery, filter, contentFilter, reader, generator, clipboard, logger);
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Discovery = config.Discovery with { Mode = "filesystem" } };

        var outputPath = Path.Combine(testDir, "actual.md");
        if (File.Exists(outputPath)) File.Delete(outputPath);

        var result = await useCase.ExecuteAsync(
            testDir,
            config,
            paths: System.Array.Empty<string>(),
            excludes: new[] { "expected.golden", "actual.md" },
            extensions: System.Array.Empty<string>(),
            outputFile: outputPath,
            appendMode: false,
            excludeLineIfStart: null,
            brainMode: false,
            compress: false,
            noCache: false,
            excludePathPatterns: null,
            includePathPatterns: null,
            excludeContentPatterns: null,
            includeContentPatterns: null,
            ct: CancellationToken.None,
            profileReporter: null,
            unsafeDirectWrite: true
        );

        Assert.True(result.IsSuccess);

        var actualContent = await File.ReadAllTextAsync(outputPath);
        var expectedContent = await File.ReadAllTextAsync(expectedPath);

        // Normalize line endings
        actualContent = actualContent.Replace("\r\n", "\n");
        expectedContent = expectedContent.Replace("\r\n", "\n");

        Assert.Equal(expectedContent, actualContent);

        if (File.Exists(outputPath)) File.Delete(outputPath);
    }
}
