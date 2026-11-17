using gc.Application.Services;
using gc.CLI.Models;
using gc.CLI.Services;
using gc.Domain.Constants;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Logging;

namespace gc.Tests;

/// <summary>The small documented flag surface from issue #14, plus the old spellings that still work.</summary>
public class CliSurfaceTests
{
    private static CliArguments Parse(params string[] args)
    {
        var result = new CliParser().Parse(args, new GcConfiguration());
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [Fact]
    public void ShortFlags_CoverTheWholeSurface()
    {
        var a = Parse("-e", "cs", "-x", "bin", "-z", "//", "-o", "out.md", "-k", "-a", "-b", "-l");

        Assert.Equal(new[] { "cs" }, a.Extensions);
        Assert.Equal(new[] { "bin" }, a.Excludes);
        Assert.Equal(new[] { "//" }, a.ExcludeLineIfStart);
        Assert.Equal("out.md", a.OutputFile);
        Assert.True(a.SplitBytes > 0);
        Assert.True(a.Append);
        Assert.True(a.BrainMode);
        Assert.True(a.List);
    }

    [Fact]
    public void LongFlags_MatchTheShortOnes()
    {
        var a = Parse("--ext", "cs", "--exclude", "bin", "--drop-lines", "//", "--output", "o.md", "--list", "--no-git");

        Assert.Equal(new[] { "cs" }, a.Extensions);
        Assert.Equal(new[] { "bin" }, a.Excludes);
        Assert.Equal(new[] { "//" }, a.ExcludeLineIfStart);
        Assert.Equal("o.md", a.OutputFile);
        Assert.True(a.List);
        Assert.True(a.NoGit);
    }

    [Theory]
    [InlineData("-p")]
    [InlineData("--paths")]
    [InlineData("--include-path")]
    public void OldIncludeSpellings_BecomePlainPaths(string flag)
    {
        var a = Parse(flag, "src", "lib");

        Assert.Equal(new[] { "src", "lib" }, a.Paths);
        Assert.Empty(a.IncludePathPatterns);
    }

    [Fact]
    public void ExcludePath_BecomesExclude()
    {
        var a = Parse("--exclude-path", "**/obj/**");

        Assert.Equal(new[] { "**/obj/**" }, a.Excludes);
        Assert.Empty(a.ExcludePathPatterns);
    }

    [Theory]
    [InlineData("-s")]
    public void OldOutputAndForceSpellings_StillWork(string flag)
    {
        Assert.Equal("x.md", Parse(flag, "x.md").OutputFile);
        Assert.True(Parse("-f").NoGit);
        Assert.True(Parse("--force").NoGit);
        Assert.True(Parse("--dry-run").List);
    }

    [Fact]
    public void Stats_WithoutFile_OnlyShowsTimings()
    {
        var a = Parse("--stats");

        Assert.True(a.ShowStats);
        Assert.Null(a.StatsOutput);
    }

    [Fact]
    public void Stats_WithJsonFile_WritesIt()
    {
        var a = Parse("--stats", "timings.json");

        Assert.True(a.ShowStats);
        Assert.Equal("timings.json", a.StatsOutput);
    }

    [Fact]
    public void Stats_DoesNotSwallowAFollowingPath()
    {
        var a = Parse("--stats", "src");

        Assert.True(a.ShowStats);
        Assert.Null(a.StatsOutput);
        Assert.Equal(new[] { "src" }, a.Paths);
    }

    [Theory]
    [InlineData("--json-stats", "a.json")]
    [InlineData("--profile-json", "a.json")]
    public void OldStatsSpellings_FoldIntoStats(string flag, string file)
    {
        var a = Parse(flag, file);

        Assert.True(a.ShowStats);
        Assert.Equal(file, a.StatsOutput);
        Assert.True(Parse("--profile-timing").ShowStats);
    }

    [Theory]
    [InlineData("--no-append")]
    [InlineData("--unsafe-direct-write")]
    public void RemovedWriteFlags_AreAcceptedAsNoOps(string flag)
    {
        var a = Parse(flag);

        Assert.False(a.Append);
    }

    [Theory]
    [InlineData("--test")]
    [InlineData("--benchmark")]
    public void RemovedDeveloperFlags_AreRejected(string flag)
    {
        Assert.False(new CliParser().Parse([flag], new GcConfiguration()).IsSuccess);
    }

    [Fact]
    public void GlobInPlainPaths_SelectsByGlob()
    {
        var filter = new FileFilter(new ConsoleLogger());
        var files = new[] { "src/a.cs", "src/sub/b.cs", "docs/readme.md", "src/c.txt" };

        var result = filter.FilterFiles(files, BuiltInPresets.GetDefaultConfiguration(), ["**/*.md", "src/*.cs"], [], []);

        Assert.True(result.IsSuccess);
        var kept = result.Value!.Select(e => e.Relative).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "docs/readme.md", "src/a.cs" }, kept);
    }

    [Fact]
    public void LiteralPaths_StillSelectByFolder()
    {
        var filter = new FileFilter(new ConsoleLogger());
        var files = new[] { "src/a.cs", "src/sub/b.cs", "docs/readme.md" };

        var result = filter.FilterFiles(files, BuiltInPresets.GetDefaultConfiguration(), ["src"], [], []);

        Assert.Equal(new[] { "src/a.cs", "src/sub/b.cs" }, result.Value!.Select(e => e.Relative).OrderBy(x => x).ToArray());
    }
}
