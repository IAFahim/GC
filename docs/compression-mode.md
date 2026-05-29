# GC Compression & Brain Modes

GC offers aggressive options for reducing the token overhead of large codebases before feeding them into an LLM context window.

## Compression Mode (`-c`, `--compress`)
Standard compression mode invokes the highly optimized, native SQZ binary algorithm. 

**Features:**
- Designed specifically for code models.
- Operates using a heavily optimized Byte-Pair Encoding (BPE) variant mapped dynamically to common syntax keywords, standard library calls, and structural braces.
- Fast streaming path with minimal memory overhead.
- Safe to use universally as it acts as a reversible (lossless) encoding for the LLM.

## Brain Mode (`-b`, `--brain`)
Brain Mode is a lossy, context-aware semantic compression pipeline designed for situations where file sizes vastly exceed the LLM's capacity, and structural understanding is prioritized over syntactical perfection.

**Features:**
- **Language-Aware Comment Stripping:** Intelligently removes comments (`//`, `/*`, `--`, `#`) without removing critical markdown boundaries or string literals. 
- **Dynamic Compressor (BPE-Style):** Uses the `SuffixArray` phrase detection algorithm in $O(N)$ time to detect the highest-ROI repetitive phrases unique to the current codebase (e.g. repeated lengthy function calls or namespace declarations) and replaces them with an auto-generated token dictionary mapping appended to the top of the output.
- **Minification:** Aggressively removes extraneous whitespace, empty lines, and redundant line breaks while maintaining readability.

**When to use which?**
- Use `-c` (SQZ Compression) for standard, dense reduction that preserves absolute syntax correctness.
- Use `-b` (Brain Mode) when you need to pack as much architectural structure into the LLM as mathematically possible, and you do not require the LLM to reproduce the exact comments or spacing of the source file.