using System.Diagnostics;
using gc.Domain.Constants;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Discovery;
using gc.Infrastructure.Logging;

namespace gc.Tests;

public sealed class SubmoduleDiscoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyIndexedGitlinksAreFollowedEvenWhenGitmodulesNamesAnIgnoredCheckout(bool worktree)
    {
        var root = Directory.CreateTempSubdirectory("gc-gitlinks-").FullName;
        try
        {
            await Git(root, "init", "--quiet");
            var child = Directory.CreateDirectory(Path.Combine(root, "dependency")).FullName;
            await Git(child, "init", "--quiet");
            Directory.CreateDirectory(Path.Combine(child, "src"));
            File.WriteAllText(Path.Combine(child, "src", "Child.cs"), "class Child {}");
            File.WriteAllText(Path.Combine(child, ".gitignore"), "src/Generated.cs\n");
            await Git(child, "add", ".");
            await Git(child, "-c", "user.name=GC Test", "-c", "user.email=test@example.invalid",
                "commit", "--quiet", "-m", "fixture");
            var head = (await Git(child, "rev-parse", "HEAD")).Trim();
            File.WriteAllText(Path.Combine(child, "src", "Generated.cs"), "class IgnoredChild {}");
            await Git(root, "update-index", "--add", "--cacheinfo", $"160000,{head},dependency");

            var stale = Path.Combine(root, "ignored", "stale");
            if (worktree)
                await Git(child, "worktree", "add", "--quiet", "--detach", stale, "HEAD");
            else
            {
                Directory.CreateDirectory(stale);
                await Git(stale, "init", "--quiet");
            }
            Directory.CreateDirectory(Path.Combine(stale, "src"));
            File.WriteAllText(Path.Combine(stale, "src", "Secret.cs"), "class MustNotExport {}");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "/ignored/\n");
            File.WriteAllText(Path.Combine(root, ".gitmodules"),
                "[submodule \"dependency\"]\npath = dependency\n[submodule \"stale\"]\npath = ignored/stale\n");
            var config = BuiltInPresets.GetDefaultConfiguration() with
            {
                Discovery = new DiscoveryConfiguration { Mode = "git", RecurseSubmodules = true }
            };
            var result = await new FileDiscovery(new ConsoleLogger()).DiscoverFilesAsync(root, config, default);
            Assert.True(result.IsSuccess, result.Error);
            var files = result.Value!.ToArray();
            Assert.Contains("dependency/src/Child.cs", files);
            Assert.DoesNotContain(files, path => path.Contains("Secret") || path.Contains("Generated") || path.StartsWith("ignored/"));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    private static async Task<string> Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
}
