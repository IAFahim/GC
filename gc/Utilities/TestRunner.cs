using System;
using System.Diagnostics;

namespace gc.Utilities;

public static class TestRunner
{
    public static void RunTests()
    {
        Console.WriteLine("Running gc test suite...");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = false // Show output to user
            };

            psi.ArgumentList.Add("test");
            psi.ArgumentList.Add("gc.tests/gc.tests.csproj");
            psi.ArgumentList.Add("--verbosity");
            psi.ArgumentList.Add("normal");
            psi.ArgumentList.Add("--no-build");

            using var process = Process.Start(psi);
            if (process == null)
            {
                Console.Error.WriteLine("Failed to start test process.");
                Environment.Exit(1);
                return;
            }

            // Stream output to console
            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                if (line != null)
                {
                    Console.WriteLine(line);
                }
            }

            // Stream errors to console
            while (!process.StandardError.EndOfStream)
            {
                var line = process.StandardError.ReadLine();
                if (line != null)
                {
                    Console.Error.WriteLine(line);
                }
            }

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"\nTests failed with exit code {process.ExitCode}");
                Environment.Exit(process.ExitCode);
            }
            else
            {
                Console.WriteLine("\n✓ All tests passed!");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to run tests: {ex.Message}");
            Console.Error.WriteLine("\nMake sure you've built the tests first:");
            Console.Error.WriteLine("  dotnet build");
            Environment.Exit(1);
        }
    }
}
