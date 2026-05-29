# gc — Review & Remediation Plan

Scope: correctness, performance, and structure. Ranked by impact. Each item is `path` → problem → fix.

The unifying defect: features were added by **copying a branch and tweaking it**, and abstractions were added **without wiring them in**. The result is multiple sources of truth (two mergers, two lexers, two default-configs, two token heuristics, two binary detectors) and a large dead surface. The fixes below collapse those into single irreducible definitions and make the output pipeline compose instead of branch.

---

## 0. Critical correctness bugs (data corruption / wrong results)

- [x] **Markdown fence is under-counted → premature fence close.** `gc.Application/Services/MarkdownGenerator.cs`
  `fence = "```"; if Contains("`````") → 10; else if Contains("````") → 6`. Content containing a run of **exactly three** backticks is never escaped (stays at 3), and any embedded fenced block (every README, every doc with code) closes the wrapper early.
  Also: for streamed files the run is detected from `sample = first 4096 bytes` only — backticks past 4 KB are missed.
  Fix: `fenceLen = max(3, longestBacktickRun(content) + 1)` over the **whole** content, one definition shared by both the in-memory and streamed paths.

- [ ] **Brain mode corrupts source it claims to preserve.** `gc.Application/Services/BrainCrusher.cs`
  The output header asserts "contains the full source code … use the ORIGINAL full identifiers." Brain mode then:
  - Treats `#` as a line comment for any extension not in `NonHashCommentExtensions`, and for `null` extension (the whole-document crush path). So **C/C++ `#include`/`#define`/`#ifdef`, C# `#region`/`#if`/`#nullable`, shell shebangs** are deleted.
  - Treats `//` as a comment in **every** language unconditionally → unquoted URLs in Markdown/text/YAML are truncated at `https://`.
  - `CollapseWhitespace` is string-unaware → multi-space runs inside string literals (SQL, format strings, regex) collapse to one space.
  - `NonHashCommentExtensions` membership is wrong: `dockerfile`/`makefile`/`gemfile`/`rakefile` use `#` comments but are listed as *not* hash-comment, so their comments survive; `yaml`/`toml`/`ini` comments survive too.
  Fix: drive all comment/string handling from **one** `CommentSyntax` table keyed by language (line-comment tokens, block-comment delims, string/raw-string/char delims). Preserve string interiors verbatim. Delete the ad-hoc heuristics. This table becomes the single source of truth shared with the lexer (see §3).

- [ ] **Path filtering matches substrings, not path components.** `gc.Application/Services/FileFilter.cs` (`IsValidPath` via `SearchValues.ContainsAny`) + `gc.Domain/Constants/BuiltInPresets.cs` (`SystemIgnoredPatterns`).
  `normalizedSpan.ContainsAny(excludeSearchValues)` is raw substring containment. `bin/` matches `src/robin/x.cs`; `.key` matches `monkey-config.cs` only if it has the dot, but `foo.keyboard.cs` is excluded; `secrets`/`.env`/`credentials` match any path containing them.
  Fix: model ignores as a typed sum — `ExtensionRule | PathSegmentRule | GlobRule` — and match each accordingly (segment equality on split path, extension on the last segment, glob via the matcher). One `IgnoreSet` used by both discovery modes.

- [ ] **Config files silently lose `Filters.*PathPatterns` and `Filters.*ContentPatterns`.** `gc.Infrastructure/Configuration/ConfigurationLoader.cs`
  `MergeConfiguration` calls the **private** `MergeFilters`, which copies only `SystemIgnoredPatterns` + `AdditionalExtensions`. The complete `ConfigurationMerger.MergeFilters` exists but is bypassed for this field. So exclude/include path & content patterns defined in `.gc/config.json` never take effect (only the CLI flags work).
  Fix: delete the private merge methods in `ConfigurationLoader`; route every field through `ConfigurationMerger`. (See §4 — there are two full merger copies.)

- [ ] **`--validate-config` exits 0 on an invalid config.** `gc.CLI/Program.cs` + `gc.Application/Services/ConfigurationService.cs`
  `ValidateConfig` returns `Result<ValidationResult>.Success(...)` whenever the config isn't null; `Program` keys the exit code off `Result.IsSuccess`, ignoring `ValidationResult.IsValid`. CI cannot detect a bad config.
  Fix: exit code = `validationResult.Value.IsValid ? 0 : 1`.

