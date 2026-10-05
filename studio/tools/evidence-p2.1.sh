#!/usr/bin/env bash
# evidence-p2.1.sh - graphical evidence of the P2.1 Studio UI on the Linux host's display (owner rule: never on the Mac).
#
# Usage:   studio/tools/evidence-p2.1.sh [<packet-name>] [<project-rel-path>]
# Default: studio/tools/evidence-p2.1.sh p2.1 games/hollowmere
#
# Run from the Mac after studio/tools/sync-to-host.sh <packet-name> (and one successful unity-compile.sh run, so the
# project's Library is warm). What it does:
#   1. on the host: takes one of the GC_STUDIO_UNITY_SLOTS host-wide Unity slots (the flock files unity-compile.sh
#      uses; every running Editor counts, interactive or batch), removes a stale Temp/UnityLockfile when no Editor
#      has the project open, and starts ONE interactive Editor (not batch mode) on DISPLAY=:1:
#        Unity -projectPath <copy> -executeMethod Hollowmere.P2_1.Evidence.StudioUiEvidence.Run -logFile <out>/editor.log
#      The evidence entry (games/hollowmere/Assets/Hollowmere/Tests/P2_1/Editor/StudioUiEvidence.cs) walks the steps
#      (first-run guide, Studio layout, select the Village Well, inspect hover, marquee, point-at, candidate preview,
#      apply, undo, prompt bar, Play mode on the player camera with W routed through the Input System and the pump
#      indicator, Maren and the well selected in Play, pause). After each step it composes the Studio windows' own
#      pixels into a PNG (never the desktop, so nothing else on the host display can appear) and finally exits;
#   2. waits for that Editor's PID (EVIDENCE_TIMEOUT seconds, default 1800); on timeout it kills only that PID;
#   3. shrinks every PNG below 300 KB with PIL (resize to <= 1600 px wide, then palette quantization if needed);
#   4. copies the PNGs and evidence-log.jsonl to artifacts/studio/evidence/P2.1/ and writes README.md there (index
#      with step, file, UTC time, caption, pump readout, B-SELECT timings, commit sha, host).
#
# Environment: GC_STUDIO_HOST (default myubuntu), GC_STUDIO_REMOTE_BASE (default wkspace/gc-studio),
# GC_STUDIO_UNITY_SLOTS (default 3), UNITY (default ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity on the host),
# EVIDENCE_DISPLAY (default :1), EVIDENCE_TIMEOUT (default 1800), GCS_FLIP (1 flips window grabs vertically when a
# graphics driver returns them bottom-up; default 0).
#
# Exit codes: 0 evidence collected and every step succeeded; 1 a step failed, the Editor timed out or fewer than
# 8 screenshots were produced; 2 bad usage.
set -euo pipefail

