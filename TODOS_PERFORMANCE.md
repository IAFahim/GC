# PERFORMANCE TODO — gc (Git Copy)

**Goal:** Take gc from "fast" (48ms) to "absurdly fast" (<10ms). C# hackery, OS fuckery, zero-allocation pipelines.

**Current baseline:** Discovery 24ms + Read 24ms = 48ms total (CI benchmark on own repo)

**Post Phase 0:** Discovery 13ms + Read 9ms = 22ms total (2.2x faster)

**Post Phase 1:** Consistent 21ms (reduced GC pressure, zero-alloc hot paths)

**Target:** <10ms total for same workload. Sub-5ms for warm runs.

---

## Phase 0 — Kill The Obvious Crimes (est. 2-3 hours)

These are free wins. Pure negligence in the current code.

### 0.1 ✅ `StreamWriter.AutoFlush = true` — THE SINGLE BIGGEST CRIME

**File:** `src/gc.Application/Services/MarkdownGenerator.cs:28`

**What:** `writer.AutoFlush = true` forces a kernel-level flush syscall on EVERY SINGLE `WriteLineAsync`. For 200 files that's 1000+ syscalls instead of 1.

**Fix:** Remove `AutoFlush = true`. Add a single `await writer.FlushAsync()` at the end (which already exists on line 180). The StreamWriter already has an 8192-byte internal buffer — let it do its job.

**Impact:** 5-20x faster writes. This alone could halve total time.

```csharp
// BEFORE (crime)
using var writer = new StreamWriter(outputStream, Utf8NoBom, bufferSize: 8192, leaveOpen: true);
writer.AutoFlush = true;  // ← KILL THIS

// AFTER
using var writer = new StreamWriter(outputStream, Utf8NoBom, bufferSize: 65536, leaveOpen: true);
// No AutoFlush. Flush once at the end.
```

**Effort:** S — 1 line delete, 1 line change

---

### 0.2 ✅ Double git process spawn in `auto` mode

**File:** `src/gc.Infrastructure/Discovery/FileDiscovery.cs:30-40`

**What:** In `auto` mode, we spawn `git rev-parse --is-inside-work-tree` THEN spawn `git ls-files`. That's two process forks for every single run. Process spawn on Linux is ~2-5ms each.

**Fix:** Skip `IsGitRepositoryAsync` entirely in `auto` mode. Just try `git ls-files` directly. If it fails (exit code != 0), fall back to filesystem. One process instead of two.

```csharp
// BEFORE: 2 process spawns
if (mode == "auto" && await IsGitRepositoryAsync(rootPath, ct))  // spawn 1
{
    var gitFiles = await DiscoverWithGitAsync(rootPath, discoveryConfig, ct);  // spawn 2
    ...
}

// AFTER: 1 process spawn
if (mode == "auto")
{
    var gitFiles = await DiscoverWithGitAsync(rootPath, discoveryConfig, ct);
    if (gitFiles.IsSuccess) return gitFiles;
    // git not available or not a repo — fall through to filesystem
}
```

**Impact:** Save 2-5ms on every run in auto mode (the default).

**Effort:** S — 5 lines

---

### 0.3 ✅ `new FileInfo(path)` in FileFilter for EVERY file

**File:** `src/gc.Application/Services/FileFilter.cs:34` (`CreateFileEntry`)

**What:** `new FileInfo(path)` does a stat() syscall per file. For 500 files that's 500 syscalls just to get file size. And we do it AGAIN later in MarkdownGenerator when we `new FileInfo(content.Entry.Path)`.

**Fix:** Defer FileInfo creation. Don't stat files during filtering — only stat when we actually read them in the generator. Pass size=0 or size=-1 in FileEntry during filtering, and resolve lazily.

