using gc.Application.Services;
using Xunit;

namespace gc.Tests;

public class GlobMatcherTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Exact match tests (no wildcards)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("test.cs", "test.cs", true)]
    [InlineData("Test.cs", "test.cs", true)]   // case-insensitive
    [InlineData("test.cs", "Test.CS", true)]   // case-insensitive
    [InlineData("test", "test.cs", false)]     // different
    [InlineData("test.cs", "test", false)]     // different
    public void IsMatch_ExactMatch_ChecksCorrectly(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(path, pattern));
        Assert.Equal(expected, GlobMatcher.IsMatch(path.AsSpan(), pattern.AsSpan()));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Single star (*) tests — matches any sequence except /
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("test.cs", "*.cs", true)]
    [InlineData("main.cs", "*.cs", true)]
    [InlineData("test.ts", "*.cs", false)]
    [InlineData("test.js", "*.cs", false)]

    [InlineData("test.cs", "test.*", true)]
    [InlineData("test.js", "test.*", true)]
    [InlineData("test", "test.*", false)]
    [InlineData("test.more.cs", "test.*", true)]  // * matches "more.cs"

    [InlineData("main.cs", "m*.cs", true)]
    [InlineData("my.cs", "m*.cs", true)]
    [InlineData("module.cs", "m*.cs", true)]
    [InlineData("a.cs", "m*.cs", false)]

    [InlineData("test.cs", "t*st.cs", true)]   // * matches "es"
    [InlineData("teeeest.cs", "t*st.cs", true)] // * matches "eeee"
    [InlineData("tsst.cs", "t*st.cs", true)]   // matches with s in the middle
    public void IsMatch_SingleStar_ChecksCorrectly(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(path, pattern));
    }

    [Theory]
    [InlineData("anything", "*", true)]          // universal match
    [InlineData("", "*", true)]                  // * matches empty
    [InlineData("a/b/c", "*", true)]             // * matches everything including /
    public void IsMatch_UniversalStar_ChecksCorrectly(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(path, pattern));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Question mark (?) tests — matches exactly one character
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("test.cs", "????.cs", true)]     // 4 chars
    [InlineData("abcd.cs", "????.cs", true)]     // 4 chars
    [InlineData("abc.cs", "????.cs", false)]      // 3 chars

    [InlineData("test1.cs", "test?.cs", true)]
    [InlineData("testA.cs", "test?.cs", true)]
    [InlineData("test.cs", "test?.cs", false)]    // no extra char
    [InlineData("test12.cs", "test?.cs", false)] // too many chars

    [InlineData("a.cs", "?.cs", true)]
    [InlineData("ab.cs", "?.cs", false)]
    public void IsMatch_QuestionMark_ChecksCorrectly(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(path, pattern));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Double star (**) tests — matches any sequence including /
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("src/main.cs", "**/*.cs", true)]
    [InlineData("lib/util.cs", "**/*.cs", true)]
    [InlineData("test.cs", "**/*.cs", true)]
    [InlineData("test.js", "**/*.cs", false)]

    [InlineData("src/main.cs", "src/**", true)]
    [InlineData("src/lib/util.cs", "src/**", true)]
    [InlineData("src/main.cs", "lib/**", false)]

    [InlineData("a/b/c/test.cs", "a/**/test.cs", true)]
    [InlineData("a/x/y/test.cs", "a/**/test.cs", true)]
    [InlineData("b/x/y/test.cs", "a/**/test.cs", false)]

    [InlineData("test.cs", "**", true)]
    [InlineData("a/b/c/test.cs", "**", true)]
    public void IsMatch_DoubleStar_ChecksCorrectly(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(path, pattern));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Multi-segment patterns with stars
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("test.cs", "t*st.*", true)]
    [InlineData("test.cpp", "t*st.*", true)]
    [InlineData("toast.cpp", "t*st.*", true)]

    [InlineData("src/main.cs", "*/*.cs", true)]
    [InlineData("lib/util.cs", "*/*.cs", true)]
    [InlineData("main.cs", "*/*.cs", false)]      // no leading segment
    public void IsMatch_MultiStar_ChecksCorrectly(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(path, pattern));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Real-world pattern tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("test/test_file.cs", "*/test/*", false)]
    [InlineData("src/test/util.cs", "*/test/*", true)]
    [InlineData("src/main/util.cs", "*/test/*", false)]

    [InlineData("test/file.bench.cs", "*.bench.*", true)]
    [InlineData("benchmark_test.cs", "*.bench.*", false)]  // bench not in middle with dots
    [InlineData("bench_test.cs", "*.bench.*", false)]

    [InlineData("libs/boost/algorithm.hpp", "**/boost/**", true)]
    [InlineData("boost/algorithm.hpp", "**/boost/**", true)]
    [InlineData("libs/std/algorithm.hpp", "**/boost/**", false)]

    [InlineData("build/Release/app.dll", "build/**", true)]
    [InlineData("build_output.dll", "build/**", false)]
    public void IsMatch_RealWorldPatterns_ChecksCorrectly(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(path, pattern));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Case insensitivity tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("SRC/MAIN.CS", "src/main.cs", true)]
    [InlineData("Src/Main.Cs", "src/main.cs", true)]
    [InlineData("Test.cs", "test.CS", true)]
    [InlineData("Test.cs", "TEST.CS", true)]
    public void IsMatch_CaseInsensitive_ChecksCorrectly(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(path, pattern));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Edge cases
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsMatch_EmptyPath_HandlesCorrectly()
    {
        Assert.False(GlobMatcher.IsMatch("", "test.cs"));
        Assert.True(GlobMatcher.IsMatch("", ""));
        Assert.True(GlobMatcher.IsMatch("", "*"));
    }

    [Fact]
    public void IsMatch_EmptyPattern_HandlesCorrectly()
    {
        Assert.False(GlobMatcher.IsMatch("test.cs", ""));
        Assert.True(GlobMatcher.IsMatch("", ""));
    }

    [Fact]
    public void IsMatch_OnlyStars_HandlesCorrectly()
    {
        Assert.True(GlobMatcher.IsMatch("anything", "***"));
        Assert.True(GlobMatcher.IsMatch("", "***"));
    }

    [Fact]
    public void IsMatch_DotsInPattern_HandlesCorrectly()
    {
        Assert.True(GlobMatcher.IsMatch("file.tar.gz", "*.tar.gz"));
        Assert.False(GlobMatcher.IsMatch("file.gz", "*.tar.gz"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MatchesAny tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MatchesAny_EmptyPatterns_ReturnsFalse()
    {
        Assert.False(GlobMatcher.MatchesAny("test.cs", Array.Empty<string>()));
    }

    [Theory]
    [InlineData("src/main.cs", new[] { "*.cs", "*.txt" }, true)]
    [InlineData("src/main.cs", new[] { "*.txt", "*.json" }, false)]
    [InlineData("test.cs", new[] { "test.*", "*.cs" }, true)]  // first match
    [InlineData("test.bench.cs", new[] { "*.bench.*", "*.cs" }, true)]  // specific first
    public void MatchesAny_MultiPattern_ChecksCorrectly(string path, string[] patterns, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.MatchesAny(path, patterns));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Performance regression tests (should complete in < 1ms)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsMatch_SimplePattern_HighVolume_PerformsWell()
    {
        var patterns = new[] { "*.cs", "*.ts", "*.js" };
        var paths = Enumerable.Range(0, 10000)
            .Select(i => $"src/module{i % 100}/file{i}.cs")
            .ToArray();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var path in paths)
        {
            GlobMatcher.MatchesAny(path, patterns);
        }
        sw.Stop();

        // Should complete in under 200ms for 10k operations
        Assert.True(sw.ElapsedMilliseconds < 200, $"Took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void IsMatch_ComplexPattern_HighVolume_PerformsWell()
    {
        var patterns = new[] { "**/test/**", "**/benchmark/**", "*/bench/*" };
        var paths = Enumerable.Range(0, 1000)
            .Select(i => $"src/module{i}/file{i}.cs")
            .ToArray();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var path in paths)
        {
            GlobMatcher.MatchesAny(path, patterns);
        }
        sw.Stop();

        // Should complete in under 100ms for 1k operations with complex patterns
        Assert.True(sw.ElapsedMilliseconds < 100, $"Took {sw.ElapsedMilliseconds}ms");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Property-like fuzz tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsMatch_FuzzTesting_DoesNotThrow()
    {
        var random = new Random(42);
        for (int i = 0; i < 1000; i++)
        {
            // Generate random path
            int pathLen = random.Next(1, 100);
            var pathChars = new char[pathLen];
            for (int j = 0; j < pathLen; j++)
            {
                pathChars[j] = random.Next(10) == 0 ? '/' : (char)random.Next('a', 'z' + 1);
            }
            var path = new string(pathChars);

            // Generate random pattern
            int patLen = random.Next(1, 50);
            var patChars = new char[patLen];
            for (int j = 0; j < patLen; j++)
            {
                int c = random.Next(15);
                patChars[j] = c switch
                {
                    0 => '*',
                    1 => '?',
                    2 => '/',
                    _ => (char)random.Next('a', 'z' + 1)
                };
            }
            var pattern = new string(patChars);

            // Must not throw or hang
            try
            {
                GlobMatcher.IsMatch(path, pattern);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Threw {ex.GetType().Name} for path '{path}' and pattern '{pattern}'");
            }
        }
    }
}