## The Vision: gc as the *collector*, sqz as the *compressor*

sqz is localed here its a sperate git repo btw not mine.

Right now gc does two things: **gather** (great) and **Brain Mode compress** (weak, because identifier substitution is a blunt instrument). sqz does one thing: **compress intelligently** (great). They're a natural split.

The cleanest end product would be:

```
gc → [rich Markdown] → sqz compress → clipboard/file
```

Brain Mode gets deprecated. `--brain` becomes `--compress` (or just a flag) that shells out to sqz.

---

## What the UX Looks Like

```bash
# Today (Brain Mode - your own compressor, mediocre)
gc --brain

# New (pipes through sqz, session-aware dedup + structural compression)
gc --compress

# Same file read again later in the same AI session?
gc src/MyService.cs --compress   # returns a 13-token §ref§ instead of 500 tokens

# Save to file (sqz still runs)
gc --compress --output context.md

# Full power combo
gc --paths src --extension cs --compress --no-cache
```

---

## The C# Integration (how gc calls sqz)

This is the key piece — gc spawns sqz as a child process, piping Markdown through it:

```csharp
// src/gc.CLI/Services/CompressionService.cs

public class SqzCompressionService
{
    private readonly bool _sqzAvailable;

    public SqzCompressionService()
    {
        _sqzAvailable = IsSqzInstalled();
    }

    public async Task<string> CompressAsync(string markdownContent, bool noCache = false)
    {
        if (!_sqzAvailable)
        {
            // Graceful fallback: warn user, return uncompressed
            Console.Error.WriteLine(
                "[gc] sqz not found. Install it: curl -fsSL https://raw.githubusercontent.com/" +
                "ojuschugh1/sqz/main/install.sh | sh\n" +
                "[gc] Falling back to uncompressed output.");
            return markdownContent;
        }

        var args = noCache ? "compress --no-cache" : "compress";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "sqz",
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
            }
        };

        process.Start();

        await process.StandardInput.WriteAsync(markdownContent);
        process.StandardInput.Close();

        var compressed = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 ? compressed : markdownContent;
    }

    private static bool IsSqzInstalled()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "sqz",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false
            });
            p?.WaitForExit(1000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }
}
```

---

## CLI Changes (Options.cs / your arg parser)

```csharp
// Remove or deprecate:
[Option("brain", HelpText = "...")]
public bool Brain { get; set; }

// Add:
[Option('c', "compress", HelpText =
    "Compress output using sqz before writing to clipboard/file. " +
    "Requires sqz to be installed (https://github.com/ojuschugh1/sqz). " +
    "Replaces --brain with session-aware dedup and structural compression.")]
public bool Compress { get; set; }

[Option("no-cache", HelpText =
    "Pass --no-cache to sqz (disable dedup for this run).")]
public bool NoCache { get; set; }
```

And in your pipeline, after building the Markdown string, before writing:

```csharp
if (options.Compress)
{
    var sqz = new SqzCompressionService();
    output = await sqz.CompressAsync(output, options.NoCache);
}
// then → clipboard or file
```

---

## Why This Is Strictly Better Than Brain Mode

| | Brain Mode (yours) | sqz --compress |
|---|---|---|
| Method | Replace long identifiers with `_A`, `_B`... | Structural summaries + dedup cache |
| Session-aware | ❌ No | ✅ Yes (same file = 13 tokens on repeat) |
| Git diff / JSON | ❌ Not handled | ✅ Per-command formatters |
| Reversible | Sort of (has a dict header) | ✅ `sqz expand` |
| Safe for secrets | ❌ Doesn't know | ✅ Entropy detection, safe mode |
| Maintained by you | You have to maintain it | ✅ Separate project, improves independently |

The **dedup angle** is especially powerful for your use case. When someone does:
```bash
gc --paths src --compress     # full output, compressed
# ... makes edits ...
gc --paths src --compress     # repeated files → §ref§ tokens, 92% smaller
```

That's something Brain Mode can never do because it has no memory across invocations.

---

## README Section to Add

````markdown
## Compression with sqz (replaces Brain Mode)

`gc --compress` pipes output through [sqz](https://github.com/ojuschugh1/sqz)
before copying to your clipboard. Install sqz first:

```bash
curl -fsSL https://raw.githubusercontent.com/ojuschugh1/sqz/main/install.sh | sh
```

Then:

```bash
gc --compress                  # structural compression + session dedup
gc --compress --no-cache       # compress without dedup (fresh output)
```

**Why sqz instead of Brain Mode?**  
sqz understands *content type* — it compresses JSON differently from logs,
differently from code. It also deduplicates across runs in a session: if you
run `gc --compress` twice, the second run sends ~13-token references for any
file that hasn't changed. Brain Mode can't do either.

> `--brain` is deprecated and will be removed in a future release.
````

---

## The Bond in One Sentence

**gc owns the *what* (which files, which repo, which shape of Markdown) — sqz owns the *how small* (compression strategy, dedup, safe mode).** Neither needs to know the other's internals. gc just needs sqz on `$PATH` and one pipe. That's the whole integration surface, and it's the right one.