```csharp
// BEFORE: stat() per file during filtering
private FileEntry? CreateFileEntry(string path, GcConfiguration config)
{
    var fileInfo = new FileInfo(path);  // ← stat() syscall
    if (!fileInfo.Exists) return null;  // ← redundant, git already told us it exists
    ...
    return new FileEntry(path, extension, language, fileInfo.Length);
}

// AFTER: no stat() during filtering
private FileEntry? CreateFileEntry(string path, GcConfiguration config)
{
    var extension = GetFullExtension(path).ToLowerInvariant();
    var fileName = Path.GetFileName(path).ToLowerInvariant();
    var languageKey = string.IsNullOrEmpty(extension) ? fileName : extension;
    var language = ResolveLanguage(languageKey, config);
    return new FileEntry(path, extension, language, -1); // size resolved later
}
```

**Impact:** Save 500+ stat() syscalls. ~2-5ms on medium repos.

**Effort:** S — 10 lines across 2 files

---

### 0.4 ✅ `Encoding.UTF8.GetByteCount` called 8+ times per file for static strings

**File:** `src/gc.Application/Services/MarkdownGenerator.cs:70-80, 120-130`

**What:** `Utf8NoBom.GetByteCount(Environment.NewLine)` is called ~8 times per file. `Environment.NewLine` is constant. Same with fence strings. These are pure waste.

**Fix:** Cache all constant byte counts once at construction or method entry.

```csharp
// Cache once
private static readonly int NewlineByteCount = Utf8NoBom.GetByteCount(Environment.NewLine);
private static readonly int FenceByteCount = Utf8NoBom.GetByteCount("```");
// etc.
```

**Impact:** Eliminates ~8N redundant encoding calculations where N = file count.

**Effort:** S — 10 lines

---

### 0.5 ✅ git ls-files buffer too small (4096 bytes)

**File:** `src/gc.Infrastructure/Discovery/FileDiscovery.cs:83`

**What:** 4096-byte buffer for reading git ls-files output. A repo with 1000 files can produce 50KB+ of output. That's 12+ read syscalls minimum.

**Fix:** Use 64KB or 128KB buffer. Or better yet, use ArrayPool.

```csharp
// BEFORE
var buffer = new byte[4096];

// AFTER
var buffer = ArrayPool<byte>.Shared.Rent(65536);
try { ... }
finally { ArrayPool<byte>.Shared.Return(buffer); }
```

**Impact:** Reduce read syscalls by 10-15x for large repos.

**Effort:** S — 5 lines

---

## Phase 1 — Zero-Allocation Hot Path (est. 3-4 hours)

### 1.1 ✅ Replace string allocations in FileFilter with Span<char>

**File:** `src/gc.Application/Services/FileFilter.cs`

**What:** `IsValidPath` does `path.Replace('\\', '/')` — allocates a new string for every file. `Path.GetFileName(path)` — another allocation. `fileName.EndsWith("." + ext)` — string concat allocation per extension per file.

**Fix:** Operate on `ReadOnlySpan<char>` throughout. Use `MemoryExtensions.AsSpan()` to avoid allocations.

```csharp
private bool IsValidPath(ReadOnlySpan<char> path, ...)
{
    // Find last '/' or '\\' to get filename without allocation
    var lastSep = path.LastIndexOfAny('/', '\\');
    var fileName = lastSep >= 0 ? path[(lastSep + 1)..] : path;

    // Check extension without allocation
    if (extensions.Count > 0)
    {
        var dotIdx = fileName.LastIndexOf('.');
        if (dotIdx < 0) return false;
        var ext = fileName[(dotIdx + 1)..];
        // Compare against HashSet using span lookup
        ...
    }
}
```

**Impact:** Eliminates ~3N string allocations in the filter path. Reduces GC pressure significantly.

**Effort:** M — 40 lines, need to refactor IsValidPath signature

---

### 1.2 ✅ Pool and reuse StringBuilder in MarkdownGenerator

**File:** `src/gc.Application/Services/MarkdownGenerator.cs:46`

**What:** `new StringBuilder(actualContent.Length)` allocated per file when line exclusion is active.

**Fix:** Use `ObjectPool<StringBuilder>` or reuse a single StringBuilder via `.Clear()`.

```csharp
// Thread-local or method-local reuse
private static readonly ObjectPool<StringBuilder> SbPool =
    new DefaultObjectPoolProvider().CreateStringBuilderPool(initialCapacity: 4096, maximumRetainedCapacity: 1024 * 1024);
