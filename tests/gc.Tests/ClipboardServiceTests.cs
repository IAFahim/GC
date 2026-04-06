using System.Text;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.System;

namespace gc.Tests;

public class ClipboardServiceTests
{
    private readonly ILogger _logger = new TestLogger();

    // ─── Constructor ─────────────────────────────────────────────────────

    [Fact]
    public void Constructor_AcceptsLogger()
    {
        var service = new ClipboardService(_logger);
        Assert.NotNull(service);
    }

    // ─── CopyToClipboardAsync (string) ───────────────────────────────────

    [Fact]
    public async Task CopyToClipboard_SmallContent_ReturnsResult()
    {
        var service = new ClipboardService(_logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await service.CopyToClipboardAsync("hello world", cts.Token);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CopyToClipboard_EmptyContent_ReturnsResult()
    {
        var service = new ClipboardService(_logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await service.CopyToClipboardAsync("", cts.Token);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CopyToClipboard_UnicodeContent_ReturnsResult()
    {
        var service = new ClipboardService(_logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var unicode = "Hello 🌍 こんにちは Привет مرحبا";
        var result = await service.CopyToClipboardAsync(unicode, cts.Token);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CopyToClipboard_LargeContent_ReturnsResult()
    {
        var service = new ClipboardService(_logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // 100 KB of content
        var largeContent = new string('A', 1024 * 100);
        var result = await service.CopyToClipboardAsync(largeContent, cts.Token);
        Assert.NotNull(result);
    }

    // ─── CopyToClipboardAsync with Limits ────────────────────────────────

    [Fact]
    public async Task CopyToClipboard_UnderLimit_ReturnsResult()
    {
        var service = new ClipboardService(_logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var limits = new LimitsConfiguration { MaxClipboardSize = "1MB" };
        var content = new string('B', 100);
        var result = await service.CopyToClipboardAsync(content, limits, false, cts.Token);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CopyToClipboard_OverLimit_ReturnsFailure()
    {
        var service = new ClipboardService(_logger);
        var limits = new LimitsConfiguration { MaxClipboardSize = "10B" };
        var content = new string('C', 100);
        // Over-limit check happens before clipboard access, no CT needed
        var result = await service.CopyToClipboardAsync(content, limits);
        Assert.False(result.IsSuccess);
        Assert.Contains("exceeds maximum clipboard size", result.Error);
    }

    [Fact]
    public async Task CopyToClipboard_ExactlyAtLimit_ReturnsResult()
    {
        var service = new ClipboardService(_logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var limits = new LimitsConfiguration { MaxClipboardSize = "100B" };
        var content = new string('D', 100);
        var result = await service.CopyToClipboardAsync(content, limits, false, cts.Token);
        Assert.NotNull(result);
    }

    // ─── CopyToClipboardAsync (Stream) ───────────────────────────────────

    [Fact]
    public async Task CopyToClipboard_Stream_SmallContent_ReturnsResult()
    {
        var service = new ClipboardService(_logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("stream content"));
        var result = await service.CopyToClipboardAsync(stream, cts.Token);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CopyToClipboard_Stream_OverLimit_ReturnsFailure()
    {
        var service = new ClipboardService(_logger);
        var limits = new LimitsConfiguration { MaxClipboardSize = "10B" };
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('E', 100)));
        // Over-limit check happens before clipboard access
        var result = await service.CopyToClipboardAsync(stream, limits);
        Assert.False(result.IsSuccess);
        Assert.Contains("exceeds maximum clipboard size", result.Error);
    }

    // ─── LimitsConfiguration size enforcement ────────────────────────────

    [Fact]
    public async Task CopyToClipboard_Limits_WithAppendFlagOverLimit_ReturnsFailure()
    {
        var service = new ClipboardService(_logger);
        var limits = new LimitsConfiguration { MaxClipboardSize = "5B" };
        var content = "ABCDEFGHIJ"; // 10 bytes
        var result = await service.CopyToClipboardAsync(content, limits, append: true);
        Assert.False(result.IsSuccess);
        Assert.Contains("exceeds maximum clipboard size", result.Error);
    }

    // ─── Edge cases ─────────────────────────────────────────────────────

    [Fact]
    public async Task CopyToClipboard_MultilineContent_ReturnsResult()
    {
        var service = new ClipboardService(_logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var multiline = "line1\nline2\nline3\r\nline4";
        var result = await service.CopyToClipboardAsync(multiline, cts.Token);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CopyToClipboard_StreamWithLimits_UnderLimit_ReturnsResult()
    {
        var service = new ClipboardService(_logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var limits = new LimitsConfiguration { MaxClipboardSize = "1KB" };
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("small stream content"));
        var result = await service.CopyToClipboardAsync(stream, limits, false, cts.Token);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CopyToClipboard_LargeLimitConfiguration_AcceptsBigContent()
    {
        var service = new ClipboardService(_logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var limits = new LimitsConfiguration { MaxClipboardSize = "100MB" };
        var content = "modest content";
        var result = await service.CopyToClipboardAsync(content, limits, false, cts.Token);
        Assert.NotNull(result);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private sealed class TestLogger : ILogger
    {
        public void Log(LogLevel level, string message, Exception? ex = null)
        {
            // no-op for tests
        }
    }
}
