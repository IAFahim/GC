using System.Diagnostics;
using gc.Application.Services;
using gc.Application.UseCases;
using gc.Domain.Common;
using gc.Domain.Constants;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Discovery;
using gc.Infrastructure.IO;

namespace gc.Tests;

/// <summary>Settings that used to be read by nothing now change behaviour (issue #14).</summary>
public sealed class SettingsWiringTests : IDisposable
{
    private readonly CaptureLogger _logger = new();
    private readonly string _root = Directory.CreateTempSubdirectory("gc-wiring").FullName;

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }
        catch
        {
            // best effort
        }
    }

    private sealed class CaptureLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = new();
        public void Log(LogLevel level, string message, Exception? ex = null) => Messages.Add((level, message));
    }

    private sealed class FixedDiscovery : IFileDiscovery
    {
        public List<RepoInfo> Repos { get; } = new();
        public Dictionary<string, List<string>> Files { get; } = new();

        public Task<Result<IEnumerable<string>>> DiscoverFilesAsync(string rootPath, GcConfiguration config,
            CancellationToken ct) =>
            Task.FromResult(Result<IEnumerable<string>>.Success(
                Files.TryGetValue(rootPath, out var files) ? files : new List<string>()));

        public Task<Result<IEnumerable<string>>> DiscoverFilesSinceAsync(string rootPath, string reference,
            GcConfiguration config, CancellationToken ct) => DiscoverFilesAsync(rootPath, config, ct);

        public Task<Result<IReadOnlyList<RepoInfo>>> DiscoverGitReposAsync(string clusterRoot,
            ClusterConfiguration clusterConfig, CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<RepoInfo>>.Success(Repos));
    }

    private GenerateContextUseCase UseCase(IFileDiscovery discovery) =>
        new(discovery, new FileFilter(_logger), new ContentFilter(_logger), new MarkdownGenerator(_logger),
            new NoClipboard(), _logger);

    private sealed class NoClipboard : IClipboardService
    {
        public Task<Result> CopyToClipboardAsync(Stream stream, CancellationToken ct) => Task.FromResult(Result.Success());

        public Task<Result> CopyToClipboardAsync(Stream stream, LimitsConfiguration limits, bool append,
            CancellationToken ct) => Task.FromResult(Result.Success());

        public Task<Result> CopyToClipboardAsync(string content, CancellationToken ct) => Task.FromResult(Result.Success());

        public Task<Result> CopyToClipboardAsync(string content, LimitsConfiguration limits, bool append,
            CancellationToken ct) => Task.FromResult(Result.Success());
    }

    private static GcConfiguration Config() => BuiltInPresets.GetDefaultConfiguration();

    // ── content patterns: wildcards and the settings file ───────────────────────

    [Theory]
    [InlineData("foo*bar", "foo and then bar", true)]
    [InlineData("foo*bar", "bar before foo", false)]
    [InlineData("a?c", "xabcx", true)]
    [InlineData("a?c", "ac", false)]
    [InlineData("TODO*urgent", "TODO\nmake it urgent", true)]
    [InlineData("plain", "some plain text", true)]
    public void IncludePattern_SupportsStarAndQuestionMark(string pattern, string content, bool expected)
    {
        var compiled = new ContentFilter(_logger).CompilePatterns([], [pattern]);

        Assert.Equal(expected, compiled.ShouldInclude(content));
        Assert.Equal(expected, compiled.ShouldInclude(System.Text.Encoding.UTF8.GetBytes(content), content.Length));
    }

    [Fact]
    public void ExcludePattern_Wildcard_RemovesMatchingFiles()
    {
        var compiled = new ContentFilter(_logger).CompilePatterns(["pass*word"], []);

        Assert.False(compiled.ShouldInclude("my password here"));
        Assert.True(compiled.ShouldInclude("nothing secret"));
    }

    [Fact]
    public async Task ContentPatternsInSettings_AreApplied()
    {
        File.WriteAllText(Path.Combine(_root, "keep.cs"), "class Keep {}");
        File.WriteAllText(Path.Combine(_root, "drop.cs"), "class Drop {}");
        var output = Path.Combine(_root, "out.md");
        var config = Config() with
        {
            Filters = Config().Filters with { ExcludeContentPatterns = ["Dr*p"] },
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };

        var result = await UseCase(new FileDiscovery(_logger))
            .ExecuteAsync(_root, config, new RunOptions { OutputFile = output, Excludes = ["out.md"] });

        Assert.True(result.IsSuccess, result.Error);
        var text = File.ReadAllText(output);
        Assert.Contains("class Keep", text);
        Assert.DoesNotContain("class Drop", text);
    }

    // ── discovery.useGit ────────────────────────────────────────────────────────

    [Fact]
    public async Task UseGitFalse_WalksTheFilesystem()
    {
        if (!RunGit("init", "-q")) return; // git not available: nothing to prove
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "ignored.cs\n");
        File.WriteAllText(Path.Combine(_root, "tracked.cs"), "x");
        File.WriteAllText(Path.Combine(_root, "ignored.cs"), "y");
        var discovery = new FileDiscovery(_logger);

        var withGit = await discovery.DiscoverFilesAsync(_root, Config() with
        {
            Discovery = new DiscoveryConfiguration { UseGit = true }
        });
        var withoutGit = await discovery.DiscoverFilesAsync(_root, Config() with
        {
            Discovery = new DiscoveryConfiguration { UseGit = false }
        });

        Assert.DoesNotContain(withGit.Value!, f => f.EndsWith("ignored.cs"));
        Assert.Contains(withoutGit.Value!, f => f.EndsWith("ignored.cs"));
    }

    private bool RunGit(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var process = Process.Start(psi)!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ── filters.additionalExtensions ────────────────────────────────────────────

    [Fact]
    public void AdditionalExtensions_AreAllowedAlongsideTheExtensionFilter()
    {
        var config = Config() with { Filters = Config().Filters with { AdditionalExtensions = [".razor"] } };
        var files = new[] { "a.cs", "b.razor", "c.md" };

        var kept = new FileFilter(_logger).FilterFiles(files, config, [], [], ["cs"]).Value!.Select(e => e.Relative);
        var unfiltered = new FileFilter(_logger).FilterFiles(files, config, [], [], []).Value!.Select(e => e.Relative);

        Assert.Equal(new[] { "a.cs", "b.razor" }, kept.OrderBy(x => x).ToArray());
        Assert.Equal(3, unfiltered.Count());
    }

    // ── markdown.languageDetection ──────────────────────────────────────────────

    [Fact]
    public void LanguageDetection_Filename_PrefersKnownFileNames()
    {
        var config = Config() with { Markdown = Config().Markdown with { LanguageDetection = "filename" } };
        config = config with
        {
            LanguageMappings = new Dictionary<string, string>(config.LanguageMappings) { ["build.cs"] = "special" }
        };

        var entry = new FileFilter(_logger).FilterFiles(["build.cs"], config, [], [], []).Value!.Single();
        var normal = new FileFilter(_logger).FilterFiles(["build.cs"], Config(), [], [], []).Value!.Single();

        Assert.Equal("special", entry.Language);
        Assert.NotEqual("special", normal.Language);
    }

    [Fact]
    public void LanguageDetection_Content_UsesTheShebangOfExtensionlessFiles()
    {
        File.WriteAllText(Path.Combine(_root, "runme"), "#!/usr/bin/env python3\nprint(1)\n");
        var config = Config() with { Markdown = Config().Markdown with { LanguageDetection = "content" } };

        var detected = new FileFilter(_logger).FilterFiles(["runme"], config, [], [], [], rootPath: _root).Value!.Single();
        var byExtension = new FileFilter(_logger).FilterFiles(["runme"], Config(), [], [], [], rootPath: _root).Value!.Single();

        Assert.Equal("python", detected.Language);
        Assert.NotEqual("python", byExtension.Language);
    }

    // ── output.includeStats ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task IncludeStats_ControlsTheSummaryFigures(bool includeStats, bool expectFigures)
    {
        File.WriteAllText(Path.Combine(_root, "a.cs"), "class A {}");
        var output = Path.Combine(_root, "out.md");
        var config = Config() with
        {
            Output = Config().Output with { IncludeStats = includeStats },
            Discovery = new DiscoveryConfiguration { Mode = "filesystem" }
        };

        var result = await UseCase(new FileDiscovery(_logger))
            .ExecuteAsync(_root, config, new RunOptions { OutputFile = output, Excludes = ["out.md"] });

        Assert.True(result.IsSuccess, result.Error);
        var summary = _logger.Messages.Single(m => m.Message.Contains("Exported to")).Message;
        Assert.Equal(expectFigures, summary.Contains("Tokens:"));
    }

    // ── cluster.includeRootFiles ────────────────────────────────────────────────

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task IncludeRootFiles_AddsLooseFilesOfTheClusterFolder(bool include, bool expected)
    {
        var repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repo);
        File.WriteAllText(Path.Combine(repo, "in-repo.cs"), "class InRepo {}");
        File.WriteAllText(Path.Combine(_root, "loose.cs"), "class Loose {}");
        var discovery = new FixedDiscovery();
        discovery.Repos.Add(new RepoInfo { RootPath = repo, RelativePath = "repo", Name = "repo", IsValid = true });
        discovery.Files[repo] = ["in-repo.cs"];
        var output = Path.Combine(_root, "out.md");
        var config = Config() with
        {
            Discovery = new DiscoveryConfiguration
            {
                Cluster = new ClusterConfiguration { IncludeRootFiles = include, MaxDepth = 2 }
            }
        };

        var result = await UseCase(discovery).ExecuteClusterAsync(_root, config,
            new RunOptions { OutputFile = output, Extensions = ["cs"] });

        Assert.True(result.IsSuccess, result.Error);
        var text = File.ReadAllText(output);
        Assert.Contains("class InRepo", text);
        Assert.Equal(expected, text.Contains("class Loose"));
    }
}
