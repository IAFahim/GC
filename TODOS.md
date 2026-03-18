# GC (Git Copy) - TODOs

This document tracks deferred work from the CEO review. Each TODO represents work that was considered valuable enough to document but deferred to a future iteration.

---

## Priority Legend

- **P1**: Critical - blocks production or severe user experience issue
- **P2**: High - significant quality or operational issue
- **P3**: Medium - nice to have but not blocking

---

## P1 (Critical)

### 0. Add memory and output size limits

**What**: Add configurable memory limit (default: 100MB) and clipboard size checks.

**Why**: On large repos (50K files), tool can consume 500MB+ memory and silently fail when clipboard overflows OS limits.

**Pros**: Prevents OOM crashes, fails fast with clear message, protects against abuse

**Cons**: ~30 min effort

**Context**: Add `--max-memory` flag (default 100MB). Check total size before reading files. Check clipboard size before copying.

**Effort**: S (human: ~30 min) → S (CC+gstack: ~5 min)

**Depends on**: None

---

### 1. Fix clipboard silent failure bug

**What**: Clipboard operations can fail (missing tools, display server not running) but user sees "[OK] Exported to Clipboard" anyway.

**Why**: Misleading user experience - user thinks it worked but clipboard is empty.

**Pros**: Accurate user feedback, trust in the tool

**Cons**: ~30 min effort to add proper error handling

**Context**: In `ClipboardExtensions.cs`, wrap clipboard commands in try-catch and verify success. Return bool or throw exception. Check exit codes from clipboard processes.

**Effort**: M (human: ~1 hour) → S (CC+gstack: ~15 min)

**Depends on**: None

---

### 2. Add null guards to prevent crashes

**What**: Add null checks to `Program.Main` and all public extension methods.

**Why**: Current code crashes with `NullReferenceException` if args is null or if `this` parameter on extension methods is null.

**Pros**: Prevents crashes, better error messages

**Cons**: ~15 min effort, minor code verbosity

**Context**: Add `if (args == null) throw new ArgumentNullException(nameof(args));` at entry points. Also add guard for `this` parameters in extension methods.

**Effort**: S (human: ~30 min) → S (CC+gstack: ~5 min)

**Depends on**: None

---

### 3. Fix command injection risk in clipboard operations

**What**: Replace string interpolation with argument arrays in `ProcessStartInfo`.

**Why**: Current code like `RunProcess($"-c \"pbcopy < '{tempFile}'\"")` breaks if tempFile has special characters. Low exploit risk but unprofessional.

**Pros**: Security best practice, handles edge cases (spaces, quotes)

**Cons**: ~20 min effort to refactor

**Context**: Change `ProcessStartInfo` to use `ArgumentList` instead of `Arguments` string. This properly escapes arguments.

**Effort**: S (human: ~30 min) → S (CC+gstack: ~10 min)

**Depends on**: None

---

### 4. Add real unit tests (replace fake TestRunner)

**What**: `TestRunner.cs` currently just prints fake success messages. Need real unit tests.

**Why**: 0% test coverage means every change is risky. Can't refactor safely.

**Pros**: Confidence in changes, catch regressions, document behavior

**Cons**: ~4 hour effort for basic coverage

**Context**: Use xUnit or NUnit. Test each extension method with happy path and error cases. Test CLI parsing, file filtering, markdown generation.

**Effort**: L (human: ~4 hours) → M (CC+gstack: ~1 hour)

**Depends on**: None

**Test Cases Needed**:
- CLI parsing (valid args, invalid flags, empty args)
- Git discovery (normal repo, not a repo, git not installed)
- File filtering (by extension, by path, excludes)
- File reading (normal files, locked files, deleted files)
- Markdown generation (sorting, language detection, formatting)
- Clipboard operations (success, failure cases)

---

### 6. Create deployment pipeline (CI/CD + releases)

**What**: Users can't actually use this tool. Need GitHub Actions + releases.

**Why**: Currently requires users to clone repo and install .NET SDK. Nobody will do that.

**Pros**: Users can actually install and use the tool

**Cons**: ~3 hour effort for full setup

**Context**: Add GitHub Actions workflow for building releases. Create release workflow with binaries for win-x64, linux-x64, osx-x64, osx-arm64.

**Effort**: L (human: ~3 hours) → M (CC+gstack: ~45 min)

**Depends on**: None

**Deliverables**:
- `.github/workflows/build.yml` - Build on every commit
- `.github/workflows/release.yml` - Build and publish releases
- Release binaries for all platforms
- Automatic versioning
- Installation script (curl/bash)

