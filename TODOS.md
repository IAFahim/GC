# TODO — gc (Git Copy)

Active development tasks and improvement ideas.

---

## Priority 1 — Critical Error Handling

### 1. Fix MemorySizeParser OverflowException
**What:** Add overflow check to prevent crashes when parsing massive size values (e.g., "999999 GB")

**Why:** Current implementation `(long)(value * multiplier)` can overflow on large inputs, causing silent corruption or crashes

**Pros:**
- Prevents crashes on edge cases
- More predictable behavior
- Completes error handling robustness

**Cons:**
- Minimal code complexity (simple bounds check)

**Context:**
File: `src/gc.Domain/Common/MemorySizeParser.cs:38`
Issue: `double` multiplication can overflow before `long` cast
Fix: Check if `value * multiplier > long.MaxValue` before casting

**Effort estimate:** S → 15 minutes with AI

**Priority:** P1 — Crash bug

**Depends on:** Nothing

---

### 2. Fix ClipboardService PlatformNotSupportedException
**What:** Add graceful degradation when clipboard tools are unavailable (headless servers, missing binaries)

**Why:** Current implementation silently returns `false` with no error message. Users have no idea why clipboard failed.

**Pros:**
- Better user experience on headless systems
- Clear error messages guide users to use `--output file.md` instead
- Prevents silent failures

**Cons:**
- Minimal code change (catch specific exception, return descriptive error)

**Context:**
File: `src/gc.Infrastructure/System/ClipboardService.cs:157-160`
Issue: `catch { return false; }` swallows all exceptions including PlatformNotSupportedException
Fix: Log exception, return `Result.Failure("Clipboard not available. Use --output file.md instead")`

**Effort estimate:** S → 15 minutes with AI

**Priority:** P1 — Crash bug

**Depends on:** Nothing

---

## Priority 2 — User-Requested Features

### 3. Re-implement Nautilus Integration
**What:** Restore file manager right-click integration for GNOME/Nautilus

**Why:** Office-hours (2026-03-24) identified this as #1 user request, but recent commit deleted it as "obsolete"

**Pros:**
- Addresses top user pain point (terminal friction)
- Enables "never in your way" philosophy
- Users can gc without opening terminal

**Cons:**
- Platform-specific (GNOME only)
- Requires testing on different Nautilus versions

**Context:**
Deleted in commit c7e9ccd (2026-03-24)
Previous location: `integration/nautilus/`
Feature: Right-click "Copy with gc" in file manager

**Implementation:**
- Recreate `integration/nautilus/setup.sh` installation script
- Recreate `integration/nautilus/gc-nautilus.sh` script
- Test right-click functionality
- Add installation instructions to README

**Effort estimate:** M → 1 hour with AI

**Priority:** P2 — User-facing feature

**Depends on:** Nothing

---

### 4. Implement Append Mode
**What:** Add `--append` flag or auto-detect consecutive gc runs within 5-second window

**Why:** Users requested multi-lib workflows: "run gc again and it asks to append within 5 seconds"

**Pros:**
- Enables efficient multi-lib workflows
- Reduces friction for power users (20+ gc runs/day)
- Aligns with "never in your way" philosophy

**Cons:**
- Requires state management (~/.gcstate file)
- Adds complexity to clipboard logic

**Context:**
User request from office-hours (2026-03-24)
Requested workflow:
```bash
gc --paths libA           # First run
gc --paths libB           # Within 5s, prompts: "Append? [Y/n]"
```

**Implementation:**
- Create `~/.gcstate` JSON file to track last run timestamp
- Add 5-second timeout for append decision
- Merge Markdown output intelligently (headers, separators)
- Handle state file corruption (create new if invalid)

**Open Questions:**
- Auto-detect consecutive runs or opt-in `--append` flag?
- Right timeout: 5s, 10s, or configurable?

**Effort estimate:** M → 30 minutes with AI

**Priority:** P2 — User-facing feature

**Depends on:** Error handling fixes (P1)

---

### 5. Implement Compact Mode
**What:** Add `--compact` flag with aggressive compression strategies (30-50% token reduction)

**Why:** Users requested token savings: "more compact mode to save tokens"

**Pros:**
- Reduces LLM API costs for users
- Faster paste operations (smaller clipboard)
- Competitive advantage over AI IDEs

**Cons:**
- Potential readability tradeoff
- Requires testing compression levels

**Context:**
User request from office-hours (2026-03-24)
Target: 30-50% token reduction while preserving code structure

**Implementation:**
```bash
gc --compact --paths src
```

**Compression strategies:**
- Remove empty lines (aggressive)
- Collapse consecutive whitespace
- Truncate long comments (configurable threshold)
- Remove non-essential metadata
- Optional: Minify JSON/XML

**Open Questions:**
- How aggressive? One level or configurable (mild/aggressive)?
- Truncate comments or remove entirely?

**Effort estimate:** M → 45 minutes with AI

**Priority:** P2 — User-facing feature

**Depends on:** Nothing

---

## Priority 3 — Installation & Onboarding

### 6. Improve Installation Script UX
**What:** Add clear value proposition messaging to install.sh output

**Why:** Office-hours identified onboarding confusion: users struggle with "how do I install" and "why is gc good"

**Pros:**
- Better first impression
- Clearer value communication
- Reduced support burden

**Cons:**
- Minimal effort (script messaging only)

**Context:**
Current install.sh: Functional but unclear
Target output: "✅ gc installed! Run 'gc' from any repo to copy code to clipboard. Learn more: github.com/IAFahim/gc"

**Implementation:**
- Add "Why gc?" section to post-install message
- Improve error messages (permission issues, PATH setup)
- Add quick-start example

**Effort estimate:** S → 15 minutes with AI

**Priority:** P3 — Polish

**Depends on:** Nothing

---

## Completed (2026-03-24)

- ✅ Fix multiple critical bugs (commit c7e9ccd)
  - Configuration limits enforcement
  - DirectoryNotFoundException fix
  - Async I/O improvements
  - MemorySizeParser deduplication
  - 925 lines of bug fix tests

---

## Notes

**Strategic Context:**
Recent bug fixes improved robustness but didn't address top user requests. Priority is balancing:
1. **P1:** Critical error handling (crashes)
2. **P2:** User-requested features (Nautilus, append, compact)
3. **P3:** Onboarding polish

**Philosophy:** "A lightweight thing that never in your way but always make you feel understood"

**Risk:** AI IDEs (Copilot, Cursor) handling context natively. gc must maintain speed/simplicity advantage.
