# FEATURE TODO: GC Run History (`--history`)

## 🎯 Objective
Create a `--history` flag that tracks previously executed `gc` commands across the system.
- `gc --history` shows a numbered list of previous runs (filtered to remove deleted directories).
- The user can interactively select a number to re-run it.
- `gc --history <N>` instantly re-runs that specific historical execution.
- Includes the directory and the exact arguments used during that run.

---

## 🏗️ Phase 1: Domain Layer (Models & Interfaces)

### 1.1 Create `HistoryEntry` Model
- [x] `src/gc.Domain/Models/Configuration/HistoryEntry.cs`
- [x] `readonly record struct` with `Directory`, `Arguments`, `LastRun`

### 1.2 Update JSON Serializer Context
- [x] `src/gc.Domain/Models/Configuration/GcJsonSerializerContext.cs`
- [x] Add `List<HistoryEntry>` serializable

### 1.3 Create `IHistoryService` Interface
- [x] `src/gc.Domain/Interfaces/IHistoryService.cs`
- [x] `AddEntryAsync`, `GetHistoryAsync`, `ClearHistoryAsync`

### 1.4 Add Relative Time Formatter
- [x] `src/gc.Domain/Common/Formatting.cs` — `FormatRelativeTime`

---

## 💾 Phase 2: Infrastructure Layer (Storage)

### 2.1 Implement HistoryService
- [x] `src/gc.Infrastructure/System/HistoryService.cs`
- [x] Store `history.json` in config directory
- [x] Load: filter deleted dirs, sort descending, prune
- [x] Save: deduplicate, cap at 50, write async

---

## ⚙️ Phase 3: Application / Orchestration

### 3.1 Hook into Successful Runs
- [x] `src/gc.CLI/Program.cs` — call `AddEntryAsync` before `return 0`

---

## 🖥️ Phase 4: CLI Layer (Parser & UI)

### 4.1 Update CliArguments
- [x] `src/gc.CLI/Models/CliArguments.cs` — `ShowHistory`, `HistoryIndex`

### 4.2 Update CliParser
- [x] `src/gc.CLI/Services/CliParser.cs` — parse `--history` and `--history N`

### 4.3 Add HistoryMenu UI Flow
- [x] `src/gc.CLI/Services/HistoryMenu.cs` — display list, accept input

### 4.4 Re-running the Command
- [x] `src/gc.CLI/Program.cs` — change dir, re-parse, re-execute

### 4.5 Update Help Text
- [x] `src/gc.CLI/Program.cs` — add `--history` to PrintHelp

---

## 🧪 Phase 5: Testing

### 5.1 Unit Test: HistoryService
- [ ] `tests/gc.Tests/HistoryServiceTests.cs`

### 5.2 Unit Test: CliParser
- [ ] `tests/gc.Tests/CliParserTests.cs` — `--history` and `--history N`

---

## 💡 Implementation Notes

- **Avoid JSON Reflection**: Native AOT serializers updated in `GcJsonSerializerContext`.
- **Handle I/O Gracefully**: History saving wrapped in try/catch so gc never crashes on history failure.
- **Relative Time Formatting**: `FormatRelativeTime` in `Formatting.cs` for polished CLI output.
- **Re-run bumps to top**: A history-triggered run gets its timestamp updated.
