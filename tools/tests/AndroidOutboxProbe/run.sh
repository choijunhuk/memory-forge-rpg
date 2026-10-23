#!/usr/bin/env bash
set -euo pipefail
probe_dir="$(cd "$(dirname "$0")" && pwd)"
probe_build="$(mktemp -d "${TMPDIR:-/tmp}/mf-android-outbox.XXXXXX")"
trap 'rm -rf "$probe_build"' EXIT
if [[ "$(uname -s)" == Darwin ]]; then
  cc -dynamiclib -Wall -Wextra -Werror "$probe_dir/native.c" -o "$probe_build/liboutbox_probe.dylib"
  export MF_OUTBOX_PROBE_NATIVE="$probe_build/liboutbox_probe.dylib"
else
  cc -shared -fPIC -Wall -Wextra -Werror "$probe_dir/native.c" -ldl -o "$probe_build/liboutbox_probe.so"
  export MF_OUTBOX_PROBE_NATIVE="$probe_build/liboutbox_probe.so"
fi
"${DOTNET:-dotnet}" build "$probe_dir/AndroidOutboxProbe.csproj" --artifacts-path "$probe_build/artifacts" --nologo
"${DOTNET:-dotnet}" "$probe_build/artifacts/bin/AndroidOutboxProbe/debug/AndroidOutboxProbe.dll"
if [[ "$(uname -s)" == Linux ]]; then
  "${DOTNET:-dotnet}" "$probe_build/artifacts/bin/AndroidOutboxProbe/debug/AndroidOutboxProbe.dll" direct
fi
