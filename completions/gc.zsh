#!/usr/bin/env zsh
# gc Zsh Completion
# Install: source completions/gc.zsh (add to ~/.zshrc)

_gc() {
    local -a opts flags_with_args

    opts=(
        '-h[Show help]' '--help'
        '--version[Show version]'
        '-v[Verbose logging]' '--verbose'
        '--debug[Debug logging]'
        '--no-git[Walk the filesystem instead of asking git]'
        '--no-sort[Disable sorting]'
        '-b[Minify]' '--brain'
        '-c[Compress output]' '--compress'
        '--no-cache[Disable sqz cache]'
        '--cluster[Cluster mode]'
        '-l[Show the files only]' '--list'
        '--count[Token estimate only]'
        '--stats[Timings, optionally written as JSON]::file:_files -g "*.json"'
        '--init-config[Write an empty .gc.json]'
        '--validate-config[Validate config]'
        '--dump-config[Show merged config]'
        '--list-profiles[List project, user and built-in profiles]'
        '--save[Save typed arguments as this folder defaults]'
        '--history[Show history]'
        '--no-clipboard[Disable clipboard copy]'
        '-a[Add to what is already there]' '--append'
        '--install-completion[Install shell tab-completion]'
        '-u[Unity source without tests]' '--unity'
        '-r[Include registered submodules]' '--submodules'
        '--temp[Save in temporary folder]'
        '-k[Markdown parts, 50 KB by default]::size:(50KB 100KB 200KB)'
        '--split[Markdown parts, 50 KB by default]::size:(50KB 100KB 200KB)'
    )

    flags_with_args=(
        '-e[Only these extensions]:exts:_guard "^[0-9,a-z,]*$" "cs py js ts md"'
        '--ext[Only these extensions]:exts:_guard "^[0-9,a-z,]*$" "cs py js ts md"'
        '-x[Skip a folder, file or glob]:paths:_files -/'
        '--exclude[Skip a folder, file or glob]:paths:_files -/'
        '-z[Drop lines that start with]:str:'
        '--drop-lines[Drop lines that start with]:str:'
        '-o[Output file]:file:_files'
        '--output[Output file]:file:_files'
        '--max-memory[Memory limit]:size:(100MB 500MB 1GB 5GB)'
        '-d[Max depth]:depth:'
        '--depth[Max depth]:depth:'
        '--preset[Use preset]:preset:(unity-source unity dotnet web backend python rust go java ruby php minimal)'
        '--cluster-dir[Cluster directory]:dir:_files -/'
        '--cluster-depth[Cluster depth]:depth:'
        '--exclude-content[Skip files containing text]:keyword:'
        '--include-content[Only files containing text]:keyword:'
        '--changed-since[Only files changed since git ref]:ref:'
        '--explain-filter[Explain include/exclude for a file]:file:_files'
        '--shard[Process shard N of M]:shard:'
        '--export-schema[Export settings JSON schema]:file:_files'
        '--save-profile[Save arguments as a user profile]:name:'
        '--print-completion[Print completion script]:shell:(bash zsh fish)'
        '--root[Folder to collect]:folder:_files -/'
        '--output-dir[Parts folder]:folder:_files -/'
    )

    _arguments -C \
        $opts \
        $flags_with_args \
        '*:files:_files' && return

    case ${line[1]} in
        grab|type|yeet|zap|spit|brain|compress|horde)
            ;;
    esac
}

compdef _gc gc
