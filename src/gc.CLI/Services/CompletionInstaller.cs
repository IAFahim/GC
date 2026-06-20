using System.Reflection;
using gc.Domain.Interfaces;

namespace gc.CLI.Services;

// ========================================================================
// Shell completion installer.
//
// The bash/zsh/fish completion scripts are embedded in the binary (see
// gc.CLI.csproj), so `gc --install-completion` works from a standalone
// download with no companion files. Each shell is written to the location
// its completion loader scans, and zsh gets an idempotent ~/.zshrc nudge
// because zsh only auto-loads from directories already on $fpath.
// ========================================================================

public static class CompletionInstaller
{
    private static readonly string[] SupportedShells = ["bash", "zsh", "fish"];

    /// <summary>
    ///     Installs the completion for <paramref name="shell" />, or auto-detects from $SHELL when null.
    ///     Returns a process exit code.
    /// </summary>
    public static int Install(ILogger logger, string? shell)
    {
        shell = Normalize(shell) ?? DetectShell();
        if (shell == null)
        {
            logger.Error(
                "Could not detect your shell. Re-run with an explicit shell, e.g. `gc --install-completion bash` (bash|zsh|fish).");
            return 1;
        }

        var script = LoadScript(shell);
        if (script == null)
        {
            logger.Error($"No embedded completion script for shell '{shell}'.");
            return 1;
        }

        try
        {
            var (target, postInstallNote) = shell switch
            {
                "bash" => InstallBash(script),
                "zsh" => InstallZsh(script),
                "fish" => InstallFish(script),
                _ => (null, null)
            };

            if (target == null)
            {
                logger.Error($"Unsupported shell '{shell}'. Supported: {string.Join(", ", SupportedShells)}.");
                return 1;
            }

            logger.Success($"Installed {shell} completion to {target}");
            if (postInstallNote != null) logger.Info(postInstallNote);
            return 0;
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to install {shell} completion: {ex.Message}", ex);
            return 1;
        }
    }

    /// <summary>
    ///     Prints the raw completion script for <paramref name="shell" /> to stdout, for
    ///     `source &lt;(gc --print-completion bash)` or manual placement. Returns an exit code.
    /// </summary>
    public static int Print(ILogger logger, string? shell)
    {
        var normalized = Normalize(shell);
        if (normalized == null)
        {
            logger.Error($"Specify a shell to print: {string.Join(", ", SupportedShells)}.");
            return 1;
        }

        var script = LoadScript(normalized);
        if (script == null)
        {
            logger.Error($"No embedded completion script for shell '{normalized}'.");
            return 1;
        }

        Console.Out.Write(script);
        return 0;
    }

    // ── Per-shell install ──

    private static (string target, string? note) InstallBash(string script)
    {
        // bash-completion's dynamic loader sources a file named after the command from
        // the per-user completions dir on first <Tab>. No rc edit needed when the
        // bash-completion package is present (it is, on virtually every modern distro).
        var dir = Path.Combine(XdgDataHome(), "bash-completion", "completions");
        var target = Path.Combine(dir, "gc");
        WriteFile(target, script);
        return (target,
            "Open a new shell to activate. If completion does not work, ensure the 'bash-completion' " +
            $"package is installed, or add `source {target}` to your ~/.bashrc.");
    }

    private static (string target, string? note) InstallZsh(string script)
    {
        // zsh only autoloads _gc from a directory already on $fpath, so we own a known
        // dir and idempotently wire it into ~/.zshrc.
        var dir = Path.Combine(Home(), ".zsh", "completions");
        var target = Path.Combine(dir, "_gc");
        WriteFile(target, script);

        var zshrc = Path.Combine(Home(), ".zshrc");
        var added = EnsureZshrcFpath(zshrc, dir);
        var note = added
            ? $"Added gc's completion dir to ~/.zshrc. Open a new shell (or run `exec zsh`) to activate."
            : "Open a new shell (or run `exec zsh`) to activate.";
        return (target, note);
    }

    private static (string target, string? note) InstallFish(string script)
    {
        // fish autoloads any *.fish in its per-user completions dir. Zero config.
        var dir = Path.Combine(XdgConfigHome(), "fish", "completions");
        var target = Path.Combine(dir, "gc.fish");
        WriteFile(target, script);
        return (target, "Open a new shell to activate (fish loads this automatically).");
    }

    /// <summary>Idempotently appends an fpath + compinit block to ~/.zshrc. Returns true if it edited the file.</summary>
    private static bool EnsureZshrcFpath(string zshrc, string completionDir)
    {
        const string marker = "# gc completion";
        if (File.Exists(zshrc) && File.ReadAllText(zshrc).Contains(marker)) return false;

        var block = $"""

                     {marker}
                     fpath=("{completionDir}" $fpath)
                     autoload -Uz compinit && compinit

                     """;
        File.AppendAllText(zshrc, block);
        return true;
    }

    // ── Helpers ──

    private static void WriteFile(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static string? LoadScript(string shell)
    {
        var resourceName = $"gc.completion.{shell}";
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? Normalize(string? shell)
    {
        if (string.IsNullOrWhiteSpace(shell)) return null;
        var s = shell.Trim().ToLowerInvariant();
        return Array.IndexOf(SupportedShells, s) >= 0 ? s : null;
    }

    private static string? DetectShell()
    {
        var shellPath = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrEmpty(shellPath)) return null;
        var name = Path.GetFileName(shellPath).ToLowerInvariant();
        if (name.Contains("zsh")) return "zsh";
        if (name.Contains("fish")) return "fish";
        if (name.Contains("bash")) return "bash";
        return null;
    }

    private static string Home() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string XdgDataHome()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return !string.IsNullOrEmpty(xdg) ? xdg : Path.Combine(Home(), ".local", "share");
    }

    private static string XdgConfigHome()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return !string.IsNullOrEmpty(xdg) ? xdg : Path.Combine(Home(), ".config");
    }
}
