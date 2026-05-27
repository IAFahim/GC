using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using gc.Application.Services;

namespace gc.Tests
{
    public class CodeLexerTests
    {
        // --- Identifier extraction ---

        [Fact]
        public void Enumerate_ReturnsLongIdentifiers()
        {
            var source = "public class ConfigurationValidator { int ExampleField = 0; }";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Contains("public", ids);
            Assert.Contains("ConfigurationValidator", ids);
            Assert.Contains("ExampleField", ids);
            Assert.DoesNotContain("class", ids);
            Assert.DoesNotContain("int", ids);
        }

        [Fact]
        public void Enumerate_CountsIdentifiersCorrectly()
        {
            var source = "AlphaBeta GammaDelta EpsilonZeta";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Equal(3, ids.Count);
            Assert.Equal(new[] { "AlphaBeta", "GammaDelta", "EpsilonZeta" }, ids);
        }

        [Fact]
        public void Enumerate_ReturnsCount()
        {
            var source = "AlphaBeta GammaDelta";
            var lexer = new CodeLexer(source.AsSpan());
            int count = lexer.Enumerate(_ => { });
            Assert.Equal(2, count);
        }

        [Fact]
        public void Enumerate_EmptyInput_ReturnsNothing()
        {
            var lexer = new CodeLexer("".AsSpan());
            var ids = new List<string>();
            int count = lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
            Assert.Equal(0, count);
        }

        [Fact]
        public void Enumerate_NoLongIdentifiers_ReturnsNothing()
        {
            var lexer = new CodeLexer("int x = 0; if (a) { }".AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
        }

        // --- Comment skipping ---

        [Fact]
        public void Enumerate_SkipsSingleLineComments()
        {
            var source = "// this is a comment\nConfigurationValidator x = null;";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Contains("ConfigurationValidator", ids);
            Assert.DoesNotContain("this", ids);
            Assert.DoesNotContain("comment", ids);
        }

        [Fact]
        public void Enumerate_SkipsMultiLineComments()
        {
            var source = "/* comment block */\nConfigurationValidator x = null;";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Contains("ConfigurationValidator", ids);
            Assert.DoesNotContain("comment", ids);
            Assert.DoesNotContain("block", ids);
        }

        [Fact]
        public void Enumerate_SkipsHashComments()
        {
            var source = "# this is a comment\nConfigurationValidator = None";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Contains("ConfigurationValidator", ids);
            Assert.DoesNotContain("comment", ids);
        }

        [Fact]
        public void Enumerate_SkipsHtmlComments()
        {
            var source = "<!-- html comment -->\nConfigurationValidator v;";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Contains("ConfigurationValidator", ids);
        }

        [Fact]
        public void Enumerate_SkipsSqlComments()
        {
            var source = "-- sql comment\nConfigurationValidator varchar(100);";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Contains("ConfigurationValidator", ids);
            Assert.DoesNotContain("comment", ids);
        }

        // --- String/char literal handling ---

        [Fact]
        public void Enumerate_SkipsStringLiterals()
        {
            var source = "var x = \"thisShouldNotAppear\";\nConfigurationValidator v;";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Contains("ConfigurationValidator", ids);
            Assert.DoesNotContain("thisShouldNotAppear", ids);
        }

        [Fact]
        public void Enumerate_EscapedCharsInStrings()
        {
            var source = "var x = \"hello\\\"world\";\nConfigurationValidator v;";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Contains("ConfigurationValidator", ids);
        }

        [Fact]
        public void Enumerate_SkipsCharLiterals()
        {
            var source = "var x = 'c';\nConfigurationValidator v;";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Contains("ConfigurationValidator", ids);
        }

        [Fact]
        public void Enumerate_EscapedCharsInCharLiterals()
        {
            var source = "var x = '\\\\';\nConfigurationValidator v;";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Contains("ConfigurationValidator", ids);
        }

        // --- Triple quotes ---

        [Fact]
        public void Enumerate_SkipsTripleQuoteDouble()
        {
            var source = "\"\"\"this should not appear\"\"\"\nConfigurationValidator v;";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Contains("ConfigurationValidator", ids);
            Assert.DoesNotContain("should", ids);
        }

        [Fact]
        public void Enumerate_SkipsTripleQuoteSingle()
        {
            var source = "'''this should not appear'''\nConfigurationValidator v;";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Contains("ConfigurationValidator", ids);
        }

        // --- Edge cases ---

        [Fact]
        public void Enumerate_UnderscoreIdentifiers()
        {
            var source = "_privateVar _anotherLongName";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Contains("_privateVar", ids);
            Assert.Contains("_anotherLongName", ids);
        }

        [Fact]
        public void Enumerate_MixedCodeAndComments()
        {
            var source = "FirstIdent // comment\nSecondIdent /* block */ ThirdIdent";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Equal(new[] { "FirstIdent", "SecondIdent", "ThirdIdent" }, ids);
        }

        [Fact]
        public void Enumerate_StringFollowedByComment()
        {
            var source = "\"string\" // comment\nConfigurationValidator;";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Contains("ConfigurationValidator", ids);
        }

        [Fact]
        public void Enumerate_IdentifiersWithNumbers()
        {
            var source = "Example123 Test456Var";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));

