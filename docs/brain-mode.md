# Brain Mode: Universal LLM-Optimized Compression

Brain Mode is gc's specialized pipeline for slashing token counts while preserving semantic meaning for LLMs. It combines universal syntax minification with adaptive, project-specific symbol substitution.

## Architecture Overview

The Brain Mode pipeline follows a strict sequence to ensure maximum compression and structural integrity:

```
Source Files
    |
    v
[1] Markdown Generation -- Aggregates files into a single document
    |
    v
[2] Dynamic BPE Fallback -- Replaces long, repeated identifiers (only if sqz is absent)
    |
    v
[3] Universal Minifier  -- Strips comments and collapses whitespace
    |
    v
Final LLM-Optimized Output
```

## Stage 1: Dynamic BPE (Byte Pair Encoding)

When the external `sqz` tool is not available, gc uses its built-in dynamic compressor. Unlike static dictionaries that replace common keywords (which are already 1 token for most LLMs), gc's dynamic compressor targets **project-specific identifiers** and **long repeated strings**.

### How it works:
1. **Identifier Extraction**: Scans the document for identifiers >= 8 characters.
2. **ROI Scoring**: Calculates the Net Token Savings (NTS) for each candidate:
   `NTS = (OriginalTokens * Frequency) - (LegendOverhead + SymbolTokens * Frequency)`
3. **Symbol Substitution**: Top candidates are replaced with single-token Unicode symbols (e.g., `Α`, `β`, `∑`).
4. **Legend Generation**: A `# GC_DICT` header is added to the output so the LLM can map symbols back to their original names.

## Stage 2: Universal Minifier (BrainCrusher)

The minifier works across all programming languages to remove "token noise" without destroying code logic.

### Comment Stripping
Handled by a robust state machine that recognizes:
- Single-line: `//`, `#`, `--`
- Multi-line: `/* ... */`, `<!-- ... -->`
- Literals: `""" ... """`, `''' ... '''`
- Boundary safety: Ensures markers inside strings/chars are not stripped.

### Whitespace Collapse
- Multiple spaces are reduced to a single space.
- Blank lines are removed.
- Significant indentation is preserved (line breaks are kept between statements).

## Integration with sqz

When `--compress` is used with `sqz`, the Dynamic BPE stage is skipped. `sqz` provides superior structural compression and session-aware deduplication. The pipeline becomes:
`Raw Markdown -> Minification -> sqz Compression`.

## Why this is "Perfect":
- **Language Agnostic**: No language-specific "magic" that can break in unexpected environments.
- **Guaranteed ROI**: Symbols are only replaced if the math proves it saves tokens.
- **LLM-Friendly**: Symbols are chosen from Unicode ranges that major LLMs (GPT-4, Claude, Gemini) recognize as single, distinct tokens.

## Common Pitfalls Avoided:
- **No Double Compression**: Minification runs AFTER dynamic substitution to ensure symbols are not accidentally treated as comment markers.
- **Safe Word Boundaries**: Substitutions only occur on full identifier boundaries to avoid partial matches (e.g., replacing `List` inside `ArrayList`).
- **Markdown Preservation**: Headers, file paths, and fences are never modified, ensuring the structural context remains intact.