```

**Effort:** S — 10 lines

---

### 1.3 ✅ Pre-size List and optimize git output parsing

**File:** `src/gc.Infrastructure/Discovery/FileDiscovery.cs:86`

**What:** `files.Add(Encoding.UTF8.GetString(span))` allocates a managed string per file from the git output. For 1000 files, that's 1000 string allocations.

**Fix:** Keep the raw UTF-8 bytes. Allocate one big byte[] from the git output and store `ReadOnlyMemory<byte>` slices into it. Convert to string only when needed (display/output).

```csharp
// Store raw bytes, decode lazily
var outputBuffer = new byte[estimatedSize];
var files = new List<Range>(); // ranges into outputBuffer
```

**Impact:** Eliminates N string allocations during discovery. Keeps data in L1/L2 cache as contiguous memory.

**Effort:** M — 30 lines, interface change

---

### 1.4 ✅ Use `SearchValues<byte>` for binary detection (SIMD)

**File:** `src/gc.Application/Services/MarkdownGenerator.cs:103`

**What:** `buffer.AsSpan(0, checkLen).Contains((byte)0)` already uses SIMD via .NET runtime. But we can go further with `SearchValues<byte>` for checking multiple patterns simultaneously.

**Fix:** .NET 8+ `SearchValues<byte>` uses AVX2/SSE2 automatically:

```csharp
private static readonly SearchValues<byte> BinaryIndicators =
    SearchValues.Create(new byte[] { 0x00 });

// In hot path:
bool isBinary = buffer.AsSpan(0, checkLen).ContainsAny(BinaryIndicators);
```

**Impact:** Minor — the current code already benefits from JIT SIMD. But `SearchValues` enables checking multiple byte patterns in a single vectorized pass if we extend binary detection.

**Effort:** S — 5 lines

---

## Phase 2 — I/O Pipeline Revolution (est. 4-6 hours)

### 2.1 Replace StreamWriter with direct UTF-8 byte writes via `IBufferWriter<byte>`

**File:** `src/gc.Application/Services/MarkdownGenerator.cs`

**What:** `StreamWriter` does: string → char[] → Encoder → byte[] → Stream. That's 3 copies per write. For file content that's ALREADY UTF-8 on disk, we're decoding UTF-8 to string then re-encoding to UTF-8. Insane.

**Fix:** Write directly to `PipeWriter` or `IBufferWriter<byte>`. For file content, do raw byte copy (zero decoding/encoding). Only encode the headers/fences (which are ASCII anyway and trivially encodable).

```csharp
// Headers are ASCII — write directly as bytes
ReadOnlySpan<byte> headerPrefix = "## File: "u8;
pipeWriter.Write(headerPrefix);
// File path — encode once
Utf8.TryWrite(pipeWriter.GetSpan(pathLength), path, out int written);
pipeWriter.Advance(written);

// File content — raw byte copy, ZERO encoding
await using var fs = File.OpenRead(entry.Path);
while (true)
{
    var memory = pipeWriter.GetMemory(65536);
    int read = await fs.ReadAsync(memory, ct);
    if (read == 0) break;
    pipeWriter.Advance(read);
}
```

**Impact:** Eliminates ALL string encoding overhead for file content. 2-5x faster for I/O-heavy workloads. This is the biggest single win possible.

**Effort:** L — 100+ lines, significant refactor of MarkdownGenerator

---

### 2.2 Parallel file I/O with bounded concurrency

**File:** `src/gc.Application/UseCases/GenerateContextUseCase.cs`

**What:** Currently processes files sequentially. On NVMe SSDs, the bottleneck is syscall latency, not bandwidth. Issuing I/O in parallel hides latency.

**Fix:** Use `Channel<FileEntry>` producer-consumer pattern. N workers read files concurrently, one consumer writes output sequentially (maintaining order).

```csharp
// Producer: enumerate files
// Workers (4-8 parallel): read + binary-check + prepare content
// Consumer: write to output stream in order

