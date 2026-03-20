using System.Globalization;
using gc.Data;
using gc.Utilities;

namespace gc;

public static class Program
{
    public static void Main(string[] args)
    {
        try
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            var cliArgs = args.ParseCli();

            if (cliArgs.InitConfig)
            {
                InitializeConfig();
                return;
            }

            if (cliArgs.ValidateConfig)
            {
                ValidateConfig(cliArgs.Configuration!);
                return;
            }

            if (cliArgs.DumpConfig)
            {
                DumpConfig(cliArgs.Configuration!);
                return;
            }

            if (cliArgs.ShowHelp)
            {
                PrintHelp();
                return;
            }

            if (cliArgs.RunTests)
            {
                TestRunner.RunTests();
                return;
            }

            if (cliArgs.RunRealBenchmark)
            {
                RealBenchmark.RunRealBenchmark();
                return;
            }

            Logger.LogDebug($"gc started with verbose logging. Arguments: {string.Join(" ", args)}");

            var rawFiles = cliArgs.DiscoverFiles();

            if (rawFiles.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine("No tracked files found in this repository.");
                Console.ResetColor();
                Console.Error.WriteLine("The repository appears to be empty (no files have been committed).");
                Environment.Exit(1);
                return;
            }

            var filteredFiles = rawFiles.FilterFiles(cliArgs);
            if (filteredFiles.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine("No files match the specified filters.");
                Console.ResetColor();
                Console.Error.WriteLine($"Found {rawFiles.Length} total files, but all were filtered out.");
                Console.Error.WriteLine("Try adjusting your --paths, --extension, or --exclude options.");
                return;
            }

            if (!string.IsNullOrEmpty(cliArgs.OutputFile))
            {
                using var outputStream = File.Create(cliArgs.OutputFile);
                var (fileCount, totalBytes) = filteredFiles.ReadContentsLazy(cliArgs)
                    .GenerateMarkdownStreaming(outputStream, cliArgs);

                var tokens = totalBytes / 4;
                var sizeStr = totalBytes < 1024 ? $"{totalBytes} B" :
                    totalBytes < 1048576 ? $"{(totalBytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} KB" :
                    $"{(totalBytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture)} MB";
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("[OK] ");
                Console.ResetColor();
                Console.WriteLine(
                    $"Exported to {cliArgs.OutputFile}: {fileCount} files | Size: {sizeStr} | Tokens: ~{tokens}");
            }
            else
            {
                var fileContents = filteredFiles.ReadContents(cliArgs);
                var markdown = fileContents.GenerateMarkdown(cliArgs);
                markdown.HandleOutput(cliArgs, fileContents);
            }
        }
        catch (Exception ex)
        {
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[FATAL ERROR] {ex.Message}");

            if (Environment.GetEnvironmentVariable("GC_DEBUG") == "1") Console.Error.WriteLine(ex.StackTrace);

            Environment.Exit(1);
        }
        finally
        {
            Console.ResetColor();
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"gc - Git Copy (C# Native Edition)

USAGE:
    gc [OPTIONS]

OPTIONS:
    -p, --paths <paths>        Filter by starting paths (e.g. -p src libs)
    -e, --extension <ext>      Filter by extension (e.g. -e js ts)
    -x, --exclude <path>       Exclude folder, path or pattern (e.g. -x node_modules *.md)
    --preset <name>            Use predefined preset (web, backend, dotnet, unity, etc)
    -o, --output <file>        Save output to file instead of clipboard
    --discovery <mode>        File discovery mode: auto, git, filesystem (default: auto)
    --max-memory <size>        Maximum memory limit (default: 100MB, e.g., 500MB, 1GB)
    -v, --verbose              Enable verbose logging (show file-by-file progress)
    --debug                    Enable debug logging (show git commands, timing, errors)
    --test                     Run built-in test suite
    --benchmark                Run automated performance benchmarks on current repository
    -h, --help                 Show this help message
    --init-config               Initialize default configuration file (.gc/config.json)
    --validate-config           Validate configuration files
    --dump-config               Show effective configuration");
    }

    private static void InitializeConfig()
    {
        var configDir = ".gc";
        var configPath = Path.Combine(configDir, "config.json");

        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
            Console.WriteLine($"Created directory: {configDir}");
        }

