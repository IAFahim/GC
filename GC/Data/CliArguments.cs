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

    public CliArguments(
        string[] paths, 
        string[] extensions, 
        string[] excludes, 
        string[] presets, 
        string outputFile, 
        bool showHelp,
        bool runTests)
    {
        Paths = paths;
        Extensions = extensions;
        Excludes = excludes;
        Presets = presets;
        OutputFile = outputFile;
        ShowHelp = showHelp;
        RunTests = runTests;
    }
}