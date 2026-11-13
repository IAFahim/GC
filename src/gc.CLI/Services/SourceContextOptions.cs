using gc.CLI.Models;
using gc.Domain.Common;
using gc.Domain.Constants;
using gc.Domain.Models.Configuration;

namespace gc.CLI.Services;

/// <summary>Applies source-oriented presets while respecting explicit paths and extension overrides.</summary>
public static class SourceContextOptions
{
    private static readonly string[] SourceExcludes =
    [
        "Tests", "tests", "test", "Tests~", "**/*.Tests/**", "EditorTests", "PlayModeTests",
        "Samples~", "Samples", "samples", "Benchmarks", "benchmarks", "bin", "obj",
        "Library", "Temp", "Logs", "UserSettings", "worktrees", ".local"
    ];

    /// <summary>Expands selected presets and enables registered submodules for the Unity source preset.</summary>
    public static Result Apply(CliArguments arguments, GcConfiguration config, string root)
    {
        var extensions = new List<string>();
        var builtins = BuiltInPresets.GetAllPresets();
        foreach (var name in arguments.Presets)
        {
            if (!config.Presets.TryGetValue(name, out var preset) && !builtins.TryGetValue(name, out preset))
                return Result.Failure("Unknown preset: " + name + ". Use -u for Unity source.");
            extensions.AddRange(preset.Extensions);
        }
        if (arguments.Extensions.Length == 0 && extensions.Count > 0)
            arguments.Extensions = extensions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!arguments.Presets.Contains("unity-source", StringComparer.OrdinalIgnoreCase)) return Result.Success();
        arguments.RecurseSubmodules = true;
        arguments.Excludes = arguments.Excludes.Concat(SourceExcludes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (arguments.Paths.Length == 0 && !AlreadyInsideSource(root))
            arguments.Paths = ["src", "Runtime", "Editor"];
        return Result.Success();
    }

    private static bool AlreadyInsideSource(string root)
    {
        for (var directory = new DirectoryInfo(root); directory != null; directory = directory.Parent)
        {
            if (directory.Name.Equals("src", StringComparison.OrdinalIgnoreCase) ||
                directory.Name.Equals("Runtime", StringComparison.OrdinalIgnoreCase) ||
                directory.Name.Equals("Editor", StringComparison.OrdinalIgnoreCase)) return true;
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git"))) break;
        }
        return false;
    }
}
