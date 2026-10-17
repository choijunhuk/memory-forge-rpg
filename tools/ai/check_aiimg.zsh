#!/bin/zsh
# Offline contract check: real PNG inspection/publication, stubbed SSH and Preview.
set -eu
source "${0:A:h}/aiimg.zsh"
aiimg_check_dir=$(mktemp -d)
aiimg_check_stamp="selfcheck-aiimg-$$"
aiimg_check_output="$HOME/Downloads/ai-$aiimg_check_stamp.png"
aiimg_check_extra="$HOME/Downloads/ai-$aiimg_check_stamp-1.png"
[[ ! -e "$aiimg_check_output" && ! -L "$aiimg_check_output" && ! -e "$aiimg_check_extra" && ! -L "$aiimg_check_extra" ]] || exit 1
trap 'command rm -rf -- "$aiimg_check_output" "$aiimg_check_extra" "$aiimg_check_dir"' EXIT
printf '%s' 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=' \
  | /usr/bin/base64 -D > "$aiimg_check_dir/pixel.png"
mktemp() { command mktemp "$aiimg_check_dir/partial.XXXXXXXX"; }
date() { print -r -- "$aiimg_check_stamp"; }
open() {
  [[ "$1" == -a && "$2" == Preview ]] || return 1
  print -r -- "$3" > "$aiimg_check_dir/opened"
}
ssh() {
  printf '%s\n' "$@" > "$aiimg_check_dir/ssh-arguments"
  command cat > "$aiimg_check_dir/stdin"
  case "$aiimg_check_mode" in
    success) command cat "$aiimg_check_dir/pixel.png" ;;
    failure) printf partial; return 42 ;;
    invalid) printf 'not a PNG' ;;
  esac
}
aiimg_check_prompt='quote " dollar $(touch NEVER_EXECUTE) `backticks` 한글'
aiimg_check_mode=success
aiimg "$aiimg_check_prompt" > "$aiimg_check_dir/result"
[[ "$(< "$aiimg_check_dir/stdin")" == "$aiimg_check_prompt" ]]
[[ "$(< "$aiimg_check_dir/opened")" == "$aiimg_check_output" ]]
command cmp "$aiimg_check_dir/pixel.png" "$aiimg_check_output"
command grep -qx 'GlobalKnownHostsFile=/dev/null' "$aiimg_check_dir/ssh-arguments"
aiimg "$aiimg_check_prompt" > "$aiimg_check_dir/result"
[[ "$(< "$aiimg_check_dir/opened")" == "$aiimg_check_extra" ]]
command cmp "$aiimg_check_dir/pixel.png" "$aiimg_check_output"
command rm -- "$aiimg_check_output" "$aiimg_check_extra"
command mkdir "$aiimg_check_output"
aiimg "$aiimg_check_prompt" > "$aiimg_check_dir/result"
[[ "$(< "$aiimg_check_dir/opened")" == "$aiimg_check_extra" ]]
[[ -z "$(command find "$aiimg_check_output" -mindepth 1 -print)" ]]
command rm -- "$aiimg_check_extra"
command rmdir "$aiimg_check_output"
command mkdir "$aiimg_check_dir/destination"
command ln -s "$aiimg_check_dir/destination" "$aiimg_check_output"
aiimg "$aiimg_check_prompt" > "$aiimg_check_dir/result"
[[ "$(< "$aiimg_check_dir/opened")" == "$aiimg_check_extra" ]]
[[ -z "$(command find "$aiimg_check_dir/destination" -mindepth 1 -print)" ]]
aiimg_check_mode=failure
if aiimg "$aiimg_check_prompt" > "$aiimg_check_dir/result" 2> "$aiimg_check_dir/error"; then exit 1; fi
aiimg_check_mode=invalid
if aiimg "$aiimg_check_prompt" > "$aiimg_check_dir/result" 2> "$aiimg_check_dir/error"; then exit 1; fi
if aiimg '   ' > "$aiimg_check_dir/result" 2> "$aiimg_check_dir/error"; then exit 1; fi
[[ -z "$(command find "$aiimg_check_dir" -name 'partial.*' -print)" ]]
print 'PASS: literal stdin, dedicated host trust, PNG validation, Preview, file/directory/symlink collisions, failed/invalid/empty prompt cleanup'
