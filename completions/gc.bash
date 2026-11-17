#!/usr/bin/env bash
# gc Bash Completion
# Install: gc --install-completion (or manually source this file)

_gc_completion() {
	local cur prev words cword
	_init_completion || return

	local flags=(
		"-h" "--help" "--version"
		"-v" "--verbose" "--debug"
		"--no-git" "--no-sort"
		"-b" "--brain" "-c" "--compress" "--no-cache"
		"--cluster" "-l" "--list" "--count"
		"--init-config" "--validate-config" "--dump-config" "--list-profiles" "--save"
		"--history" "--no-clipboard"
		"-a" "--append"
		"--stats"
		"--install-completion"
		"-u" "--unity" "-r" "--submodules" "--temp" "-k" "--split"
	)

	local flags_with_args=(
		"-e" "--ext"
		"-x" "--exclude"
		"-z" "--drop-lines"
		"-o" "--output"
		"--max-memory"
		"-d" "--depth"
		"--preset"
		"--cluster-dir" "--cluster-depth"
		"--exclude-content" "--include-content"
		"--changed-since" "--explain-filter" "--shard"
		"--export-schema" "--save-profile" "--format"
		"--print-completion"
		"--root" "--output-dir"
	)

	local keywords=("grab" "type" "yeet" "zap" "spit" "brain" "compress" "horde" "init")
	local presets="unity-source unity dotnet web backend python rust go java ruby php minimal"

	case $cur in
	-*)
		COMPREPLY=($(compgen -W "${flags[*]} ${flags_with_args[*]}" -- "$cur"))
		;;
	@*)
		COMPREPLY=($(compgen -W "$(for p in $presets; do echo "@$p"; done)" -- "$cur"))
		;;
	*)
		# Check if previous word is a flag that expects an argument
		case $prev in
		grab | --root | --output-dir)
			_filedir
			;;
		-e | --ext | type)
			COMPREPLY=($(compgen -W "cs py js ts go rs java rb php sh md" -- "$cur"))
			;;
		-x | --exclude | yeet)
			COMPREPLY=($(compgen -d -- "$cur"))
			;;
		-z | --drop-lines | zap) ;;
		-o | --output | spit)
			COMPREPLY=($(compgen -f -- "$cur"))
			;;
		--max-memory)
			COMPREPLY=($(compgen -W "100MB 500MB 1GB 5GB" -- "$cur"))
			;;
		-k | --split)
			_filedir
			COMPREPLY+=($(compgen -W "50KB 100KB 200KB" -- "$cur"))
			;;
		--stats)
			_filedir json
			;;
		-d | --depth) ;;
		--preset)
			COMPREPLY=($(compgen -W "$presets" -- "$cur"))
			;;
		--format)
			COMPREPLY=($(compgen -W "markdown plain json" -- "$cur"))
			;;
		--print-completion)
			COMPREPLY=($(compgen -W "bash zsh fish" -- "$cur"))
			;;
		--cluster-dir)
			COMPREPLY=($(compgen -d -- "$cur"))
			;;
		--exclude-content | --include-content) ;;
		*)
			_filedir
			COMPREPLY+=($(compgen -W "${keywords[*]}" -- "$cur"))
			;;
		esac
		;;
	esac
}

complete -F _gc_completion gc
