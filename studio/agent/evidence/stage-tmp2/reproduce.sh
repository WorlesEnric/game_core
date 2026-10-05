#!/usr/bin/env bash
# Scratch authentication only; never installed companion/etosd, never worker/provider ops.
set -euo pipefail
repo=$(git rev-parse --show-toplevel)
cd "$repo"
evidence="$repo/studio/agent/evidence/stage-tmp2"
# Provision a private cold cache first, using the existing pinned host inputs.
cache=${STAGE_TMP_CACHE:?Set STAGE_TMP_CACHE to an operator-provisioned private cold cache}
python3 studio/stage/cache.py "$cache" --verify
cargo fmt --manifest-path studio/agent/Cargo.toml --check
cargo clippy --manifest-path studio/agent/Cargo.toml --all-targets -- -D warnings
cargo test --manifest-path studio/agent/Cargo.toml
cargo test --manifest-path studio/agent/Cargo.toml --test stage_tmp2 -- --ignored --nocapture
cargo test --manifest-path studio/agent/Cargo.toml r2_11_docker_isolation -- --ignored --nocapture
# Sequential Unity allocations: the harness awaits each stage before submitting the next.
python3 "$evidence/observe.py" &
observer=$!
python3 "$evidence/observe-pids.py" &
pids=$!
trap 'kill "$observer" "$pids" 2>/dev/null || true' EXIT
status=0
cargo test --manifest-path studio/agent/Cargo.toml --test stage_tmp2_acceptance -- --ignored --nocapture || status=$?
python3 "$evidence/capture.py"
cargo test --manifest-path studio/agent/Cargo.toml --test stage_licensing -- --ignored --nocapture
work=$(mktemp -d /tmp/gamecore-stage-tmp2-negative.XXXXXXXX)
rmdir "$work"
bash studio/stage/analyzer/offline-check.sh "$work" "$cache"
PYTHONDONTWRITEBYTECODE=1 python3 tools/check_stage_slot.py --self-test
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s studio/stage/tests -v
exit "$status"
