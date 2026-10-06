#!/usr/bin/env bash
# Authorized P4.2b activation; never runs full installer or restarts etosd.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
if pgrep -f '[g]c-studio/p3' >/dev/null || pgrep -x Unity >/dev/null || pgrep -x ffmpeg >/dev/null; then
  echo 'BLOCKED: live-run guard is occupied'; exit 2
fi
clone="$HOME/wkspace/gc-studio/companion"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
if [[ -e "$clone" ]]; then mv "$clone" "${clone}-retained-${stamp}"; fi
if [[ -x "$repo/.evidence/companion-next/studio/agent/target/release/gamecore-studio" ]]; then
  mv "$repo/.evidence/companion-next" "$clone"
else
  git clone --branch main "$(git -C "$repo" remote get-url origin)" "$clone"
  git -C "$clone" cherry-pick 8d1574e4
fi
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
cargo build --release --locked --manifest-path "$clone/studio/agent/Cargo.toml"
bash "$repo/artifacts/studio/verification/TOOLS/install-companion.sh"