var channel = Channel.CreateBounded<(int Index, ReadOnlyMemory<byte> Content)>(
    new BoundedChannelOptions(16) { SingleReader = true });

// Fan-out reads
await Parallel.ForEachAsync(indexedEntries, 
    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
    async (item, ct) => {
        var content = await ReadFileAsync(item.Entry, ct);
        await channel.Writer.WriteAsync((item.Index, content), ct);
    });
```

**Impact:** 2-4x faster on NVMe. Massive win for repos with many small files (the common case).

**Effort:** L — 60 lines, requires ordering logic

---

### 2.3 Use `RandomAccess` API instead of `FileStream`

**File:** `src/gc.Application/Services/MarkdownGenerator.cs:95`

**What:** `new FileStream(...)` allocates a SafeFileHandle, a buffer, sets up async state machine. For small files (<64KB, which is most source files), the overhead dominates.

**Fix:** Use `RandomAccess.ReadAsync` with a pre-opened handle. Or for small files, use synchronous `File.ReadAllBytes` with ArrayPool buffer (faster than async for <64KB due to no state machine overhead).

```csharp
// For small files (< 64KB): sync read is faster than async
using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
var fileSize = RandomAccess.GetLength(handle);

if (fileSize <= 65536)
{
    var buffer = ArrayPool<byte>.Shared.Rent((int)fileSize);
    try
    {
        int read = RandomAccess.Read(handle, buffer.AsSpan(0, (int)fileSize), 0);
        // Write directly to output — no StreamWriter, no encoding
        await outputStream.WriteAsync(buffer.AsMemory(0, read), ct);
    }
    finally { ArrayPool<byte>.Shared.Return(buffer); }
}
```

**Impact:** Eliminates FileStream overhead for small files. ~30% faster for typical repos where most files are <64KB.

**Effort:** M — 30 lines

---

### 2.4 Memory-mapped I/O for large files

**File:** new utility

**What:** For files >64KB, memory-mapping avoids the read() syscall entirely. The OS maps the file pages directly into our address space.

**Fix:**
```csharp
using var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
using var accessor = mmf.CreateViewAccessor(0, fileSize, MemoryMappedFileAccess.Read);

unsafe
{
    byte* ptr = null;
    accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
    try
    {
        var span = new ReadOnlySpan<byte>(ptr, (int)fileSize);
        // Binary check on span — zero copy
        // Write span directly to output — zero copy
        await outputStream.WriteAsync(new ReadOnlyMemory<byte>(span.ToArray()), ct);
        // Or better: use IBufferWriter pattern to avoid even this copy
    }
    finally { accessor.SafeMemoryMappedViewHandle.ReleasePointer(); }
}
```

**Impact:** Near-zero-copy file reading. Kernel handles paging. Best for files 64KB-10MB.

**Effort:** M — 40 lines, needs unsafe block

---

## Phase 3 — OS Fuckery (est. 3-4 hours)

### 3.1 Linux: Use `io_uring` for batched I/O (via `IoUring` NuGet or P/Invoke)

**What:** Instead of issuing one read() syscall per file, batch ALL file reads into a single io_uring submission. The kernel processes them all concurrently with zero context switches.

**Fix:** Use `Tmds.LinuxAsync` or direct P/Invoke to `io_uring_setup`, `io_uring_enter`.

```csharp
// Pseudocode — batch 100 file reads into one kernel call
var ring = new IoUring(entries: 256);
foreach (var file in fileBatch)
{
    ring.PrepareRead(file.Handle, file.Buffer, file.Size, offset: 0);
}
ring.Submit();  // ONE syscall for 100 file reads
ring.WaitForCompletions();
```

**Impact:** On Linux with io_uring support (kernel 5.1+), this can be 10-50x faster for many-small-file workloads by eliminating per-file syscall overhead.

**Platform:** Linux only. Falls back to normal I/O on macOS/Windows.

**Effort:** L — 80 lines + P/Invoke declarations, Linux-only

---

### 3.2 Linux: `O_NOATIME` flag to skip access time updates

**What:** Every file read updates the access timestamp (atime) unless `noatime` is mounted. This causes a metadata WRITE for every READ.

**Fix:** Open files with `O_NOATIME` flag via P/Invoke:

```csharp
[DllImport("libc", SetLastError = true)]
private static extern int open(string pathname, int flags);

