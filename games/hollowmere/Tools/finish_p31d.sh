#!/usr/bin/env bash
# Run after the release build appears. All player activity remains under the host allocator.
set -euo pipefail
root="$(cd "$(dirname "$0")/../../.." && pwd)"
cd "$root"
out="$root/artifacts/studio/evidence/P3.1d"
while [[ ! -f "$out/player/build-report.json" ]]; do sleep 5; done
python3 - "$out" <<'PY'
import json,sys
from pathlib import Path
p=Path(sys.argv[1])
r=json.loads((p/'player/build-report.json').read_text())
assert r['result']=='Succeeded' and not r['development'],r
(p/'build/build-report.json').write_text(json.dumps(r,indent=2)+'\n')
PY
unity_tools_dir="$root/studio/tools"
source "$unity_tools_dir/unity-slot.sh"
export GC_STUDIO_UNITY_SLOTS=1
unity_slot_acquire
trap unity_slot_release EXIT
mkdir -p "$out/smoke"
rc=0
timeout --signal=TERM --kill-after=30 300 xvfb-run -a "$out/player/Hollowmere.x86_64" -batchmode -nographics \
  -frameLog "$out/smoke/frame-log.csv" -autoplay "$root/games/hollowmere/Autoplay/smoke.txt" \
  -saveDir "$out/smoke/saves" -logFile "$out/smoke/player.log" > "$out/smoke/stdout.txt" 2>&1 || rc=$?
printf '%s\n' "$rc" > "$out/smoke/exit-code.txt"
python3 - "$out/smoke" "$rc" <<'PY'
import json,sys
from pathlib import Path
p=Path(sys.argv[1]);rc=int(sys.argv[2])
rows=[r for r in (p/'frame-log.csv').read_text().splitlines() if r and r[0].isdigit()]
result={'exit':rc,'frames':len(rows),'passed':rc==0 and len(rows)>=600}
(p/'result.json').write_text(json.dumps(result,indent=2)+'\n')
assert result['passed'],result
print('Player smoke:',result)
PY
unity_slot_release
trap - EXIT
PROBE_RUNS=2 "$root/games/hollowmere/Tools/measure_frames_p31d.sh"
python3 "$root/games/hollowmere/Tools/qualify_p31d.py" "$out/measurement"
