using gc.CLI.Models;
using gc.Domain.Constants;

namespace gc.CLI.Services;

/// <summary>Resolves direct folder/file arguments without changing the caller's working directory.</summary>
public static class InvocationRootResolver
{
    /// <summary>Finds the target before loading its project configuration and saved defaults.</summary>
    public static string FindRoot(string[] arguments, string currentDirectory)
    {
        if (arguments.Length > 0 && arguments[0] is "init" or "--save-profile" or "--list-profiles")
            return currentDirectory;
        var filtered = new List<string>();
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i].Equals("--save", StringComparison.OrdinalIgnoreCase)) continue;
            if (arguments[i].Equals("--profile", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }
            filtered.Add(arguments[i]);
        }
        var parsed = new CliParser().Parse(filtered.ToArray(), BuiltInPresets.GetDefaultConfiguration());
        return parsed.IsSuccess ? Resolve(parsed.Value!, currentDirectory) : currentDirectory;
    }

    /// <summary>Normalizes the selected root and include filters; a direct file targets only that file.</summary>
    public static string Resolve(CliArguments arguments, string currentDirectory)
    {
        if (arguments.RootResolved) return currentDirectory;
        var target = arguments.RootPath;
        if (target == null && arguments.PositionalPaths.Length == 1)
        {
            var candidate = Path.GetFullPath(arguments.PositionalPaths[0], currentDirectory);
            if (Directory.Exists(candidate) || File.Exists(candidate)) target = arguments.PositionalPaths[0];
            else if (Path.IsPathRooted(arguments.PositionalPaths[0]))
                throw new DirectoryNotFoundException("Folder or file does not exist: " + candidate);
        }
        var root = target == null ? currentDirectory : Path.GetFullPath(target, currentDirectory);
        if (target != null && !Directory.Exists(root) && !File.Exists(root))
            throw new DirectoryNotFoundException("Folder or file does not exist: " + root);
        if (target != null && arguments.RootPath == null)
        {
            var normalized = target.Replace('\\', '/');
            arguments.Paths = arguments.Paths.Where(p => !p.Equals(normalized, StringComparison.Ordinal)).ToArray();
        }
        if (File.Exists(root))
        {
            arguments.Paths = [Path.GetFileName(root)];
            root = Path.GetDirectoryName(root)!;
        }
        var normalizedPaths = arguments.Paths.Select(path => NormalizeFilter(root, path)).ToArray();
        arguments.Paths = normalizedPaths.Contains(".") ? [] : normalizedPaths;
        arguments.RootResolved = true;
        return root;
    }

    private static string NormalizeFilter(string root, string value)
    {
        if (!Path.IsPathRooted(value)) return value.Replace('\\', '/');
        var relative = Path.GetRelativePath(root, value);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Include path is outside the selected folder: " + value);
        return relative.Replace('\\', '/');
    }
}
