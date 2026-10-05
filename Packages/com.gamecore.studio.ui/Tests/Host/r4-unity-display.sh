#!/usr/bin/env bash
# UNITY adapter for unity-batch.sh: keep its watchdog, results and one slot, but run on :1.
# Hold the allocator mutex during this graphical run so other launchers cannot reserve
# a second Editor. GC_STUDIO_UNITY_SLOTS=1 must be set on the outer wrapper invocation.
set -euo pipefail
[[ "${GC_STUDIO_UNITY_SLOTS:-}" == 1 ]] || { echo 'R4-B graphics requires GC_STUDIO_UNITY_SLOTS=1' >&2; exit 2; }
exec {r4_display_lock}>"$HOME/${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}/.unity-slots/allocator.lock"
flock "$r4_display_lock"
python3 - <<'PY'
from pathlib import Path
for path in Path('/proc').iterdir():
    if not path.name.isdigit():
        continue
    try:
        if (path / 'comm').read_text().strip() != 'Unity':
            continue
        if b'AssetImportWorker' in (path / 'cmdline').read_bytes():
            continue
        stat = (path / 'stat').read_text().rsplit(')', 1)[1].split()
        if stat[0] != 'Z':
            raise SystemExit('R4-B graphics refused: another Editor is active')
    except (FileNotFoundError, ProcessLookupError):
        continue
print('[R4-B graphics] display=:1, other Editors=0, allocator mutex held')
PY
export DISPLAY=:1
export DOTNET_PROCESSOR_COUNT=4
r4_args=()
for arg in "$@"; do
    case "$arg" in -batchmode|-nographics) ;; *) r4_args+=("$arg");; esac
done
"$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" "${r4_args[@]}"
