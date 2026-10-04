#!/usr/bin/env bash
# Build and install etos for GameCore Studio on the Linux host (docs/studio/04-etos-integration.md §8.1).
#
#   studio/tools/host-build-etos.sh            (run on the host, from the game_core checkout)
#
# In ~/wkspace/etos-studio (a git clone made by host-sync-etos.sh; the lock pins its HEAD):
#   1. `cargo build --release --locked -p etnode -p etcli`  -> etosd, etos (glibc, for the host)
#   2. the static musl etos in an Alpine container (etos docs/operator.md §1), for the image layer
#   3. installs them to ~/.local/opt/etos/bin/{etosd,etos,etos-musl} (only when they differ)
#   4. writes studio/etos/etos.lock (commit, sha256 of the three binaries, layer version)
# Idempotent: cargo and the container build are incremental; unchanged binaries are not
# reinstalled and the lock is rewritten only when its content changes.
set -euo pipefail

SRC="${ETOS_STUDIO_SRC:-$HOME/wkspace/etos-studio}"
BIN="${ETOS_STUDIO_BIN:-$HOME/.local/opt/etos/bin}"
HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
LOCK="$HERE/../etos/etos.lock"
RUST_IMAGE="rust:1.97.1-alpine"
CARGO_CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/gamecore-studio/cargo-musl"
export PATH="$HOME/.cargo/bin:$PATH"

[ -d "$SRC/.git" ] || { echo "$SRC is not a git clone: run studio/tools/host-sync-etos.sh on the Mac first" >&2; exit 1; }
[ -z "$(git -C "$SRC" status --porcelain --untracked-files=no)" ] || { echo "$SRC has local changes" >&2; exit 1; }
commit="$(git -C "$SRC" rev-parse HEAD)"
branch="$(git -C "$SRC" rev-parse --abbrev-ref HEAD)"
base="$(git -C "$SRC" rev-parse 6c2c3f4)"
command -v cc >/dev/null || { echo "a C toolchain (cc) is required" >&2; exit 1; }

echo "== cargo build --release (etnode, etcli) at $commit"
(cd "$SRC" && cargo build --release --locked -p etnode -p etcli 2>&1 | tail -n 3)

echo "== static musl etos ($RUST_IMAGE)"
# The build container needs the network for crates.io; this host reaches it through its local
# HTTP proxy when one listens on 127.0.0.1:7897 (host networking makes it reachable).
# RUSTUP_TOOLCHAIN selects the image's own 1.97.1 toolchain, so rust-toolchain.toml does not
# make rustup download its components into the throwaway container on every run.
proxy_args=()
if ss -ltn 2>/dev/null | grep -q '127.0.0.1:7897 '; then
    proxy_args=(-e http_proxy=http://127.0.0.1:7897 -e https_proxy=http://127.0.0.1:7897
                -e HTTP_PROXY=http://127.0.0.1:7897 -e HTTPS_PROXY=http://127.0.0.1:7897)
fi
mkdir -p "$CARGO_CACHE"
docker run --rm --network host "${proxy_args[@]}" \
    -v "$SRC":/src -v "$CARGO_CACHE":/usr/local/cargo/registry -w /src \
    -e CARGO_PROFILE_RELEASE_STRIP=true -e RUSTUP_TOOLCHAIN=1.97.1-x86_64-unknown-linux-musl "$RUST_IMAGE" \
    sh -c "apk add -q musl-dev >/dev/null && cargo build -q --locked -p etcli --release \
             --target x86_64-unknown-linux-musl --target-dir /src/target/musl \
           && chown -R $(id -u):$(id -g) /src/target/musl /usr/local/cargo/registry"
musl="$SRC/target/musl/x86_64-unknown-linux-musl/release/etos"
file "$musl" | grep -qE 'static-pie linked|statically linked' || { echo "the musl etos is not static" >&2; exit 1; }

echo "== install to $BIN"
mkdir -p "$BIN"
install_bin() { # src dst
    if cmp -s "$1" "$2"; then echo "  $2 unchanged"; else install -m 0755 "$1" "$2"; echo "  $2 installed"; fi
}
install_bin "$SRC/target/release/etosd" "$BIN/etosd"
install_bin "$SRC/target/release/etos" "$BIN/etos"
install_bin "$musl" "$BIN/etos-musl"

sum() { sha256sum "$1" | cut -d' ' -f1; }
layer="$(tr -d '[:space:]' < "$SRC/image/layer/VERSION")"
tmp="$(mktemp)"
cat > "$tmp" <<EOF
# GameCore Studio etos pin (docs/studio/04-etos-integration.md §8.1). Written by
# studio/tools/host-build-etos.sh on the build host; do not edit by hand.
[etos]
base = "$base"
commit = "$commit"
branch = "$branch"
rust = "1.97.1"
build = "cargo build --release --locked -p etnode -p etcli; etcli for x86_64-unknown-linux-musl in $RUST_IMAGE"

[layer]
version = "$layer"

[sha256]
etosd = "$(sum "$BIN/etosd")"
etos = "$(sum "$BIN/etos")"
etos-musl = "$(sum "$BIN/etos-musl")"
EOF
if cmp -s "$tmp" "$LOCK"; then rm "$tmp"; echo "== $LOCK unchanged"; else mv "$tmp" "$LOCK"; echo "== $LOCK written"; fi
"$BIN/etos" --version 2>/dev/null || true
