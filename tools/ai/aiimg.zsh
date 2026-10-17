# Source from ~/.zshrc; host trust is pinned separately in ~/.ssh/known_hosts_aiimg.
aiimg() (
  setopt localoptions pipefail
  if (( $# != 1 )) || [[ -z "${1//[[:space:]]/}" ]]; then
    print -u2 '사용법: aiimg "이미지 프롬프트"'
    return 2
  fi
  local aiimg_dir="$HOME/Downloads"
  local aiimg_pin="$HOME/.ssh/known_hosts_aiimg"
  local aiimg_tmp aiimg_file aiimg_stamp aiimg_size aiimg_suffix=0
  [[ -r "$aiimg_pin" ]] || { print -u2 "SSH 지문 파일이 없습니다: $aiimg_pin"; return 1; }
  mkdir -p "$aiimg_dir" || return 1
  aiimg_tmp=$(mktemp "$aiimg_dir/.aiimg-XXXXXXXX") || return 1
  trap 'rm -f -- "$aiimg_tmp"' EXIT
  trap 'exit 130' INT
  trap 'exit 143' TERM HUP
  if ! printf '%s' "$1" | ssh -T -o BatchMode=yes -o ConnectTimeout=10 \
      -o ServerAliveInterval=30 -o ServerAliveCountMax=4 \
      -o StrictHostKeyChecking=yes -o HostKeyAlgorithms=ssh-ed25519 \
      -o GlobalKnownHostsFile=/dev/null \
      -o "UserKnownHostsFile=$aiimg_pin" choij@100.100.28.127 \
      'C:\Users\choij\Documents\Codex\2026-10-17\di-s\outputs\Generate-Image-SSH.cmd --stdin --stdout' \
      > "$aiimg_tmp"; then
    print -u2 '이미지 생성·전송 실패. 미완성 파일을 제거합니다.'
    return 1
  fi
  aiimg_size=$(sips -g pixelWidth -g pixelHeight "$aiimg_tmp" 2>/dev/null)
  if [[ ! -s "$aiimg_tmp" ]] || [[ "$(file -b --mime-type "$aiimg_tmp")" != image/png ]] \
      || ! printf '%s\n' "$aiimg_size" | awk '
          /pixelWidth:/ { w=$2 } /pixelHeight:/ { h=$2 }
          END { exit !(w+0 > 0 && h+0 > 0) }'; then
    print -u2 '유효한 PNG가 아닙니다. 미완성 파일을 제거합니다.'
    return 1
  fi
  aiimg_stamp=$(date +%Y%m%d-%H%M%S)
  aiimg_file="$aiimg_dir/ai-$aiimg_stamp.png"
  # A hard link publishes complete bytes without overwriting a simultaneous result.
  until python3 -c 'import os, sys; os.link(sys.argv[1], sys.argv[2])' \
      "$aiimg_tmp" "$aiimg_file" 2>/dev/null; do
    [[ -e "$aiimg_file" || -L "$aiimg_file" ]] || { print -u2 'PNG 저장 실패'; return 1; }
    (( aiimg_suffix += 1 ))
    aiimg_file="$aiimg_dir/ai-$aiimg_stamp-$aiimg_suffix.png"
  done
  print -r -- "$aiimg_file"
  open -a Preview "$aiimg_file" || { print -u2 'PNG는 저장됐지만 미리보기를 열지 못했습니다.'; return 1; }
)
