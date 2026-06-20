# Installation & Setup

`gc` ships as a single native binary with no runtime dependencies. This guide
covers every way to install it, how to enable shell tab-completion, and the
optional editor / file-manager integrations.

## Contents

- [Quick install](#quick-install)
- [Install from source](#install-from-source)
- [Shell completion](#shell-completion)
- [PATH setup](#path-setup)
- [Integrations](#integrations)
- [Updating](#updating)
- [Uninstalling](#uninstalling)

## Quick install

### Linux & macOS

```bash
curl -sSL https://raw.githubusercontent.com/IAFahim/gc/main/install.sh | bash
```

The script detects your OS/architecture, downloads the matching release,
verifies its SHA-256 checksum, installs the binary to `~/.local/bin/gc`, and
sets up shell tab-completion automatically.

### Windows (PowerShell)

```powershell
powershell -ExecutionPolicy Bypass -Command "iwr https://raw.githubusercontent.com/IAFahim/gc/main/install.ps1 | iex"
```

Installs `gc.exe` to `%LOCALAPPDATA%\Programs\gc` and adds it to your user
`PATH`. Restart your terminal afterwards.

## Install from source

Prerequisites: **.NET 10.0 SDK** and **Git**.

### One-shot local install (Linux)

Builds a native AOT binary, installs it to `~/.local/bin`, and sets up
completions:

```bash
./install-local.sh
```

### Manual publish

```bash
dotnet publish src/gc.CLI/gc.CLI.csproj -c Release \
    -r <your-platform-id> --self-contained -p:PublishAot=true
```

Replace `<your-platform-id>` with e.g. `linux-x64`, `linux-arm64`,
`osx-arm64`, or `win-x64`. The binary lands in the publish output directory;
copy it somewhere on your `PATH`.

## Shell completion

Tab-completion is installed automatically by both install scripts. To set it
up by hand — or after a manual `dotnet publish` — run:

```bash
gc --install-completion          # auto-detects bash / zsh / fish from $SHELL
gc --install-completion zsh      # or name the shell explicitly
```

The completion scripts are embedded in the binary, so this works from a
standalone download with no extra files. Open a new shell to activate.

### Where each shell installs

| Shell | Location | Activation |
|---|---|---|
| bash | `${XDG_DATA_HOME:-~/.local/share}/bash-completion/completions/gc` | Loaded by the `bash-completion` package on next shell start |
| zsh  | `~/.zsh/completions/_gc` | `~/.zshrc` is updated (idempotently) to add the dir to `$fpath` and run `compinit` |
| fish | `${XDG_CONFIG_HOME:-~/.config}/fish/completions/gc.fish` | Auto-loaded by fish, no config needed |

### Manual / scripted setup

Print the script to stdout instead of installing it — handy for sourcing
directly or placing in a custom location:

```bash
# Source for the current bash session only
source <(gc --print-completion bash)

# Write to a specific path
gc --print-completion zsh > ~/.zsh/completions/_gc
```

If bash completion doesn't activate, make sure the `bash-completion` package
is installed, or add `source ~/.local/share/bash-completion/completions/gc`
to your `~/.bashrc`.

## PATH setup

If `gc` isn't found after install, `~/.local/bin` likely isn't on your
`PATH`. Add it:

```bash
# Bash (~/.bashrc) or Zsh (~/.zshrc)
export PATH="$PATH:$HOME/.local/bin"

# Fish (~/.config/fish/config.fish)
fish_add_path ~/.local/bin
```

Then restart your terminal or `source` the file.

## Integrations

Editor and file-manager integrations live in `integration/` as git
submodules. Clone them with:

```bash
git submodule update --init --recursive
```

| Integration | Submodule | Repo |
|---|---|---|
| GNOME Nautilus (right-click → copy context) | `integration/nautilus` | [gc-nautilus](https://github.com/IAFahim/gc-nautilus) |
| Unity | `integration/unity` | [gc-unity](https://github.com/IAFahim/gc-unity) |
| JetBrains IDEs | `integration/gc-jetbrains-plugin` | [gc-jetbrains-plugin](https://github.com/IAFahim/gc-jetbrains-plugin) |

After initializing the Nautilus submodule:

```bash
chmod +x integration/nautilus/setup.sh
./integration/nautilus/setup.sh
```

## Updating

Re-run the install command — it always fetches the latest release:

```bash
curl -sSL https://raw.githubusercontent.com/IAFahim/gc/main/install.sh | bash
```

## Uninstalling

```bash
# Binary
rm -f ~/.local/bin/gc

# Completions
rm -f ~/.local/share/bash-completion/completions/gc
rm -f ~/.zsh/completions/_gc
rm -f ~/.config/fish/completions/gc.fish
```

For zsh, also remove the `# gc completion` block from your `~/.zshrc`. On
Windows, delete `%LOCALAPPDATA%\Programs\gc` and remove it from your `PATH`.