- [ ] **`--profile-json <file>` (without `--profile`) does nothing.** `gc.CLI/Services/CliParser.cs` + `gc.CLI/Program.cs`
  `TryGetNewState` matches `--profile-json` first and `continue`s, so the `IsFlag` branch that sets `profile=true` is unreachable. `needsReporter = Profile || ShowStats || !empty(StatsOutput)` — it checks `StatsOutput` but **not** `ProfileOutput`. Reporter is never created; the JSON is never written. (`--json-stats` works only by accident because `StatsOutput` is in the predicate.)
  Fix: include `!string.IsNullOrEmpty(ProfileOutput)` in `needsReporter`, and remove the dead/unreachable `IsFlag` entries. Better: collapse flag-vs-state duplication (see §6).

- [ ] **Cluster file sizes are wrong → broken shard balancing.** `gc.Application/UseCases/GenerateContextUseCase.cs` (`ExecuteClusterAsync`) + `gc.Application/Services/FileFilter.cs` (`CreateFileEntry`).
  In cluster mode `FilterFiles` is called without `rootPath`, so `CreateFileEntry` computes `new FileInfo(relativePathWithinRepo).Length` against the process CWD, not the repo root → file-not-found → `Size = -1`. The later `e with { … }` reprojects paths but not `Size`. Shard balancing then bins `-1`s (effectively round-robin), and reported sizes are wrong.
  Fix: see §2 — stat once at discovery using a real absolute path; make `FileEntry` carry root + relative so size is always computed against the right base.

- [ ] **`ConfigurationService` serializes with reflection and a divergent hand-written default.** `gc.Application/Services/ConfigurationService.cs`
  `WriteDefaultConfigAsync` serializes an **anonymous object** and `DumpConfig` uses `JsonSerializer.Serialize(config, options)` — both reflection-based, while the loader uses the source-gen `GcJsonSerializerContext`. Under AOT/trimming this throws. The hand-written default also disagrees with `BuiltInPresets.GetDefaultConfiguration()` (`"## File: {path}"` vs `"{path}"`, trailing-space header, missing sections).
  Fix: serialize `BuiltInPresets.GetDefaultConfiguration()` through the source-gen context. One canonical default, one serializer.

- [ ] **`sqz` integration can deadlock on large input.** `gc.Application/Services/SqzCompressionService.cs`
  `CompressAsync` writes the entire stdin, closes it, **then** begins reading stdout. If `sqz` streams output while still reading input, its stdout pipe (~64 KB) fills, `sqz` blocks on write, gc blocks on the remaining stdin write → deadlock. Large repos are exactly the trigger.
  Fix: start the stdout/stderr read tasks **before** writing stdin; `await Task.WhenAll(write+close, readStdout, readStderr)`.

- [ ] **Windows `clip.exe` mangles the brain-mode PUA symbols.** `gc.Infrastructure/System/ClipboardService.cs` + `SingleTokenLexicon.cs`
  `CopyToWindowsAsync` tries `clip.exe` first; the PowerShell fallback only runs if clip "fails," but clip succeeds while truncating/garbling non-ANSI text — i.e. the `\uE000…` private-use symbols. Brain-mode clipboard output is corrupted on Windows.
  Fix: prefer the PowerShell `Set-Clipboard` path (UTF-8/UTF-16 safe) on Windows, or write the bytes to the clipboard via the Win32 API directly.

- [ ] **`SingleTokenLexicon` assumes PUA codepoints are one token — unverified.** `gc.Application/Services/SingleTokenLexicon.cs`
  Private-use codepoints are 3 bytes in UTF-8 and are tokenizer-dependent; they can become `<unk>`, split into multiple tokens, or collide with PUA already present in source. The whole token-savings value prop rests on this.
  Fix: measure against the actual target tokenizer; pick symbols that are verified single tokens; guard against source already containing chosen symbols (skip or escape). Treat the savings number as measured, not estimated (see determinism note in §5).

---

## 1. Performance (you said this is the priority)

