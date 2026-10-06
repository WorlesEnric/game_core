#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
clone="$HOME/wkspace/gc-studio/companion"
revision="$(git -C "$repo" rev-parse origin/main)"
if pgrep -x Unity >/dev/null || pgrep -x ffmpeg >/dev/null; then
  echo 'BLOCKED: Editor or recorder active'; exit 2
fi
if [[ -e "$clone" ]]; then mv "$clone" "${clone}-retained-$(date -u +%Y%m%dT%H%M%SZ)"; fi
git clone --branch main "$(git -C "$repo" remote get-url origin)" "$clone"
test "$(git -C "$clone" rev-parse HEAD)" = "$revision"
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
cd "$clone/studio/agent"
cargo fmt --check
cargo clippy --all-targets -- -D warnings
cargo test
cargo build --release --locked
