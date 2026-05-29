namespace gc.Domain.Language;

/// <summary>
///     Single source of truth for language syntax: line comments, block comments,
///     string/char delimiters, and hash-comment semantics. Shared by lexer and
///     brain-crusher — one definition, no duplication.
/// </summary>
public static class LanguageProfiles
{
    private static readonly LanguageProfile Default = new()
    {
        LineComment = ["//"],
        BlockComment = ["/*", "*/"],
        HashComment = false,
        SqlComment = false
    };

    private static readonly LanguageProfile CSharp = new()
    {
        LineComment = ["//"],
        BlockComment = ["/*", "*/"],
        HashComment = false,
        SqlComment = false
    };

    private static readonly LanguageProfile Python = new()
    {
        LineComment = ["#", "//"],
        BlockComment = ["/*", "*/"],
        HashComment = true,
        SqlComment = false
    };

    private static readonly LanguageProfile Shell = new()
    {
        LineComment = ["#"],
        BlockComment = [],
        HashComment = true,
        SqlComment = false
    };

    private static readonly LanguageProfile Sql = new()
    {
        LineComment = ["--", "//"],
        BlockComment = ["/*", "*/"],
        HashComment = false,
        SqlComment = true
    };

    private static readonly LanguageProfile Html = new()
    {
        LineComment = [],
        BlockComment = ["<!--", "-->"],
        HashComment = false,
        SqlComment = false
    };

    // YAML, TOML, INI, JSON: hash is a legitimate value character, not a comment.
    // e.g. "key: #this is part of the value" or "sed 's/foo#bar/baz/'"
    private static readonly LanguageProfile DataConfig = new()
    {
        LineComment = ["//"],
        BlockComment = ["/*", "*/"],
        HashComment = false,
        SqlComment = false
    };

    private static readonly LanguageProfile Markdown = new()
    {
        LineComment = [],
        BlockComment = [],
        HashComment = false,
        SqlComment = false
    };

    // C/C++: # is a preprocessor directive, NOT a comment — preserve it.
    // Use C-style comments only.
    private static readonly LanguageProfile C_Cpp = new()
    {
        LineComment = ["//"],
        BlockComment = ["/*", "*/"],
        HashComment = false,
        SqlComment = false
    };

    private static readonly Dictionary<string, LanguageProfile> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // C-family
        ["cs"] = CSharp,
        ["csharp"] = CSharp,
        ["c"] = C_Cpp,
        ["cpp"] = C_Cpp,
        ["cc"] = C_Cpp,
        ["cxx"] = C_Cpp,
        ["c++"] = C_Cpp,
        ["h"] = C_Cpp,
        ["hpp"] = C_Cpp,
        ["hxx"] = C_Cpp,
        ["h++"] = C_Cpp,

        // Scripting
        ["py"] = Python,
        ["python"] = Python,
        ["rb"] = Shell,
        ["ruby"] = Shell,
        ["sh"] = Shell,
        ["bash"] = Shell,
        ["zsh"] = Shell,
        ["shell"] = Shell,
        ["ps1"] = Shell,
        ["powershell"] = Shell,

        // Data/config (hash is NOT a comment here)
        ["yaml"] = DataConfig,
        ["yml"] = DataConfig,
        ["toml"] = DataConfig,
        ["ini"] = DataConfig,
        ["cfg"] = DataConfig,
        ["conf"] = DataConfig,
        ["properties"] = DataConfig,
        ["json"] = DataConfig,
        ["jsonc"] = DataConfig,

        // Markup
        ["md"] = Markdown,
        ["markdown"] = Markdown,
        ["html"] = Html,
        ["htm"] = Html,
        ["xml"] = Html,
        ["razor"] = CSharp, // C# with HTML
        ["vue"] = Html,
        ["svelte"] = Html,

        // SQL
        ["sql"] = Sql,
        ["pgsql"] = Sql,
        ["psql"] = Sql,
        ["mysql"] = Sql,
        ["sqlite"] = Sql,
        ["mariadb"] = Sql,
        ["mssql"] = Sql,
        ["oracle"] = Sql,
        ["plsql"] = Sql,
        ["tsql"] = Sql,
        ["transactsql"] = Sql,

        // Build/config files that use # comments
        ["dockerfile"] = Shell,
        ["makefile"] = Shell,
        ["gemfile"] = Shell,
        ["rakefile"] = Shell,
        ["bagfile"] = Shell
    };
    // Each profile declares which comment styles are active for a language.
    // String/char handling is universal (strip contents verbatim).

    public static LanguageProfile For(string? languageOrExt)
    {
        if (string.IsNullOrEmpty(languageOrExt)) return Default;
        var key = languageOrExt.ToLowerInvariant().TrimStart('.');
        return Map.GetValueOrDefault(key, Default);
    }
}

/// <summary>
///     Language-specific comment and preprocessor syntax.
/// </summary>
/// <param name="LineComment">Line-comment start sequences (e.g. "//", "#", "--")</param>
/// <param name="BlockComment">Block-comment delimiters as [open, close] pairs</param>
/// <param name="HashComment">Whether # starts a line comment (shell-style)</param>
/// <param name="SqlComment">Whether -- starts a SQL line comment</param>
public readonly record struct LanguageProfile(
    string[] LineComment,
    string[] BlockComment,
    bool HashComment,
    bool SqlComment);