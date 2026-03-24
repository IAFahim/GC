using gc.Domain.Models.Configuration;

namespace gc.Domain.Constants;

public static class BuiltInPresets
{
    public static readonly string[] PresetWeb =
        ["html", "htm", "css", "scss", "sass", "less", "js", "jsx", "ts", "tsx", "json", "svg", "vue", "svelte"];

    public static readonly string[] PresetBackend =
        ["py", "rb", "php", "pl", "go", "rs", "java", "cs", "cpp", "cc", "cxx", "h", "c", "hpp", "hxx", "swift", "kt", "ex", "exs", "sh"];

    public static readonly string[] PresetDotnet = ["cs", "razor", "csproj", "json", "http", "xaml"];

    public static readonly string[] PresetUnity =
        ["cs", "shader", "cginc", "hlsl", "glsl", "asmdef", "asmref", "uss", "uxml", "json", "yaml"];

    public static readonly string[] PresetJava = ["java", "kt", "kts", "scala"];
    public static readonly string[] PresetCpp = ["c", "h", "cpp", "cc", "cxx", "hpp", "hxx", "rs", "go", "swift"];
    public static readonly string[] PresetScript = ["py", "rb", "php", "pl", "pm", "lua", "sh", "bash", "zsh", "ps1"];

    public static readonly string[] PresetData =
        ["sql", "xml", "json", "yaml", "yml", "toml", "ini", "md", "csv", "graphql"];

    public static readonly string[] PresetConfig =
        ["env", "conf", "ini", "Dockerfile", "Makefile", "Gemfile", "package.json", "cargo.toml", "go.mod"];

    public static readonly string[] PresetBuild = ["Dockerfile", "Makefile", "Gemfile", "package.json"];
    public static readonly string[] PresetDocs = ["md", "txt", "rst", "adoc"];

    public static readonly string[] SystemIgnoredPatterns =
    [
        "node_modules/", "bin/", "obj/", "package-lock.json", "yarn.lock", "Cargo.lock",
        ".DS_Store", "Thumbs.db", ".git/", ".png", ".jpg", ".jpeg", ".gif", ".ico",
        ".woff", ".woff2", ".pdf", ".exe", ".bin", ".pyc", ".dll", ".pdb",
        ".min.js", ".min.css", ".meta", "id_rsa", "id_dsa", ".pem", ".key", ".p12",
        ".env", "secrets", "credentials"
    ];

    public static readonly Dictionary<string, string> LanguageMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["js"] = "javascript",
        ["ts"] = "typescript",
        ["py"] = "python",
        ["cs"] = "csharp",
        ["sh"] = "bash",
        ["md"] = "markdown",
        ["h"] = "c",
        ["hpp"] = "cpp",
        ["razor"] = "html",
        ["vue"] = "html",
        ["shader"] = "glsl",
        ["cginc"] = "glsl",
        ["hlsl"] = "glsl",
        ["uss"] = "css",
        ["uxml"] = "xml",
        ["ps1"] = "powershell",
        ["dockerfile"] = "dockerfile",
        ["makefile"] = "makefile",
        ["gemfile"] = "ruby",
        ["rakefile"] = "ruby"
    };

    public static Dictionary<string, PresetConfiguration> GetAllPresets()
    {
        return new Dictionary<string, PresetConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["web"] = new()
            {
                Extensions = PresetWeb,
                Description = "Web development files"
            },
            ["backend"] = new()
            {
                Extensions = PresetBackend,
                Description = "Backend development files"
            },
            ["dotnet"] = new()
            {
                Extensions = PresetDotnet,
                Description = ".NET development files"
            },
            ["unity"] = new()
            {
                Extensions = PresetUnity,
                Description = "Unity game engine files"
            },
            ["java"] = new()
            {
                Extensions = PresetJava,
                Description = "JVM languages"
            },
            ["cpp"] = new()
            {
                Extensions = PresetCpp,
                Description = "C/C++ files"
            },
            ["script"] = new()
            {
                Extensions = PresetScript,
                Description = "Scripting languages"
            },
            ["data"] = new()
            {
                Extensions = PresetData,
                Description = "Data formats"
            },
            ["config"] = new()
            {
                Extensions = PresetConfig,
                Description = "Configuration files"
            },
            ["build"] = new()
            {
                Extensions = PresetBuild,
                Description = "Build files"
            },
            ["docs"] = new()
            {
                Extensions = PresetDocs,
                Description = "Documentation"
            }
        };
    }

    public static GcConfiguration GetDefaultConfiguration()
    {
        return new GcConfiguration
        {
            Version = "1.0.0",
            Limits = new LimitsConfiguration
            {
                MaxFileSize = "1MB",
                MaxClipboardSize = "10MB",
                MaxMemoryBytes = "100MB",
                MaxFiles = 100000
            },
            Discovery = new DiscoveryConfiguration
            {
                Mode = "auto",
                UseGit = true,
                FollowSymlinks = false
            },
            Filters = new FiltersConfiguration
            {
                SystemIgnoredPatterns = SystemIgnoredPatterns,
                AdditionalExtensions = Array.Empty<string>()
            },
            Presets = GetAllPresets(),
            LanguageMappings = new Dictionary<string, string>(LanguageMappings, StringComparer.OrdinalIgnoreCase),
            Markdown = new MarkdownConfiguration
            {
                Fence = "```",
                ProjectStructureHeader = "_Project Structure:_",
                FileHeaderTemplate = "## File: {path}",
                LanguageDetection = "extension"
            },
            Output = new OutputConfiguration
            {
                DefaultFormat = "markdown",
                IncludeStats = true,
                SortByPath = true
            },
            Logging = new LoggingConfiguration
            {
                Level = "normal",
                IncludeTimestamps = false
            }
        };
    }
}
