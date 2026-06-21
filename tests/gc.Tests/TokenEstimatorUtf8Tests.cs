using System.Text;
using gc.Application.Services;

namespace gc.Tests;

/// <summary>
///     Differential equivalence tests for <see cref="TokenEstimator.EstimateTokensUtf8" />.
///     The byte-direct estimator MUST produce a count identical to decoding the bytes to UTF-16
///     (exactly as the streaming pipeline used to) and running the char-based heuristic. These tests
///     are the correctness gate for replacing the per-file UTF-16 decode + StreamingTokenEstimator.
/// </summary>
public class TokenEstimatorUtf8Tests
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    // The contract: byte-direct == char-based over the decoded string.
    private static void AssertEquivalent(byte[] bytes)
    {
        var decoded = Utf8NoBom.GetString(bytes);
        var expected = TokenEstimator.EstimateTokens(decoded);
        var actual = TokenEstimator.EstimateTokensUtf8(bytes);
        Assert.True(expected == actual,
            $"mismatch: expected {expected}, got {actual} for bytes [{Convert.ToHexString(bytes)}] decoded=\"{decoded}\"");
    }

    private static void AssertEquivalent(string text) => AssertEquivalent(Utf8NoBom.GetBytes(text));

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("   \t\r\n")]
    [InlineData("!@#$%^&*()")]
    [InlineData("hello world")]
    [InlineData("camelCaseIdentifier")]
    [InlineData("snake_case_identifier")]
    [InlineData("_leadingUnderscore")]
    [InlineData("XMLParser")]
    [InlineData("IConfigurationValidator")]
    [InlineData("public async Task<Result> DoThing()")]
    [InlineData("abc123def456")]
    [InlineData("123abc")]
    [InlineData("HTTPSConnection")]
    [InlineData("```csharp\nvar x = 1;\n```")]
    [InlineData("````````````")]
    [InlineData("line1\r\nline2\r\nline3")]
    public void Ascii_and_structure_are_equivalent(string text) => AssertEquivalent(text);

    [Theory]
    [InlineData("café résumé naïve")]
    [InlineData("日本語のテキストサンプル")]
    [InlineData("Здравствуй мир")]
    [InlineData("emoji 😀🎉🚀 here")]
    [InlineData("astral𝕏math")]
    [InlineData("abc文def")] // non-ASCII absorbed mid-word
    [InlineData("fooБbar")]
    [InlineData("end日")]
    [InlineData("日start")]
    [InlineData("mix日本ABCdef日本")]
    public void Unicode_runs_are_equivalent(string text) => AssertEquivalent(text);

    [Fact]
    public void Malformed_utf8_matches_decoder_replacement()
    {
        var cases = new[]
        {
            new byte[] { 0x80 }, // lone continuation
            new byte[] { 0xE0, 0x41 }, // truncated 3-byte lead followed by ASCII
            new byte[] { 0xC0, 0x80 }, // overlong encoding of NUL
            new byte[] { 0xF8, 0xFF, 0xFE }, // bytes that are never valid UTF-8 leads
            new byte[] { 0xE9 }, // bare Latin-1 'é' (not valid standalone UTF-8)
            new byte[] { 0x61, 0xE9, 0x62 }, // ASCII, invalid, ASCII
            new byte[] { 0xED, 0xA0, 0x80 }, // UTF-8 of a lone high surrogate (ill-formed)
            new byte[] { 0xF4, 0x90, 0x80, 0x80 }, // above U+10FFFF
            new byte[] { 0xC2 }, // truncated 2-byte sequence at EOF
            new byte[] { 0x41, 0xC2 }, // word then truncated lead at EOF
            new byte[] { 0xE2, 0x82 }, // truncated 3-byte (€ missing last byte)
        };
        foreach (var c in cases) AssertEquivalent(c);
    }

    [Fact]
    public void Empty_and_boundary_inputs()
    {
        Assert.Equal(0, TokenEstimator.EstimateTokensUtf8(ReadOnlySpan<byte>.Empty));
        AssertEquivalent(Array.Empty<byte>());
        AssertEquivalent(new byte[] { 0x20 }); // single space
        AssertEquivalent(new byte[] { 0x2E }); // single punct
    }

    [Fact]
    public void Real_repo_source_files_are_equivalent()
    {
        // The actual corpus the tool processes — strongest real-world signal.
        var root = FindRepoRoot();
        if (root == null) return; // running outside the repo tree; skip silently
        var src = Path.Combine(root, "src");
        if (!Directory.Exists(src)) return;
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
            AssertEquivalent(File.ReadAllBytes(file));
    }

    [Fact]
    public void Random_byte_fuzz_matches_decoder()
    {
        // Deterministic seed so failures reproduce.
        var rng = new Random(0xC0FFEE);
        for (var iter = 0; iter < 20_000; iter++)
        {
            var len = rng.Next(0, 64);
            var bytes = new byte[len];
            rng.NextBytes(bytes);
            AssertEquivalent(bytes);
        }
    }

    [Fact]
    public void Random_mostly_ascii_fuzz_matches_decoder()
    {
        // Bias toward ASCII text with occasional high bytes — closer to real source/code.
        var rng = new Random(0xBADF00D);
        for (var iter = 0; iter < 20_000; iter++)
        {
            var len = rng.Next(0, 200);
            var bytes = new byte[len];
            for (var j = 0; j < len; j++)
                bytes[j] = rng.Next(100) < 90 ? (byte)rng.Next(32, 127) : (byte)rng.Next(256);
            AssertEquivalent(bytes);
        }
    }

    [Fact]
    public void Large_input_matches_streaming_estimator_across_chunk_boundary()
    {
        // The pipeline previously summed tokens via StreamingTokenEstimator over 64 KB decode chunks.
        // Verify the byte-direct method matches that exact path for inputs spanning the boundary,
        // so swapping it preserves the historical LastEstimatedTokens value.
        var rng = new Random(0x5EED);
        foreach (var size in new[] { 65_530, 65_536, 65_540, 131_072, 200_000 })
        {
            var sb = new StringBuilder(size);
            string[] words = { "configurationValidator", "XMLParser", "foo_bar", "日本語", "café", "x", "123abc", "😀" };
            while (sb.Length < size)
            {
                sb.Append(words[rng.Next(words.Length)]);
                sb.Append(rng.Next(4) == 0 ? '\n' : ' ');
            }

            var text = sb.ToString();
            var bytes = Utf8NoBom.GetBytes(text);

            var streaming = new TokenEstimator.StreamingTokenEstimator();
            var decoder = Utf8NoBom.GetDecoder();
            var charBuf = new char[65536 + 8];
            var off = 0;
            while (off < bytes.Length)
            {
                var count = Math.Min(65536, bytes.Length - off);
                var flush = off + count == bytes.Length;
                var chars = decoder.GetChars(bytes.AsSpan(off, count), charBuf.AsSpan(), flush);
                streaming.Append(charBuf.AsSpan(0, chars));
                off += count;
            }

            streaming.Flush();

            Assert.Equal(streaming.Tokens, TokenEstimator.EstimateTokensUtf8(bytes));
            Assert.Equal(TokenEstimator.EstimateTokens(text), TokenEstimator.EstimateTokensUtf8(bytes));
        }
    }

    private static string? FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir, "gc.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }
}
