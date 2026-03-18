# GC (Git Copy) - Critical Issues & TODOs

**Status**: Code review revealed critical architectural flaws and security issues. Previous claims of "100% perfect" were false.

---

## P0 (Critical - Blocks Production)

### 1. Fix Fake Streaming Architecture

**Issue**: Claims of "constant ~5MB memory usage" are false.

**Current Code** (`FileReaderExtensions.cs:75-91`):
```csharp
var text = File.ReadAllText(entry.Path);  // Loads ALL files into memory
processedCount++;
return new FileContent(entry, text, fileInfo.Length);
// ...
.OfType<FileContent>().ToArray();  // Forces materialization
```

**Problem**: All file contents loaded into RAM simultaneously as UTF-16 strings (2x size).

**Fix Required**:
- Implement lazy `IEnumerable<FileContent>` that yields one file at a time
- Markdown generator should stream: read from disk → write to output → dispose string immediately
- True constant memory usage regardless of repository size

**Impact**: 90MB repo = 180MB RAM (current) vs 5MB RAM (fixed)

---

### 2. Fix Docker AOT Crash

**Issue**: Docker container crashes because `GC.dll` doesn't exist.

**Root Cause**: `GC.csproj` has `<PublishAot>true</PublishAot>`, which produces native binary `GC` (or `GC.exe`), not `GC.dll`.

**Current Dockerfile**:
```dockerfile
RUN dotnet publish "GC/GC.csproj" -c Release -o /app/publish
ENTRYPOINT["dotnet", "GC.dll"]  # CRASH: GC.dll doesn't exist
```

**Fix Required**:
- Either disable AOT in Docker: `-p:PublishAot=false`
- Or change ENTRYPOINT to run native binary directly
- Test container actually starts and works

---

### 3. Fix Data Race (Concurrency Bug)

**Issue**: `processedCount++` in parallel loop is not thread-safe.

**Current Code** (`FileReaderExtensions.cs:76`):
```csharp
var text = File.ReadAllText(entry.Path);
processedCount++;  // RACE CONDITION!
```

**Inconsistency**: Lines 69, 86 correctly use `Interlocked.Increment(ref skippedCount)` but line 76 doesn't.

**Fix Required**:
```csharp
Interlocked.Increment(ref processedCount);
```

**Impact**: Progress reporting is inaccurate in verbose mode.

---

### 4. Fix Markdown Fencing Injection

**Issue**: Hardcoded triple backticks break on files containing ``````.

**Current Code** (`MarkdownGeneratorExtensions.cs`):
```csharp
writer.WriteLine($"{Constants.MarkdownFence}{content.Entry.Language}");
writer.WriteLine(content.Content);  // If this contains ```, it breaks
writer.WriteLine(Constants.MarkdownFence);
```

**Problem**: If user has markdown files with code blocks, output becomes malformed.

**Fix Required**:
- Scan file content for ```, `~~~`, etc.
- Dynamically choose fence length: ``````` for content that has ````
- Or escape existing fences in content

---

## P1 (High - Security & Reliability)

### 5. Fix Remaining Path Injection Risk

**Issue**: PowerShell command still vulnerable to single quotes in temp paths.

**Current Code** (`ClipboardExtensions.cs` - Windows):
```csharp
psi.ArgumentList.Add($"Set-Clipboard -Value (Get-Content '{tempFile}' -Raw)");
```

**Problem**: If tempFile is `C:\Users\O'Connor\AppData\...\temp.txt`, PowerShell syntax error.

**Fix Required**:
- Use ArgumentList for all parameters (no string interpolation)
- Or escape single quotes in path
- Test with paths containing special characters

---

### 6. Add Binary File Detection

**Issue**: Binary files (`.dll`, `.png`, `.so`) cause massive memory spikes and corruption.

**Current Code**: No binary detection before `File.ReadAllText()`.

**Problem**:
- Binary files read as UTF-8 create massive replacement character strings: ``�``
- Memory usage explodes
- Terminal corruption

**Fix Required**:
```csharp
private static bool IsBinaryFile(string path)
{
    var buffer = new byte[4096];
    using var fs = File.OpenRead(path);
    var bytesRead = fs.Read(buffer, 0, buffer.Length);
    return buffer.AsSpan(0, bytesRead).Contains((byte)0);  // Check for null bytes
}
```

- Skip binary files with warning
- Add more extensions to `Constants.SystemIgnoredPatterns`

---

### 7. Fix CLI Parser State Machine

**Issue**: Parser swallows dangling arguments and missing values.

**Example**: `--extension cs my_file.txt` → `my_file.txt` added to extensions, not paths.

**Problem**: Missing validation for:
- Dangling arguments (no flag specified)
- Missing values (e.g., `--output` with no filename)
- Invalid state transitions

**Fix Required**:
- Validate that all arguments are consumed
- Throw errors on missing required values
- Clear separation between flags and path arguments

---

## P2 (Medium - Performance & Quality)

### 8. Optimize Git Output Parsing

**Issue**: `List<byte>.Add()` and `.ToArray()` create thousands of allocations.

**Current Code** (`GitDiscoveryExtensions.cs:43-54`):
```csharp
for (var i = 0; i < bytesRead; i++) {
    if (buffer[i] == 0) {
        files.Add(Encoding.UTF8.GetString(currentFile.ToArray()));  // Allocation
        currentFile.Clear();
    } else {
        currentFile.Add(buffer[i]);  // List<byte>.Add() overhead
    }
}
```

**Problem**: For 50K files = 50K unnecessary byte array allocations.

**Fix Required**:
- Use `Span<byte>` and `Span<byte>.Slice()` for zero-allocation parsing
- Find null terminator, slice directly, convert to string
- Modern C# 10 optimization

---

## Summary

**Total Issues**: 8 critical problems
- P0 (Production Blocking): 4
- P1 (Security/Reliability): 3
- P2 (Performance): 1

**Previous Status**: FALSE - Claims of "100% perfect" were incorrect
**Actual Status**: Code is above average but has critical flaws

**Estimated Effort**:
- P0: ~4 hours
- P1: ~3 hours
- P2: ~1 hour
- **Total**: ~8 hours

**Recommended Order**:
1. #3 (Data race) - Quickest fix, prevents inaccurate logging
2. #2 (Docker crash) - Blocks container usage
3. #1 (Streaming architecture) - Core performance issue
4. #4 (Markdown injection) - Data corruption risk
5. #5 (Path injection) - Security issue
6. #6 (Binary files) - Reliability issue
7. #7 (CLI parser) - User experience
8. #8 (Git parsing) - Performance optimization

---

## NOT in Scope

- Plugin architecture (not needed)
- Configuration file (CLI args sufficient)
- Progress bars (verbose logging sufficient)
- Language server integration (out of scope)
