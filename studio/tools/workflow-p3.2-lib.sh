#!/usr/bin/env bash
# workflow-p3.2-lib.sh - shared driver of the P3.2 AI-workflow recordings (docs/studio/07 W-AI-01..06, W-VOICE-01,
# B-AGENT-UX). The per-workflow scripts (studio/tools/workflow-p3.2-<name>.sh) call it; it can also be run directly:
#
#   studio/tools/workflow-p3.2-lib.sh <workflow> [--packet <name>] [--voice] [--env KEY=VALUE]... [--no-copy]
#
# <workflow>: smoke | text | robe | narrative | reopen | voice | batch | honesty | mech-a | mech-b (Workflows.cs).
#
# From the Mac (after studio/tools/sync-to-host.sh <packet>), the script re-runs itself on the host over ssh and copies
# the run folder back to artifacts/studio/workflows/P3.2/runs/<workflow>-<stamp>/. On the host (Linux) it works directly
# and leaves the run folder under ~/wkspace/gc-studio/<packet>-runs/ (nothing runs on the Mac).
#
# On the host it:
#   1. checks the node (`systemctl --user is-active etosd`; when down it waits and retries every 60 s for up to 30 min,
#      never starting or stopping etosd itself) and that the app key file exists (path from GAMECORE_ETOS_KEY_FILE,
#      default ~/.config/gamecore-studio/app-key.json; the key is never printed or copied - P2.2's Studio session
#      reads the file itself);
#   2. takes ONE of the host-wide Unity slots (the flock protocol of unity-compile.sh / unity-batch.sh; at most
#      GC_STUDIO_UNITY_SLOTS Editors host-wide) and removes a stale Temp/UnityLockfile when no Editor has the project;
#   3. with --voice: a PipeWire virtual microphone (pw-loopback: sink gc_p32_sink -> source gc_p32_mic), made the default
#      source for the run (Unity's default microphone), and a player that plays <run>/voice/<file> into the sink when
#      the Editor writes <run>/voice/play-<label>; the loopback (and with it the default) is removed afterwards;
#   4. starts ONE interactive Editor (not batch mode) on DISPLAY (default :1):
#        Unity -projectPath <clone>/games/hollowmere -executeMethod Hollowmere.P3_2.Workflows.WorkflowRunner.Run
#      with GCS_P32_WORKFLOW and GCS_P32_OUT; a watchdog (WORKFLOW_TIMEOUT s, default 5400; log silence
#      WORKFLOW_SILENCE_TIMEOUT s, default 900) kills only that PID; it is retried once only when nothing was sent yet
#      (a retry after a submit would repeat paid work);
#   5. afterwards: `etos task show --json` and `etos budget --task --json` for every task id the run recorded
#      (etos/<task>.json, usage.json with token totals), the frame sequence encoded as recording.mp4 (ffmpeg, <= 25 MB,
#      frames removed after encoding; keyframes kept, each shrunk below 300 KB), and a scan of every file for the key and
#      for credential shapes (etk_, ett_, sk-, Bearer) - a hit is redacted in place and fails the run.
#
# Environment: GC_STUDIO_HOST (myubuntu), GC_STUDIO_REMOTE_BASE (wkspace/gc-studio), GC_STUDIO_UNITY_SLOTS (3), UNITY,
# WORKFLOW_DISPLAY (:1), WORKFLOW_TIMEOUT, WORKFLOW_SILENCE_TIMEOUT, GAMECORE_ETOS_KEY_FILE.
# Exit codes: 0 the Editor finished every step; 1 a step failed / the Editor was killed / a credential was found;
# 2 bad usage or a missing prerequisite.
set -euo pipefail

usage() {
  sed -n '2,36p' "${BASH_SOURCE[0]:-$0}" 2>/dev/null | sed 's/^# \{0,1\}//' >&2 || true
  exit 2
}