        if (File.Exists(configPath))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Configuration file already exists: {configPath}");
            Console.ResetColor();
            Console.Write("Overwrite? (y/N): ");
            var response = Console.ReadLine();
            if (!string.Equals(response, "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Initialization cancelled.");
                return;
            }
        }

        var defaultConfigJson = @"{
  ""version"": ""1.0.0"",
  ""limits"": {
    ""maxFileSize"": ""1MB"",
    ""maxClipboardSize"": ""10MB"",
    ""maxMemoryBytes"": ""100MB"",
    ""maxFiles"": 100000
  },
  ""discovery"": {
    ""mode"": ""auto"",
    ""useGit"": true,
    ""followSymlinks"": false
  },
  ""presets"": {
    ""my-custom"": {
      ""extensions"": [""cs"", ""js"", ""ts""],
      ""description"": ""My custom preset""
    }
  },
  ""markdown"": {
    ""fence"": ""```"",
    ""projectStructureHeader"": ""_Project Structure:_"",
    ""fileHeaderTemplate"": ""## File: {path}""
  }
}";

        File.WriteAllText(configPath, defaultConfigJson);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Configuration file created: {configPath}");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("You can now customize the configuration by editing this file.");
        Console.WriteLine("Run 'gc --validate-config' to verify your configuration.");
        Console.WriteLine();
        Console.WriteLine("Example: Add custom presets, change markdown formatting, or adjust limits.");
    }

    private static void ValidateConfig(GcConfiguration config)
    {
        Console.WriteLine("Validating configuration...");
        Console.WriteLine();

        var paths = ConfigurationLoader.GetConfigPaths();

        Console.WriteLine("Configuration files:");
        if (File.Exists(paths.ProjectConfig))
            Console.WriteLine($"  ✓ Project: {paths.ProjectConfig}");
        else
            Console.WriteLine("  - Project: Not found");

        if (File.Exists(paths.UserConfig))
            Console.WriteLine($"  ✓ User: {paths.UserConfig}");
        else
            Console.WriteLine("  - User: Not found");

        if (File.Exists(paths.SystemConfig))
            Console.WriteLine($"  ✓ System: {paths.SystemConfig}");
        else
            Console.WriteLine("  - System: Not found");

        Console.WriteLine();

        var result = ConfigurationValidator.ValidateConfiguration(config);

        if (config.Presets != null && config.Presets.Count > 0)
        {
            Console.WriteLine($"Effective presets: {string.Join(", ", config.Presets.Keys)}");
            Console.WriteLine();
        }

        Console.WriteLine(result.ToString());

        Environment.Exit(result.IsValid ? 0 : 1);
    }

    private static void DumpConfig(GcConfiguration config)
    {
        Console.WriteLine("{");
        Console.WriteLine($"  \"version\": \"{config.Version}\",");
        Console.WriteLine("  \"limits\": {");
        Console.WriteLine($"    \"maxFileSize\": \"{config.Limits.MaxFileSize}\",");
        Console.WriteLine($"    \"maxClipboardSize\": \"{config.Limits.MaxClipboardSize}\",");
        Console.WriteLine($"    \"maxMemoryBytes\": \"{config.Limits.MaxMemoryBytes}\",");
        Console.WriteLine($"    \"maxFiles\": {config.Limits.MaxFiles}");
        Console.WriteLine("  },");
        Console.WriteLine("  \"discovery\": {");
        Console.WriteLine($"    \"mode\": \"{config.Discovery.Mode}\",");
        Console.WriteLine($"    \"useGit\": {config.Discovery.UseGit.ToString().ToLowerInvariant()},");
        Console.WriteLine($"    \"followSymlinks\": {config.Discovery.FollowSymlinks.ToString().ToLowerInvariant()}");
        Console.WriteLine("  },");
        Console.WriteLine($"  \"presetsCount\": {config.Presets.Count},");
        Console.WriteLine($"  \"languageMappingsCount\": {config.LanguageMappings.Count},");
        Console.WriteLine("  \"markdown\": {");
        Console.WriteLine($"    \"fence\": \"{config.Markdown.Fence}\",");
        Console.WriteLine($"    \"projectStructureHeader\": \"{config.Markdown.ProjectStructureHeader}\"");
        Console.WriteLine("  }");
        Console.WriteLine("}");

        Console.WriteLine();
        Console.WriteLine("Presets:");
        foreach (var preset in config.Presets)
            Console.WriteLine($"  - {preset.Key}: {string.Join(", ", preset.Value.Extensions)}");
    }
}