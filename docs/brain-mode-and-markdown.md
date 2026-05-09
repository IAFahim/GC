# Brain Mode v1 (Static) and Markdown Output

> **Note**: Brain Mode v2 replaces the static keyword dictionary with dynamic, project-specific identifier compression. See [brain-mode-v2-dynamic-compression.md](brain-mode-v2-dynamic-compression.md) for the new architecture.

This document explains how gc builds markdown context output, how Brain Mode v1 works, and how the dynamic compression layer fits into the pipeline.

## Overview

gc produces a markdown file from source files, then optionally compresses the code blocks for LLM consumption.

The pipeline is:

1. Collect files
2. Generate markdown
3. If Brain Mode is enabled, compress only code blocks inside fenced code sections
4. Apply dynamic compression to the full markdown output
5. Write the final markdown to disk or clipboard

Important rules:
- Only code inside ``` fences is crushed
- Headers, file paths, and markdown structure are preserved
- The output must remain readable by a human or an AI agent using the legends at the top
- No emoji should appear in compressed output

## Brain Mode

Brain Mode reduces token cost by replacing common C# keywords and patterns with short symbols.

### Static token dictionary

BrainCrusher maps common C# keywords to short tokens such as:
- `public` -> `!1`
- `class` -> `!e`
- `static` -> `!5`
- `void` -> `!l`
- `int` -> `%2`

It uses a trie for longest-match keyword replacement and a reverse map for decompression.

### What Brain Mode does not touch

Brain Mode does not modify:
- file paths
- markdown headings
- non-code prose
- code fence markers

This boundary protection is critical. If the markdown structure is altered, downstream AI tools can no longer reliably decode the output.

## Dynamic Compression

Dynamic compression runs after the raw markdown has been generated, but before static BrainCrusher token replacement is finalized in the output file.

It does three things:

1. Removes noisy attributes and emoji from code blocks
2. Detects repeated high-value tokens and phrases
3. Replaces them with short dynamic symbols such as `_a`, `_b`, `~1`, `~2`

The dynamic compression legend is emitted at the top of the output so a reader can reverse the substitutions.

### Why the order matters

Dynamic compression must see readable source text, not already-compressed BrainCrusher tokens.

Correct order:

`raw markdown -> dynamic compression -> BrainCrusher -> final markdown`

If this order is reversed, the dynamic legend becomes full of compressed symbols like `!k` and `!1`, which makes the output harder to understand and harder to verify.

## Markdown generation rules

`MarkdownGenerator` is responsible for writing the file structure. It should:
- preserve headings and file metadata
- apply crushing only inside fenced code blocks
- keep code fence boundaries intact
- preserve multiline markdown text outside code blocks

This means the markdown generator is the boundary between raw source and compressed code.

## Legends

The output may include two legends:

1. Dynamic compression legend
2. Brain token dictionary legend

These legends must stay single-line or clearly line-broken in a way that does not corrupt the markdown structure. Newlines inside legend entries are not allowed.

## Common pitfalls

1. Crushing headers or paths instead of code blocks
   - Fix: only apply compression inside fenced code sections.

2. Compressing dynamic data after BrainCrusher
   - Fix: dynamic compression must run on raw text.

3. Letting newline-containing phrases enter the legend
   - Fix: reject phrases containing `\n` or `\r` and sanitize legend entries.

4. Stripping `array[i]` as if it were an attribute
   - Fix: attribute stripping must only trigger at statement start or after whitespace.

5. Breaking output readability with over-aggressive replacements
   - Fix: preserve markdown structure and keep legends decodable.

## Verification checklist

- [ ] Code fences still exist in the final markdown
- [ ] Headers and file paths are unchanged
- [ ] Only code inside fenced blocks is compressed
- [ ] Dynamic legend is present and readable
- [ ] Brain token dictionary is present and readable
- [ ] No newline-containing legend entries
- [ ] No emoji appears in compressed output
- [ ] The output can be decoded by a subagent or human using the legends

## Implementation notes

Relevant files:
- `src/gc.Application/Services/BrainCrusher.cs`
- `src/gc.Application/Services/DynamicCompressor.cs`
- `src/gc.Application/Services/MarkdownGenerator.cs`
- `src/gc.Application/UseCases/GenerateContextUseCase.cs`

Relevant behaviors:
- `BrainCrusher` is the static keyword compressor
- `DynamicCompressor` is the adaptive phrase/token compressor
- `MarkdownGenerator` applies crushing only inside code fences
- `GenerateContextUseCase` orchestrates the final output pipeline

## Suggested test cases

- Code fence content is compressed, but headings are not
- File paths remain unchanged
- Dynamic legend and Brain legend are both present
- Indexers like `array[i]` are not stripped
- Newline-containing phrases are rejected from the legend
- Brain Mode output can be decoded back into valid C#