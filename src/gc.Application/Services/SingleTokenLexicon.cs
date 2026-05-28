namespace gc.Application.Services;

/// <summary>
/// Curated list of Unicode Private Use Area (PUA) characters used by the compression
/// pipeline as replacement symbols. PUA codepoints U+E000–U+F8FF are guaranteed by the
/// Unicode standard to never represent any real character, so they CANNOT collide with
/// source code — unlike Greek/Cyrillic which are valid identifiers in C#, Rust, Python, etc.
///
/// Symbol categories (in order):
///   0–19  : Basic PUA block (U+E000–U+E013)
///  20–39  : Supplemental PUA (U+E014–U+E027)
///  40–59  : Extended PUA-A (U+E028–U+E03B)
///  60–79  : Supplementary PUA-A (U+E04C–U+E05B)
///  80–99  : Ideographic Symbols (U+E080–U+E093)
///  100–119 : Variation Selectors (U+E100–U+E113)
///
/// WARNING: Do NOT use Greek (Α-Ω), Cyrillic (А-Я), or other Unicode letters that are
/// valid in programming language identifiers — compression will corrupt source code
/// that contains those characters as identifiers.
/// </summary>
public static class SingleTokenLexicon
{
    private static readonly string[] Symbols = new string[]
    {
        // ── Basic PUA block (U+E000–U+E013) ───────────────────────────────
        "\uE000",
        "\uE001",
        "\uE002",
        "\uE003",
        "\uE004",
        "\uE005",
        "\uE006",
        "\uE007",
        "\uE008",
        "\uE009",
        "\uE00A",
        "\uE00B",
        "\uE00C",
        "\uE00D",
        "\uE00E",
        "\uE00F",
        "\uE010",
        "\uE011",
        "\uE012",
        "\uE013",

        // ── Supplemental PUA (U+E014–U+E027) ──────────────────────────────
        "\uE014",
        "\uE015",
        "\uE016",
        "\uE017",
        "\uE018",
        "\uE019",
        "\uE01A",
        "\uE01B",
        "\uE01C",
        "\uE01D",
        "\uE01E",
        "\uE01F",
        "\uE020",
        "\uE021",
        "\uE022",
        "\uE023",
        "\uE024",
        "\uE025",
        "\uE026",
        "\uE027",

        // ── Extended PUA-A (U+E028–U+E03B) ────────────────────────────────
        "\uE028",
        "\uE029",
        "\uE02A",
        "\uE02B",
        "\uE02C",
        "\uE02D",
        "\uE02E",
        "\uE02F",
        "\uE030",
        "\uE031",
        "\uE032",
        "\uE033",
        "\uE034",
        "\uE035",
        "\uE036",
        "\uE037",
        "\uE038",
        "\uE039",
        "\uE03A",
        "\uE03B",

        // ── Supplementary PUA-A (U+E04C–U+E05B) ──────────────────────────
        "\uE04C",
        "\uE04D",
        "\uE04E",
        "\uE04F",
        "\uE050",
        "\uE051",
        "\uE052",
        "\uE053",
        "\uE054",
        "\uE055",
        "\uE056",
        "\uE057",
        "\uE058",
        "\uE059",
        "\uE05A",
        "\uE05B",

        // ── Ideographic Symbols (U+E080–U+E093) ───────────────────────────
        "\uE080",
        "\uE081",
        "\uE082",
        "\uE083",
        "\uE084",
        "\uE085",
        "\uE086",
        "\uE087",
        "\uE088",
        "\uE089",
        "\uE08A",
        "\uE08B",
        "\uE08C",
        "\uE08D",
        "\uE08E",
        "\uE08F",
        "\uE090",
        "\uE091",
        "\uE092",
        "\uE093",

        // ── Variation Selectors (U+E100–U+E113) ────────────────────────────
        "\uE100",
        "\uE101",
        "\uE102",
        "\uE103",
        "\uE104",
        "\uE105",
        "\uE106",
        "\uE107",
        "\uE108",
        "\uE109",
        "\uE10A",
        "\uE10B",
        "\uE10C",
        "\uE10D",
        "\uE10E",
        "\uE10F",
        "\uE110",
        "\uE111",
        "\uE112",
        "\uE113",
    };

    /// <summary>
    /// Total number of single-token symbols available in the lexicon.
    /// </summary>
    public static int Count => Symbols.Length;

    /// <summary>
    /// Returns the single-token symbol at the given <paramref name="index"/>.
    /// Wraps around modulo <see cref="Count"/> if the index exceeds the array bounds.
    /// </summary>
    public static string GetSymbol(int index)
    {
        if (Count == 0)
            throw new InvalidOperationException("SingleTokenLexicon contains no symbols.");

        return Symbols[((index % Count) + Count) % Count];
    }
}
