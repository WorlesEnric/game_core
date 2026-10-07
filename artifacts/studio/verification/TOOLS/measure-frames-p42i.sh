#!/usr/bin/env bash
# P3.1d: two graphical probes per VSync state; PROFILE=1 is diagnostic, not qualification.
set -euo pipefail
root="$(cd "$(dirname "$0")/../../../.." && pwd)"
cd "$root"
[[ "${PROBE_RUNS:-}" == 2 ]] || { echo 'PROBE_RUNS=2 is required' >&2; exit 2; }
out="${EVIDENCE_DIR:-$root/artifacts/studio/evidence/P3.1d}"
player="${PLAYER_DIR:-$out/player}"
exe="$player/Hollowmere.x86_64"
[[ -x "$exe" && ! -e "$out/measurement" ]] || { echo 'Need a built player and an unused measurement directory' >&2; exit 2; }
git merge-base --is-ancestor 1752ca8 "$(cat "$player/revision.txt")"
mkdir -p "$out/measurement"
out="$out/measurement"
export DISPLAY=:1 XAUTHORITY=/run/user/1000/gdm/Xauthority
if ! xdpyinfo > "$out/display.txt" 2>&1 || ! glxinfo -B > "$out/opengl.txt" 2>&1; then
  echo 'BLOCKED: cannot use :1; see display.txt/opengl.txt' | tee "$out/BLOCKED.txt"
  exit 3
fi
if ! grep -q 'OpenGL renderer string: NVIDIA GeForce RTX 4060 Ti' "$out/opengl.txt"; then
  echo 'BLOCKED: :1 does not expose the required NVIDIA renderer' | tee "$out/BLOCKED.txt"
  exit 3
fi

inventory() {
  python3 - "$1" <<'PY'
import datetime,json,os,pathlib,sys
active=[]
for p in pathlib.Path('/proc').glob('[0-9]*'):
    try:
        exe=os.readlink(p/'exe')
        if pathlib.Path(exe).name in ('omp','bun','node','codex'):
            cwd=pathlib.Path(os.readlink(p/'cwd'))
            if not cwd.is_relative_to(pathlib.Path.home()/'wkspace/gc-studio'):
                continue
        if pathlib.Path(exe).name=='Unity' or 'UnityPlayer.so' in (p/'maps').read_text(errors='replace'):
            active.append({'pid':int(p.name),'exe':exe})
    except (OSError,PermissionError):
        pass
pathlib.Path(sys.argv[1]).write_text(json.dumps({'time':datetime.datetime.now().astimezone().isoformat(),
    'loadAverage':os.getloadavg(),'editorsAndPlayers':active},indent=2)+'\n')
sys.exit(bool(active))
PY
}
inventory "$out/wait-start.json" || true
cp "$out/wait-start.json" "$out/unity-processes-at-start.json"
unity_tools_dir="$root/studio/tools"
source "$unity_tools_dir/unity-slot.sh"
export GC_STUDIO_UNITY_SLOTS=1
wait_start=$SECONDS
# Bound both allocator acquisition and the subsequent idle check to sixty minutes.
python3 - "$$" <<'PY' &
import os,signal,sys,time
time.sleep(3600)
os.kill(int(sys.argv[1]),signal.SIGTERM)
PY
watchdog=$!
cleanup() {
  if [[ -n "${watchdog:-}" ]]; then kill "$watchdog" 2>/dev/null || true; wait "$watchdog" 2>/dev/null || true; fi
  if [[ -n "${measurement_mutex:-}" ]]; then flock -u "$measurement_mutex"; exec {measurement_mutex}>&-; fi
  unity_slot_release
}
trap cleanup EXIT
trap 'echo "BLOCKED: interrupted or sixty-minute idle wait expired" > "$out/BLOCKED.txt"; exit 3' TERM INT HUP
unity_slot_acquire
while true; do
  exec {measurement_mutex}>"$slot_dir/allocator.lock"
  flock "$measurement_mutex"
  if inventory "$out/idle-before.json"; then break; fi
  flock -u "$measurement_mutex"
  exec {measurement_mutex}>&-
  unset measurement_mutex
  sleep 5
done
kill "$watchdog" 2>/dev/null || true
wait "$watchdog" 2>/dev/null || true
unset watchdog
printf '%s\n' "$((SECONDS-wait_start))" > "$out/wait-seconds.txt"
{
  echo "source_revision=$(cat "$player/revision.txt")"
  echo "launcher_revision=$(git rev-parse HEAD)"
  echo "host=$(hostname)"
  echo "started=$(date -Is)"
  echo 'PROBE_RUNS=2 per VSync state; one allocator reservation; allocator mutex held throughout all four probes'
  echo 'All four routes recorded; capture overhead included; no service operations; no batchmode; no Xvfb'
  sha256sum "$root/games/hollowmere/Autoplay/playthrough.txt" "$root/games/hollowmere/Tools/frame_stats.py" "$root/artifacts/studio/verification/TOOLS/measure-frames-p42i.sh"
} > "$out/provenance.txt"
for vsync in 0 1; do
for run in 1 2; do
  target="$out/vsync$vsync/run$run"
  mkdir -p "$target"
  inventory "$target/host-before.json"
  route="$root/games/hollowmere/Autoplay/playthrough.txt"
  if [[ "${PROFILE:-0}" == 1 ]]; then route="$root/games/hollowmere/Autoplay/profile.txt"; fi
  args=("$exe" -force-glcore -screen-width 1920 -screen-height 1080 -screen-fullscreen 1 -window-mode borderless
    -frameVsync "$vsync" -frameLog "$target/frame-log.csv" -autoplay "$route"
    -saveDir "$target/saves" -logFile "$target/player.log")
  if [[ "${PROFILE:-0}" == 1 ]]; then
    if [[ "${PROFILE_FRAMES:-0}" != 0 ]]; then args+=(-quitAfterFrames "$PROFILE_FRAMES"); fi
    args+=(-frameProfile "$target/timings.csv" -profiler-enable -profiler-log-file "$target/capture.raw")
  fi
  printf '%q ' "${args[@]}" > "$target/command.txt"
  printf '\n' >> "$target/command.txt"
  rc=0
  timeout --signal=TERM --kill-after=30 1000 "${args[@]}" < /dev/null > "$target/stdout.txt" 2>&1 || rc=$?
  printf '%s\n' "$rc" > "$target/exit-code.txt"
  inventory "$target/host-after.json"
  python3 games/hollowmere/Tools/frame_stats.py "$target" > "$target/summary.txt"
  if ! grep -Eq 'Renderer:.*GeForce RTX 4060 Ti' "$target/player.log" || ! grep -q '^# screen 1920x1080' "$target/frame-log.csv"; then
    echo "BLOCKED: run $run did not establish NVIDIA/1080p; see player.log and frame-log.csv" | tee "$out/BLOCKED.txt"
    exit 3
  fi
  echo "run $run finished $(date -Is), player exit $rc"
done
done
