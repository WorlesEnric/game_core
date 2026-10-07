#!/usr/bin/env bash
# The shared host allocator and redactor still own this graphical :1 Editor.
set -euo pipefail
args=()
for arg in "$@"; do
  case "${arg,,}" in
    -batchmode|-nographics) ;;
    *) args+=("$arg") ;;
  esac
done
export DISPLAY=:1
export GAMECORE_ETOS_KEY_FILE="${GAMECORE_R8_MEDIA_PAIRING_PATH:?scratch pairing path required}"
export GAMECORE_ETOS_AUTOSTART=0
exec "${HOME}/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" "${args[@]}"
