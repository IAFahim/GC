using gc.Application.Services;

namespace gc.Tests;

/// <summary>
///     Tests for BrainCrusher v2 — pure minifier (comment stripping + whitespace collapse).
///     Static keyword dictionary removed in v2 (was token-pessimal).
/// </summary>
public class BrainCrusherTests
{
    private readonly BrainCrusher _crusher = new();

    // =========================================================================
    // 1. Comment stripping
    // =========================================================================

    [Fact]
    public void Crush_SingleLineComment_StripsComment()
    {
        var input = "int x = 5; // this is a comment";
        var result = _crusher.Crush(input);
        Assert.DoesNotContain("// this is a comment", result);
        Assert.Contains("5", result);
    }

    [Fact]
    public void Crush_MultiLineComment_StripsEntireComment()
    {
        var input = "int x = 5; /* multi\nline\ncomment */ int y = 10;";
        var result = _crusher.Crush(input);
        Assert.DoesNotContain("multi", result);
        Assert.DoesNotContain("line", result);
        Assert.Contains("5", result);
        Assert.Contains("10", result);
    }

    [Fact]
    public void Crush_XmlDocComment_StripsDocComment()
    {
        var input = "/// <summary>\n/// Some doc\n/// </summary>\npublic void Foo() {}";
        var result = _crusher.Crush(input);
        Assert.DoesNotContain("summary", result);
        Assert.DoesNotContain("Some doc", result);
    }

    [Fact]
    public void Crush_CommentInString_PreservesStringContent()
    {
        var input = "var s = \"hello // not a comment\";";
        var result = _crusher.Crush(input);
        Assert.Contains("hello // not a comment", result);
    }

    [Fact]
    public void Crush_EscapedQuoteInString_PreservesString()
    {
        var input = "var s = \"say \\\"hi\\\"\";";
        var result = _crusher.Crush(input);
        Assert.Contains("say \\\"hi\\\"", result);
    }

    [Fact]
    public void Crush_CharLiteral_PreservesChar()
    {
        var input = "var c = '/';";
        var result = _crusher.Crush(input);
        Assert.Contains("'/'", result);
    }

    [Fact]
    public void Crush_HashComment_Stripped()
    {
        var input = "x = 1 # this is a comment\ny = 2";
        var result = _crusher.Crush(input);
        Assert.DoesNotContain("# this is a comment", result);
        Assert.Contains("y = 2", result);
    }

    [Fact]
    public void Crush_HtmlComment_Stripped()
    {
        var input = "<div>hello</div> <!-- a comment --> <p>world</p>";
        var crusher = new BrainCrusher(".html");
        var result = crusher.Crush(input);
        Assert.DoesNotContain("a comment", result);
        Assert.Contains("hello", result);
    }

    [Fact]
    public void Crush_SqlComment_Stripped()
    {
        var input = "SELECT * FROM users -- get all users\nWHERE active = 1";
        var crusher = new BrainCrusher(".sql");
        var result = crusher.Crush(input);
        Assert.DoesNotContain("-- get all users", result);
        Assert.Contains("active = 1", result);
    }

    [Fact]
    public void Crush_SqlComment_NotStrippedForNonSqlFiles()
    {
        // -- should NOT be stripped for non-SQL files (e.g., shell scripts with --verbose)
        var input = "echo --verbose\nexit 0";
        var crusher = new BrainCrusher(".sh");
        var result = crusher.Crush(input);
        Assert.Contains("--verbose", result);
    }

    [Fact]
    public void Crush_SqlComment_NotStrippedWhenNoExtension()
    {
        // Default crusher (no extension) should NOT strip SQL-style comments
        // since -- appears in many non-SQL contexts (CLI flags, YAML, etc.)
        var input = "echo --verbose\nexit 0";
        var crusher = new BrainCrusher();
        var result = crusher.Crush(input);
        Assert.Contains("--verbose", result);
    }

    // =========================================================================
    // 2. Whitespace collapsing
    // =========================================================================

    [Fact]
    public void Crush_MultipleSpaces_CollapsesToSingle()
    {
        var input = "public    static    void    Main()";
        var result = _crusher.Crush(input);
        Assert.DoesNotContain("    ", result);
    }

    [Fact]
    public void Crush_BlankLines_Removed()
    {
        var input = "line1\n\n\n\nline2";
        var result = _crusher.Crush(input);
        Assert.DoesNotContain("\n\n", result);
    }

