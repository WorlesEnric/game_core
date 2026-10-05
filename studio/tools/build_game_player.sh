#!/usr/bin/env bash
# build_game_player.sh - build the Hollowmere StandaloneLinux64 IL2CPP graphical player on the Linux host (P3.1,
# W-GAME-06) and, with --gate, run the V1 gate on the same revision.
#
# Usage:   studio/tools/build_game_player.sh <packet-name> [--gate]
# Example: studio/tools/sync-to-host.sh p3.1 && studio/tools/build_game_player.sh p3.1 --gate
#
# The packet clone must exist on the host at ~/wkspace/gc-studio/<packet-name> (studio/tools/sync-to-host.sh puts it
# there). Run from the Mac, the script re-runs itself on the host over non-interactive ssh; run on the host, it works
# directly. Owner rule: nothing is built on the Mac.
#
# What it does on the host:
#   1. records the clone's `git rev-parse HEAD` (the revision every artifact below is tied to);
#   2. runs ONE batchmode Editor under the host-wide Unity lock (studio/tools/unity-batch.sh, timeout 3600 s, one retry
#      on the known hang):  -executeMethod Hollowmere.Build.BuildLinuxPlayer
#        -buildOutput ~/wkspace/gc-studio/<packet>/build/HollowmereLinux -buildRevision <sha>
#      (the method exits the Editor itself, so no -quit);
#   3. writes into ~/wkspace/gc-studio/<packet>/build/:
#        build.log            the Editor log of the build
#        sha256.txt           sha256sum of HollowmereLinux/Hollowmere.x86_64
#        data-manifest.txt    sorted sha256sum of every file under HollowmereLinux/ (relative paths)
#        build-summary.json   revision, Unity version, durations, sizes, file count, executable sha256, result
#      and prints them;
#   4. with --gate: runs the V1 gate tools/run_w7_gate.sh on the SAME clone revision (UNITY, DOTNET=~/.dotnet/dotnet,
#      PROBE_RUNS=2, ARTIFACTS=<clone>/artifacts/w7-gate) while holding ONE host-wide Unity slot for the whole gate
#      (the slot protocol of studio/tools/unity-batch.sh), bounded by `timeout 14400`, with the transcript teed to
#      build/v1-gate-transcript.txt and its exit code + duration recorded in build-summary.json ("gate").
#      NOTE (SADR-017): the gate's qualified profile is the HEADLESS validation player (unity/GameCore.Validation,
#      audio disabled). The graphical Hollowmere player built in step 2 is a separate target; the gate transcript is
#      evidence that the V1 kernel profile still passes on this revision, not a qualification of the game player.
#
# Environment:
#   GC_STUDIO_HOST         ssh host when run off-host (default: myubuntu)
#   GC_STUDIO_REMOTE_BASE  directory under the host home (default: wkspace/gc-studio)
#   UNITY                  Editor binary on the host (default: ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity)
#   GC_STUDIO_UNITY_SLOTS  host-wide concurrent batchmode Editors allowed (default: 3)
#
# After a successful build the player smoke runs the built player headless under xvfb-run -a (-batchmode -nographics -frameLog
# build/smoke/frame-log.csv -autoplay games/hollowmere/Autoplay/smoke.txt) and records it in build-summary.json ("smoke").
#
# Exit codes: 0 build succeeded (and the gate passed with --gate); 1 build failed; 5 the player smoke failed; 4 the gate
# failed; 2 bad usage or missing clone.
set -euo pipefail

usage() {
  sed -n '2,40p' "${BASH_SOURCE[0]:-$0}" 2>/dev/null | sed 's/^# \{0,1\}//' >&2 || true
  exit 2
}

[[ $# -ge 1 ]] || usage
packet="$1"
shift
gate=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --gate) gate=1; shift ;;
    *) echo "build_game_player.sh: unknown argument '$1'" >&2; usage ;;
  esac
