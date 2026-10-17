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
  # Standard-library structure check: chunk length/type/CRC, one leading IHDR with valid fields, IDAT, final empty IEND
  # and no trailing bytes. Truncation fails at the first short chunk; image dimensions are not fixed.
  local aiimg_png_check='if 1:
    import struct, sys, zlib
    with open(sys.argv[1], "rb") as source:
        data = source.read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        sys.exit("PNG 서명 오류")
    depths = {0: (1, 2, 4, 8, 16), 2: (8, 16), 3: (1, 2, 4, 8), 4: (8, 16), 6: (8, 16)}
    at, kinds = 8, []
    while not kinds or kinds[-1] != b"IEND":
        if len(data) - at < 12:
            sys.exit("PNG가 잘렸습니다")
        length, kind = struct.unpack(">I4s", data[at:at + 8])
        end = at + 12 + length
        if length > 0x7FFFFFFF or not kind.isalpha() or end > len(data):
            sys.exit("PNG chunk 길이·형식 오류")
        body = data[at + 8:end - 4]
        if zlib.crc32(kind + body) != struct.unpack(">I", data[end - 4:end])[0]:
            sys.exit("PNG CRC 오류")
        if (kind == b"IHDR") != (not kinds):
            sys.exit("IHDR 위치 오류")
        if kind == b"IHDR":
            if length != 13:
                sys.exit("IHDR 크기 오류")
            width, height, depth, color, compression, filtering, interlace = struct.unpack(">IIBBBBB", body)
            if not (0 < width <= 0x7FFFFFFF and 0 < height <= 0x7FFFFFFF) or depth not in depths.get(color, ()) \
                    or compression or filtering or interlace > 1:
                sys.exit("IHDR 값 오류")
        kinds.append(kind)
        at = end
    if length or b"IDAT" not in kinds or at != len(data):
        sys.exit("IEND·IDAT·후행 바이트 오류")'
  [[ -r "$aiimg_pin" ]] || { print -u2 "SSH 지문 파일이 없습니다: $aiimg_pin"; return 1; }
  # The validator and publisher need Python 3; check it before spending a remote generation.
  python3 -c 'import os, struct, sys, zlib' 2>/dev/null \
    || { print -u2 'python3가 필요합니다(PNG 검사·저장). 원격 생성을 시작하지 않습니다.'; return 1; }
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
  if [[ ! -s "$aiimg_tmp" ]] || ! python3 -c "$aiimg_png_check" "$aiimg_tmp" \
      || [[ "$(file -b --mime-type "$aiimg_tmp")" != image/png ]] \
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
    # Not a name collision: keep the transferred, validated PNG instead of letting the cleanup trap delete it.
    [[ -e "$aiimg_file" || -L "$aiimg_file" ]] || {
      trap - EXIT
      print -u2 -r -- "PNG 저장 실패. 검증된 PNG를 보존했습니다: $aiimg_tmp"
      return 1
    }
    (( aiimg_suffix += 1 ))
    aiimg_file="$aiimg_dir/ai-$aiimg_stamp-$aiimg_suffix.png"
  done
  print -r -- "$aiimg_file"
  open -a Preview "$aiimg_file" || { print -u2 'PNG는 저장됐지만 미리보기를 열지 못했습니다.'; return 1; }
)
