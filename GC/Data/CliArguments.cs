using GC.Utilities;

namespace GC.Data;

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
        bool debug)
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

        // Set log level based on flags
        if (debug)
        {
            Logger.SetLevel(LogLevel.Debug);
        }
        else if (verbose)
        {
            Logger.SetLevel(LogLevel.Verbose);
        }
        else
        {
            Logger.SetLevel(LogLevel.Normal);
        }
    }
}