done
if ! [[ "${packet}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
  echo "build_game_player.sh: packet name must match [A-Za-z0-9][A-Za-z0-9._-]*: '${packet}'" >&2
  exit 2
fi

host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"
default_unity="${HOME}/Unity/Hub/Editor/6000.0.75f1/Editor/Unity"

# Off the host: re-run this very script on the host.
if [[ "${GC_STUDIO_ON_HOST:-0}" != "1" ]] && { [[ "$(uname -s)" != "Linux" ]] || [[ ! -x "${UNITY:-${default_unity}}" ]]; }; then
  remote_env="GC_STUDIO_ON_HOST=1 GC_STUDIO_REMOTE_BASE=$(printf '%q' "${remote_base}")"
  remote_env+=" GC_STUDIO_UNITY_SLOTS=$(printf '%q' "${GC_STUDIO_UNITY_SLOTS:-3}")"
  if [[ -n "${UNITY:-}" ]]; then
    remote_env+=" UNITY=$(printf '%q' "${UNITY}")"
  fi
  forwarded=("${packet}")
  (( gate == 1 )) && forwarded+=(--gate)
  rc=0
  # The clone's own copy (synced by sync-to-host.sh) runs on the host; stdin is closed so no child can eat it.
  remote_script="\${HOME}/${remote_base}/${packet}/studio/tools/build_game_player.sh"
  # shellcheck disable=SC2029  # quoting is done here on purpose (printf %q)
  ssh -o BatchMode=yes "${host}" "${remote_env} bash ${remote_script} $(printf '%q ' "${forwarded[@]}")" < /dev/null || rc=$?
  exit "${rc}"
fi

unity="${UNITY:-${default_unity}}"
slots="${GC_STUDIO_UNITY_SLOTS:-3}"
clone="${HOME}/${remote_base}/${packet}"
project="${clone}/games/hollowmere"
build_root="${clone}/build"
output="${build_root}/HollowmereLinux"
logs="${clone}/.unity-logs"
slot_dir="${HOME}/${remote_base}/.unity-slots"

if [[ ! -d "${project}/ProjectSettings" || ! -f "${clone}/studio/tools/unity-batch.sh" ]]; then
  echo "build_game_player.sh: no Hollowmere project in ${clone} (run studio/tools/sync-to-host.sh ${packet} first)" >&2
  exit 2
fi
if [[ ! -x "${unity}" ]]; then
  echo "build_game_player.sh: Unity Editor not executable: ${unity}" >&2
  exit 2
fi
for tool in git sha256sum python3 flock timeout pgrep; do
  command -v "${tool}" >/dev/null 2>&1 || { echo "build_game_player.sh: '${tool}' is required" >&2; exit 2; }
done

revision="$(git -C "${clone}" rev-parse HEAD)"
unity_version="$(basename "$(dirname "$(dirname "${unity}")")")"
mkdir -p "${build_root}" "${logs}"
rm -rf "${output}"
mkdir -p "${output}"

echo "== Hollowmere Linux player build =="
echo "clone    : ${clone}"
echo "revision : ${revision}"
echo "unity    : ${unity} (${unity_version})"
echo "output   : ${output}"

build_start="$(date +%s)"
build_rc=0
batch_out="${build_root}/unity-batch.txt"
UNITY="${unity}" GC_STUDIO_REMOTE_BASE="${remote_base}" GC_STUDIO_UNITY_SLOTS="${slots}" \
  bash "${clone}/studio/tools/unity-batch.sh" --project "${project}" --log-dir "${logs}" --label build \
  --timeout 3600 --attempts 2 -- \
  -executeMethod Hollowmere.Build.BuildLinuxPlayer -buildOutput "${output}" -buildRevision "${revision}" \
  2>&1 | tee "${batch_out}" || build_rc=$?
build_seconds=$(( $(date +%s) - build_start ))

editor_log="$(sed -n 's/^RESULT build: .* log \(.*\))$/\1/p' "${batch_out}" | tail -n 1)"
if [[ -n "${editor_log}" && -f "${editor_log}" ]]; then
  cp "${editor_log}" "${build_root}/build.log"
else
  echo "(no Editor log found in the unity-batch output)" > "${build_root}/build.log"
fi
grep -E '^HOLLOWMERE-BUILD ' "${build_root}/build.log" || true

exe="${output}/Hollowmere.x86_64"
result="failed"
exe_sha=""
if (( build_rc == 0 )) && [[ -f "${exe}" ]]; then
  result="succeeded"
  (cd "${output}" && sha256sum Hollowmere.x86_64) > "${build_root}/sha256.txt"
  exe_sha="$(cut -d' ' -f1 "${build_root}/sha256.txt")"
  (cd "${output}" && find . -type f ! -name data-manifest.txt -printf '%P\n' | LC_ALL=C sort | \
    while IFS= read -r file; do sha256sum "${file}"; done) > "${build_root}/data-manifest.txt"
else
  echo "build failed (unity-batch exit ${build_rc}); see ${build_root}/build.log" > "${build_root}/sha256.txt"
  : > "${build_root}/data-manifest.txt"
fi

python3 - "${build_root}" "${output}" "${revision}" "${unity_version}" "${result}" "${build_rc}" "${build_seconds}" "${exe_sha}" <<'PY'
import json, os, sys
root, output, revision, unity, result, rc, seconds, exe_sha = sys.argv[1:9]
files = []
size = 0
for base, _, names in os.walk(output):
    for name in names:
        path = os.path.join(base, name)
        files.append(path)
        size += os.path.getsize(path)
report = {}
report_path = os.path.join(output, "build-report.json")
if os.path.exists(report_path):
    try:
        with open(report_path, encoding="utf-8") as handle:
            report = json.load(handle)
    except ValueError:
        report = {"unreadable": True}
exe = os.path.join(output, "Hollowmere.x86_64")
summary = {
    "revision": revision,
    "unityVersion": unity,
    "result": result,
    "unityBatchExit": int(rc),
    "buildSeconds": int(seconds),
    "executable": "HollowmereLinux/Hollowmere.x86_64",
    "executableSha256": exe_sha,
    "executableBytes": os.path.getsize(exe) if os.path.exists(exe) else 0,
    "outputBytes": size,
    "fileCount": len(files),
    "buildReport": {k: report.get(k) for k in ("result", "platform", "scriptingBackend", "managedStripping", "totalSize", "totalTimeSeconds", "errors", "warnings", "development")},
}
with open(os.path.join(root, "build-summary.json"), "w", encoding="utf-8") as handle:
    json.dump(summary, handle, indent=2, sort_keys=True)
    handle.write("\n")
PY

echo "-- sha256.txt:"
cat "${build_root}/sha256.txt"
echo "-- data-manifest.txt: $(wc -l < "${build_root}/data-manifest.txt") files"
echo "-- build-summary.json:"
cat "${build_root}/build-summary.json"

if [[ "${result}" != "succeeded" ]]; then
  echo "RESULT build_game_player ${packet}: FAIL (build; revision ${revision})"
  exit 1
fi

# Player smoke: the built player headless (-batchmode -nographics) runs games/hollowmere/Autoplay/smoke.txt (boot,
# new game, ~600 frames) with a frame log and must exit 0 within 300 s.
smoke_dir="${build_root}/smoke"
rm -rf "${smoke_dir}"
mkdir -p "${smoke_dir}/saves"
smoke_start="$(date +%s)"
smoke_rc=0
# The Unity 6 Linux player selects its window backend even under -nographics and crashes (SIGSEGV in PlayerMain,
# "window backend is (null)") when no X display is reachable, as in a non-interactive ssh session: run it under a
# private virtual display (xvfb-run -a), never on the shared :1.
smoke_launcher=()
if command -v xvfb-run >/dev/null 2>&1; then
  smoke_launcher=(xvfb-run -a)
fi
timeout --signal=TERM --kill-after=30 300 "${smoke_launcher[@]}" "${exe}" -batchmode -nographics \
  -frameLog "${smoke_dir}/frame-log.csv" -autoplay "${project}/Autoplay/smoke.txt" -saveDir "${smoke_dir}/saves" \
  -logFile "${smoke_dir}/player.log" < /dev/null > "${smoke_dir}/stdout.txt" 2>&1 || smoke_rc=$?
smoke_seconds=$(( $(date +%s) - smoke_start ))
smoke_frames=0
if [[ -f "${smoke_dir}/frame-log.csv" ]]; then
  smoke_frames=$(( $(wc -l < "${smoke_dir}/frame-log.csv") - 1 ))
fi
echo "-- player smoke: exit ${smoke_rc}, ${smoke_seconds}s, ${smoke_frames} frame-log rows; $(grep -E '\[autoplay\] (FAILED|finished|quit)' "${smoke_dir}/player.log" 2>/dev/null | tail -n 1 || true)"
python3 - "${build_root}/build-summary.json" "${smoke_rc}" "${smoke_seconds}" "${smoke_frames}" <<'PY'
import json, sys
path, rc, seconds, frames = sys.argv[1:5]
with open(path, encoding="utf-8") as handle:
    summary = json.load(handle)
summary["smoke"] = {
    "command": "xvfb-run -a Hollowmere.x86_64 -batchmode -nographics -frameLog build/smoke/frame-log.csv -autoplay games/hollowmere/Autoplay/smoke.txt",
    "exit": int(rc),
    "seconds": int(seconds),
    "frameLogRows": int(frames),
    "result": "pass" if int(rc) == 0 and int(frames) >= 600 else "fail",
}
with open(path, "w", encoding="utf-8") as handle:
    json.dump(summary, handle, indent=2, sort_keys=True)
    handle.write("\n")
PY
if (( smoke_rc != 0 || smoke_frames < 600 )); then
  echo "RESULT build_game_player ${packet}: build PASS, player smoke FAIL (exit ${smoke_rc}, ${smoke_frames} frames; ${smoke_dir}/player.log)"
  exit 5
fi

if (( gate == 0 )); then
  echo "RESULT build_game_player ${packet}: PASS (revision ${revision}, ${build_seconds}s, exe sha256 ${exe_sha})"
  exit 0
fi

# ------------------------------------------------------------------------------------------------ V1 gate (--gate)
count_batchmode_editors() {
  local pid n=0
  for pid in $(pgrep -x Unity 2>/dev/null || true); do
    if tr '\0' ' ' < "/proc/${pid}/cmdline" 2>/dev/null | grep -- '-batchmode' | grep -vq 'AssetImportWorker'; then
      n=$((n + 1))
    fi
  done
  echo "${n}"
}

gate_slot_fd=""
acquire_gate_slot() {
  local waited=0 i fd running
  mkdir -p "${slot_dir}"
  while true; do
    for ((i = 1; i <= slots; i++)); do
      exec {fd}>"${slot_dir}/slot${i}.lock"
      if flock -n "${fd}"; then
        running="$(count_batchmode_editors)"
        if (( running < slots )); then
          gate_slot_fd="${fd}"
          printf '%s pid=%s caller=build_game_player-gate packet=%s since=%s\n' "$(hostname)" "$$" "${packet}" "$(date -Is)" \
            > "${slot_dir}/slot${i}.owner"
          echo "-- Unity slot ${i}/${slots} acquired for the whole V1 gate (${running} batchmode Editor(s) already running)"
          return 0
        fi
        flock -u "${fd}"
      fi
      exec {fd}>&-
    done
    if (( waited % 60 == 0 )); then
      echo "-- waiting for a Unity slot for the V1 gate (${slots} max host-wide; waited ${waited}s)"
    fi
    sleep 10
    waited=$((waited + 10))
  done
}

transcript="${build_root}/v1-gate-transcript.txt"
acquire_gate_slot
gate_start="$(date +%s)"
gate_rc=0
{
  echo "== V1 gate (tools/run_w7_gate.sh) on revision ${revision} =="
  echo "started   : $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "clone     : ${clone}"
  echo "profile   : headless validation player (SADR-017); the graphical Hollowmere player is a separate target"
} | tee "${transcript}"
(
  cd "${clone}"
  UNITY="${unity}" DOTNET="${HOME}/.dotnet/dotnet" PROBE_RUNS=2 ARTIFACTS="${clone}/artifacts/w7-gate" \
    timeout --signal=TERM --kill-after=120 14400 bash tools/run_w7_gate.sh
) 2>&1 | tee -a "${transcript}" || gate_rc=$?
gate_seconds=$(( $(date +%s) - gate_start ))
echo "finished  : $(date -u +%Y-%m-%dT%H:%M:%SZ) exit=${gate_rc} seconds=${gate_seconds}" | tee -a "${transcript}"
if [[ -n "${gate_slot_fd}" ]]; then
  flock -u "${gate_slot_fd}" || true
fi

python3 - "${build_root}/build-summary.json" "${gate_rc}" "${gate_seconds}" "${revision}" <<'PY'
import json, sys
path, rc, seconds, revision = sys.argv[1:5]
with open(path, encoding="utf-8") as handle:
    summary = json.load(handle)
summary["gate"] = {
    "script": "tools/run_w7_gate.sh",
    "revision": revision,
    "exit": int(rc),
    "seconds": int(seconds),
    "result": "pass" if int(rc) == 0 else "fail",
    "profile": "headless validation player (SADR-017); graphical Hollowmere player is a separate target",
    "transcript": "build/v1-gate-transcript.txt",
}
with open(path, "w", encoding="utf-8") as handle:
    json.dump(summary, handle, indent=2, sort_keys=True)
    handle.write("\n")
PY

if (( gate_rc != 0 )); then
  echo "RESULT build_game_player ${packet}: build PASS, V1 gate FAIL (exit ${gate_rc}, ${gate_seconds}s; ${transcript})"
  exit 4
fi
echo "RESULT build_game_player ${packet}: PASS (build + V1 gate on revision ${revision}; gate ${gate_seconds}s)"
exit 0
