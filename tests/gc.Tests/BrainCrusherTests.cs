using gc.Application.Services;
using Xunit;

namespace gc.Tests;

/// <summary>
/// Tests for BrainCrusher — the Brain Mode tokenizer that compresses code for LLMs.
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
        Assert.Contains("%2", result); // int → %2
        Assert.Contains("5", result);
    }

    [Fact]
    public void Crush_MultiLineComment_StripsEntireComment()
    {
        var input = "int x = 5; /* multi\nline\ncomment */ int y = 10;";
        var result = _crusher.Crush(input);
        Assert.DoesNotContain("multi", result);
        Assert.DoesNotContain("line", result);
        Assert.Contains("%2", result); // int → %2
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
        // public should be tokenized to !1, void to !l
        Assert.Contains("!1", result);
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
    public void Crush_LeadingTrailingWhitespace_Trimmed()
    {
        var input = "   public void Foo()   ";
        var result = _crusher.Crush(input);
        // After crushing, leading spaces should be gone and keywords tokenized
        Assert.DoesNotContain("   ", result);
    }

    // =========================================================================
    // 3. Token dictionary mapping
    // =========================================================================

    [Fact]
    public void Crush_Public_MappedToToken()
    {
        var input = "public class Foo";
        var result = _crusher.Crush(input);
        Assert.Contains("!1", result); // public → !1
        Assert.Contains("!e", result); // class → !e
    }

    [Fact]
    public void Crush_Static_MappedToToken()
    {
        var input = "static void Main()";
        var result = _crusher.Crush(input);
        Assert.Contains("!5", result); // static → !5
        Assert.Contains("!l", result); // void → !l
    }

    [Fact]
    public void Crush_AsyncAwait_MappedToTokens()
    {
        var input = "async Task DoSomething() { await Task.Delay(1); }";
        var result = _crusher.Crush(input);
        Assert.Contains("$1", result); // async → $1
        Assert.Contains("$2", result); // await → $2
        Assert.Contains("$3", result); // Task → $3
    }

    [Fact]
    public void Crush_ControlFlow_MappedToTokens()
    {
        var input = "if (x) { return; } else { break; }";
        var result = _crusher.Crush(input);
        Assert.Contains("#1", result); // if → #1
        Assert.Contains("#2", result); // else → #2
        Assert.Contains("!m", result); // return → !m
        Assert.Contains("#9", result); // break → #9
    }

    [Fact]
    public void Crush_CommonTypes_MappedToTokens()
    {
        var input = "string name; int age; bool active; long id;";
        var result = _crusher.Crush(input);
        Assert.Contains("%1", result); // string → %1
        Assert.Contains("%2", result); // int → %2
        Assert.Contains("%3", result); // bool → %3
        Assert.Contains("%4", result); // long → %4
    }

    [Fact]
    public void Crush_KeywordInVariableName_NotMapped()
    {
        // "myclass" should NOT have "class" replaced inside it (whole-word matching)
        var input = "var myclass = 1;";
        var result = _crusher.Crush(input);
        Assert.DoesNotContain("!e", result); // class should not be matched inside myclass
    }

    [Fact]
    public void Crush_KeywordAtWordBoundary_Mapped()
    {
        var input = "x.class = 1;"; // "class" preceded by . (non-identifier)
        var result = _crusher.Crush(input);
        Assert.Contains("!e", result);
    }

    // =========================================================================
    // 4. Round-trip (Crush → Uncrush)
    // =========================================================================

    [Fact]
    public void Uncrush_RoundTrip_Public()
    {
        var input = "public";
        var crushed = _crusher.Crush(input);
        var uncrushed = _crusher.Uncrush(crushed);
        Assert.Equal("public", uncrushed);
    }

    [Fact]
    public void Uncrush_RoundTrip_MultipleKeywords()
    {
        var input = "public static void";
        var crushed = _crusher.Crush(input);
        var uncrushed = _crusher.Uncrush(crushed);
        Assert.Equal("public static void", uncrushed);
    }

    [Fact]
    public void Uncrush_RoundTrip_MixedCode()
    {
        var input = "public class Foo { private int x; }";
        var crushed = _crusher.Crush(input);
        var uncrushed = _crusher.Uncrush(crushed);
        Assert.Equal("public class Foo { private int x; }", uncrushed);
    }

    // =========================================================================
    // 5. Token reduction measurement
    // =========================================================================

    [Fact]
    public void Crush_TypicalCSharpCode_ReducesTokenCount()
    {
        var input = @"using System;
using System.Collections.Generic;
using System.Linq;

namespace MyApp
{
    public class Calculator
    {
        private readonly List<int> _values;

        public Calculator()
        {
            _values = new List<int>();
        }

        public void Add(int value)
        {
            _values.Add(value);
        }

        public int Sum()
        {
            return _values.Sum();
        }

        // This is a comment that should be removed
        public async Task<int> ComputeAsync()
        {
            await Task.Delay(100);
            return _values.Count;
        }
    }
}";
        var result = _crusher.Crush(input);
        // The crushed result should be shorter than the input
        Assert.True(result.Length < input.Length, $"Expected crushed ({result.Length}) < input ({input.Length})");
        // Should not contain comments
        Assert.DoesNotContain("This is a comment", result);
        // Should not have using keyword (tokenized)
        Assert.DoesNotContain("using", result);
        // Should have compressed tokens
        Assert.Contains("!1", result); // public
        Assert.Contains("!2", result); // private
    }

    // =========================================================================
    // 6. Edge cases
    // =========================================================================

    [Fact]
    public void Crush_EmptyString_ReturnsEmpty()
    {
        var result = _crusher.Crush("");
        Assert.Equal("", result);
    }

    [Fact]
    public void Crush_Null_ReturnsNull()
    {
        var result = _crusher.Crush(null!);
        Assert.Null(result);
    }

    [Fact]
    public void Crush_OnlyComments_ReturnsEmptyOrMinimal()
    {
        var input = "// comment1\n// comment2\n/* block */";
        var result = _crusher.Crush(input);
        Assert.True(string.IsNullOrWhiteSpace(result) || result.Trim().Length == 0);
    }

    [Fact]
    public void Crush_NoReplaceableKeywords_PassesThrough()
    {
        var input = "Foo bar baz 123";
        var result = _crusher.Crush(input);
        Assert.Contains("Foo", result);
        Assert.Contains("bar", result);
        Assert.Contains("baz", result);
        Assert.Contains("123", result);
    }

    // =========================================================================
    // 7. Dictionary header
    // =========================================================================

    [Fact]
    public void GetDictionaryHeader_ContainsTokenMappings()
    {
        var header = _crusher.GetDictionaryHeader();
        Assert.Contains("public = !1", header);
        Assert.Contains("class = !e", header);
        Assert.Contains("Brain Mode Token Dictionary", header);
    }

    // =========================================================================
    // 8. Uncrush edge cases
    // =========================================================================

    [Fact]
    public void Uncrush_EmptyString_ReturnsEmpty()
    {
        var result = _crusher.Uncrush("");
        Assert.Equal("", result);
    }

    [Fact]
    public void Uncrush_NoTokens_ReturnsAsIs()
    {
        var result = _crusher.Uncrush("hello world");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Uncrush_PartialTokenPrefix_ReturnsAsIs()
    {
        // "!x" is not a valid token
        var result = _crusher.Uncrush("foo !x bar");
        Assert.Equal("foo !x bar", result);
    }
}
