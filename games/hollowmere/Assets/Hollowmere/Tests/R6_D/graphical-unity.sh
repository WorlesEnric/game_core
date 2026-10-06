#!/usr/bin/env bash
# Use as UNITY for studio/tools/unity-batch.sh: the shared allocator still owns the Editor.
set -euo pipefail
args=()
for arg in "$@"; do
  case "${arg,,}" in
    -batchmode|-nographics) ;;
    *) args+=("$arg") ;;
  esac
done
export DISPLAY=:1
exec "${GAMECORE_R6_D_UNITY_EDITOR:-${HOME}/Unity/Hub/Editor/6000.0.75f1/Editor/Unity}" "${args[@]}"
