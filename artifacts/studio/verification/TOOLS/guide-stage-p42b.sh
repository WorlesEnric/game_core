#!/usr/bin/env bash
# Satisfy 09's explicit built-binary prerequisite from this packet's final-main build.
# No installed node, service restart, host-confinement fallback or admission.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
clone="$repo/.evidence/p42b-guides"
binary="$repo/.evidence/companion-next/studio/agent/target/release/gamecore-studio"
[[ -x "$binary" ]]
mkdir -p "$clone/studio/agent/target/release"
cp "$binary" "$clone/studio/agent/target/release/gamecore-studio"
sha256sum "$binary" "$clone/studio/agent/target/release/gamecore-studio"
cd "$clone"
export GAMECORE_STAGE_ROOT="$repo/.evidence/p42b-guide-stage"
studio/agent/target/release/gamecore-studio stage run plate-example \
 --candidate samples/mechanisms/pressure-plate/candidate \
 --source-project games/hollowmere --verdict-out "$1/plate-verdict.json"