- [ ] **Content "glob" filter is O(n²) on the common case.** `gc.Application/Services/ContentFilter.cs` (`GlobContains`)
  For a pattern with no fixed prefix/suffix (e.g. `*TODO*`), the fallback loops `for len = content.Length downTo 1` calling `GlobMatcher.IsMatch(content[0..len], pattern)`. When the keyword is **absent** (most files), every iteration is an O(n) match → O(n²) per file. And content filters are described as "files containing this keyword" — they should be plain substring tests, not globs over file bytes.
  Fix: treat `--exclude-content`/`--include-content` as literal substrings. Feed them straight into Aho-Corasick (`AhoCorasickContainsAny`, already present) for a single O(n) multi-pattern pass. Drop `GlobContains` entirely.

- [ ] **`DynamicCompressor` is the throughput floor and runs on the full output.** `gc.Application/Services/SuffixArray.cs` + `DynamicCompressor.cs`
  - `SuffixArray.Build` uses prefix-doubling with `Array.Sort(sa, comparator)` → O(n log² n) with a delegate call per comparison. On multi-MB concatenated code this dominates.
  - `ExtractMaximalPhrases` then recomputes each candidate's frequency via `CountOccurrences` (O(n) each) → O(unique·n).
  - Substring de-dup is `foreach filtered { Contains(...) }` → O(candidates²).
  Fix: replace the comparator sort with a radix/bucket doubling (or SA-IS); derive occurrence counts from LCP intervals instead of re-scanning; bound candidate count before the de-dup. Consider gating DynamicCompressor by input size / making it opt-in for very large inputs.

- [ ] **Three `stat`s per file.** `FileFilter.CreateFileEntry` (`FileInfo.Length`) + `MarkdownGenerator` (`FileInfo` existence/length) + `RandomAccess.GetLength`.
  Fix: stat once during discovery, carry `Size`/exists in `FileEntry`, and have the generator open the handle and read length from the handle it already holds.

- [ ] **`ShardSplitter` recomputes bucket sums in inner loops.** `gc.Application/Services/ShardSplitter.cs`
  `shardBuckets[i].Sum(e => e.Size)` is called inside the per-entry placement loop → quadratic in file count. `MergeSmallGroups` is also wasted work: `AssignBySize` flattens every group into individual files (`SelectMany`) and re-bin-packs, discarding the grouping it just built.
  Fix: keep a running `long[] bucketSizes`; update on each placement. Delete `MergeSmallGroups` (it has no effect) or actually preserve module affinity in the size path.

- [ ] **Transient RAM can exceed the configured limit.** `gc.Application/Services/MarkdownGenerator.cs`
  The `maxMemoryBytes` check bounds **output** size, not working set. The reorder buffer can hold up to `~2 × ProcessorCount` rented buffers of up to 10 MB each (`2·P·10 MB`). On a 32-core box that's ~640 MB before the limit ever triggers.
  Fix: bound the in-flight bytes against the memory budget (track rented bytes, backpressure the producer), not just the emitted byte count.

- [ ] **Delegate-per-token lexing.** `gc.Application/Services/CodeLexer.cs` (`Enumerate(Action<ReadOnlySpan<char>>)`)
  One virtual call per identifier in a hot loop.
  Fix: expose a ref-struct enumerator (`while (lexer.MoveNext()) { var span = lexer.Current; … }`) so the caller pulls spans with no delegate dispatch.

- [ ] **LINQ in hot paths.** `ShardSplitter`, `DynamicCompressor`, parts of `GenerateContextUseCase`.
  `OrderBy/Sum/Select/SelectMany` allocate iterators/closures per file. Replace with explicit loops + preallocated arrays in the per-file paths.

- [ ] **`[MethodImpl(AggressiveInlining)]` on large methods.** `FileFilter.IsValidPath`, `ContentFilter.MatchesAnyPattern`, `GlobMatcher.IsMatch`/`MatchesAny`.
  These exceed the JIT inlining budget; the attribute is a no-op at best, counterproductive at worst (bloats callers it does inline). Keep it only on the genuinely tiny helpers (`IsIdStart`, `IsDigit`, `Goto`).

