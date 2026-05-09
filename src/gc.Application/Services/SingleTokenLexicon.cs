namespace gc.Application.Services;

/// <summary>
/// Curated list of Unicode characters known to be single tokens in major LLM tokenizers
/// (GPT-4, Claude, etc.). Used by the compression pipeline as replacement symbols that
/// are guaranteed not to merge with surrounding ASCII text.
///
/// Symbol categories (in order):
///   0–19  : Greek uppercase letters
///  20–39  : Greek lowercase letters
///  40–59  : Cyrillic uppercase letters
///  60–79  : Cyrillic lowercase letters
///  80–99  : Mathematical / miscellaneous symbols
/// 100–119 : Arrows, box-drawing, geometric shapes, and supplemental symbols
/// </summary>
public static class SingleTokenLexicon
{
    private static readonly string[] Symbols = new string[]
    {
        // ── Greek uppercase (0–19) ──────────────────────────────────────
        "Α",  // U+0391  Alpha
        "Β",  // U+0392  Beta
        "Γ",  // U+0393  Gamma
        "Δ",  // U+0394  Delta
        "Ε",  // U+0395  Epsilon
        "Ζ",  // U+0396  Zeta
        "Η",  // U+0397  Eta
        "Θ",  // U+0398  Theta
        "Ι",  // U+0399  Iota
        "Κ",  // U+039A  Kappa
        "Λ",  // U+039B  Lambda
        "Μ",  // U+039C  Mu
        "Ν",  // U+039D  Nu
        "Ξ",  // U+039E  Xi
        "Ο",  // U+039F  Omicron
        "Π",  // U+03A0  Pi
        "Ρ",  // U+03A1  Rho
        "Σ",  // U+03A3  Sigma
        "Τ",  // U+03A4  Tau
        "Υ",  // U+03A5  Upsilon

        // ── Greek lowercase (20–39) ─────────────────────────────────────
        "α",  // U+03B1  alpha
        "β",  // U+03B2  beta
        "γ",  // U+03B3  gamma
        "δ",  // U+03B4  delta
        "ε",  // U+03B5  epsilon
        "ζ",  // U+03B6  zeta
        "η",  // U+03B7  eta
        "θ",  // U+03B8  theta
        "ι",  // U+03B9  iota
        "κ",  // U+03BA  kappa
        "λ",  // U+03BB  lambda
        "μ",  // U+03BC  mu
        "ν",  // U+03BD  nu
        "ξ",  // U+03BE  xi
        "ο",  // U+03BF  omicron
        "π",  // U+03C0  pi
        "ρ",  // U+03C1  rho
        "σ",  // U+03C3  sigma
        "τ",  // U+03C4  tau
        "υ",  // U+03C5  upsilon

        // ── Cyrillic uppercase (40–59) ──────────────────────────────────
        "Ж",  // U+0416  Zhe
        "З",  // U+0417  Ze
        "И",  // U+0418  I
        "Й",  // U+0419  Short I
        "К",  // U+041A  Ka
        "Л",  // U+041B  El
        "М",  // U+041C  Em
        "Н",  // U+041D  En
        "П",  // U+041F  Pe
        "Р",  // U+0420  Er
        "С",  // U+0421  Es
        "Т",  // U+0422  Te
        "У",  // U+0423  U
        "Ф",  // U+0424  Ef
        "Х",  // U+0425  Ha
        "Ц",  // U+0426  Tse
        "Ч",  // U+0427  Che
        "Ш",  // U+0428  Sha
        "Щ",  // U+0429  Shcha
        "Ъ",  // U+042A  Hard sign

        // ── Cyrillic lowercase (60–79) ──────────────────────────────────
        "ж",  // U+0436  zhe
        "з",  // U+0437  ze
        "и",  // U+0438  i
        "й",  // U+0439  short i
        "к",  // U+043A  ka
        "л",  // U+043B  el
        "м",  // U+043C  em
        "н",  // U+043D  en
        "п",  // U+043F  pe
        "р",  // U+0440  er
        "с",  // U+0441  es
        "т",  // U+0442  te
        "у",  // U+0443  u
        "ф",  // U+0444  ef
        "х",  // U+0445  ha
        "ц",  // U+0446  tse
        "ч",  // U+0447  che
        "ш",  // U+0448  sha
        "щ",  // U+0449  shcha
        "ъ",  // U+044A  hard sign

        // ── Mathematical / miscellaneous symbols (80–99) ────────────────
        "Ω",  // U+03A9  Omega
        "Φ",  // U+03A6  Phi
        "Ψ",  // U+03A8  Psi
        "⋐",  // U+22D0  Double subset
        "∑",  // U+2211  N-ary summation
        "∏",  // U+220F  N-ary product
        "∞",  // U+221E  Infinity
        "∫",  // U+222B  Integral
        "√",  // U+221A  Square root
        "≈",  // U+2248  Almost equal to
        "≠",  // U+2260  Not equal to
        "≤",  // U+2264  Less-than or equal to
        "≥",  // U+2265  Greater-than or equal to
        "∂",  // U+2202  Partial differential
        "∇",  // U+2207  Nabla
        "∈",  // U+2208  Element of
        "∀",  // U+2200  For all
        "∃",  // U+2203  There exists
        "⊕",  // U+2295  Circled plus
        "⊗",  // U+2297  Circled times

        // ── Arrows, box-drawing, geometric shapes, supplemental (100–119)
        "→",  // U+2192  Rightwards arrow
        "←",  // U+2190  Leftwards arrow
        "↑",  // U+2191  Upwards arrow
        "↓",  // U+2193  Downwards arrow
        "↔",  // U+2194  Left right arrow
        "⇒",  // U+21D2  Rightwards double arrow
        "⇐",  // U+21D0  Leftwards double arrow
        "─",  // U+2500  Box drawings light horizontal
        "│",  // U+2502  Box drawings light vertical
        "┌",  // U+250C  Box drawings light down and right
        "┐",  // U+2510  Box drawings light down and left
        "└",  // U+2514  Box drawings light up and right
        "┘",  // U+2518  Box drawings light up and left
        "◆",  // U+25C6  Black diamond
        "●",  // U+25CF  Black circle
        "■",  // U+25A0  Black square
        "▲",  // U+25B2  Black up-pointing triangle
        "▼",  // U+25BC  Black down-pointing triangle
        "◉",  // U+25C9  Fisheye
        "◎",  // U+25CE  Bullseye
    };

    /// <summary>
    /// Total number of single-token symbols available in the lexicon.
    /// </summary>
    public static int Count => Symbols.Length;

    /// <summary>
    /// Returns the single-token symbol at the given <paramref name="index"/>.
    /// Wraps around modulo <see cref="Count"/> if the index exceeds the array bounds,
    /// so callers never need to bounds-check.
    /// </summary>
    /// <param name="index">Zero-based index into the symbol table.</param>
    /// <returns>A single Unicode character guaranteed to be a standalone token.</returns>
    public static string GetSymbol(int index)
    {
        if (Count == 0)
            throw new InvalidOperationException("SingleTokenLexicon contains no symbols.");

        return Symbols[((index % Count) + Count) % Count];
    }
}
