#!/usr/bin/env bash
# Build-only lane: the installed release and its source path remain untouched.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
clone="$repo/.evidence/companion-next"
if [[ ! -d "$clone/.git" ]]; then
  git clone --shared --branch main "$(git -C "$repo" remote get-url origin)" "$clone"
  git -C "$clone" cherry-pick 8d1574e4
fi
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
cd "$clone/studio/agent"
cargo fmt --check && cargo clippy --all-targets -- -D warnings && cargo test || exit $?
cargo build --release --locked
