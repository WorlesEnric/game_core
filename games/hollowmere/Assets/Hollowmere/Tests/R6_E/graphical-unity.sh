#!/usr/bin/env bash
# The host allocator still owns this Editor. Its redactor admits the nonsecret pairing
# path; translate it only here, because credential-shaped environment names are filtered.
set -euo pipefail
args=()
for arg in "$@"; do
  case "${arg,,}" in
    -batchmode|-nographics) ;;
    *) args+=("$arg") ;;
  esac
done
export DISPLAY=:1
export GAMECORE_ETOS_KEY_FILE="${GAMECORE_R6_E_PAIRING_PATH:?external scratch pairing path required}"
exec "${GAMECORE_R6_E_UNITY_EDITOR:-${HOME}/Unity/Hub/Editor/6000.0.75f1/Editor/Unity}" "${args[@]}"