- [ ] **`FrequencyAnalyzer` (if it gets used) ignores discovery/ignores.** `gc.Application/Services/FrequencyAnalyzer.cs`
  `Directory.EnumerateFiles(root, "*.cs", AllDirectories)` walks `node_modules`, `bin`, `obj`, `.git`. Currently dead (see §7) — either delete or route it through the real discovery+ignore path.

---

## 2. Make `FileEntry` honest (root cause of several bugs)

- [ ] `gc.Domain/Models/FileEntry.cs`: the field named `AbsolutePath` frequently holds a **relative** path (git mode sets `AbsolutePath = path` where `path` is repo-relative). The naming lies; `FileInfo(AbsolutePath)` then resolves against CWD and breaks cluster sizing.
  Fix:
  ```
  FileEntry(Root, Relative, Extension, Language, Size)
  Absolute => Path.GetFullPath(Path.Combine(Root, Relative))
  Display  => optional override
  ```
  Compute `Size` once at discovery against `Absolute`. Replace the `[`-prefixed sentinel paths (see §3) so `Path` never carries a fake value.

---

## 3. Model the output pipeline as composition (kills the duplication)

- [ ] **Synthetic entries are smuggled as fake paths starting with `[`.** `GenerateContextUseCase` and `MarkdownGenerator` both branch on `Entry.Path.StartsWith('[')`.
  Fix: a typed item — `OutputItem = SourceItem(FileEntry) | MarkerItem(string text)`. Pattern-match it. No string sentinels, no `StartsWith('[')` anywhere.

- [ ] **`WriteOutputAsync` is a 6-way matrix of near-identical blocks** (`brain × compress × {file,clipboard}`), ~300 lines, with the same generate→transform→emit logic copied each time.
  The interface for this already exists and is **unused**: `IOutputTransform` / `ICompressTransform` / `ILegendlessTransform` in `gc.Domain/Interfaces/IOutputTransform.cs`, plus the adapters in `gc.Application/Adapters/OutputTransformAdapters.cs`.
  Fix: define the run as data, then fold:
  ```
  pipeline : IReadOnlyList<ITransform>      // e.g. [] | [Crush] | [Dynamic, Crush] | [Crush, Sqz]
  sink     : Sink = FileSink(path, append, safe) | ClipboardSink(append)
  output   = pipeline.Aggregate(rawMarkdown, (acc, t) => t.Apply(acc))
  await sink.Emit(output)
  ```
  Each transform is total and referentially transparent; adding a transform is one list element, not a new branch. This is the "alive by composition / deletion shrinks it" shape you specified.

- [ ] **`LlmContextHeader` is applied inconsistently.** Present on compress paths, absent on the brain-only (dynamic) path. With the pipeline model the header becomes one transform applied uniformly (or never), not a per-branch string concat.