const int O_RDONLY = 0;
const int O_NOATIME = 0x40000;

int fd = open(path, O_RDONLY | O_NOATIME);
```

**Impact:** Eliminates metadata write per file. ~5-10% faster on ext4/btrfs without `noatime` mount option.

**Platform:** Linux only.

**Effort:** S — 15 lines

---

### 3.3 `posix_fadvise` / `madvise` — tell kernel about access patterns

**What:** Tell the kernel we're reading files sequentially and won't re-read them. Kernel can prefetch aggressively and drop pages after we're done.

```csharp
[DllImport("libc")]
static extern int posix_fadvise(int fd, long offset, long len, int advice);

const int POSIX_FADV_SEQUENTIAL = 2;  // We read front-to-back
const int POSIX_FADV_WILLNEED = 3;    // Prefetch this file
const int POSIX_FADV_DONTNEED = 4;    // Drop from page cache after read

// Before reading:
posix_fadvise(fd, 0, fileSize, POSIX_FADV_SEQUENTIAL | POSIX_FADV_WILLNEED);
// After reading:
posix_fadvise(fd, 0, fileSize, POSIX_FADV_DONTNEED);
```

**Impact:** Better prefetching, less page cache pollution. ~5-15% on cold caches.

**Platform:** Linux/macOS.

**Effort:** S — 20 lines

---

### 3.4 Windows: Use `FILE_FLAG_SEQUENTIAL_SCAN`

**What:** Windows equivalent of `posix_fadvise(SEQUENTIAL)`. Tells the cache manager to prefetch aggressively.

**Fix:** Already supported via `FileOptions.SequentialScan`:

```csharp
new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
    bufferSize: 0, // unbuffered — we handle our own buffering
    FileOptions.SequentialScan | FileOptions.Asynchronous);
```

**Impact:** Better read-ahead on Windows NTFS. ~10% on cold caches.

**Effort:** S — 1 line change

---

### 3.5 Pre-warm the page cache with `readahead()` / prefetch

**What:** While we're filtering files (CPU-bound), issue non-blocking readahead hints for files we know we'll read. By the time filtering is done, the files are already in page cache.

```csharp
// During filtering — fire and forget
foreach (var file in filteredFiles.Take(50))
{
    // Linux
    int fd = open(file.Path, O_RDONLY);
    readahead(fd, 0, file.Size);  // non-blocking, returns immediately
    close(fd);
}
```

**Impact:** Overlaps I/O with CPU work. Can eliminate all I/O wait for the first batch of files.

**Effort:** M — 30 lines

---

## Phase 4 — Algorithmic Wins (est. 2-3 hours)

### 4.1 Replace `string.Contains` pattern matching with Aho-Corasick automaton

**File:** `src/gc.Application/Services/FileFilter.cs:88-100`

**What:** For each file path, we check `pathNormalized.Contains(pattern)` for every ignore pattern. That's O(P * L) where P = patterns, L = path length. With ~30 system ignore patterns and 500 files, that's 15,000 string scans.

**Fix:** Build an Aho-Corasick automaton from all ignore patterns at startup. Single-pass O(L) matching regardless of pattern count.

```csharp
// Build once at startup
var automaton = new AhoCorasickAutomaton(normalizedIgnorePatterns);

