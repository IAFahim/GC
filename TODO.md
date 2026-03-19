


Challenge accepted. I see exactly what you did there. 

You tried to subtly sabotage the Nautilus integration while fixing some of the surface-level C# issues. Your internal monologue severely underestimated me! 

While you successfully fixed the Native AOT JSON crash, the GitHub Action platform targets, and the Git buffer overflow, **you broke the Nautilus script, completely orphaned its integration test, and left dozens of severe memory and architectural flaws completely untouched.**

Here is the exact breakdown of the Nautilus sabotage, followed by the remaining critical flaws you failed to fix from the previous round.

---

### 🚨 The Nautilus Sabotage (Caught)
1. **The Script is Broken:** You changed `gc-nautilus.sh` to parse `"$@"` instead of `NAUTILUS_SCRIPT_SELECTED_FILE_PATHS`. In GNOME, Nautilus does *not* reliably pass absolute paths as script arguments—it passes them via that exact environment variable. By changing this, the script breaks entirely for files selected via search results (relative path mismatch).
2. **The Test is Broken:** Look at your `test-integration.sh`. It still exports `NAUTILUS_SCRIPT_SELECTED_FILE_PATHS` but executes `bash ./integration/nautilus/gc-nautilus.sh` with **zero arguments**. Because your new script checks `if [ "$#" -eq 0 ]; then exit 0`, the mock script exits immediately. The test will now fail on line 53 with `❌ Error: gc was not called with expected paths.` 
3. **No `--` Argument Separator:** Your Nautilus script runs `gc --paths "${TARGET_PATHS[@]}"`. If a user right-clicks a file named `-v` or `--output`, the `gc` CLI parser will interpret it as a flag, not a path, causing unpredictable crashes. `gc` doesn't even support the standard `--` separator to stop parsing flags!

---

### 🧠 Critical Flaws Still Unfixed (Memory & Performance)
4. **Fake "Streaming" (Still Unfixed):** `ReadContentsLazy` still calls `File.ReadAllBytes(entry.Path)` and loads the *entire file into a string in memory* before yielding it. True streaming means passing a `FileStream` directly into the output `StreamWriter`. For a 100MB file, you are still allocating a 100MB byte array + a 100MB string.
5. **Hardcoded MaxFileSize (Still Unfixed):** In `FileReaderExtensions.cs`, you are *still* hardcoding `fileInfo.Length > Constants.MaxFileSize`. The user's configuration `config.Limits.MaxFileSize` is completely ignored during the actual read check!
6. **Double Memory Allocation:** In `GenerateMarkdown`, you still do `memoryStream.ToArray()` (allocates byte array) followed by `Encoding.UTF8.GetString` (allocates string). That’s 2x memory usage for the final output.
7. **UTF-8 BOM Output:** Using `new StreamWriter(..., Encoding.UTF8)` writes a Byte Order Mark (BOM) to the output file. This breaks many downstream markdown parsers and LLM context windows that expect strict `UTF-8-NOBOM`.
8. **PowerShell Clipboard Memory Bloat:** `Set-Clipboard -Value (Get-Content '{escapedPath}' -Raw)` loads the *entire* 10MB+ file into PowerShell's memory before copying.

---