- [ ] **One comment/lex engine, not two.** `BrainCrusher.StripComments` and `CodeLexer`/`CodeLexerOptions` are separate implementations of the same state machine with **different** language rules (and the crusher's are buggy — see §0). `CodeLexer` + `CodeLexerOptions.ForLanguage` are otherwise unused.
  Fix: one `CommentSyntax`/`LanguageProfile` table → one lexer that both the crusher and any analyzer consume. Delete the loser.

---

## 4. One configuration merger, one default

- [ ] `gc.Infrastructure/Configuration/ConfigurationLoader.cs` defines private `MergeLimits/MergeDiscovery/MergeCluster/MergePresets/MergeLanguageMappings/MergeMarkdown/MergeOutput/MergeLogging/MergeFilters`, while `gc.Infrastructure/Configuration/ConfigurationMerger.cs` defines a parallel **complete** set. The loader uses a *mix* (mostly `ConfigurationMerger.*`, but the buggy private `MergeFilters`), and the private `MergeDiscovery` (missing `MaxDepth`) is dead.
  Fix: delete every private merge method in the loader; call `ConfigurationMerger.Merge` for the whole object. This also fixes §0's dropped filter patterns.

- [ ] Two definitions of memory-size parsing/validation: `gc.Domain/Common/MemorySizeParser.cs` vs `ConfigurationValidator.ValidateMemorySize`. Collapse to one (parser returns `Result<long>`; validator reuses it).

- [ ] `GcJsonSerializerContext` lists most config types explicitly but not `PerformanceConfiguration` (reachable from `GcConfiguration`). The generator traverses reachable types so it likely works, but the partial explicit list is misleading. Either list all or list none and rely on traversal — pick one rule. Verify `performance` round-trips.

---

## 5. Determinism & robustness

- [ ] **Non-deterministic output in brain mode.** `gc.Application/Services/DynamicCompressor.cs` + `SuffixArray.cs`
  Symbol assignment (`refinedMap.OrderByDescending(k => k.Key.Length * k.Value)`) and legend emission (`foreach (… in symbolMap)`) depend on `Dictionary` enumeration order, which is implementation-defined. Equal-score phrases can map to different symbols across runtimes → byte-different output. Violates "deterministic always."
  Fix: add a total tiebreaker (`.ThenBy(k => k.Key, StringComparer.Ordinal)`) and emit the legend from an ordered sequence, not a dictionary.

- [ ] **Token reporting uses two different heuristics.** `bytes / 4` in `GenerateContextUseCase` vs `TokenEstimator` for `--count`. Pick one estimator and reuse it everywhere; label it as an estimate.

- [x] **`MarkdownGenerator` early-return leaks the producer.** On the output-size-exceeded path it `return`s from inside the consumer loop without `await generateTask`, without `writer.CompleteAsync()`, and without returning the buffers still parked in `outOfOrderBuffer`. The `Parallel.ForEachAsync` producer then blocks forever on the bounded channel (ct isn't cancelled). Harmless for a one-shot process exit, but a real leak if gc is ever hosted/embedded.
  Fix: cancel the producer, drain+return buffers, complete the writer in a `finally`/`using` around the whole streaming operation.

- [ ] **`AhoCorasick` constructor is partial.** `gc.Application/Services/AhoCorasick.cs`
  Empty `patterns` → `_alphabetSize = 0` → `_gotoFunc = new int[0]` → any later `Goto` throws. Callers guard today, but it's a landmine.
  Fix: make construction total (no-op automaton that matches nothing) or a factory that returns `null`/`Option`.

- [ ] **`HistoryService` load→release→save gap + non-atomic write.** `gc.Infrastructure/System/HistoryService.cs`
  The `SemaphoreSlim` is released between `LoadInternalAsync` and `SaveInternalAsync`, so two concurrent ops (or processes — semaphore is in-process only) can lose entries. The save uses `FileMode.Create` directly; a crash mid-write corrupts history — ironic given `SafeFileWriter` exists.
  Fix: hold the lock across read-modify-write; write via the atomic temp-rename writer; for cross-process use a file lock.

- [ ] **`SafeFileWriter` isn't atomic and over-claims durability.** `gc.Application/Services/SafeFileWriter.cs`
  It does `File.Delete(path)` then `File.Move(tmp, path, overwrite:true)` — the delete is redundant and opens a window where the target is gone if the move fails (you lose the original). `FlushAsync` flushes to OS cache, not disk; there's no fsync, so the "transactional" framing (justifying the `--unsafe-direct-write` flag) is weak.
  Fix: drop the pre-delete (rename is already atomic on the same FS); fsync the temp file and the directory if you want the durability the flag implies.

- [ ] **P/Invoke string marshalling is implicit.** `gc.Application/Native/LinuxFastPath.cs`
  `open(string pathname, …)` relies on default `CharSet.Ansi`→UTF-8 mapping. "No implicit anything": annotate `[MarshalAs(UnmanagedType.LPUTF8Str)] string pathname`. Also `PrewarmAsync`/`Prewarm` don't retry without `O_NOATIME` (which `EPERM`s on files you don't own), unlike the generator's open path — make the fallback consistent.

- [ ] **Pipe-deadlock pattern repeats in git discovery.** `gc.Infrastructure/Discovery/FileDiscovery.cs` (`DiscoverFilesSinceAsync`) reads stdout fully, then waits, then reads stderr. Same fix as sqz: read both streams concurrently.

- [ ] **`--export-schema` resource name.** `gc.CLI/Program.cs` uses `GetManifestResourceStream("schema.json")`. Embedded resources are namespaced unless `<LogicalName>` is set — verify this resolves; otherwise it silently errors.

---

## 6. CLI parser: declarative table instead of a `ref bool` storm

- [ ] `gc.CLI/Services/CliParser.cs`: `ProcessFlag` takes ~25 `ref bool` parameters; flags appear in **both** `IsFlag` and `TryGetNewState` (the source of the `--profile-json` bug). Adding or removing a flag edits 4+ sites — the opposite of "deletion shrinks it."
  Fix: a single `IReadOnlyDictionary<string, OptionSpec>` mapping token → (kind: Flag|Value|Repeated, setter). Parse by table lookup. One place to add a flag, one place to delete it.

- [ ] **Verb aliases collide with real paths.** Positional words `brain`, `compress`, `grab`, `type`, `yeet`, `zap`, `spit`, `horde` are interpreted as flags/states. A directory literally named `type` or `brain` can't be passed as a path without `--`. Either drop the bare-word verbs or require them to be prefixed.

- [ ] **Single-value flag swallows the next flag.** `gc -o --verbose` treats `--verbose` as the output value's *replacement* (the `IsFlag` branch runs before the `state != None` branch and resets state), silently producing an empty output path → falls back to clipboard. A value-expecting state should error if the next token is a flag.

- [ ] `gc.CLI/Models/CliArguments.cs` is a ~50-property god-record. Group into `DiscoveryArgs`, `FilterArgs`, `OutputArgs`, `ModeArgs`. Mirrors the config structure and shrinks the parser surface.

---

## 7. Dead code to delete (deletion must shrink the system)

Each of these is defined but unreachable from the live path. Removing them changes no behavior — that's the test.

- [ ] `gc.Domain/Constants/MagicNumbers.cs` — **entirely unused**; the literals (`4096`, `65536`, `8192`, `10*1024*1024`) are hardcoded everywhere instead. Either reference these constants throughout or delete the file.
- [ ] `gc.Domain/Common/ErrorKind.cs` — `Result.Failure` is called everywhere **without** a `kind`, so every error is `Unknown`. `Category()`/`IsRetryable()` are dead. Wire kinds through at failure sites or delete the enum + extensions.
- [ ] `gc.Application/Adapters/OutputTransformAdapters.cs` + `IOutputTransform`/`ICompressTransform`/`ILegendlessTransform` — defined, never used. **Don't delete — adopt** them as the pipeline in §3 (this is the right abstraction, just unwired).
- [ ] `gc.Application/Services/AhoCorasick.cs` — `ReplaceAll` is unused; `_patterns` and `_reverseCharMap` fields are written but never read; the failure-link computation is pointless for `ReplaceAll` (which restarts from root each position). Keep only the parts `AhoCorasickContainsAny` needs.
- [ ] `gc.Application/Services/ContentFilter.cs` — `UpdatePatterns`, `MatchesWithCache`, the instance `MatchesAnyPattern`, `MatchesAnyPatternStatic`, and the cache fields are all dead (only `CompilePatterns` is live). `MatchesWithCache` is also buggy (uses `excludeExact ?? includeExact` for both checks). Delete; keep `CompilePatterns` + Aho-Corasick.
- [ ] `gc.Application/Services/SuffixArray.cs` — `FindRepeatedPhrases` (and its word/line scanning) is unused; only `ExtractMaximalPhrases` is live.
- [ ] `gc.Application/Services/FrequencyAnalyzer.cs` + likely all of `CodeLexer.cs`/`CodeLexerOptions` — no live callers found. Delete, or fold the lexer into the single engine from §3.
- [ ] `gc.Application/Native/LinuxFastPath.cs` `PrewarmAsync`/`Prewarm` + `PerformanceConfiguration` prewarm fields — configured (`PrewarmEnabled`, merged in `ConfigurationMerger`) but never invoked. Either wire prewarm into discovery or delete the feature and its config.
- [ ] `gc.Application/Services/MarkdownGenerator.cs` — the injected `IFileReader _reader` is **never used** (the class opens files itself); `ProcessLineSequence` is unused; the `IBrainCrusher? brainCrusher` parameter is always `null` in the live path (brain crush happens on the assembled doc). Drop the dependency, the method, and the dead parameter branch.
- [ ] `gc.Application/Services/SafeFileWriter.cs` `WriteAllTextAsync` — live path uses `WriteAllBytesAsync`. Remove if no caller.
- [ ] `gc.Domain/Interfaces/IFileReader.cs` — the `…(…, LimitsConfiguration, …)` overloads and `IsBinaryFileAsync(string)` appear unused; `gc.Domain/Interfaces/IClipboardService.cs` string overloads likewise. Trim to what's called.
- [ ] `gc.Infrastructure/IO/FileReader.cs` — `ReadStreamingAsync` methods are `async` with no `await` (compiler warning) and possibly unused; `IsBinaryStreamAsync` + `IsBinaryFileAsync` duplicate the same byte-scan. One binary detector.

---

## 8. Smaller correctness / consistency

- [x] **Two output formattings for the same content.** `MarkdownGenerator.cs`: the in-memory (`Content != null`) path emits `header / fence / content / fence / ""`; the streamed path emits `header / fence / content / "" / fence / ""` (extra blank line before the closing fence). Output differs by whether content was preloaded. Pick one block layout.
- [ ] **Mixed line endings on Windows.** Wrapper lines use `Environment.NewLine` (CRLF) while file bodies keep their own `\n`. Normalize the wrapper to `\n` for stable, diffable output.
- [ ] **`IBrainCrusher.Uncrush` is a no-op** (returns input). The interface promises reversibility brain mode can't provide. Either remove `Uncrush` from the contract or rename the abstraction to reflect that it's a lossy, one-way minifier.
- [ ] **`HistoryEntry` is a `class`** while every config model is an immutable `record`. Make it a `record` for consistency with the stated "immutable by default."
- [ ] **Filesystem vs git discovery disagree on ignores.** Git mode honors `.gitignore`; filesystem mode uses only a hardcoded dir set. Document the difference or share one ignore model (ties into §0's typed `IgnoreSet`).
- [ ] **`GetFullExtension`/extension matching use `LastIndexOf('.')`** so `foo.tar.gz` → `gz`; a user filter of `tar.gz` never matches. Decide single- vs compound-extension semantics and apply it in both `GetFullExtension` and `IsValidPath`.
- [ ] **`GlobMatcher.IsMatch` fast paths are inconsistent.** The `*.ext` shortcut uses `EndsWith` (matches across `/`), but the general matcher treats single `*` as not crossing `/`. Same pattern, two semantics depending on which branch fires. Collapse the redundant fast paths into one matcher with one defined rule for `/`.

---

## Suggested target structure

Keep the Domain / Application / Infrastructure / CLI split — the layering is fine; the rot is inside the layers. The reshaping:

```
Domain
  Models/        FileEntry(Root, Relative, …)   OutputItem = Source | Marker
  Filtering/     IgnoreRule = Extension | Segment | Glob ; IgnoreSet
  Language/      LanguageProfile  (one CommentSyntax/string table — single source of truth)
  Pipeline/      ITransform (total, pure)        Sink = File | Clipboard
  Common/        Result / Result<T>              (delete ErrorKind or wire it through)

Application
  Discovery → FileEntry stream (size stat'd once)
  Filter    → IgnoreSet + path-component matcher (no substring matching)
  Lex/Crush → one engine over LanguageProfile (string-preserving)
  Transforms→ Crush, DynamicCompress, Sqz  : ITransform
  Generate  → markdown stream from OutputItem (no `[` sentinels, RAM-bounded)
  UseCase   → raw |> pipeline.Fold |> sink   (one path; ~40 lines, no brain×compress×sink matrix)

Infrastructure
  Configuration → ConfigurationMerger only (delete the loader's private duplicates); serialize the canonical default via the source-gen context
  IO            → one FileReader, one binary detector, atomic writer used by every writer (incl. history)
  System        → clipboard prefers UTF-safe path on Windows
  CLI           → declarative OptionSpec table; grouped arg records
```

Acceptance checks for the refactor:
- Deleting any single transform or sink touches exactly one list/sum, never a branch matrix.
- The same content produces byte-identical output across OSes and runtimes (determinism).
- Brain mode preserves string literals and preprocessor directives (no semantic loss), or is explicitly documented as lossy with `Uncrush` removed.
- A `.gc/config.json` with `Filters.excludePathPatterns` actually filters.
- `--validate-config` and `--profile-json` behave as advertised.
- One `stat` per file end-to-end; content filters are O(n) per file.
```