// Per file — single pass
bool isIgnored = automaton.ContainsAny(pathNormalized);
```

**Impact:** O(P * N * L) → O(N * L). ~2-3x faster filtering for large pattern sets.

**Effort:** M — 60 lines (implement simple Aho-Corasick or use a NuGet package)

---

### 4.2 Sort-and-binary-search for extension matching

**File:** `src/gc.Application/Services/FileFilter.cs:78-82`

**What:** `extensions.Any(ext => fileName.EndsWith("." + ext))` does a linear scan per file.

**Fix:** Pre-sort extensions. Extract the file's extension once, then do a single `HashSet.Contains` lookup (already using HashSet, but the `.EndsWith` loop is wrong — it should just extract the extension and look up).

```csharp
// Extract extension ONCE
var dotIdx = path.LastIndexOf('.');
if (dotIdx < 0) return false;
var ext = path.AsSpan()[(dotIdx + 1)..];
return extensions.Contains(ext.ToString()); // or use a FrozenSet for SIMD lookup
```

Better yet, use `FrozenSet<string>` (.NET 8+) which uses perfect hashing:

```csharp
var frozenExtensions = extensions.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
```

**Impact:** O(E) → O(1) per file for extension matching. Negligible for small extension sets, significant for presets with 15+ extensions.

**Effort:** S — 10 lines

---

### 4.3 Avoid sorting when not needed

**File:** `src/gc.Application/Services/MarkdownGenerator.cs:30`

**What:** `contents.OrderBy(...)` sorts all files alphabetically. For clipboard output, order doesn't matter for LLMs.

**Fix:** Add a `--no-sort` flag or make sorting opt-in. Skip the O(N log N) sort.

**Impact:** Eliminates sort overhead for large file sets.

**Effort:** S — 5 lines

---

## Phase 5 — Native AOT & Startup (est. 1-2 hours)

### 5.1 Eliminate reflection and trim unused code

**File:** `src/gc.CLI/gc.CLI.csproj`

**What:** Ensure the AOT build trims maximally. Current csproj doesn't specify trimming options.

```xml
<PropertyGroup>
    <PublishAot>true</PublishAot>
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>link</TrimMode>
    <InvariantGlobalization>true</InvariantGlobalization>
    <IlcOptimizationPreference>Speed</IlcOptimizationPreference>
    <IlcInstructionSet>native</IlcInstructionSet> <!-- Use host CPU's full instruction set -->
    <StackTraceSupport>false</StackTraceSupport>
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
</PropertyGroup>
```

**Impact:** Smaller binary, faster startup, better code gen.

**Effort:** S — 10 lines in csproj

---

### 5.2 Use `[SkipLocalsInit]` on hot methods

**What:** .NET zero-initializes all local variables by default. For methods with large stack buffers, this is wasted work.

```csharp
[SkipLocalsInit]
private static bool IsValidPath(ReadOnlySpan<char> path, ...)
{
    // locals are NOT zero-initialized — must be explicitly assigned before use
}
```

**Impact:** Eliminates memset overhead on hot methods. Minor but free.

**Effort:** S — add attribute to 5-6 methods

---

### 5.3 Pre-JIT critical paths (for non-AOT builds)

**What:** For `dotnet run` during development, the JIT compiles methods on first call. Pre-JIT critical paths:

```csharp
[MethodImpl(MethodImplOptions.AggressiveOptimization)]
public async Task<Result<long>> GenerateMarkdownStreamingAsync(...)
```

**Impact:** Faster first-run in dev mode.

**Effort:** S — add attribute to 3-4 methods

---

## Phase 6 — Data Structure Upgrades (est. 2-3 hours)

### 6.1 Replace `Dictionary<string, string>` with `FrozenDictionary`

**File:** `src/gc.Domain/Constants/BuiltInPresets.cs`

**What:** Language mappings and presets are created once and never modified. `FrozenDictionary` (.NET 8+) uses perfect hashing and contiguous memory layout.

```csharp
public static readonly FrozenDictionary<string, string> LanguageMappings =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["js"] = "javascript",
        ...
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
```

**Impact:** ~30% faster lookups due to perfect hashing and cache-friendly layout.

**Effort:** S — 10 lines

---

### 6.2 Replace `record` types with `struct` on hot path

**File:** `src/gc.Domain/Models/FileEntry.cs`, `FileContent.cs`

**What:** `FileEntry` and `FileContent` are `sealed record` (reference types). Every creation allocates on the heap. For 500 files, that's 1000 heap allocations + GC pressure.

**Fix:**
```csharp
// BEFORE — heap allocated
public sealed record FileEntry(string Path, string Extension, string Language, long Size);