---

## P2 (High)

### 5. Add structured logging for debugging

**What**: Add `--verbose` flag that logs progress and errors to stderr.

**Why**: When users report "it didn't work", you have zero insight. No logs, no error context.

**Pros**: Debuggable, better error messages, can diagnose issues

**Cons**: ~1 hour effort

**Context**: Add CLI flag, log to `Console.Error`, include file-by-file progress.

**Effort**: M (human: ~1.5 hours) → S (CC+gstack: ~20 min)

**Depends on**: None

**Log Levels**:
- Normal: Just final status ("Exported 14 files to clipboard")
- Verbose: File-by-file progress ("Reading file 42/1000: src/app.ts")
- Debug: Git commands, timing info, error details

---

### 7. Add project documentation

**What**: Create `README.md` in project root explaining what this is, how to develop, and how to use.

**Why**: No documentation exists. New engineers (and future you) won't understand the project.

**Pros**: Onboarding, clarity, attracts contributors

**Cons**: ~1 hour effort

**Context**: Write README with project description, install instructions, dev setup, and architecture overview.

**Effort**: M (human: ~1 hour) → S (CC+gstack: ~20 min)

**Depends on**: None

**Sections Needed**:
- What is GC? (elevator pitch)
- Why C# instead of shell? (differentiation)
- Quick start (install and use)
- Development setup (how to build/test)
- Architecture overview (diagram)
- Contributing guidelines

---

### 8. Improve error messages and remove silent catches

**What**: Add context to error messages (what file failed? why?) and remove empty catch blocks.

**Why**: `FileReaderExtensions.cs:40-42` has empty catch block. When files fail to read, user has no idea why.

**Pros**: Debuggable, transparent, trustworthy

**Cons**: ~30 min effort

**Context**: Log errors to stderr with file path and exception type.

**Effort**: S (human: ~45 min) → S (CC+gstack: ~10 min)

**Depends on**: TODO #5 (logging)

**Current Problem Areas**:
- Empty catch at `FileReaderExtensions.cs:40`
- No context when git fails
- No context when clipboard fails

---

### 9. Handle edge cases explicitly

**What**: Add explicit handling for: git not installed, not in git repo, empty repo, no matching files.

**Why**: Current behavior is inconsistent or crashes. Some cases show warnings, others fail silently.

**Pros**: Consistent UX, no crashes, clear error messages

**Cons**: ~1 hour effort

**Context**: Detect git availability early. Check for .git directory. Provide helpful error messages.

**Effort**: M (human: ~1 hour) → S (CC+gstack: ~20 min)

**Depends on**: TODO #5 (logging)

**Edge Cases to Handle**:
- Git binary not found → "Git not found. Install git from https://git-scm.com"
- Not in a git repo → "Not a git repository. Run from inside a git repo"
- Empty repo (no tracked files) → "No tracked files found"
- All files filtered out → "No files match the specified filters"
- No clipboard tools → "No clipboard tool found. Install xclip/wl-copy"

---

## Summary

**Total TODOs**: 9
- P1 (Critical): 5
- P2 (High): 4

**Estimated Effort** (Human → CC+gstack):
- P1: ~9.5 hours → ~2 hours
- P2: ~4 hours → ~1 hour
- **Total**: ~13.5 hours → ~3 hours

**Recommended Order**:
1. TODO #2 (null guards) - Quickest win, prevents crashes
2. TODO #3 (command injection) - Security issue
3. TODO #1 (clipboard failures) - UX critical
4. TODO #5 (logging) - Enables #8 and #9
5. TODO #8 (error messages) - Depends on logging
6. TODO #9 (edge cases) - Depends on logging
7. TODO #4 (tests) - Can be done incrementally
8. TODO #6 (CI/CD) - Can be done in parallel
9. TODO #7 (docs) - Can be done anytime

---

## NOT in Scope

These items were considered but explicitly rejected as out of scope for this project:

- **Plugin architecture**: Current design is intentionally simple. No extensibility needed yet.
- **Configuration file**: CLI args are sufficient. No ~/.gitcopyconfig planned.
- **Progress bars**: Verbose logging is sufficient. No TUI planned.
- **Language server integration**: Out of scope. This is a CLI tool, not an editor integration.
- **Cloud sync**: Out of scope. Clipboard only.
- **Git submodules**: Not supported by original git-copy. Defer until requested.
