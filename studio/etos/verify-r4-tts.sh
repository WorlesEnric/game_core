#!/usr/bin/env bash
# ONE cheap TTS call only after the reviewed R4 release is active. No restarts.
set -euo pipefail
if pgrep -af '[g]c-studio/p3' >/dev/null; then
    echo 'BLOCKED: a P3 live run is active; no config change or paid call made'
    exit 2
fi
root="$(cd "$(dirname "$0")/../.." && pwd)"
: "${GAMECORE_ETOS_PROJECT_ID:?set the registered project authority}"
export STUDIO_REAL_APP_KEY="${STUDIO_REAL_APP_KEY:-$HOME/.config/gamecore-studio/app-key.json}"
export STUDIO_REAL_ALLOW_OPS=1 STUDIO_REAL_R4_LIVE=1
export R4_EVIDENCE_DIR="${R4_EVIDENCE_DIR:-$root/studio/agent/evidence/r4-c/live}"
export PATH="$HOME/.cargo/bin:$PATH"
cd "$root/studio/agent"
cargo test --test real_node r4_one_priced_tts_records_published_tariff -- --ignored --exact --nocapture