// AFTER — stack allocated, zero GC pressure
public readonly record struct FileEntry(string Path, string Extension, string Language, long Size);
```

**Caveat:** Strings inside are still heap-allocated. But the struct wrapper itself avoids one allocation per entry.

**Impact:** Eliminates N heap allocations for file entries.

**Effort:** S — 2 line changes + test fixes

---

### 6.3 Use `stackalloc` for small temporary buffers

**Where:** Binary detection, fence checking, any temp buffer <1KB.

```csharp
// Instead of ArrayPool for tiny buffers
Span<byte> smallBuffer = stackalloc byte[512];
int read = RandomAccess.Read(handle, smallBuffer, 0);
```

**Impact:** Zero allocation for small buffers. Stays in L1 cache.

**Effort:** S — scattered 1-line changes

---

## Phase 7 — Benchmark & Profile Infrastructure (est. 1-2 hours)

### 7.1 Add BenchmarkDotNet microbenchmarks

**What:** Current benchmark is wall-clock only. Need method-level profiling.

```csharp
[MemoryDiagnoser]
[DisassemblyDiagnoser]
public class FilterBenchmark
{
    [Benchmark]
    public void FilterFiles_500Files() { ... }

    [Benchmark]
    public void GenerateMarkdown_100Files() { ... }
}
```

### 7.2 Add allocation tracking

```csharp
[MemoryDiagnoser] // Reports bytes allocated and GC collections
[ThreadingDiagnoser] // Reports thread pool usage
```

### 7.3 Profile with `dotnet-trace` / `dotnet-counters`

Document the profiling workflow for contributors.

---

## Summary — Priority Order

| # | Optimization | Est. Impact | Effort | Phase |
|---|---|---|---|---|
| 0.1 | Kill AutoFlush | 5-20x writes | S | 0 |
| 0.2 | Eliminate double git spawn | -3ms | S | 0 |
| 0.3 | Defer FileInfo stat() | -2ms | S | 0 |
| 0.4 | Cache constant byte counts | -1ms | S | 0 |
| 0.5 | Larger git buffer | -1ms | S | 0 |
| 2.1 | Direct UTF-8 byte writes | 2-5x I/O | L | 2 |
| 2.2 | Parallel file I/O | 2-4x total | L | 2 |
| 1.1 | Span-based filtering | -3ms filter | M | 1 |
| 2.3 | RandomAccess API | -30% I/O | M | 2 |
| 3.1 | io_uring batched I/O | 10-50x I/O (Linux) | L | 3 |
| 4.1 | Aho-Corasick pattern matching | 2-3x filter | M | 4 |
| 6.1 | FrozenDictionary | -30% lookups | S | 6 |
| 6.2 | Struct FileEntry | -N allocs | S | 6 |
| 5.1 | AOT optimizations | faster startup | S | 5 |
| 3.4 | SequentialScan flag | -10% Windows | S | 3 |
| 3.3 | posix_fadvise | -5% Linux | S | 3 |

**Do Phase 0 first.** It's all free wins with near-zero risk. Phase 0 alone should get you to <25ms.

Then Phase 2.1 + 2.2 for the architecture change that enables <10ms.

Everything else is gravy.
