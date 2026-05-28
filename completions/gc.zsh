#!/usr/bin/env zsh
# gc Zsh Completion
# Install: source completions/gc.zsh (add to ~/.zshrc)

_gc() {
    local -a opts flags flags_with_args

    opts=(
        '-h[Show help]' '--help'
        '--version[Show version]'
        '-v[Verbose logging]' '--verbose'
        '--debug[Debug logging]'
        '-f[Force filesystem discovery]' '--force'
        '--no-sort[Disable sorting]'
        '-b[Brain mode]' '--brain'
        '-c[Compress output]' '--compress'
        '--no-cache[Disable sqz cache]'
        '--cluster[Cluster mode]'
        '--dry-run[List files only]'
        '--list[List files only]'
        '--count[Show token count]'
        '--tokens-only[Show token count]'
        '--init-config[Initialize config]'
        '--validate-config[Validate config]'
        '--dump-config[Show config]'
        '--history[Show history]'
        '--no-append[Do not append]'
        '--append[Append to clipboard]'
        '--test[Run tests]'
        '--benchmark[Run benchmark]'
    )

    flags_with_args=(
        '-g[Paths to include]:paths:_files -/'
        '-p[Paths to include]:paths:_files -/'
        '--paths[Paths to include]:paths:_files -/'
        '-t[File extensions]:exts:_guard "^[0-9,a-z,]*$" "cs py js ts"'
        '-e[File extensions]:exts:_guard "^[0-9,a-z,]*$" "cs py js ts"'
        '--extension[File extensions]:exts:_guard "^[0-9,a-z,]*$" "cs py js ts"'
        '-y[Exclude paths]:paths:_files -/'
        '-x[Exclude paths]:paths:_files -/'
        '--exclude[Exclude paths]:paths:_files -/'
        '-z[Exclude line if starts with]:str:'
        '--exclude-line-if-start[Exclude line if starts with]:str:'
        '-s[Output file]:file:_files'
        '-o[Output file]:file:_files'
        '--output[Output file]:file:_files'
        '--max-memory[Memory limit]:size:(100MB 500MB 1GB 5GB)'
        '-d[Max depth]:depth:'
        '--depth[Max depth]:depth:'
        '--preset[Use preset]:preset:(dotnet web python rust go java minimal)'
        '--cluster-dir[Cluster directory]:dir:_files -/'
        '--cluster-depth[Cluster depth]:depth:'
        '--exclude-path[Exclude path pattern]:pattern:'
        '--include-path[Include path pattern]:pattern:'
        '--exclude-content[Exclude content keyword]:keyword:'
        '--include-content[Include content keyword]:keyword:'
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
