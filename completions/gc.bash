#!/usr/bin/env bash
# gc Bash Completion
# Install: gc --install-completion (or manually source this file)

_gc_completion() {
	local cur prev words cword
	_init_completion || return

	local flags=(
		"-h" "--help" "--version"
		"-v" "--verbose" "--debug"
		"-f" "--force" "--no-sort"
		"-b" "--brain" "-c" "--compress" "--no-cache"
		"--cluster" "--dry-run" "--list" "--count" "--tokens-only"
		"--init-config" "--validate-config" "--dump-config" "--list-profiles"
		"--history" "--no-clipboard"
		"--no-append" "--append"
		"--stats" "--profile-timing" "--unsafe-direct-write"
		"--install-completion"
		"--test" "--benchmark"
		"-u" "--unity" "-r" "--submodules" "--recurse-submodules" "--temp" "-k" "--split"
	)

	local flags_with_args=(
		"-g" "-p" "--paths" "--Paths"
		"-t" "-e" "--extension" "--extensions" "--Extension" "--Extensions"
		"-y" "-x" "--exclude" "--excludes" "--Exclude" "--Excludes"
		"-z" "--exclude-line-if-start"
		"-s" "-o" "--output" "--Output"
		"--max-memory" "--Max-Memory"
		"-d" "--depth" "--Depth"
		"--preset" "--presets"
		"--cluster-dir" "--cluster-depth"
		"--exclude-path" "--include-path"
		"--exclude-content" "--include-content"
		"--changed-since" "--explain-filter" "--shard"
		"--export-schema" "--profile-json" "--json-stats"
		"--print-completion"
		"--root" "--output-dir"
	)

	local keywords=("grab" "type" "yeet" "zap" "spit" "brain" "compress" "horde")

	case $cur in
	-*)
		COMPREPLY=($(compgen -W "${flags[*]} ${flags_with_args[*]}" -- "$cur"))
		;;
	*)
		# Check if previous word is a flag that expects an argument
		case $prev in
		-g | -p | --paths | --Paths | grab | --root | --output-dir)
			_filedir
			;;
		-t | -e | --extension | --extensions | --ext | type)
			COMPREPLY=($(compgen -W "cs py js ts go rs java rb php sh" -- "$cur"))
			;;
		-y | -x | --exclude | --exclude | yeet)
			COMPREPLY=($(compgen -d -- "$cur"))
			;;
		-z | --exclude-line-if-start | zap)
			;;
		-s | -o | --output | spit)
			COMPREPLY=($(compgen -f -- "$cur"))
			;;
		--max-memory | --Max-Memory)
			COMPREPLY=($(compgen -W "100MB 500MB 1GB 5GB" -- "$cur"))
			;;
		-k | --split)
			_filedir
			COMPREPLY+=($(compgen -W "50KB 100KB 200KB" -- "$cur"))
			;;
		-d | --depth | --Depth)
			;;
		--preset | --presets)
			COMPREPLY=($(compgen -W "unity-source unity dotnet web python rust go java ruby php minimal" -- "$cur"))
			;;
		--print-completion)
			COMPREPLY=($(compgen -W "bash zsh fish" -- "$cur"))
			;;
		--cluster-dir)
			COMPREPLY=($(compgen -d -- "$cur"))
			;;
		--exclude-path | --include-path)
			;;
		--exclude-content | --include-content)
			;;
		*)
			_filedir
			COMPREPLY+=($(compgen -W "${keywords[*]}" -- "$cur"))
			;;
		esac
		;;
	esac
}

complete -F _gc_completion gc
