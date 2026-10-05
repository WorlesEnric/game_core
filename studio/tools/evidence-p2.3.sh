#!/usr/bin/env bash
# evidence-p2.3.sh - graphical evidence of the P2.3 structural views on the Linux host's display (never on the Mac).
#
# Usage:   studio/tools/evidence-p2.3.sh [<packet-name>] [<project-rel-path>] [--no-play]
# Default: studio/tools/evidence-p2.3.sh p2.3 games/hollowmere
#
# Run from the Mac after studio/tools/sync-to-host.sh <packet-name> and one successful unity-compile.sh run (so the
# project's Library is warm). What it does:
#   1. on the host: takes one of the GC_STUDIO_UNITY_SLOTS host-wide Unity slots (the flock files unity-compile.sh
#      uses; every running Editor counts), removes a stale Temp/UnityLockfile only when no Editor has the project
#      open, and starts ONE interactive Editor (not batch mode) on DISPLAY=:1:
#        Unity -projectPath <copy> -executeMethod GameCore.Studio.Views.Evidence.StudioViewsEvidence.Run -p23Out <dir>
#      The entry (Packages/com.gamecore.studio.views/Editor/Evidence/StudioViewsEvidence.cs) opens each view in a
#      1280x720 window, sets it up and captures the window's own pixels to <dir>/<shot>.png. If a capture comes back
#      blank it writes <dir>/.request-<shot>.json; this script then grabs that rectangle of the display with
#      `import -window root -crop` and deletes the request;
#   2. waits for that Editor's PID (EVIDENCE_TIMEOUT seconds, default 1200, plus a log-silence watchdog of
#      EVIDENCE_SILENCE_TIMEOUT seconds, default 600); on either it kills only that PID;
#   3. shrinks every PNG below 300 KB with PIL;
#   4. copies the PNGs and capture.json to artifacts/studio/evidence/P2.3/.
# The README there is written by hand from capture.json (what each shot shows and the numbers in it).
#
# Environment: GC_STUDIO_HOST (default myubuntu), GC_STUDIO_REMOTE_BASE (default wkspace/gc-studio),
# GC_STUDIO_UNITY_SLOTS (default 3), UNITY (host Editor binary), EVIDENCE_DISPLAY (default :1), EVIDENCE_TIMEOUT,
# EVIDENCE_SILENCE_TIMEOUT, GCS_FLIP (1 flips window grabs vertically when a driver returns them top-down).
#
# Exit codes: 0 evidence collected (at least 6 PNGs); 1 the Editor failed or timed out, or fewer than 6 PNGs; 2 usage.
set -euo pipefail

packet="p2.3"
project="games/hollowmere"
no_play=""
positional=0
for arg in "$@"; do
  case "${arg}" in
    --no-play) no_play="-p23NoPlay" ;;
    *)
      positional=$((positional + 1))
      if (( positional == 1 )); then packet="${arg}"; elif (( positional == 2 )); then project="${arg}"; else echo "evidence-p2.3.sh: unexpected '${arg}'" >&2; exit 2; fi
      ;;
  esac