[[ $# -ge 1 ]] || usage
workflow="$1"
shift
packet="p3.2"
voice=0
copy=1
extra_env=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --packet) [[ $# -ge 2 ]] || usage; packet="$2"; shift 2 ;;
    --voice) voice=1; shift ;;
    --no-copy) copy=0; shift ;;
    --env) [[ $# -ge 2 ]] || usage; extra_env+=("$2"); shift 2 ;;
    *) echo "workflow-p3.2-lib.sh: unknown argument '$1'" >&2; usage ;;
  esac
done
[[ "${workflow}" =~ ^[a-z][a-z0-9-]*$ ]] || { echo "bad workflow '${workflow}'" >&2; exit 2; }
[[ "${packet}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]] || { echo "bad packet '${packet}'" >&2; exit 2; }
for kv in "${extra_env[@]+"${extra_env[@]}"}"; do
  [[ "${kv}" =~ ^GCS_P32_[A-Z0-9_]+=[A-Za-z0-9_./:@%+=,-]*$ ]] || { echo "bad --env '${kv}' (GCS_P32_* only)" >&2; exit 2; }
done

host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"

# ------------------------------------------------------------------------------------------------ Mac: relay to host
if [[ "${GC_STUDIO_ON_HOST:-0}" != "1" && "$(uname -s)" != "Linux" ]]; then
  root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
  forwarded=("${workflow}" --packet "${packet}" --no-copy)
  (( voice )) && forwarded+=(--voice)
  for kv in "${extra_env[@]+"${extra_env[@]}"}"; do forwarded+=(--env "${kv}"); done
  remote_env="GC_STUDIO_ON_HOST=1 GC_STUDIO_REMOTE_BASE=$(printf '%q' "${remote_base}")"
  for name in GC_STUDIO_UNITY_SLOTS UNITY WORKFLOW_DISPLAY WORKFLOW_TIMEOUT WORKFLOW_SILENCE_TIMEOUT WORKFLOW_KEEP_INDEX_CACHE GAMECORE_ETOS_KEY_FILE; do
    [[ -n "${!name:-}" ]] && remote_env+=" ${name}=$(printf '%q' "${!name}")"
  done
  rc=0
  log="$(mktemp "${TMPDIR:-/tmp}/workflow-p3.2-${workflow}.XXXXXX")"
  # shellcheck disable=SC2029
  ssh -o BatchMode=yes "${host}" "${remote_env} bash -s -- $(printf '%q ' "${forwarded[@]}")" < "${BASH_SOURCE[0]}" | tee "${log}" || rc=$?
  out_dir="$(grep -E '^RUN_DIR ' "${log}" | tail -n 1 | cut -d' ' -f2- || true)"
  if [[ -n "${out_dir}" ]]; then
    dest="${root}/artifacts/studio/workflows/P3.2/runs/$(basename "${out_dir}")"
    mkdir -p "${dest}"
    rsync -a --exclude frames --exclude "editor-a*.log" "${host}:${out_dir}/" "${dest}/" || { echo "-- copying the run folder failed (rsync exit $?); it stays on the host at ${out_dir}" >&2; rc=1; }
    echo "-- copied the run folder to ${dest}"
  else
    echo "-- the host run printed no RUN_DIR" >&2
  fi
  exit "${rc}"
fi

# ----------------------------------------------------------------------------------------------------- host side
base="${HOME}/${remote_base}/${packet}"
project_dir="${base}/games/hollowmere"
unity="${UNITY:-${HOME}/Unity/Hub/Editor/6000.0.75f1/Editor/Unity}"
etos_bin="${HOME}/.local/opt/etos/bin/etos"
export ETOS_ROOT="${ETOS_ROOT:-${HOME}/.local/share/etos-studio}"
key_file="${GAMECORE_ETOS_KEY_FILE:-${HOME}/.config/gamecore-studio/app-key.json}"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
runs="${HOME}/${remote_base}/${packet}-runs"
out="${runs}/${workflow}-${stamp}"
slot_dir="${HOME}/${remote_base}/.unity-slots"
slots="${GC_STUDIO_UNITY_SLOTS:-3}"
display="${WORKFLOW_DISPLAY:-:1}"
timeout_s="${WORKFLOW_TIMEOUT:-5400}"
silence_s="${WORKFLOW_SILENCE_TIMEOUT:-900}"
mkdir -p "${out}" "${slot_dir}"
echo "RUN_DIR ${out}"
[[ -x "${unity}" ]] || { echo "no Unity at ${unity}" >&2; exit 2; }
[[ -f "${project_dir}/Packages/manifest.json" ]] || { echo "no project at ${project_dir} (sync-to-host.sh ${packet})" >&2; exit 2; }
[[ -r "${key_file}" ]] || { echo "no readable app key file at ${key_file}" >&2; exit 2; }
sha="$(git -C "${base}" rev-parse HEAD </dev/null)"

waited=0
until systemctl --user is-active --quiet etosd; do
  if (( waited >= 1800 )); then
    echo "etosd stayed inactive for 30 min; recording the gap" | tee "${out}/node-down.txt" >&2
    exit 2
  fi
  echo "-- etosd is not active (another packet may have stopped it); retrying in 60 s (waited ${waited}s)" >&2
  sleep 60
  waited=$((waited + 60))
done
echo "-- node: etosd active; key file present (not read here); clone ${base} at ${sha}"

count_editors() {
  local pid n=0
  for pid in $(pgrep -x Unity 2>/dev/null || true); do
    if ! tr '\0' ' ' < "/proc/${pid}/cmdline" 2>/dev/null | grep -q 'AssetImportWorker'; then
      n=$((n + 1))
    fi
  done
  echo "${n}"
}

slot_fd=""
waited=0
while [[ -z "${slot_fd}" ]]; do
  for ((i = 1; i <= slots; i++)); do
    exec {fd}>"${slot_dir}/slot${i}.lock"
    if flock -n "${fd}"; then
      if (( $(count_editors) < slots )); then
        slot_fd="${fd}"
        printf '%s pid=%s packet=%s project=games/hollowmere workflow=%s since=%s\n' "$(hostname)" "$$" "${packet}" "${workflow}" "$(date -Is)" > "${slot_dir}/slot${i}.owner"
        echo "-- Unity slot ${i}/${slots} acquired" >&2
        break
      fi
      flock -u "${fd}"
    fi
    exec {fd}>&-
  done
  if [[ -z "${slot_fd}" ]]; then
    (( waited % 120 == 0 )) && echo "-- waiting for a Unity slot (waited ${waited}s)" >&2
    sleep 10
    waited=$((waited + 10))
  fi
done

if [[ -f "${project_dir}/Temp/UnityLockfile" ]]; then
  for pid in $(pgrep -x Unity 2>/dev/null || true); do
    if tr '\0' ' ' < "/proc/${pid}/cmdline" 2>/dev/null | grep -q -- "${project_dir}"; then
      echo "the project is open in Unity pid ${pid}; not starting a second Editor" >&2
      exit 1
    fi
  done
  rm -f "${project_dir}/Temp/UnityLockfile"
  echo "-- removed a stale Temp/UnityLockfile" >&2
fi

# The Studio restores its semantic index from Library/GameCoreStudio/index.sources.json without checking that the cache
# covers the current assets (SemanticIndexService.LoadCache); a cache written by an earlier, narrower run leaves NPC,
# dialogue, quest and item definitions out of the index (P3.2 defect D-INDEX-CACHE). Every workflow run starts from a
# full rebuild; set WORKFLOW_KEEP_INDEX_CACHE=1 to reproduce the defect.
if [[ "${WORKFLOW_KEEP_INDEX_CACHE:-0}" != "1" ]]; then
  for cache in index.sources.json index.json; do
    if [[ -f "${project_dir}/Library/GameCoreStudio/${cache}" ]]; then
      rm -f "${project_dir}/Library/GameCoreStudio/${cache}"
      echo "-- removed the Studio index cache Library/GameCoreStudio/${cache} (full rebuild at start-up)" >&2
    fi
  done
fi

export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
loopback_pid=""
player_pid=""
cleanup() {
  [[ -n "${player_pid}" ]] && kill "${player_pid}" 2>/dev/null || true
  [[ -n "${loopback_pid}" ]] && kill "${loopback_pid}" 2>/dev/null || true
}
trap cleanup EXIT

if (( voice )); then
  mkdir -p "${out}/voice"
  pw-loopback --capture-props='media.class=Audio/Sink node.name=gc_p32_sink node.description=GC_P3.2_sink' \
    --playback-props='media.class=Audio/Source node.name=gc_p32_mic node.description=GC_P3.2_mic' \
    > "${out}/voice/pw-loopback.log" 2>&1 &
  loopback_pid=$!
  sleep 2
  mic_id="$(wpctl status </dev/null 2>/dev/null | sed -n '/Sources:/,/Source endpoints:/p' | grep -m1 'GC_P3.2_mic' | grep -oE '[0-9]+\.' | head -n1 | tr -d '.')"
  if [[ -n "${mic_id}" ]]; then
    wpctl set-default "${mic_id}" </dev/null && echo "-- virtual microphone GC_P3.2_mic (node ${mic_id}) is the default source for this run"
  else
    echo "-- the virtual microphone did not appear in wpctl status" >&2
  fi
  wpctl status </dev/null 2>/dev/null | sed -n '/Audio/,/Video/p' > "${out}/voice/wpctl-status.txt" || true
  (
    played=""
    for _ in $(seq 1 7200); do
      for marker in "${out}"/voice/play-*; do
        [[ -f "${marker}" ]] || continue
        label="${marker##*/play-}"
        case " ${played} " in *" ${label} "*) continue ;; esac
        wav="${out}/voice/$(head -n1 "${marker}" | tr -d '\r\n')"
        sleep 1
        if [[ -f "${wav}" ]]; then
          pw-play --target gc_p32_sink "${wav}" >> "${out}/voice/pw-play.log" 2>&1 || echo "pw-play exit $?" >> "${out}/voice/pw-play.log"
        else
          echo "missing ${wav}" >> "${out}/voice/pw-play.log"
        fi
        date -u +%FT%T.%3NZ > "${out}/voice/played-${label}"
        played="${played} ${label}"
      done
      sleep 0.5
    done
  ) &
  player_pid=$!
fi

editor_env=(DISPLAY="${display}" GCS_P32_WORKFLOW="${workflow}" GCS_P32_OUT="${out}" GAMECORE_ETOS_KEY_FILE="${key_file}")
for kv in "${extra_env[@]+"${extra_env[@]}"}"; do editor_env+=("${kv}"); done
rc=0
for attempt in 1 2; do
  log="${out}/editor-a${attempt}.log"
  env "${editor_env[@]}" nohup "${unity}" -projectPath "${project_dir}" \
    -executeMethod Hollowmere.P3_2.Workflows.WorkflowRunner.Run -logFile "${log}" >/dev/null 2>&1 &
  pid=$!
  echo "-- attempt ${attempt}/2: interactive Editor pid ${pid} on ${display}, workflow ${workflow}, output ${out}" >&2
  start=$(date +%s)
  last_size=-1
  last_change=${start}
  rc=0
  while kill -0 "${pid}" 2>/dev/null; do
    now=$(date +%s)
    size=$(stat -c %s "${log}" 2>/dev/null || echo 0)
    if [[ "${size}" != "${last_size}" ]]; then
      last_size="${size}"
      last_change=${now}
    fi
    if (( now - start > timeout_s || now - last_change > silence_s )); then
      echo "-- no progress (timeout ${timeout_s}s / log silent ${silence_s}s); killing pid ${pid}" >&2
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
  echo "-- attempt ${attempt} finished rc=${rc} after $(( $(date +%s) - start ))s" >&2
  if (( rc != 124 )); then
    break
  fi
  if grep -q -- '-sent"' "${out}/run-log.jsonl" 2>/dev/null; then
    echo "-- not retrying: the run already sent a request (a retry would repeat paid work)" >&2
    break
  fi
  rm -f "${project_dir}/Temp/UnityLockfile"
done
cleanup
loopback_pid=""
player_pid=""
grep -hE '\[P3\.2 workflow\]|error CS|Exception' "${out}"/editor-a*.log 2>/dev/null | tail -n 400 > "${out}/editor-excerpt.log" || true

# Usage of every task the run recorded.
mkdir -p "${out}/etos"
find "${out}" -name task-ids.txt -exec cat {} + 2>/dev/null | grep -E '^t[0-9a-f]+$' | sort -u > "${out}/etos/task-ids.txt" || true
while read -r task; do
  [[ -n "${task}" ]] || continue
  {
    echo '{"show":'
    "${etos_bin}" task show "${task}" --json </dev/null 2>/dev/null || echo 'null'
    echo ',"budget":'
    "${etos_bin}" budget --task "${task}" --json </dev/null 2>/dev/null || echo 'null'
    echo '}'
  } > "${out}/etos/${task}.json"
done < "${out}/etos/task-ids.txt"
python3 - "${out}" <<'PY'
import json, os, sys
out = sys.argv[1]
tasks, totals = [], {"input_tokens": 0, "output_tokens": 0, "tokens": 0, "micro_usd": 0}
for name in sorted(os.listdir(os.path.join(out, "etos"))):
    if not name.endswith(".json"):
        continue
    try:
        doc = json.load(open(os.path.join(out, "etos", name)))
    except ValueError:
        continue
    show = (doc.get("show") or {}).get("task") or {}
    used = ((doc.get("budget") or {}).get("task") or {}).get("used") or {}
    for key in totals:
        totals[key] += int(used.get(key) or 0)
    tasks.append({"task": name[:-5], "worker": show.get("worker"), "status": show.get("status"), "error": show.get("error"),
                  "seconds": round(((show.get("ended_at") or 0) - (show.get("started_at") or 0)) / 1000.0, 1) if show.get("ended_at") else None,
                  "used": used, "result": (show.get("result") or {}).get("text")})
json.dump({"tasks": tasks, "totals": totals}, open(os.path.join(out, "usage.json"), "w"), indent=2)
print("-- usage: %d task(s), %d input + %d output tokens" % (len(tasks), totals["input_tokens"], totals["output_tokens"]))
PY

# Recording: frames -> mp4 (<= 25 MB); keyframes shrunk.
if compgen -G "${out}/frames/*.png" > /dev/null; then
  ffmpeg -nostdin -loglevel error -y -framerate 2 -pattern_type glob -i "${out}/frames/*.png" \
    -vf "scale='min(1600,iw)':-2:flags=lanczos,pad=ceil(iw/2)*2:ceil(ih/2)*2,format=yuv420p" -c:v libx264 -crf 30 -preset veryfast \
    "${out}/recording.mp4" 2> "${out}/ffmpeg.log" || echo "-- ffmpeg failed (see ffmpeg.log)" >&2
  if [[ -f "${out}/recording.mp4" ]]; then
    size=$(stat -c %s "${out}/recording.mp4")
    frames=$(ls "${out}/frames" | wc -l)
    if (( size > 25 * 1024 * 1024 )); then
      mkdir -p "${runs}/videos"
      mv "${out}/recording.mp4" "${runs}/videos/${workflow}-${stamp}.mp4"
      sha256sum "${runs}/videos/${workflow}-${stamp}.mp4" > "${out}/recording.external.txt"
      echo "-- recording is ${size} bytes: kept on the host (${out}/recording.external.txt)"
    else
      echo "-- recording.mp4: ${frames} frames, ${size} bytes"
    fi
    rm -rf "${out}/frames"
  fi
fi
python3 - "${out}" <<'PY' >&2 || true
import os, sys
from PIL import Image
folder = os.path.join(sys.argv[1], "keyframes")
if os.path.isdir(folder):
    for name in sorted(os.listdir(folder)):
        if not name.endswith(".png"):
            continue
        path = os.path.join(folder, name)
        image = Image.open(path).convert("RGB")
        width = min(1600, image.width)
        while True:
            scaled = image if width == image.width else image.resize((width, round(image.height * width / image.width)), Image.LANCZOS)
            scaled.save(path, optimize=True)
            if os.path.getsize(path) <= 300 * 1024:
                break
            scaled.quantize(colors=256).save(path, optimize=True)
            if os.path.getsize(path) <= 300 * 1024 or width <= 640:
                break
            width = int(width * 0.85)
PY

# Credential scan (the key itself and credential shapes), redacted in place.
scan_rc=0
python3 - "${out}" "${key_file}" <<'PY' || scan_rc=$?
import json, os, re, sys
root, key_file = sys.argv[1], sys.argv[2]
raw = open(key_file, encoding="utf-8").read().strip()
try:
    key = json.loads(raw).get("key", "")
except ValueError:
    key = raw
shape = re.compile(r"(?:etk_|ett_|etp_|eta_)[A-Za-z0-9_\-.~+/=]{4,}|\bsk-[A-Za-z0-9]{8,}|(?i:bearer)\s+[A-Za-z0-9]")
hits = 0
for folder, _, files in os.walk(root):
    for name in files:
        if name.endswith((".png", ".mp4", ".wav")):
            continue
        path = os.path.join(folder, name)
        try:
            text = open(path, encoding="utf-8", errors="replace").read()
        except OSError:
            continue
        original = text
        if key and key in text:
            print("KEY FOUND in " + os.path.relpath(path, root) + " (redacted in place)")
            text = text.replace(key, "[redacted]")
            hits += 1
        text = shape.sub("[redacted]", text)
        if text != original:
            print("credential shape redacted in " + os.path.relpath(path, root))
            open(path, "w", encoding="utf-8").write(text)
            hits += 1
sys.exit(1 if hits else 0)
PY
(( scan_rc == 0 )) || { echo "-- credential material was found and redacted; failing the run" >&2; rc=1; }

printf '{"workflow":"%s","revision":"%s","host":"%s","display":"%s","editorExit":%s,"stamp":"%s"}\n' \
  "${workflow}" "${sha}" "$(hostname)" "${display}" "${rc}" "${stamp}" > "${out}/run.json"
echo "-- files:"
(cd "${out}" && find . -maxdepth 2 -type f | sort | head -n 200 | sed 's/^/   /')
echo "RUN_DIR ${out}"
exit "${rc}"
