using System;
using System.IO;
using System.Threading.Tasks;
using gc.CLI.Services;
using gc.Infrastructure.Configuration;
using Xunit;

namespace gc.Tests;

public class CliArgumentResolverTests
{
    private static string CreateTempDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gc-test-resolver-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    [Fact]
    public void SplitArguments_HandlesQuotesAndSpaces()
    {
        var cmd = "--extension cs -x \"bin obj\" -o 'out file.md' --verbose";
        var args = CliArgumentResolver.SplitArguments(cmd);

        Assert.Equal(7, args.Length);
        Assert.Equal("--extension", args[0]);
        Assert.Equal("cs", args[1]);
        Assert.Equal("-x", args[2]);
        Assert.Equal("bin obj", args[3]);
        Assert.Equal("-o", args[4]);
        Assert.Equal("out file.md", args[5]);
        Assert.Equal("--verbose", args[6]);
    }

    [Fact]
    public async Task ResolveAsync_InitCommand_CreatesProjectSettingsFile()
    {
        var tempDir = CreateTempDir();
        try
        {
            var configDir = Path.Combine(tempDir, "config");
            var originalArgs = new[] { "init", "--extension", "cs", "-x", "bin" };

            var (resolvedArgs, shouldExit, exitCode) = await CliArgumentResolver.ResolveAsync(originalArgs, configDir, tempDir);

            Assert.True(shouldExit);
            Assert.Equal(0, exitCode);
            Assert.Empty(resolvedArgs);

            var file = await SettingsStore.LoadAsync(Path.Combine(tempDir, SettingsStore.ProjectFileName));
            Assert.Equal(new[] { "--extension", "cs", "-x", "bin" }, file.Defaults);
            Assert.False(File.Exists(Path.Combine(tempDir, ".gc")), "the old .gc arguments file is no longer written");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ResolveAsync_LoadsProjectDefaultsFromHierarchy()
    {
        var tempDir = CreateTempDir();
        var subDir = Path.Combine(tempDir, "sub", "deep");
        Directory.CreateDirectory(subDir);
        try
        {
            var configDir = Path.Combine(tempDir, "config");
            await File.WriteAllTextAsync(Path.Combine(tempDir, SettingsStore.ProjectFileName),
                "{ \"defaults\": [\"--verbose\", \"--extension\", \"cs\"] }");

            var (resolvedArgs, shouldExit, _) =
                await CliArgumentResolver.ResolveAsync(new[] { "--output", "out.md" }, configDir, subDir);

            Assert.False(shouldExit);
            Assert.Equal(new[] { "--verbose", "--extension", "cs", "--output", "out.md" }, resolvedArgs);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ResolveAsync_UserDefaultsComeBeforeProjectDefaults()
    {
        var tempDir = CreateTempDir();
        try
        {
            var configDir = Path.Combine(tempDir, "config");
            Directory.CreateDirectory(configDir);
            await File.WriteAllTextAsync(SettingsStore.UserPath(configDir), "{ \"defaults\": [\"-v\"] }");
            await File.WriteAllTextAsync(Path.Combine(tempDir, SettingsStore.ProjectFileName),
                "{ \"defaults\": [\"-e\", \"cs\"] }");

            var (resolvedArgs, _, _) = await CliArgumentResolver.ResolveAsync(new[] { "-o", "x.md" }, configDir, tempDir);

            Assert.Equal(new[] { "-v", "-e", "cs", "-o", "x.md" }, resolvedArgs);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ResolveAsync_OldGcArgumentsFile_IsIgnored()
    {
        var tempDir = CreateTempDir();
        try
        {
            var configDir = Path.Combine(tempDir, "config");
            await File.WriteAllTextAsync(Path.Combine(tempDir, ".gc"), "--verbose --extension cs");

            var (resolvedArgs, _, _) = await CliArgumentResolver.ResolveAsync(new[] { "--output", "out.md" }, configDir, tempDir);

            Assert.Equal(new[] { "--output", "out.md" }, resolvedArgs);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData("--profile")]
    [InlineData("@")]
    public async Task ResolveAsync_Profiles_SaveAndLoad(string style)
    {
        var tempDir = CreateTempDir();
        try
        {
            var configDir = Path.Combine(tempDir, "config");

            var saveArgs = new[] { "--save-profile", "myprofile", "--extension", "py", "--verbose" };
            var (_, shouldExit1, exitCode1) = await CliArgumentResolver.ResolveAsync(saveArgs, configDir, tempDir);
            Assert.True(shouldExit1);
            Assert.Equal(0, exitCode1);
            Assert.True(File.Exists(SettingsStore.UserPath(configDir)), "profiles are saved in the user settings file");

            var loadArgs = style == "@"
                ? new[] { "@myprofile", "-o", "out.md" }
                : new[] { "--profile", "myprofile", "-o", "out.md" };
            var (resolvedArgs, shouldExit2, _) = await CliArgumentResolver.ResolveAsync(loadArgs, configDir, tempDir);
            Assert.False(shouldExit2);
            Assert.Equal(new[] { "--extension", "py", "--verbose", "-o", "out.md" }, resolvedArgs);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ResolveAsync_ProjectProfileWinsOverUserProfile()
    {
        var tempDir = CreateTempDir();
        try
        {
            var configDir = Path.Combine(tempDir, "config");
            Directory.CreateDirectory(configDir);
            await File.WriteAllTextAsync(SettingsStore.UserPath(configDir), "{ \"profiles\": { \"docs\": [\"-e\", \"txt\"] } }");
            await File.WriteAllTextAsync(Path.Combine(tempDir, SettingsStore.ProjectFileName),
                "{ \"profiles\": { \"docs\": [\"-e\", \"md\"] } }");

            var (resolvedArgs, _, _) = await CliArgumentResolver.ResolveAsync(new[] { "@docs" }, configDir, tempDir);

            Assert.Equal(new[] { "-e", "md" }, resolvedArgs);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ResolveAsync_BuiltInPresetIsAProfile()
    {
        var tempDir = CreateTempDir();
        try
        {
            var configDir = Path.Combine(tempDir, "config");

            var (web, _, _) = await CliArgumentResolver.ResolveAsync(new[] { "@web" }, configDir, tempDir);
            var (unity, _, _) = await CliArgumentResolver.ResolveAsync(new[] { "@unity-source" }, configDir, tempDir);

            Assert.Equal(new[] { "--preset", "web" }, web);
            Assert.Equal(new[] { "-u" }, unity);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ResolveAsync_UnknownProfile_Exits()
    {
        var tempDir = CreateTempDir();
        try
        {
            var configDir = Path.Combine(tempDir, "config");

            var (_, shouldExit, exitCode) = await CliArgumentResolver.ResolveAsync(new[] { "@nope" }, configDir, tempDir);

            Assert.True(shouldExit);
            Assert.Equal(1, exitCode);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ResolveAsync_DirectoryDefaults_SaveAndLoad()
    {
        var tempDir = CreateTempDir();
        try
        {
            var configDir = Path.Combine(tempDir, "config");

            var (resolvedArgs1, shouldExit1, _) =
                await CliArgumentResolver.ResolveAsync(new[] { "--extension", "rs", "--save" }, configDir, tempDir);
            Assert.False(shouldExit1);
            Assert.Equal(new[] { "--extension", "rs" }, resolvedArgs1); // --save is stripped, value kept

            var saved = await SettingsStore.LoadAsync(Path.Combine(tempDir, SettingsStore.ProjectFileName));
            Assert.Equal(new[] { "--extension", "rs" }, saved.Defaults);

            var (resolvedArgs2, shouldExit2, _) =
                await CliArgumentResolver.ResolveAsync(Array.Empty<string>(), configDir, tempDir);
            Assert.False(shouldExit2);
            Assert.Equal(new[] { "--extension", "rs" }, resolvedArgs2);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
