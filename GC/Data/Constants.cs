namespace GC.Data;

public static class Constants
{
    public const int MaxFileSize = 1048576;
    public const string MarkdownFence = "```";
    public const string ProjectStructureHeader = "_Project Structure:_";
    public const string TextLang = "text";
    
    public static readonly string[] SystemIgnoredPatterns =[
        "node_modules/", "bin/", "obj/", "package-lock.json", "yarn.lock", "Cargo.lock", 
        ".DS_Store", "Thumbs.db", ".git/", ".png", ".jpg", ".jpeg", ".gif", ".ico", 
        ".woff", ".woff2", ".pdf", ".exe", ".bin", ".pyc", ".dll", ".pdb", 
        ".min.js", ".min.css", ".meta", "id_rsa", "id_dsa", ".pem", ".key", ".p12", 
        ".env", "secrets", "credentials"
    ];

    public static readonly string[] PresetWeb =["html", "htm", "css", "scss", "sass", "less", "js", "jsx", "ts", "tsx", "json", "svg", "vue", "svelte"];
    public static readonly string[] PresetBackend =["py", "rb", "php", "pl", "go", "rs", "java", "cs", "cpp", "h", "c", "hpp", "swift", "kt", "ex", "exs", "sh"];
    public static readonly string[] PresetDotnet =["cs", "razor", "csproj", "json", "http", "xaml"];
    public static readonly string[] PresetUnity =["cs", "shader", "cginc", "hlsl", "glsl", "asmdef", "asmref", "uss", "uxml", "json", "yaml"];
    public static readonly string[] PresetJava = ["java", "kt", "kts", "scala"];
    public static readonly string[] PresetCpp =["c", "h", "cpp", "cc", "cxx", "hpp", "hxx", "rs", "go", "swift"];
    public static readonly string[] PresetScript =["py", "rb", "php", "pl", "pm", "lua", "sh", "bash", "zsh", "ps1"];
    public static readonly string[] PresetData =["sql", "xml", "json", "yaml", "yml", "toml", "ini", "md", "csv", "graphql"];
    public static readonly string[] PresetConfig =["env", "conf", "ini", "Dockerfile", "Makefile", "Gemfile", "package.json", "cargo.toml", "go.mod"];
    public static readonly string[] PresetBuild = ["Dockerfile", "Makefile", "Gemfile", "package.json"];
    public static readonly string[] PresetDocs = ["md", "txt", "rst", "adoc"];

    public static readonly string[] LangMapKeys =[
        "js", "ts", "py", "cs", "sh", "md", "h", "hpp", "razor", "vue", 
        "shader", "cginc", "hlsl", "uss", "uxml", "ps1", "dockerfile", 
        "makefile", "gemfile", "rakefile"
    ];

    public static readonly string[] LangMapValues =[
        "javascript", "typescript", "python", "csharp", "bash", "markdown", "c", "cpp", "html", "html", 
        "glsl", "glsl", "glsl", "css", "xml", "powershell", "dockerfile", 
        "makefile", "ruby", "ruby"
    ];
}