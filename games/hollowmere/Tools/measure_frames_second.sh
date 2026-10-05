#!/usr/bin/env bash
# Second and final P3.1b attempt: retain the failed Null Device run; use the Xvfb-rendered rehearsal profile.
set -euo pipefail
root="$(cd "$(dirname "$0")/../../.." && pwd)"
cd "$root"
[[ "${PROBE_RUNS:-2}" == 2 ]] || exit 2
out="$root/artifacts/studio/evidence/P3.1b/measurement"
[[ -f "$out/run1/run-result.json" && ! -e "$out/run2" ]] || exit 2
unity_tools_dir="$root/studio/tools"
source "$unity_tools_dir/unity-slot.sh"
export GC_STUDIO_UNITY_SLOTS=1
unity_slot_acquire
while true; do
  exec {measurement_mutex}>"$slot_dir/allocator.lock"
  flock "$measurement_mutex"
  if ! pgrep -x Unity > /dev/null; then break; fi
  flock -u "$measurement_mutex"
  exec {measurement_mutex}>&-
  sleep 1
done
cleanup() {
  flock -u "$measurement_mutex"
  exec {measurement_mutex}>&-
  unity_slot_release
}
trap cleanup EXIT
mkdir -p "$out/run2"
target="$out/run2"
{
  echo "revision=$(git rev-parse HEAD)"
  echo "player_revision=$(cat "$root/build/HollowmereLinux/revision.txt")"
  echo "started=$(date -Is)"
  echo 'Attempt 2 of 2; private Xvfb display with rendering enabled, 1920x1080; no video capture'
  echo 'One allocator reservation; mutex held; no Editor active'
  echo 'Run 1 used -nographics and failed navigation; this profile differs and is reported separately.'
  echo 'xvfb-run -a -s "-screen 0 1920x1080x24" Hollowmere.x86_64 -batchmode -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -frameLog <run2>/frame-log.csv -autoplay games/hollowmere/Autoplay/playthrough.txt -saveDir <run2>/saves'
} > "$target/provenance.txt"
ps -C Unity -o pid,comm > "$target/editors-before.txt" || true
rc=0
timeout --signal=TERM --kill-after=30 1800 xvfb-run -a -s '-screen 0 1920x1080x24' \
  "$root/build/HollowmereLinux/Hollowmere.x86_64" -batchmode -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 \
  -frameLog "$target/frame-log.csv" -autoplay "$root/games/hollowmere/Autoplay/playthrough.txt" \
  -saveDir "$target/saves" -logFile "$target/player.log" > "$target/stdout.txt" 2>&1 || rc=$?
ps -C Unity -o pid,comm > "$target/editors-after.txt" || true
python3 games/hollowmere/Tools/frame_stats.py "$target" > "$target/summary.txt"
python3 - "$target" "$rc" <<'PY'
from pathlib import Path
import json,sys
p=Path(sys.argv[1]);rc=int(sys.argv[2])
with (p/'frame-log.csv').open() as f:
    actual=next(line.strip()[9:] for line in f if line.startswith('# screen '))
(p/'run-result.json').write_text(json.dumps({'exit':rc,'complete':rc==0,'profile':'Xvfb device-enabled batchmode','requestedDisplay':'1920x1080','actualDeviceAndSize':actual,'countsAsAttempt':2},indent=2)+'\n')
PY
echo "attempt 2 finished with exit $rc $(date -Is)"
exit "$rc"