    [Fact]
    public void Crush_LeadingIndentation_Preserved()
    {
        var input = "   public void Foo()   ";
        var result = _crusher.Crush(input);
        // Leading spaces are preserved for indentation safety (flawless mode)
        Assert.StartsWith("   ", result);
        // Trailing spaces are still trimmed
        Assert.False(result.EndsWith("   "));
    }

    // =========================================================================
    // 3. Keywords preserved (v2: no token replacement)
    // =========================================================================

    [Fact]
    public void Crush_Public_PreservedAsIs()
    {
        var input = "public class Foo";
        var result = _crusher.Crush(input);
        Assert.Contains("public", result);
        Assert.Contains("class", result);
        Assert.Contains("Foo", result);
    }

    [Fact]
    public void Crush_StaticVoidMain_PreservedAsIs()
    {
        var input = "static void Main()";
        var result = _crusher.Crush(input);
        Assert.Contains("static", result);
        Assert.Contains("void", result);
        Assert.Contains("Main", result);
    }

    [Fact]
    public void Crush_AsyncAwait_PreservedAsIs()
    {
        var input = "async Task DoSomething() { await Task.Delay(1); }";
        var result = _crusher.Crush(input);
        Assert.Contains("async", result);
        Assert.Contains("await", result);
        Assert.Contains("Task", result);
    }

    [Fact]
    public void Crush_ControlFlow_PreservedAsIs()
    {
        var input = "if (x) { return; } else { break; }";
        var result = _crusher.Crush(input);
        Assert.Contains("if", result);
        Assert.Contains("else", result);
        Assert.Contains("return", result);
        Assert.Contains("break", result);
    }

    [Fact]
    public void Crush_KeywordInVariableName_Preserved()
    {
        var input = "var myclass = 1;";
        var result = _crusher.Crush(input);
        Assert.Contains("myclass", result);
    }

    // =========================================================================
    // 4. Round-trip (Crush → Uncrush)
    // v2: Uncrush is identity since we only minify, no symbol substitution
    // =========================================================================

    [Fact]
    public void Uncrush_IsIdentity()
    {
        var input = "public class Foo { }";
        var crushed = _crusher.Crush(input);
        var uncrushed = _crusher.Uncrush(crushed);
        Assert.Equal(crushed, uncrushed);
    }

    [Fact]
    public void GetDictionaryHeader_IsEmpty()
    {
        var header = _crusher.GetDictionaryHeader();
        Assert.Equal(string.Empty, header);
    }

    // =========================================================================
    // 5. Empty / edge cases
    // =========================================================================

    [Fact]
    public void Crush_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", _crusher.Crush(""));
    }

    [Fact]
    public void Crush_NullInput_ReturnsNull()
    {
        Assert.Null(_crusher.Crush(null!));
    }

    [Fact]
    public void CrushBlock_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", _crusher.CrushBlock(""));
    }

    [Fact]
    public void CrushBlock_NullInput_ReturnsNull()
    {
        Assert.Null(_crusher.CrushBlock(null!));
    }

    [Fact]
    public void Uncrush_NullInput_ReturnsNull()
    {
        Assert.Null(_crusher.Uncrush(null!));
    }

    [Fact]
    public void Crush_ComplexCode_AllCommentsStripped()
    {
        var input = @"
// file header
using System;

/* block comment */
public class Foo
{
    // inline comment
    public void Bar()
    {
        var s = ""hello // not a comment"";
        var x = 1; // trailing comment
    }
}";
        var result = _crusher.Crush(input);
        Assert.DoesNotContain("file header", result);
        Assert.DoesNotContain("block comment", result);
        Assert.DoesNotContain("inline comment", result);
        Assert.DoesNotContain("trailing comment", result);
        Assert.Contains("hello // not a comment", result);
        Assert.Contains("public class Foo", result);
    }

    [Fact]
    public void Crush_TripleQuoteDocstring_Preserved()
    {
        var input = "def foo():\n    \"\"\"docstring\"\"\"\n    pass";
        var result = _crusher.Crush(input);
        Assert.Contains("\"\"\"docstring\"\"\"", result);
    }

    [Fact]
    public void Crush_PreprocessorDirective_Stripped()
    {
        var input = "#region MyRegion\npublic class Foo { }\n#endregion";
        var result = _crusher.Crush(input);
        Assert.DoesNotContain("MyRegion", result);
        Assert.Contains("public class Foo", result);
    }
}