packet="${1:-p2.1}"
project="${2:-games/hollowmere}"
if ! [[ "${packet}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
  echo "evidence-p2.1.sh: bad packet name '${packet}'" >&2
  exit 2
fi

host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
dest="${root}/artifacts/studio/evidence/P2.1"
sha="$(cd "${root}" && /usr/bin/git rev-parse HEAD)"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"

echo "-- evidence for ${packet}/${project} at ${sha} on ${host}"
set +e
remote_out="$(ssh "${host}" \
  "PACKET='${packet}' PROJECT='${project}' REMOTE_BASE='${remote_base}' STAMP='${stamp}' SLOTS='${GC_STUDIO_UNITY_SLOTS:-3}' UNITY_BIN='${UNITY:-}' EVIDENCE_DISPLAY='${EVIDENCE_DISPLAY:-:1}' EVIDENCE_TIMEOUT='${EVIDENCE_TIMEOUT:-1800}' GCS_FLIP='${GCS_FLIP:-0}' bash -s" <<'HOST'
set -euo pipefail
unity="${UNITY_BIN:-${HOME}/Unity/Hub/Editor/6000.0.75f1/Editor/Unity}"
base="${HOME}/${REMOTE_BASE}/${PACKET}"
project_dir="${base}/${PROJECT%/}"
out="${base}/.evidence/P2.1-${STAMP}"
slot_dir="${HOME}/${REMOTE_BASE}/.unity-slots"
mkdir -p "${out}" "${slot_dir}"
[[ -x "${unity}" ]] || { echo "no Unity at ${unity}" >&2; exit 2; }
[[ -f "${project_dir}/Packages/manifest.json" ]] || { echo "no project at ${project_dir}" >&2; exit 2; }

count_editors() {
  local pid n=0
  for pid in $(pgrep -x Unity 2>/dev/null || true); do
    if ! tr '\0' ' ' < "/proc/${pid}/cmdline" 2>/dev/null | grep -q 'AssetImportWorker'; then
      n=$((n + 1))
    fi
  done
  echo "${n}"
}

waited=0
slot_fd=""
while [[ -z "${slot_fd}" ]]; do
  for ((i = 1; i <= SLOTS; i++)); do
    exec {fd}>"${slot_dir}/slot${i}.lock"
    if flock -n "${fd}"; then
      if (( $(count_editors) < SLOTS )); then
        slot_fd="${fd}"
        printf '%s pid=%s packet=%s project=%s evidence since=%s\n' "$(hostname)" "$$" "${PACKET}" "${PROJECT}" "$(date -Is)" > "${slot_dir}/slot${i}.owner"
        echo "-- Unity slot ${i}/${SLOTS} acquired" >&2
        break
      fi
      flock -u "${fd}"
    fi
    exec {fd}>&-
  done
  if [[ -z "${slot_fd}" ]]; then
    (( waited % 60 == 0 )) && echo "-- waiting for a Unity slot (waited ${waited}s)" >&2
    sleep 10
    waited=$((waited + 10))
  fi
done

if [[ -f "${project_dir}/Temp/UnityLockfile" ]]; then
  holder=""
  for pid in $(pgrep -x Unity 2>/dev/null || true); do
    if tr '\0' ' ' < "/proc/${pid}/cmdline" 2>/dev/null | grep -q -- "${project_dir}"; then
      holder="${pid}"
    fi
  done
  if [[ -n "${holder}" ]]; then
    echo "project is open in Unity pid ${holder}; not starting a second Editor" >&2
    exit 1
  fi
  rm -f "${project_dir}/Temp/UnityLockfile"
  echo "-- removed a stale Temp/UnityLockfile" >&2
fi

screen="$(xdpyinfo -display "${EVIDENCE_DISPLAY}" 2>/dev/null | awk '/dimensions:/{print $2}')"
DISPLAY="${EVIDENCE_DISPLAY}" GCS_EVIDENCE_DIR="${out}" GCS_FLIP="${GCS_FLIP}" nohup "${unity}" -projectPath "${project_dir}" \
  -executeMethod Hollowmere.P2_1.Evidence.StudioUiEvidence.Run -logFile "${out}/editor.log" >/dev/null 2>&1 &
pid=$!
echo "-- interactive Editor pid ${pid} on ${EVIDENCE_DISPLAY} (${screen}); output ${out}" >&2
start=$(date +%s)
rc=0
while kill -0 "${pid}" 2>/dev/null; do
  if (( $(date +%s) - start > EVIDENCE_TIMEOUT )); then
    echo "-- timeout after ${EVIDENCE_TIMEOUT}s; killing pid ${pid}" >&2
    kill -TERM "${pid}" 2>/dev/null || true
    sleep 20
    kill -KILL "${pid}" 2>/dev/null || true
    rc=124
    break
  fi
  sleep 5
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
  echo "evidence-p2.1.sh: the host run produced no output directory (rc=${editor_rc})" >&2
  exit 1
fi

mkdir -p "${dest}"
rm -f "${dest}"/*.png "${dest}/evidence-log.jsonl"
scp -q "${host}:${out_dir}/*.png" "${host}:${out_dir}/evidence-log.jsonl" "${dest}/" || true
ssh "${host}" "grep -E '\\[P2\\.1 evidence\\]|error CS|Exception' '${out_dir}/editor.log' | tail -n 80" > "${TMPDIR:-/tmp}/evidence-p2.1-editor-excerpt.txt" 2>/dev/null || true
echo "-- editor log excerpt: ${TMPDIR:-/tmp}/evidence-p2.1-editor-excerpt.txt"

python3 - "${dest}" "${sha}" "${host}" "${out_dir}" "${editor_rc}" <<'PY'
import json, os, sys
dest, sha, host, out_dir, rc = sys.argv[1:6]
entries = []
log = os.path.join(dest, "evidence-log.jsonl")
if os.path.exists(log):
    with open(log, encoding="utf-8") as handle:
        entries = [json.loads(line) for line in handle if line.strip()]
pngs = sorted(name for name in os.listdir(dest) if name.endswith(".png"))
lines = [
    "# P2.1 studio-ui graphical evidence",
    "",
    f"Commit `{sha}`, host `{host}` (interactive Unity 6000.0.75f1 on display :1, not batch mode), run directory `{out_dir}`, editor exit code {rc}.",
    "Produced by `studio/tools/evidence-p2.1.sh` driving `Hollowmere.P2_1.Evidence.StudioUiEvidence.Run`.",
    "",
    "| Step | Screenshot | UTC | What it shows | Viewport | Pump readout |",
    "|---|---|---|---|---|---|",
]
for entry in entries:
    if "file" not in entry:
        continue
    size = os.path.getsize(os.path.join(dest, entry["file"])) // 1024 if os.path.exists(os.path.join(dest, entry["file"])) else "missing"
    viewport = f"{entry.get('mode', '')} {entry.get('area', '')} pt, RT {entry.get('texture', '-')}, render {entry.get('render', '-')}"
    lines.append(f"| {entry['step']} | [{entry['file']}]({entry['file']}) ({size} KB) | {entry['utc']} | {entry['caption']} | {viewport} | {entry.get('pump', '-')} |")
lines += ["", "## B-SELECT timings (picking queries, Stopwatch, ms)", ""]
selects = [entry.get("bSelect") for entry in entries if entry.get("bSelect")]
lines.append(f"Last cumulative report: `{selects[-1]}`" if selects else "No picking samples were recorded.")
problems = [entry for entry in entries if entry.get("problem") or entry.get("name") == "error"]
lines += ["", "## Problems", ""]
lines += [f"- step {entry['step']}: {entry.get('problem') or entry.get('caption')}" for entry in problems] or ["None."]
lines += ["", f"{len(pngs)} screenshot(s), each under 300 KB. Each is composed from the Studio windows' own pixels (no desktop capture); no gateway key is configured in this run, so no secret can be on screen."]
with open(os.path.join(dest, "README.md"), "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("\n".join(lines))
failed = bool(problems) or int(rc) != 0 or len(pngs) < 8
sys.exit(1 if failed else 0)
PY
