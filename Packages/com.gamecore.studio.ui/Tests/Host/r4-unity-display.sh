#!/usr/bin/env bash
# UNITY adapter for unity-batch.sh: keep its watchdog, results and one slot, but run on :1.
# Hold the allocator mutex during this graphical run so other launchers cannot reserve
# new Editors. Existing Editors drain before Unity starts; the outer wrapper holds just one slot.
set -euo pipefail
exec {r4_display_lock}>"$HOME/${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}/.unity-slots/allocator.lock"
flock "$r4_display_lock"
python3 - <<'PY'
from pathlib import Path
import time

def editors():
    result = []
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
                result.append(path.name)
        except (FileNotFoundError, ProcessLookupError):
            continue
    return result

waited = 0
while editors():
    if waited % 30 == 0:
        print('[R4-B graphics] waiting for active Editors to finish; new reservations blocked', flush=True)
    time.sleep(1)
    waited += 1
print('[R4-B graphics] display=:1, other Editors=0, allocator mutex held')
PY
export DISPLAY=:1
export DOTNET_PROCESSOR_COUNT=4
r4_args=()
for arg in "$@"; do
    case "$arg" in -batchmode|-nographics) ;; *) r4_args+=("$arg");; esac
done
"$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" "${r4_args[@]}"
