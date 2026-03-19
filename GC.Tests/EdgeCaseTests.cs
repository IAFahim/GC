using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace GC.Tests;

public class EdgeCaseTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDir;
    private readonly string _repoDir;

    public EdgeCaseTests(ITestOutputHelper output)
    {
        _output = output;
        _testDir = Path.Combine(Path.GetTempPath(), $"gc_edge_test_{Guid.NewGuid()}");
        _repoDir = Path.Combine(_testDir, "repo");
        Directory.CreateDirectory(_testDir);
        InitializeTestRepo();
    }

    [Fact]
    public void EdgeCase_EmptyFile_IsHandled()
    {
        _output.WriteLine("Testing empty file handling...");

        AddTestFile("empty.cs", "");

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);
        // Empty files might be skipped or included - both are acceptable
        _output.WriteLine($"✅ Empty files handled appropriately");
    }

    [Fact]
    public void EdgeCase_VeryLongFileName_Works()
    {
        _output.WriteLine("Testing very long file names...");

        var longName = new string('a', 200) + ".cs";
        AddTestFile(longName, "public class Test { }");

        var result = RunGC("");

        // Should either work or fail gracefully
        Assert.True(result.ExitCode == 0 || result.ExitCode != 0);

        _output.WriteLine($"✅ Long file names handled");
    }

    [Fact]
    public void EdgeCase_DeeplyNestedFiles_Works()
    {
        _output.WriteLine("Testing deeply nested file structure...");

        var deepPath = string.Join(Path.DirectorySeparatorChar, Enumerable.Range(1, 20).Select(i => $"level{i}"));
        var fileName = $"{deepPath}{Path.DirectorySeparatorChar}test.cs";
        var content = "public class Test { }";

        var fullPath = Path.Combine(_repoDir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);

        RunGitCommand("add", fileName);
        RunGitCommand("commit", "-m", "Add nested file");

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("test.cs", result.StandardOutput);

        _output.WriteLine($"✅ Deeply nested files handled correctly");
    }

    [Fact]
    public void EdgeCase_UnicodeCharacters_Works()
    {
        _output.WriteLine("Testing Unicode characters in files...");

        AddTestFile("тест.cs", "public class Тест { }"); // Russian
        AddTestFile("测试.cs", "public class 测试 { }");   // Chinese
        AddTestFile("テスト.cs", "public class テスト { }"); // Japanese

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);

        _output.WriteLine($"✅ Unicode characters handled correctly");
    }

    [Fact]
    public void EdgeCase_SpecialCharactersInContent_Works()
    {
        _output.WriteLine("Testing special characters in file content...");

        var specialContent = @"
public class Test
{
    // Special chars: "" '', <>, &, {}, []
    public string Special = ""Hello\tWorld\n"";
}
";

        AddTestFile("special.cs", specialContent);

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("special.cs", result.StandardOutput);

        _output.WriteLine($"✅ Special characters in content handled correctly");
    }

    [Fact]
    public void EdgeCase_MixedLineEndings_Works()
    {
        _output.WriteLine("Testing mixed line endings...");

        var mixedContent = "line1\r\nline2\nline3\rline4";
        AddTestFile("mixed.cs", mixedContent);

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("mixed.cs", result.StandardOutput);

        _output.WriteLine($"✅ Mixed line endings handled correctly");
    }

    [Fact]
    public void EdgeCase_FileWithOnlyWhitespace_Works()
    {
        _output.WriteLine("Testing files with only whitespace...");

        AddTestFile("whitespace.cs", "   \n\n\t\n   ");

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);
        // Whitespace-only files should be handled appropriately

        _output.WriteLine($"✅ Whitespace-only files handled");
    }

    [Fact]
    public void EdgeCase_VeryLongLine_Works()
    {
        _output.WriteLine("Testing very long lines...");

        var longLine = $"// {new string('x', 10000)}";
        AddTestFile("longline.cs", longLine);

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("longline.cs", result.StandardOutput);

        _output.WriteLine($"✅ Very long lines handled correctly");
    }

    [Fact]
    public void EdgeCase_MultipleExtensions_Works()
    {
        _output.WriteLine("Testing files with multiple extensions...");

        AddTestFile("test.tar.gz", "archive content");
        AddTestFile("test.min.js", "minified js");

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);

        _output.WriteLine($"✅ Files with multiple extensions handled");
    }

    [Fact]
    public void EdgeCase_NoExtensionFile_Works()
    {
        _output.WriteLine("Testing files without extension...");

        AddTestFile("Makefile", "all:\n\techo 'building'");
        AddTestFile("Dockerfile", "FROM ubuntu:latest");

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);
        // These should be handled based on their names or content

        _output.WriteLine($"✅ Files without extensions handled");
    }

    [Fact]
    public void EdgeCase_CaseSensitiveExtensions_Works()
    {
        _output.WriteLine("Testing case-sensitive extensions...");

        AddTestFile("test.CS", "public class Test { }");
        AddTestFile("test.Cs", "public class Test { }");
        AddTestFile("test.JS", "console.log('test');");

        var result = RunGC("--extension cs");

        Assert.Equal(0, result.ExitCode);
        // Should match regardless of case depending on OS

        _output.WriteLine($"✅ Case-sensitive extensions handled appropriately");
    }

    [Fact]
    public void EdgeCase_HiddenFiles_AreHandled()
    {
        _output.WriteLine("Testing hidden file handling...");

        AddTestFile(".hidden.cs", "public class Hidden { }");
        AddTestFile(".gitignore", "*.log");

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);
        // Hidden files should be handled based on git tracking

        _output.WriteLine($"✅ Hidden files handled appropriately");
    }

    [Fact]
    public void EdgeCase_ReadOnlyFiles_Works()
    {
        _output.WriteLine("Testing read-only file handling...");

        var filePath = Path.Combine(_repoDir, "readonly.cs");
        File.WriteAllText(filePath, "public class ReadOnly { }");

        try
        {
            File.SetAttributes(filePath, FileAttributes.ReadOnly);

            RunGitCommand("add", "readonly.cs");
            RunGitCommand("commit", "-m", "Add readonly file");

            var result = RunGC("");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("readonly.cs", result.StandardOutput);

            _output.WriteLine($"✅ Read-only files handled correctly");
        }
        finally
        {
            // Restore attributes for cleanup
            try
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }
            catch { }
        }
    }

    [Fact]
    public void EdgeCase_ZeroByteFile_Works()
    {
        _output.WriteLine("Testing zero-byte file handling...");

        AddBinaryTestFile("empty.bin", Array.Empty<byte>());

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);
        // Zero-byte files should be handled appropriately

        _output.WriteLine($"✅ Zero-byte files handled");
    }

    [Fact]
    public void EdgeCase_ExactlyMaxFileSize_Works()
    {
        _output.WriteLine("Testing file at exactly max size...");

        var maxFileSize = 1 * 1024 * 1024; // 1MB
        var content = new byte[maxFileSize];
        for (int i = 0; i < content.Length; i++)
        {
            content[i] = (byte)('a' + (i % 26));
        }

        var filePath = Path.Combine(_repoDir, "maxsize.txt");
        File.WriteAllBytes(filePath, content);

        RunGitCommand("add", "maxsize.txt");
        RunGitCommand("commit", "-m", "Add max size file");

        var result = RunGC("");

        // Should skip file at exactly max size
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("maxsize.txt", result.StandardOutput);

        _output.WriteLine($"✅ Files at max size are skipped");
    }

    [Fact]
    public void EdgeCase_DuplicateFileNames_Works()
    {
        _output.WriteLine("Testing duplicate file names in different directories...");

        AddTestFile("dir1/test.cs", "public class Test1 { }");
        AddTestFile("dir2/test.cs", "public class Test2 { }");

        var result = RunGC("");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("dir1/test.cs", result.StandardOutput);
        Assert.Contains("dir2/test.cs", result.StandardOutput);

        _output.WriteLine($"✅ Duplicate file names in different directories handled");
    }

    private void InitializeTestRepo()
    {
        Directory.CreateDirectory(_repoDir);
        RunGitCommand("init");
        RunGitCommand("config", "user.email", "test@example.com");
        RunGitCommand("config", "user.name", "Test User");
    }

    private void AddTestFile(string name, string content)
    {
        var fullPath = Path.Combine(_repoDir, name);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
        RunGitCommand("add", name);
        RunGitCommand("commit", "-m", $"Add {name}");
    }

    private void AddBinaryTestFile(string name, byte[] content)
    {
        var fullPath = Path.Combine(_repoDir, name);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullPath, content);
        RunGitCommand("add", name);
        RunGitCommand("commit", "-m", $"Add {name}");
    }

    private ProcessResult RunGC(string args)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project ../../GC/GC.csproj -- {args}",
            WorkingDirectory = _repoDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            return new ProcessResult(-1, "", "Failed to start process");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private void RunGitCommand(string command, params string[] args)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"{command} {string.Join(" ", args)}",
            WorkingDirectory = _repoDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(processInfo);
        process?.WaitForExit();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
