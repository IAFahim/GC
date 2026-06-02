using System;
using System.IO;
using System.Threading.Tasks;
using gc.CLI.Services;
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
    public async Task ResolveAsync_InitCommand_CreatesLocalGcFile()
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

            var gcFilePath = Path.Combine(tempDir, ".gc");
            Assert.True(File.Exists(gcFilePath));

            var content = await File.ReadAllTextAsync(gcFilePath);
            Assert.Equal("--extension cs -x bin", content);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ResolveAsync_LoadsLocalGcFileFromHierarchy()
    {
        var tempDir = CreateTempDir();
        var subDir = Path.Combine(tempDir, "sub", "deep");
        Directory.CreateDirectory(subDir);
        try
        {
            var configDir = Path.Combine(tempDir, "config");
            var gcFilePath = Path.Combine(tempDir, ".gc");
            await File.WriteAllTextAsync(gcFilePath, "--verbose --extension cs");

            var originalArgs = new[] { "--output", "out.md" };

            // Run resolution from the subDir; it should crawl up to find .gc in tempDir
            var (resolvedArgs, shouldExit, exitCode) = await CliArgumentResolver.ResolveAsync(originalArgs, configDir, subDir);

            Assert.False(shouldExit);
            Assert.Equal(3, resolvedArgs.Length);
            Assert.Equal("--verbose", resolvedArgs[0]);
            Assert.Equal("--extension", resolvedArgs[1]);
            Assert.Equal("cs", resolvedArgs[2]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ResolveAsync_Profiles_SaveAndLoad()
    {
        var tempDir = CreateTempDir();
        try
        {
            var configDir = Path.Combine(tempDir, "config");

            // 1. Save profile
            var saveArgs = new[] { "--save-profile", "myprofile", "--extension", "py", "--verbose" };
            var (resolvedArgs1, shouldExit1, exitCode1) = await CliArgumentResolver.ResolveAsync(saveArgs, configDir, tempDir);
            Assert.True(shouldExit1);
            Assert.Equal(0, exitCode1);

            // 2. Load profile
            var loadArgs = new[] { "--profile", "myprofile", "-o", "out.md" };
            var (resolvedArgs2, shouldExit2, exitCode2) = await CliArgumentResolver.ResolveAsync(loadArgs, configDir, tempDir);
            Assert.False(shouldExit2);
            Assert.Equal(5, resolvedArgs2.Length);
            Assert.Equal("--extension", resolvedArgs2[0]);
            Assert.Equal("py", resolvedArgs2[1]);
            Assert.Equal("--verbose", resolvedArgs2[2]);
            Assert.Equal("-o", resolvedArgs2[3]);
            Assert.Equal("out.md", resolvedArgs2[4]);
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

            // 1. Save directory default using -s
            var saveArgs = new[] { "--extension", "rs", "-s" };
            var (resolvedArgs1, shouldExit1, exitCode1) = await CliArgumentResolver.ResolveAsync(saveArgs, configDir, tempDir);
            Assert.False(shouldExit1);
            Assert.Single(resolvedArgs1);
            Assert.Equal("--extension", resolvedArgs1[0]); // -s flag is stripped

            // 2. Run gc in same directory with no arguments - should load default
            var (resolvedArgs2, shouldExit2, exitCode2) = await CliArgumentResolver.ResolveAsync(Array.Empty<string>(), configDir, tempDir);
            Assert.False(shouldExit2);
            Assert.Equal(2, resolvedArgs2.Length);
            Assert.Equal("--extension", resolvedArgs2[0]);
            Assert.Equal("rs", resolvedArgs2[1]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
