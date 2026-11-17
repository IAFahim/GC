# gc Fish Completion
# Install: gc --install-completion (or copy to ~/.config/fish/completions/gc.fish)

complete -c gc -f

# What to collect
complete -c gc -a "(__fish_complete_path)" -d "Folder or file to collect"
complete -c gc -r -s e -l ext -d "Only these extensions" -a "cs js ts py go rs java rb php sh md"
complete -c gc -r -s x -l exclude -d "Skip a folder, file or glob" -a "(__fish_complete_directories)"
complete -c gc -r -s z -l drop-lines -d "Drop lines that start with this"
complete -c gc -r -l root -d "Folder to collect" -a "(__fish_complete_directories)"
complete -c gc -s r -l submodules -d "Include registered submodules"
complete -c gc -l no-git -d "Walk the filesystem instead of asking git"
complete -c gc -s u -l unity -d "Unity source, excluding tests and samples"
complete -c gc -r -l exclude-content -d "Skip files containing this text"
complete -c gc -r -l include-content -d "Only files containing this text"
complete -c gc -r -l changed-since -d "Only files changed since git ref"
complete -c gc -r -l explain-filter -d "Explain include/exclude for a file" -a "(__fish_complete_files)"
complete -c gc -r -s d -l depth -d "Maximum directory depth"
complete -c gc -r -l preset -d "Use preset" -a "unity-source unity dotnet web backend python rust go java ruby php minimal"

# Where it goes
complete -c gc -r -s o -l output -d "Write a file instead of the clipboard" -a "(__fish_complete_files)"
complete -c gc -s k -l split -d "Markdown parts, 50 KB by default" -a "50KB 100KB 200KB"
complete -c gc -s a -l append -d "Add to what is already there"
complete -c gc -s b -l brain -d "Minify"
complete -c gc -s c -l compress -d "Structural compression via sqz"
complete -c gc -l no-cache -d "Disable sqz dedup cache"
complete -c gc -l temp -d "Save in the temporary folder"
complete -c gc -r -l output-dir -d "Parts folder" -a "(__fish_complete_directories)"
complete -c gc -l no-clipboard -d "Disable clipboard copy"
complete -c gc -l no-sort -d "Disable alphabetical sorting"
complete -c gc -r -l max-memory -d "Memory limit" -a "100MB 500MB 1GB 5GB"

# Look, do not write
complete -c gc -s l -l list -d "Show the files only"
complete -c gc -l count -d "Token estimate only"
complete -c gc -l stats -d "Timings; optionally write them as JSON" -a "(__fish_complete_suffix .json)"

# Cluster and shard
complete -c gc -l cluster -d "Cluster mode (multi-repo)"
complete -c gc -r -l cluster-dir -d "Cluster directory" -a "(__fish_complete_directories)"
complete -c gc -r -l cluster-depth -d "Cluster max depth"
complete -c gc -r -l shard -d "Process shard N of M (e.g. 1.3)"

# Settings
complete -c gc -l init-config -d "Write an empty .gc.json"
complete -c gc -l save -d "Save typed arguments as this folder's defaults"
complete -c gc -r -l save-profile -d "Save arguments as a user profile"
complete -c gc -l list-profiles -d "List project, user and built-in profiles"
complete -c gc -l validate-config -d "Validate configuration"
complete -c gc -l dump-config -d "Show merged configuration"
complete -c gc -r -l export-schema -d "Export settings JSON schema" -a "(__fish_complete_files)"

# Shell completion
complete -c gc -l install-completion -d "Install shell tab-completion"
complete -c gc -r -l print-completion -d "Print completion script" -a "bash zsh fish"

# Other
complete -c gc -l help -s h -d "Show help message"
complete -c gc -l version -d "Show version information"
complete -c gc -s v -l verbose -d "Enable verbose logging"
complete -c gc -l debug -d "Enable debug logging"
complete -c gc -l history -d "Show run history"

# Keywords (positional arguments)
complete -c gc -n "__fish_seen_subcommand_from grab" -r -d "Path to collect"
complete -c gc -n "__fish_seen_subcommand_from type" -a "cs js ts py go rs java rb php sh md" -d "File extension"
complete -c gc -n "__fish_seen_subcommand_from yeet" -r -d "Path to exclude"
complete -c gc -n "__fish_seen_subcommand_from zap" -d "Line prefix to drop"
complete -c gc -n "__fish_seen_subcommand_from spit" -r -d "Output file"
complete -c gc -n "__fish_seen_subcommand_from brain" -d "Minify"
complete -c gc -n "__fish_seen_subcommand_from compress" -d "Enable sqz compression"
complete -c gc -n "__fish_seen_subcommand_from horde" -d "Enable cluster mode"
