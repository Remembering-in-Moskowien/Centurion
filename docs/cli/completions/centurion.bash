# Centurion bash completion — source this file:  source centurion.bash
# Commands and global options; subcommand completion for models/providers.

_centurion_commands="asr ocr from-script correct translate dub convert build quality pipeline-graph validate migrate update init models providers"
_centurion_global_opts="--json --dry-run --verbose -v --lang --profile --github-proxy --no-github-proxy --help -h"
_centurion_models_sub="list install verify remove"
_centurion_providers_sub="list test"
_centurion_formats="ass srt txt"
_centurion_profiles="offline fast quality cheap"

_centurion() {
    local cur prev words cword
    COMPREPLY=()
    cur="${COMP_WORDS[COMP_CWORD]}"
    prev="${COMP_WORDS[COMP_CWORD-1]}"

    # 全局选项值
    case "$prev" in
        --lang)
            COMPREPLY=( $(compgen -W "zh-CN en-US ja-JP" -- "$cur") ); return ;;
        --profile|-p)
            COMPREPLY=( $(compgen -W "$_centurion_profiles" -- "$cur") ); return ;;
        --format|-f)
            COMPREPLY=( $(compgen -W "$_centurion_formats" -- "$cur") ); return ;;
        -w|--workflow)
            COMPREPLY=( $(compgen -W "asr ocr from-script translate dub correct" -- "$cur") ); return ;;
    esac

    # 子命令参数
    if [ "$cword" -ge 2 ]; then
        case "${COMP_WORDS[1]}" in
            models)
                COMPREPLY=( $(compgen -W "$_centurion_models_sub" -- "$cur") ); return ;;
            providers)
                COMPREPLY=( $(compgen -W "$_centurion_providers_sub" -- "$cur") ); return ;;
            pipeline-graph)
                COMPREPLY=( $(compgen -W "$_centurion_commands" -- "$cur") ); return ;;
        esac
    fi

    COMPREPLY=( $(compgen -W "$_centurion_commands $_centurion_global_opts" -- "$cur") )
    return 0
}
complete -F _centurion centurion
