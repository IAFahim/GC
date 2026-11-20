using System.Diagnostics;
using gc.Application.Services;
using gc.Application.UseCases;
using gc.Domain.Common;
using gc.Domain.Constants;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Discovery;
using gc.Infrastructure.Logging;

namespace gc.Tests;

public sealed class SubmoduleDiscoveryTests
{
    private sealed class CaptureLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = [];
        public void Log(LogLevel level, string message, Exception? ex = null) => Messages.Add((level, message));
    }

    private sealed class NoClipboard : IClipboardService
    {
        public Task<Result> CopyToClipboardAsync(Stream stream, CancellationToken ct) => Task.FromResult(Result.Success());
        public Task<Result> CopyToClipboardAsync(Stream stream, LimitsConfiguration limits, bool append, CancellationToken ct) => Task.FromResult(Result.Success());
        public Task<Result> CopyToClipboardAsync(string content, CancellationToken ct) => Task.FromResult(Result.Success());
        public Task<Result> CopyToClipboardAsync(string content, LimitsConfiguration limits, bool append, CancellationToken ct) => Task.FromResult(Result.Success());
    }
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
            StandardOutputEncoding = System.Text.Encoding.UTF8,
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

    [Fact]
    public async Task ThreeInitializedSubmodules_ExportOnceAndExcludeSymlinkAliasAndIgnoredWorktree()
    {
        var root = Directory.CreateTempSubdirectory("gc-3subs-").FullName;
        try
        {
            await Git(root, "init", "--quiet");
            var names = new[] { "Packages/alpha", "Packages/beta", "Packages/gamma" };
            var gitmodules = new System.Text.StringBuilder();
            string? alphaDir = null;

            foreach (var rel in names)
            {
                var child = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.Combine(child, "Runtime"));
                alphaDir ??= child;
                await Git(child, "init", "--quiet");
                var marker = Path.GetFileName(rel);
                File.WriteAllText(Path.Combine(child, "Runtime", $"{marker}.cs"), $"class {marker}Class {{}}");
                await Git(child, "add", ".");
                await Git(child, "-c", "user.name=GC Test", "-c", "user.email=test@example.invalid",
                    "commit", "--quiet", "-m", "init");
                var head = (await Git(child, "rev-parse", "HEAD")).Trim();
                await Git(root, "update-index", "--add", "--cacheinfo", $"160000,{head},{rel}");
                gitmodules.AppendLine($"[submodule \"{rel}\"]").AppendLine($"\tpath = {rel}");
            }

            // Symlink alias pointing to alpha registered in .gitmodules (must be deduplicated/excluded)
            var aliasPath = Path.Combine(root, "Packages", "alpha-alias");
            var symlinkCreated = false;
            try
            {
                Directory.CreateSymbolicLink(aliasPath, alphaDir!);
                symlinkCreated = true;
                var alphaHead = (await Git(alphaDir!, "rev-parse", "HEAD")).Trim();
                await Git(root, "update-index", "--add", "--cacheinfo", $"160000,{alphaHead},Packages/alpha-alias");
                gitmodules.AppendLine("[submodule \"alpha-alias\"]").AppendLine("\tpath = Packages/alpha-alias");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unprivileged Windows without Developer Mode cannot create symlinks; gitlink tests still apply.
            }

            // Ignored nested worktree listed in .gitmodules without gitlink
            var stale = Path.Combine(root, "ignored", "wt");
            await Git(alphaDir!, "worktree", "add", "--quiet", "--detach", stale, "HEAD");
            File.WriteAllText(Path.Combine(stale, "Runtime", "IgnoredWt.cs"), "class IgnoredWt {}");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "/ignored/\n");
            File.WriteAllText(Path.Combine(root, ".gitmodules"), gitmodules.ToString());

            var config = BuiltInPresets.GetDefaultConfiguration() with
            {
                Discovery = new DiscoveryConfiguration { Mode = "git", RecurseSubmodules = true }
            };
            var result = await new FileDiscovery(new ConsoleLogger()).DiscoverFilesAsync(root, config, default);
            Assert.True(result.IsSuccess, result.Error);
            var files = result.Value!.ToArray();

            Assert.Single(files, f => f == "Packages/alpha/Runtime/alpha.cs");
            Assert.Single(files, f => f == "Packages/beta/Runtime/beta.cs");
            Assert.Single(files, f => f == "Packages/gamma/Runtime/gamma.cs");
            if (symlinkCreated)
                Assert.DoesNotContain(files, f => f.StartsWith("Packages/alpha-alias/"));
            Assert.DoesNotContain(files, f => f.Contains("IgnoredWt"));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task NoMatchingFiles_WithSubmodulesDisabled_EmitsClearHint()
    {
        var root = Directory.CreateTempSubdirectory("gc-subhint-").FullName;
        try
        {
            await Git(root, "init", "--quiet");
            var child = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
            await Git(child, "init", "--quiet");
            Directory.CreateDirectory(Path.Combine(child, "src"));
            File.WriteAllText(Path.Combine(child, "src", "OnlyInSub.cs"), "class OnlyInSub {}");
            await Git(child, "add", ".");
            await Git(child, "-c", "user.name=GC Test", "-c", "user.email=test@example.invalid",
                "commit", "--quiet", "-m", "init");
            var head = (await Git(child, "rev-parse", "HEAD")).Trim();
            await Git(root, "update-index", "--add", "--cacheinfo", $"160000,{head},sub");
            File.WriteAllText(Path.Combine(root, ".gitmodules"), "[submodule \"sub\"]\n\tpath = sub\n");
            await Git(root, "add", ".gitmodules");
            await Git(root, "-c", "user.name=GC Test", "-c", "user.email=test@example.invalid",
                "commit", "--quiet", "-m", "add sub");

            var logger = new CaptureLogger();
            var useCase = new GenerateContextUseCase(new FileDiscovery(logger), new FileFilter(logger),
                new ContentFilter(logger), new MarkdownGenerator(logger), new NoClipboard(), logger);

            var withoutRecurse = await useCase.ExecuteAsync(root, BuiltInPresets.GetDefaultConfiguration(),
                new RunOptions { Extensions = ["cs"] });
            Assert.True(withoutRecurse.IsSuccess);
            Assert.Contains(logger.Messages, m => m.Level == LogLevel.Warning && m.Message.Contains("--submodules"));

            logger.Messages.Clear();
            var withRecurse = await useCase.ExecuteAsync(root, BuiltInPresets.GetDefaultConfiguration() with
            {
                Discovery = new DiscoveryConfiguration { Mode = "git", RecurseSubmodules = true }
            }, new RunOptions { Extensions = ["cs"], OutputFile = Path.Combine(root, "out.md") });
            Assert.True(withRecurse.IsSuccess);
            Assert.DoesNotContain(logger.Messages, m => m.Message.Contains("--submodules"));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