done
if ! [[ "${packet}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
  echo "evidence-p2.3.sh: bad packet name '${packet}'" >&2
  exit 2
fi

host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
dest="${EVIDENCE_DEST:-${root}/artifacts/studio/evidence/P2.3}"
on_host=0
[[ "$(uname -s)" != Linux ]] || on_host=1
host_exec() {
  if (( on_host )); then bash -c "$2"; else command ssh "$@"; fi
}
stamp="$(date -u +%Y%m%dT%H%M%SZ)"

echo "-- P2.3 evidence for ${packet}/${project} on ${host}"
set +e
remote_out="$(host_exec "${host}" \
  "PACKET='${packet}' PROJECT='${project}' REMOTE_BASE='${remote_base}' STAMP='${stamp}' SLOTS='${GC_STUDIO_UNITY_SLOTS:-3}' UNITY_BIN='${UNITY:-}' EVIDENCE_DISPLAY='${EVIDENCE_DISPLAY:-:1}' EVIDENCE_TIMEOUT='${EVIDENCE_TIMEOUT:-1200}' EVIDENCE_SILENCE_TIMEOUT='${EVIDENCE_SILENCE_TIMEOUT:-600}' GCS_FLIP='${GCS_FLIP:-0}' NO_PLAY='${no_play}' bash -s" <<'HOST'
set -euo pipefail
unity="${UNITY_BIN:-${HOME}/Unity/Hub/Editor/6000.0.75f1/Editor/Unity}"
base="${HOME}/${REMOTE_BASE}/${PACKET}"
project_dir="${base}/${PROJECT%/}"
out="${base}/.evidence/P2.3-${STAMP}"
slot_dir="${HOME}/${REMOTE_BASE}/.unity-slots"
mkdir -p "${out}" "${slot_dir}"
[[ -x "${unity}" ]] || { echo "no Unity at ${unity}" >&2; exit 2; }
[[ -f "${project_dir}/Packages/manifest.json" ]] || { echo "no project at ${project_dir}" >&2; exit 2; }

unity_tools_dir="${base}/studio/tools"
source "${unity_tools_dir}/unity-slot.sh"
export GC_STUDIO_REMOTE_BASE="$REMOTE_BASE" GC_STUDIO_UNITY_SLOTS="$SLOTS"
unity_slot_acquire
trap unity_slot_release EXIT
stop_evidence() { kill -TERM "${pid:-0}" 2>/dev/null || true; wait "${pid:-0}" 2>/dev/null || true; exit 143; }
trap stop_evidence TERM INT HUP

if [[ -f "${project_dir}/Temp/UnityLockfile" ]]; then
  for pid in $(pgrep -x Unity 2>/dev/null || true); do
    if tr '\0' ' ' < "/proc/${pid}/cmdline" 2>/dev/null | grep -q -- "${project_dir}"; then
      echo "project is open in Unity pid ${pid}; not starting a second Editor" >&2
      exit 1
    fi
  done
  rm -f "${project_dir}/Temp/UnityLockfile"
  echo "-- removed a stale Temp/UnityLockfile" >&2
fi

log="${out}/editor.log"
# shellcheck disable=SC2086
DISPLAY="${EVIDENCE_DISPLAY}" GCS_FLIP="${GCS_FLIP}" python3 "${base}/studio/stage/run-redacted.py" --log "$log" --timeout "$EVIDENCE_TIMEOUT" --silence "${EVIDENCE_SILENCE_TIMEOUT:-600}" -- "${unity}" -projectPath "${project_dir}" \
  -executeMethod GameCore.Studio.Views.Evidence.StudioViewsEvidence.Run -p23Out "${out}" ${NO_PLAY} -logFile - >/dev/null 2>&1 &
pid=$!
echo "-- interactive Editor pid ${pid} on ${EVIDENCE_DISPLAY}; output ${out}" >&2
start=$(date +%s)
last_size=-1
last_change=${start}
rc=0
while kill -0 "${pid}" 2>/dev/null; do
  for request in "${out}"/.request-*.json; do
    [[ -f "${request}" ]] || continue
    read -r x y w h png < <(python3 -c 'import json,sys; r=json.load(open(sys.argv[1])); print(r["x"], r["y"], r["width"], r["height"], r["png"])' "${request}")
    if DISPLAY="${EVIDENCE_DISPLAY}" import -window root -crop "${w}x${h}+${x}+${y}" +repage "${png}" 2>/dev/null; then
      echo "-- captured $(basename "${png}") with import (${w}x${h}+${x}+${y})" >&2
    else
      echo "-- import failed for $(basename "${png}")" >&2
    fi
    rm -f "${request}"
  done
  now=$(date +%s)
  size=$(stat -c %s "${log}" 2>/dev/null || echo 0)
  if [[ "${size}" != "${last_size}" ]]; then
    last_size="${size}"
    last_change=${now}
  fi
  if (( now - start > EVIDENCE_TIMEOUT || (EVIDENCE_SILENCE_TIMEOUT > 0 && now - last_change > EVIDENCE_SILENCE_TIMEOUT) )); then
    echo "-- no progress (timeout ${EVIDENCE_TIMEOUT}s / log silent ${EVIDENCE_SILENCE_TIMEOUT}s); killing own pid ${pid}" >&2
    kill -TERM "${pid}" 2>/dev/null || true
    sleep 20
    wait "${pid}" 2>/dev/null || true
    rc=124
    break
  fi
  sleep 1
done
if (( rc == 0 )); then
  wait "${pid}" || rc=$?
fi
echo "-- Editor finished rc=${rc} after $(( $(date +%s) - start ))s" >&2

python3 - "${out}" <<'PY' >&2
import os, sys
from PIL import Image
out = sys.argv[1]
limit = 300 * 1024
for name in sorted(os.listdir(out)):
    if not name.endswith(".png"):
        continue
    path = os.path.join(out, name)
    if os.path.getsize(path) <= limit:
        print(f"{name}: {os.path.getsize(path)} bytes")
        continue
    image = Image.open(path).convert("RGB")
    width = min(1600, image.width)
    while True:
        scaled = image if width == image.width else image.resize((width, round(image.height * width / image.width)), Image.LANCZOS)
        scaled.save(path, optimize=True)
        if os.path.getsize(path) <= limit:
            break
        scaled.quantize(colors=256, method=Image.Quantize.MEDIANCUT).save(path, optimize=True)
        if os.path.getsize(path) <= limit or width <= 640:
            break
        width = int(width * 0.85)
    print(f"{name}: {scaled.width}x{scaled.height} {os.path.getsize(path)} bytes")
PY
echo "${out}"
exit "${rc}"
HOST
)"
editor_rc=$?
set -e
out_dir="$(printf '%s\n' "${remote_out}" | tail -n 1)"
if [[ -z "${out_dir}" || "${out_dir}" != /* ]]; then
  echo "evidence-p2.3.sh: the host run produced no output directory (rc=${editor_rc})" >&2
  exit 1
fi

mkdir -p "${dest}"
rm -f "${dest}"/*.png "${dest}/capture.json"
if (( on_host )); then
  cp -a "${out_dir}/." "${dest}/"
else
  scp -q "${host}:${out_dir}/*.png" "${host}:${out_dir}/capture.json" "${dest}/" || true
fi
host_exec "${host}" "grep -E '\\[P2\\.3 evidence\\]|error CS|Exception' '${out_dir}/editor.log' | tail -n 120" > "${dest}/editor-excerpt.txt" 2>/dev/null || true
count=$(find "${dest}" -maxdepth 1 -name '*.png' | wc -l | tr -d ' ')
echo "-- ${count} PNG(s) in ${dest} (editor rc ${editor_rc}); run directory ${host}:${out_dir}"
if (( editor_rc != 0 || count < 6 )); then
  exit 1
fi
