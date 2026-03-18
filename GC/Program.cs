using System;
using GC.Data;
using GC.Utilities;

namespace GC;

public static class Program
{
    public static void Main(string[] args)
    {
        var cliArgs = args.ParseCli();

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

        var rawFiles = cliArgs.DiscoverFiles();
        if (rawFiles.Length == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: Not a git repository or no tracked files found.");
            Console.ResetColor();
            return;
        }

        var filteredFiles = rawFiles.FilterFiles(cliArgs);
        var fileContents = filteredFiles.ReadContents();
        var markdown = fileContents.GenerateMarkdown();
        
        markdown.HandleOutput(cliArgs, fileContents);
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"GIT-COPY (C# Native Edition)

USAGE:
    git-copy-cs [OPTIONS]

OPTIONS:
    -p, --paths <paths>        Filter by starting paths (e.g. -p src libs)
    -e, --extension <ext>      Filter by extension (e.g. -e js ts)
    -x, --exclude <path>       Exclude folder, path or pattern (e.g. -x node_modules *.md)
    --preset <name>            Use predefined preset (web, backend, dotnet, unity, etc)
    -o, --output <file>        Save output to file instead of clipboard
    --test                     Run built-in test suite
    -h, --help                 Show this help message");
    }
}

