using System.Buffers;
using System.Diagnostics;
using System.Text;
using gc.Application.Services;
using gc.Domain.Constants;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Configuration;
using gc.Infrastructure.IO;
using gc.Infrastructure.Logging;
using Xunit.Abstractions;

namespace gc.Tests;

public class Phase2PerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly gc.Domain.Interfaces.ILogger _logger;
    private readonly string _tempDir;
    private readonly GcConfiguration _config;

    public Phase2PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = new ConsoleLogger();
        _tempDir = Path.Combine(Path.GetTempPath(), $"gc_phase2_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _config = BuiltInPresets.GetDefaultConfiguration();
    }

    [Fact]
    public async Task Optimization2_ParallelRead_PreservesOrder()
    {
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var filesDir = Path.Combine(_tempDir, "parallel_order");
        Directory.CreateDirectory(filesDir);

        var entries = new List<FileContent>();
        for (int i = 0; i < 50; i++)
        {
            var path = Path.Combine(filesDir, $"file_{i:D2}.txt");
            await File.WriteAllTextAsync(path, $"Content {i}");
            entries.Add(new FileContent(new FileEntry(path, "txt", "text", -1), null, -1));
        }

        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var output = Encoding.UTF8.GetString(ms.ToArray());
        
        // Assert output order is strictly file_00 to file_49
        for (int i = 0; i < 49; i++)
        {
            var idxA = output.IndexOf($"file_{i:D2}.txt");
            var idxB = output.IndexOf($"file_{i+1:D2}.txt");
            Assert.True(idxA != -1, $"Missing file_{i:D2}.txt");
            Assert.True(idxB != -1, $"Missing file_{i+1:D2}.txt");
            Assert.True(idxA < idxB, $"Out of order: file_{i:D2} vs file_{i+1:D2}");
        }
    }

    [Fact]
    public async Task Optimization2_DirectUTF8Writes_ByteCountAccurate()
    {
        var generator = new MarkdownGenerator(_logger, new FileReader(_logger));
        var content = "direct utf8 test content";
        var path = Path.Combine(_tempDir, "utf8.txt");
        await File.WriteAllTextAsync(path, content);
        
        var entries = new List<FileContent> { new(new FileEntry(path, "txt", "text", -1), null, -1) };
        using var ms = new MemoryStream();
        var result = await generator.GenerateMarkdownStreamingAsync(entries, ms, _config, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ms.ToArray().Length, result.Value);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