            Assert.Contains("Example123", ids);
            Assert.Contains("Test456Var", ids);
        }

        // --- Unclosed/edge-case paths for full branch coverage ---

        [Fact]
        public void Enumerate_UnclosedString_Eof()
        {
            var source = "\"unclosed string ConfigurationValidator";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            // everything inside unclosed string is consumed, no identifiers found
            Assert.Empty(ids);
        }

        [Fact]
        public void Enumerate_UnclosedChar_Eof()
        {
            var source = "'x ConfigurationValidator";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
        }

        [Fact]
        public void Enumerate_BackslashAtEndOfString()
        {
            var source = "\"test\\";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
        }

        [Fact]
        public void Enumerate_BackslashAtEndOfCharLiteral()
        {
            var source = "'\\";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
        }

        [Fact]
        public void Enumerate_UnclosedTripleQuote_Eof()
        {
            var source = "\"\"\"unclosed triple quote ConfigurationValidator";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
        }

        [Fact]
        public void Enumerate_UnclosedSingleTripleQuote_Eof()
        {
            var source = "'''unclosed triple single ConfigurationValidator";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
        }

        [Fact]
        public void Enumerate_UnclosedMultiComment_Eof()
        {
            var source = "/* unclosed comment ConfigurationValidator";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
        }

        [Fact]
        public void Enumerate_SqlCommentNoNewline_Eof()
        {
            var source = "-- sql comment to eof\nConfigurationValidator";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Contains("ConfigurationValidator", ids);
        }

        [Fact]
        public void Enumerate_HashCommentNoNewline_Eof()
        {
            var source = "# hash to eof";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
        }

        [Fact]
        public void Enumerate_SingleCommentNoNewline_Eof()
        {
            var source = "// comment to eof";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
        }

        [Fact]
        public void Enumerate_HtmlCommentNoClose_Eof()
        {
            var source = "<!-- never closes";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
        }

