using System.Diagnostics;

namespace gc.Infrastructure.Testing;

public sealed class TestRunner
{
    private readonly string _testProjectPath;

    public TestRunner(string testProjectPath)
    {
        _testProjectPath = testProjectPath;
    }

    public static void RunTests()
    {
        var testRunner = new TestRunner(FindTestProject());
        testRunner.ExecuteAsync().GetAwaiter().GetResult();
    }

    private static string FindTestProject()
    {
        var currentDir = AppContext.BaseDirectory;
        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir, "tests", "gc.Tests", "gc.Tests.csproj");
            if (File.Exists(candidate)) return candidate;
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        throw new FileNotFoundException("Could not find gc.Tests.csproj.");
    }

    public async Task ExecuteAsync()
    {
        Console.WriteLine("Running gc test suite...");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test {_testProjectPath} --filter FullyQualifiedName!~ReleaseBinaryTests --verbosity normal",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            
            process.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

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
            Environment.Exit(1);
        }
    }
}
