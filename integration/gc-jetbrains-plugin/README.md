# gc Copy — JetBrains plugin

Adds **Copy with gc (.cs)** to the Project-view right-click menu. It runs
`gc -e cs -z \\ \n\n` in the selected folder (login shell, so your `~/.local/bin`
is on `PATH`) and `gc` copies the result to the clipboard. A balloon reports
success or the captured error.

## Prerequisites

- **JDK 17 or newer** on `PATH` (this project targets a JDK 21 toolchain).
- **Internet on first build.** The Gradle wrapper downloads Gradle 9.5, and the
  IntelliJ Platform Gradle Plugin downloads the target IDE (~1 GB, cached after).
- The **`gc` CLI** installed and working from your terminal.

You do **not** need an IDE pre-installed to test — `runIde` downloads a sandbox one.

## Test it (sandbox IDE)

```bash
./gradlew runIde
```

A throwaway IDE launches with the plugin loaded. Open any project, right-click a
folder in the Project tool window, choose **Copy with gc (.cs)**.

## Build the installable zip

```bash
./gradlew buildPlugin
```

Output: `build/distributions/gc-copy-1.0.0.zip`. Install it in your real IDE via
**Settings → Plugins → ⚙ → Install Plugin from Disk…**, then restart.

## Changing the command

Edit the single line `gcInvocation` in
`src/main/kotlin/com/github/iafahim/gccopy/GcCopyAction.kt`. The string is passed
to `bash -lc` verbatim, so it is tokenized exactly as in your terminal.

## Notes

- Targets the IntelliJ Platform (IDEA, PyCharm, Rider, WebStorm, etc.).
- Uses `bash`, so it expects Linux or macOS. Windows would need a shell tweak.
- If a version fails to resolve, bump `intellijIdea(...)` in `build.gradle.kts`
  and the plugin versions in `settings.gradle.kts` to current releases.
