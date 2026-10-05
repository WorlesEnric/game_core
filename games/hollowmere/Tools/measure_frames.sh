#!/usr/bin/env bash
# Reuse P3.1's xvfb player/autoplay/frameLog path, with the original statistics and no video capture.
set -euo pipefail
root="$(cd "$(dirname "$0")/../../.." && pwd)"
cd "$root"
[[ "${PROBE_RUNS:-2}" == 2 ]] || { echo 'P3.1b requires PROBE_RUNS=2' >&2; exit 2; }
exe="$root/build/HollowmereLinux/Hollowmere.x86_64"
[[ -x "$exe" ]] || { echo 'build the Linux player first' >&2; exit 2; }
out="$root/artifacts/studio/evidence/P3.1b/measurement"
mkdir -p "$out"
unity_tools_dir="$root/studio/tools"
source "$unity_tools_dir/unity-slot.sh"
export GC_STUDIO_UNITY_SLOTS=1
unity_slot_acquire
# Holding the allocator mutex after acquiring our single reservation prevents another packet starting an Editor
# during either run. Never reserve extra slots. Wait without holding the mutex if another Editor won the race.
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
{
  echo "revision=$(git rev-parse HEAD)"
  echo "player_revision=$(cat "$root/build/HollowmereLinux/revision.txt")"
  echo "host=$(hostname)"
  echo "started=$(date -Is)"
  echo 'PROBE_RUNS=2; one allocator reservation, allocator mutex held; no Editor active'
  echo 'xvfb-run -a Hollowmere.x86_64 -batchmode -nographics -frameLog <run>/frame-log.csv -autoplay games/hollowmere/Autoplay/playthrough.txt -saveDir <run>/saves'
  echo 'The P3.1 video predates these fixes and is not re-recorded.'
} > "$out/provenance.txt"
for run in 1 2; do
  target="$out/run$run"
  [[ ! -e "$target" ]] || { echo "refusing to overwrite measurement $run" >&2; exit 2; }
  mkdir -p "$target"
  ps -C Unity -o pid,comm > "$target/editors-before.txt" || true
  timeout --signal=TERM --kill-after=30 1000 xvfb-run -a "$exe" -batchmode -nographics \
    -frameLog "$target/frame-log.csv" -autoplay "$root/games/hollowmere/Autoplay/playthrough.txt" \
    -saveDir "$target/saves" -logFile "$target/player.log" > "$target/stdout.txt" 2>&1
  ps -C Unity -o pid,comm > "$target/editors-after.txt" || true
  python3 games/hollowmere/Tools/frame_stats.py "$target" > "$target/summary.txt"
  echo "run $run finished $(date -Is)"
done
