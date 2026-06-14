using System.Text;

namespace gc.Benchmarks;

/// <summary>
/// Base class providing shared utilities and test data generation for benchmarks.
/// </summary>
public abstract class BenchmarkBase
{
    // Size categories in bytes
    protected const int SmallSize = 1_024;           // 1KB
    protected const int MediumSize = 100_000;        // 100KB
    protected const int LargeSize = 10_000_000;      // 10MB

    /// <summary>
    /// Generates C# code-like text with various patterns including:
    /// - Comments (single-line and multi-line)
    /// - String literals (single, double, verbatim, interpolated)
    /// - Identifiers with CamelCase and snake_case
    /// - XML docs
    /// - Attributes
    /// </summary>
    protected static string GenerateCSharpCode(int size)
    {
        var sb = new StringBuilder(size);
        var random = new Random(42); // Fixed seed for reproducibility

        var snippets = new[]
        {
            // Single-line comments
            "// This is a single line comment\n",
            "// TODO: Fix this later\n",
            "// FIXME: Potential issue here\n",

            // Multi-line comments
            "/*\n * This is a multi-line comment\n * With multiple lines\n */\n",

            // XML doc comments
            "/// <summary>\n/// Does something important\n/// </summary>\n",

            // Attributes
            "[HttpGet(\"/api/users\")]\n",
            "[Authorize(Roles = \"Admin\")]\n",
            "[Obsolete(\"Use NewMethod instead\")]\n",

            // Method definitions
            "public async Task<IActionResult> GetUserAsync(int id)\n{\n",
            "    var user = await _userService.GetByIdAsync(id);\n",
            "    if (user == null) return NotFound();\n",
            "    return Ok(user);\n}\n",

            // Property definitions
            "public string UserName { get; set; }\n",
            "public int Age { get; private set; }\n",
            "public bool IsActive { get; } = true;\n",

            // String literals
            "var message = \"Hello, World!\";\n",
            "var path = @\"C:\\Users\\Documents\\file.txt\";\n",
            "var interpolated = $\"User: {user.Name}, Age: {user.Age}\";\n",
            "var raw = \"\"\"This is a raw string literal\"\"\";\n",

            // CamelCase identifiers
            "ConfigurationValidator configurationValidator = new();\n",
            "var authenticationService = new AuthenticationService();\n",
            "private readonly ILogger<Controller> _logger;\n",

            // Generic types
            "List<User> users = new();\n",
            "Dictionary<string, int> counts = new();\n",
            "ObservableCollection<Item> items = new();\n",
        };

        while (sb.Length < size)
        {
            var snippet = snippets[random.Next(snippets.Length)];
            sb.Append(snippet);

            // Add some random whitespace variations
            if (random.Next(10) == 0)
            {
                sb.Append("    "); // Extra indentation
            }

            // Add blank lines
            if (random.Next(20) == 0)
            {
                sb.Append("\n\n");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates text with repeated patterns for SuffixArray testing.
    /// Creates natural language-like text with common phrases.
    /// </summary>
    protected static string GenerateRepeatedText(int size)
    {
        var sb = new StringBuilder(size);
        var random = new Random(42);

        var commonPhrases = new[]
        {
            "the quick brown fox",
            "jumps over the lazy dog",
            "public static void Main",
            "string[] args",
            "Console.WriteLine",
            "return await Task",
            "using System",
            "namespace Application",
            "class Program",
            "async Task",
        };

        while (sb.Length < size)
        {
            var phrase = commonPhrases[random.Next(commonPhrases.Length)];
            sb.Append(phrase);
            sb.Append(random.Next(2) == 0 ? " " : "\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates text with various backtick patterns for MarkdownGenerator fence testing.
    /// </summary>
    protected static string GenerateTextWithBackticks(int size)
    {
        var sb = new StringBuilder(size);
        var random = new Random(42);

        var patterns = new[]
        {
            "normal text without backticks",
            "text with `single backtick`",
            "text with ``double backticks``",
            "text with ```triple backticks```",
            "text with ````quadruple backticks````",
            "code block:\n```javascript\nfunction test() { return true; }\n```\n",
            "nested:\n```\ncode with `backticks` inside\n```\n",
        };

        while (sb.Length < size)
        {
            var pattern = patterns[random.Next(patterns.Length)];
            sb.Append(pattern);
            sb.Append("\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates glob patterns for testing GlobMatcher.
    /// </summary>
    protected static string[] GenerateGlobPatterns(int count)
    {
        var patterns = new List<string>
        {
            "*.cs",
            "*.ts",
            "*.js",
            "*.py",
            "*.go",
            "*.rs",
            "src/**/*.cs",
            "test/**/*.ts",
            "**/*.txt",
            "**/README.md",
            "src/generated/*.cs",
            "*.csproj",
            "*.sln",
            "Dockerfile",
            "Makefile",
            ".gitignore",
            "**/node_modules/**",
            "**/bin/**",
            "**/obj/**",
            "src/*Component*.cs",
            "**/*Service*.ts",
        };

        var result = new List<string>();
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            result.Add(patterns[random.Next(patterns.Count)]);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Generates file paths for glob pattern matching.
    /// </summary>
    protected static string[] GenerateFilePaths(int count)
    {
        var paths = new List<string>
        {
            "src/Services/UserService.cs",
            "src/Controllers/UserController.cs",
            "src/Models/User.cs",
            "test/UserServiceTests.cs",
            "README.md",
            "package.json",
            "tsconfig.json",
            "src/app.ts",
            "src/components/Button.tsx",
            "src/utils/helpers.ts",
            "python/main.py",
            "python/utils/helpers.py",
            "go/main.go",
            "go/handlers/api.go",
            "rust/src/main.rs",
            "rust/src/lib.rs",
            "src/generated/Configuration.cs",
            "bin/Debug/net10.0/app.dll",
            "obj/Debug/net10.0/app.csproj.FileListAbsolute.txt",
            "node_modules/package/index.js",
            "src/Component/BaseComponent.cs",
            "src/Component/ButtonComponent.cs",
            "src/Service/DataService.cs",
            "test/Service/DataServiceTests.cs",
        };

        var result = new List<string>();
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            // Generate hierarchical paths
            var parts = new List<string> { "src" };
            var depth = random.Next(1, 4);
            for (int j = 0; j < depth; j++)
            {
                parts.Add($"dir{i}{j}");
            }
            var extensions = new[] { ".cs", ".ts", ".js", ".py", ".go", ".rs", ".md", ".txt" };
            parts.Add($"file{i}{extensions[random.Next(extensions.Length)]}");
            result.Add(string.Join("/", parts));
        }

        return result.ToArray();
    }

    /// <summary>
    /// Generates Aho-Corasick search patterns.
    /// </summary>
    protected static string[] GenerateAhoCorasickPatterns(int count)
    {
        var commonKeywords = new[]
        {
            "async", "await", "Task", "IEnumerable", "List", "Dictionary",
            "public", "private", "protected", "internal", "static",
            "class", "interface", "struct", "enum", "record",
            "string", "int", "bool", "double", "float", "long",
            "namespace", "using", "return", "if", "else", "for", "foreach",
            "while", "switch", "case", "break", "continue",
            "try", "catch", "finally", "throw", "throws",
            "new", "this", "base", "null", "true", "false",
            "void", "var", "const", "readonly", "ref", "out",
            "string.IsNullOrEmpty", "Console.WriteLine", "DateTime.Now",
            "HttpContext", "IServiceProvider", "ILogger",
        };

        var result = new List<string>();
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            result.Add(commonKeywords[random.Next(commonKeywords.Length)]);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Estimates token count for a given text (helper for size planning).
    /// </summary>
    protected static int EstimateTokenCount(string text)
    {
        // Rough estimation: ~4 characters per token for code
        return text.Length / 4;
    }

    /// <summary>
    /// Creates a FileEntry for testing.
    /// </summary>
    protected static (string path, string language) CreateTestFileEntry(int index)
    {
        var extensions = new Dictionary<string, string>
        {
            { ".cs", "cs" },
            { ".ts", "typescript" },
            { ".js", "javascript" },
            { ".py", "python" },
            { ".go", "go" },
            { ".rs", "rust" },
            { ".md", "markdown" },
            { ".txt", "text" },
        };

        var random = new Random(index);
        var ext = extensions.Keys.ElementAt(random.Next(extensions.Count));
        return ($"src/module{index % 10}/file{index}{ext}", extensions[ext]);
    }
}
