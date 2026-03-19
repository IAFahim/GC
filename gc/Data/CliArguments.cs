using gc.Utilities;

namespace gc.Data;

public readonly struct CliArguments
{
    public readonly string[] Paths;
    public readonly string[] Extensions;
    public readonly string[] Excludes;
    public readonly string[] Presets;
    public readonly string OutputFile;
    public readonly bool ShowHelp;
    public readonly bool RunTests;
    public readonly bool RunRealBenchmark;
    public readonly DiscoveryMode DiscoveryMode;
    public readonly long MaxMemoryBytes;
    public readonly bool Verbose;
    public readonly bool Debug;
    public readonly bool InitConfig;
    public readonly bool ValidateConfig;
    public readonly bool DumpConfig;
    public readonly GcConfiguration? Configuration;

    public CliArguments(
        string[] paths,
        string[] extensions,
        string[] excludes,
        string[] presets,
        string outputFile,
        bool showHelp,
        bool runTests,
        bool runRealBenchmark,
        DiscoveryMode discoveryMode,
        long maxMemoryBytes,
        bool verbose,
        bool debug,
        bool initConfig = false,
        bool validateConfig = false,
        bool dumpConfig = false,
        GcConfiguration? configuration = null)
    {
        Paths = paths;
        Extensions = extensions;
        Excludes = excludes;
        Presets = presets;
        OutputFile = outputFile;
        ShowHelp = showHelp;
        RunTests = runTests;
        RunRealBenchmark = runRealBenchmark;
        DiscoveryMode = discoveryMode;
        MaxMemoryBytes = maxMemoryBytes;
        Verbose = verbose;
        Debug = debug;
        InitConfig = initConfig;
        ValidateConfig = validateConfig;
        DumpConfig = dumpConfig;
        Configuration = configuration;

        // Set log level based on flags
        if (debug)
        {
            Logger.SetLevel(LogLevel.Debug);
        }
        else if (verbose)
        {
            Logger.SetLevel(LogLevel.Verbose);
        }
        else if (configuration?.Logging?.Level != null)
        {
            var level = configuration.Logging.Level.ToLowerInvariant() switch
            {
                "debug" => LogLevel.Debug,
                "verbose" => LogLevel.Verbose,
                _ => LogLevel.Normal
            };
            Logger.SetLevel(level);
        }
        else
        {
            Logger.SetLevel(LogLevel.Normal);
        }

        if (configuration?.Logging?.IncludeTimestamps == true)
        {
            Logger.IncludeTimestamps = true;
        }
    }
}