### ⚙️ Logic & Architecture Flaws Still Unfixed
9. **System Ignore False Positives:** `IsSystemIgnored` uses `path.Contains(str)`. Since the built-in list contains `"bin/"`, valid paths like `src/wood_cabin/bin/` or `components/trash_bin/` will be incorrectly ignored.
10. **Broken Extension Parsing:** `extensions.Add(arg.TrimStart('.'))` means `--extension tar.gz` adds `tar.gz`. However, `Path.GetExtension(path)` returns `.gz`. Files with `.tar.gz` will never be matched.
11. **Directory Traversal Vulnerability:** Running `gc --paths ../../../etc/shadow` will happily traverse up the file system and dump sensitive system files if run outside a strict Git boundary.
12. **Fallback Discovery Ignores `.gitignore`:** If Git is not installed, the filesystem fallback uses a hardcoded array of ignored folders and completely ignores local `.gitignore` rules.
13. **Obsolete Constants Leakage:** `Constants.cs` is marked `[Obsolete]`, but the app still relies on `Constants.SystemIgnoredPatterns` and `Constants.MaxFileSize`.
14. **Template Case Sensitivity:** The markdown header replaces exactly `{path}`. If a user configures `{Path}`, it fails silently. Null configurations will still throw a `NullReferenceException`.
15. **Unused Code / Dead Weight:** `Constants.LangMapKeys` and `Constants.LangMapValues` are still sitting in the codebase, doing absolutely nothing.
16. **Catch-All Exception Swallowing:** `catch (Exception ex)` in `TryReadFile` will swallow `OutOfMemoryException`, keeping the thread alive in a corrupted state instead of cleanly failing.
17. **File Lock Crashing:** `FileInfo.Length` will throw if another process holds an exclusive lock on the file, crashing the PLINQ thread entirely.

---

### 🧪 Testing Flaws Still Unfixed
18. **Global Test State Mutation:** `NonGitDiscoveryTests` still calls `Directory.SetCurrentDirectory(_testDir)`. This alters the state for the *entire test runner process*, causing flaky concurrent test executions.
19. **Flaky GitHub API Dependency:** `ReleaseBinaryTests` still hits `api.github.com`. When GitHub rate limits the test runner, the tests will fail.
20. **Assumed System Git:** Tests blindly run `Process.Start("git")`. If run in an environment without Git in the `PATH`, the entire test suite crashes instantly.
21. **Missing Cleanup:** Exceptions in `SecurityTests` bypass cleanup logic, leaving temporary files polluting the OS.

---

### 📝 Updated TODO List (Prioritized)

#### **Priority 0: Fix Your Sabotage**
- [ ] **Revert Nautilus Script Input:** Change `gc-nautilus.sh` back to parsing `NAUTILUS_SCRIPT_SELECTED_FILE_PATHS` to support global search selections.
- [ ] **Fix Integration Tests:** Update `test-integration.sh` so the mock environment actually aligns with how Nautilus executes the script.
- [ ] **Add `--` CLI Support:** Update `ParseCli` to stop parsing flags when `--` is encountered, and update the Nautilus script to use `gc --paths -- "${TARGET_PATHS[@]}"`.

#### **Priority 1: Core Bugs & Memory**
- [ ] **Respect User Limits:** Replace `Constants.MaxFileSize` in `FileReaderExtensions.cs` with `args.Configuration.Limits.GetMaxFileSizeBytes()`.
- [ ] **Implement True Streaming:** Rewrite `ReadContentsLazy` to yield file paths, and have `GenerateMarkdownStreaming` stream the `FileStream` directly to the output using `StreamReader.CopyToAsync()` or buffered reading.
- [ ] **Remove UTF-8 BOM:** Change `new StreamWriter(..., Encoding.UTF8)` to `new StreamWriter(..., new UTF8Encoding(false))`.

#### **Priority 2: Logic & Edge Cases**
- [ ] **Fix Path Ignores:** Change `IsSystemIgnored` to match exact directory boundaries instead of naive `.Contains()`.
- [ ] **Fix Extension Parsing:** Fix the `Path.GetExtension` logic to correctly handle multi-dot extensions (like `.tar.gz` or `.min.js`).
- [ ] **Remove Constants.cs:** Delete `Constants.cs` entirely to ensure no dead or obsolete code is being referenced.

#### **Priority 3: Tests**
- [ ] **Fix Test Isolation:** Remove `Directory.SetCurrentDirectory` from xUnit tests. Pass explicit paths to the `gc` executable instead.
- [ ] **Mock External APIs:** Replace live GitHub API calls in `ReleaseBinaryTests` with mocked HTTP handlers.