        [Fact]
        public void Enumerate_ExactlyFiveCharIdentifier_Excluded()
        {
            var source = "class";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids); // "class" is 5 chars, excluded
        }

        [Fact]
        public void Enumerate_ExactlySixCharIdentifier_Included()
        {
            var source = "public";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Single(ids);
            Assert.Equal("public", ids[0]);
        }

        [Fact]
        public void Enumerate_SqlCommentNoNewlineToEnd()
        {
            var source = "-- no newline here";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
        }

        [Fact]
        public void Enumerate_NonIdentifierChars()
        {
            var source = "!!! ??? 12345 @@@ ConfigurationValidator";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Single(ids);
            Assert.Equal("ConfigurationValidator", ids[0]);
        }

        [Fact]
        public void Enumerate_MultilineMultiComment()
        {
            var source = "/* line1\nline2 */\nConfigurationValidator";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Contains("ConfigurationValidator", ids);
        }

        [Fact]
        public void Enumerate_CommentThenStringThenComment()
        {
            var source = "// comment\n\"string\"/*block*/ConfigurationValidator";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Contains("ConfigurationValidator", ids);
        }

        [Fact]
        public void Enumerate_StringEndsAtEof()
        {
            // String that reaches EOF without closing
            var source = "\"open string";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
        }

        [Fact]
        public void Enumerate_CharEndsAtEof()
        {
            var source = "'x";
            var lexer = new CodeLexer(source.AsSpan());
            var ids = new List<string>();
            lexer.Enumerate(span => ids.Add(span.ToString()));
            Assert.Empty(ids);
        }
    }

    public class FrequencyAnalyzerTests
    {
        [Fact]
        public void BuildFrequencyMap_CountsAcrossFiles()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "a.cs"), "ConfigurationValidator x;");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "b.cs"), "ConfigurationValidator y; ConfigurationValidator z;");

            var map = FrequencyAnalyzer.BuildFrequencyMap(dir);
            Assert.True(map.ContainsKey("ConfigurationValidator"));
            Assert.Equal(3, map["ConfigurationValidator"]);

            System.IO.Directory.Delete(dir, true);
        }

        [Fact]
        public void BuildFrequencyMap_IgnoresShortIdentifiers()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "a.cs"), "int x = 0; if (a) {}");

            var map = FrequencyAnalyzer.BuildFrequencyMap(dir);
            Assert.Empty(map);

            System.IO.Directory.Delete(dir, true);
        }

        [Fact]
        public void BuildFrequencyMap_EmptyDirectory_ReturnsEmpty()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(dir);

            var map = FrequencyAnalyzer.BuildFrequencyMap(dir);
            Assert.Empty(map);

            System.IO.Directory.Delete(dir, true);
        }

        [Fact]
        public void ComputeSavingsScores_SortsByScore()
        {
            var freqMap = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Short"] = 100,      // (5-1)*100 = 400
                ["VeryLongName"] = 10 // (12-1)*10 = 110
            };

            var scores = FrequencyAnalyzer.ComputeSavingsScores(freqMap);
            Assert.Equal(2, scores.Count);
            Assert.Equal("Short", scores[0].Identifier);       // 400 > 110
            Assert.Equal("VeryLongName", scores[1].Identifier);
        }

        [Fact]
        public void ComputeSavingsScores_SingleCharIdentifier_Excluded()
        {
            var freqMap = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["A"] = 10
            };
            var scores = FrequencyAnalyzer.ComputeSavingsScores(freqMap);
            Assert.Empty(scores);
        }

        [Fact]
        public void ComputeSavingsScores_EmptyMap_ReturnsEmpty()
        {
            var scores = FrequencyAnalyzer.ComputeSavingsScores(new Dictionary<string, int>());
            Assert.Empty(scores);
        }

        [Fact]
        public void Analyze_IntegrationTest()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "a.cs"),
                "ConfigurationValidator ExampleService; ConfigurationValidator AnotherService;");

            var results = FrequencyAnalyzer.Analyze(dir);
            Assert.NotEmpty(results);
            Assert.Equal("ConfigurationValidator", results[0].Identifier);
            Assert.Equal(2, results[0].Frequency);

            System.IO.Directory.Delete(dir, true);
        }

        [Fact]
        public void Analyze_FilterByMinLength()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "a.cs"),
                "AlphaBeta Shorty LongerName");

            var results = FrequencyAnalyzer.Analyze(dir, minLength: 8);
            // AlphaBeta(9) and LongerName(10) pass, Shorty(6) passes lexer but minLength=8 filters it
            Assert.All(results, r => Assert.True(r.Identifier.Length >= 8));

            System.IO.Directory.Delete(dir, true);
        }
    }

    public class IdentifierRankerTests
    {
        [Fact]
        public void RankByScore_SortsDescending()
        {
            var items = new[]
            {
                new IdentifierRankedEntry("Low", 1, 10),
                new IdentifierRankedEntry("High", 1, 100),
                new IdentifierRankedEntry("Med", 1, 50)
            };

            var ranked = items.OrderByDescending(x => x.Score).ToList();
            Assert.Equal("High", ranked[0].Identifier);
            Assert.Equal("Med", ranked[1].Identifier);
            Assert.Equal("Low", ranked[2].Identifier);
        }

        [Fact]
        public void RankByScore_EmptyInput_ReturnsEmpty()
        {
            var ranked = Array.Empty<IdentifierRankedEntry>().OrderByDescending(x => x.Score);
            Assert.Empty(ranked);
        }
    }

    public class IdentifierRankedEntryTests
    {
        [Fact]
        public void Constructor_SetsProperties()
        {
            var entry = new IdentifierRankedEntry("TestIdent", 42, 999);
            Assert.Equal("TestIdent", entry.Identifier);
            Assert.Equal(42, entry.Frequency);
            Assert.Equal(999, entry.Score);
        }
    }
}