#!/usr/bin/env bash
# Diagnostic guide 09 flow. Does not install services, call providers or authorize admission.
# Usage: bash studio/stage/guide-flow.sh <new-private-stage-root> <trusted-Unity-Library> [NuGet-cache] [public-UPM-cache]
set -euo pipefail
[[ $# -ge 2 && $# -le 4 ]] || { echo 'usage: guide-flow.sh NEW_STAGE_ROOT TRUSTED_UNITY_LIBRARY [NUGET_CACHE] [PUBLIC_UPM_CACHE]' >&2; exit 2; }
repo="$(cd "$(dirname "$0")/../.." && pwd)"
root="$(realpath -ms "$1")"
library="$(realpath -e "$2")"
nuget="${3:-$HOME/.nuget/packages}"
upm="${4:-$HOME/.cache/Unity/upm}"
binary="${GAMECORE_STAGE_BINARY:-$repo/studio/agent/target/release/gamecore-studio}"
[[ ! -e "$root" && ! -L "$root" ]] || { echo 'guide flow requires a new private root; existing evidence and cold-grace markers are never reset' >&2; exit 2; }
[[ -x "$binary" ]] || { echo 'build prerequisite: cargo build --release --manifest-path studio/agent/Cargo.toml (or set GAMECORE_STAGE_BINARY to a local built binary)' >&2; exit 2; }
binary="$(realpath -e "$binary")"
cd "$repo"
python3 samples/mechanisms/pressure-plate/make-catalog.py
python3 samples/mechanisms/pressure-plate/make-catalog.py --check
python3 samples/mechanisms/pressure-plate/make-candidate.py
python3 samples/mechanisms/pressure-plate/make-candidate.py --check
python3 studio/etos/install-state.py stage-cache --stage-root "$root" --repo "$repo" \
  --source-project "$repo/games/hollowmere" --binary "$binary" \
  --offline-from "$nuget" --unity-library "$library" --upm-from "$upm"
python3 studio/etos/install-state.py stage-cache --stage-root "$root" --repo "$repo" \
  --source-project "$repo/games/hollowmere" --binary "$binary" --verify
# Full lane only, with the unchanged 360 s warm / once-only 1800 s cold budget.
# Rust's CLI defaults to Docker; no host-confinement or verdict-import path is used.
exec "$binary" stage run plate-example --repo "$repo" --root "$root" \
  --candidate "$repo/samples/mechanisms/pressure-plate/candidate" \
  --source-project "$repo/games/hollowmere" --budget-s 360 \
  --verdict-out "$root/plate-verdict.json"
