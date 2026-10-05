#!/usr/bin/env bash
# live-etos-tests.sh - P2.2 live verification of the Studio etos client against the REAL node on the Studio host.
#
# Usage:
#   studio/tools/live-etos-tests.sh <packet-name> [--skip-dotnet] [--skip-unity] [--unity-filter <regex>]
#                                   [--dotnet-filter <expr>] [--no-mic]
# Example:
#   studio/tools/live-etos-tests.sh p2.2
#
# The packet's clone must be on the host (studio/tools/sync-to-host.sh <packet-name>). Run from the Mac, the script
# re-runs itself on the host over non-interactive ssh; on the host it works directly. Nothing runs on the Mac.
#
# What it does on the host:
#   1. checks the node (`systemctl --user is-active etosd`) and that the app key file exists - the key is never
#      printed, echoed, exported in a printed command, or copied; only the file path is passed to the tests in
#      GAMECORE_ETOS_KEY_FILE;
#   2. dotnet live tests (Category=Live of GameCore.Studio.Etos.Client.Tests): hello, authority probes, events
#      resume + cancel, realtime voice from a TTS-spoken prompt, 3D refusal, tamper refusal; sanitized fixtures are
#      written to <evidence>/fixtures;
#   3. a PipeWire virtual microphone: `pw-loopback` with a sink (gc_p22_sink) whose loopback is an Audio/Source
#      ("GC P2.2 mic"); a background player waits for the Unity test's marker file and plays the spoken prompt
#      into the sink with `pw-play`;
#   4. Unity EditMode live tests (GameCore.Studio.Hollowmere.P2_2.Live) through studio/tools/unity-compile.sh (one
#      Unity slot, the host-wide limit is respected): NPC request to staging, icon PNG, TTS WAV, describe, 3D
#      refusal, tamper refusal, voice from a WAV and from the microphone;
#   5. scans every evidence file for credential shapes and for the key itself (fails the run on a hit).
#
# Evidence: ~/wkspace/gc-studio/<packet>/artifacts/studio/evidence/P2.2/live-<stamp>/ (copy it back to the
# worktree and commit; the generated PNG/WAV are under games/hollowmere/Assets/Hollowmere/Generated/P2_2/).
#
# Environment:
#   GC_STUDIO_HOST, GC_STUDIO_REMOTE_BASE   as for the other studio tools
#   GAMECORE_ETOS_KEY_FILE                  app key file on the host (default ~/.config/gamecore-studio/app-key.json)
#   GAMECORE_ETOS_STAGE_CHANGESET           optional owned mechanism candidate id for R2-H stage qualification
#   GAMECORE_ETOS_STAGE_CATALOG             candidate catalog SHA-256 (required with STAGE_CHANGESET)
#   GAMECORE_ETOS_PROJECT_ID                derived below from productGUID + absolute project path
#   GC_ETOS_MIC_SECONDS                     microphone capture length in the Unity test (default 14)
#
# Exit codes: 0 every selected live test passed (an Inconclusive microphone row is reported, not a pass of that
# row); 1 a failure; 2 bad usage or a missing prerequisite (node down, no key file).
set -euo pipefail

usage() {
  sed -n '2,41p' "${BASH_SOURCE[0]:-$0}" 2>/dev/null | sed 's/^# \{0,1\}//' >&2 || true
  exit 2
}

[[ $# -ge 1 ]] || usage
packet="$1"
shift
skip_dotnet=0
skip_unity=0
use_mic=1
unity_filter='GameCore\.Studio\.Hollowmere\.P2_2\.Live\..*'
dotnet_filter='TestCategory=Live'
while [[ $# -gt 0 ]]; do
  case "$1" in
    --skip-dotnet) skip_dotnet=1; shift ;;
    --skip-unity) skip_unity=1; shift ;;
    --no-mic) use_mic=0; shift ;;
    --unity-filter) [[ $# -ge 2 ]] || usage; unity_filter="$2"; shift 2 ;;
    --dotnet-filter) [[ $# -ge 2 ]] || usage; dotnet_filter="$2"; shift 2 ;;
    *) echo "live-etos-tests.sh: unknown argument '$1'" >&2; usage ;;
  esac
done
if ! [[ "${packet}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
  echo "live-etos-tests.sh: packet name must match [A-Za-z0-9][A-Za-z0-9._-]*: '${packet}'" >&2
  exit 2
fi

host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"

if [[ "${GC_STUDIO_ON_HOST:-0}" != "1" ]] && [[ "$(uname -s)" != "Linux" ]]; then
  self="${BASH_SOURCE[0]}"
  remote_env="GC_STUDIO_ON_HOST=1 GC_STUDIO_REMOTE_BASE=$(printf '%q' "${remote_base}")"
  remote_env+=" GC_ETOS_MIC_SECONDS=$(printf '%q' "${GC_ETOS_MIC_SECONDS:-14}")"
  if [[ -n "${GAMECORE_ETOS_KEY_FILE:-}" ]]; then
    remote_env+=" GAMECORE_ETOS_KEY_FILE=$(printf '%q' "${GAMECORE_ETOS_KEY_FILE}")"
  fi
  forwarded=("${packet}" --unity-filter "${unity_filter}" --dotnet-filter "${dotnet_filter}")
  (( skip_dotnet )) && forwarded+=(--skip-dotnet)
  (( skip_unity )) && forwarded+=(--skip-unity)
  (( use_mic )) || forwarded+=(--no-mic)
  rc=0
  # shellcheck disable=SC2029  # the quoting is done here on purpose (printf %q)
  ssh -o BatchMode=yes "${host}" "${remote_env} bash -s -- $(printf '%q ' "${forwarded[@]}")" < "${self}" || rc=$?
  exit "${rc}"
fi

base="${HOME}/${remote_base}/${packet}"
tools="${base}/studio/tools"
key_file="${GAMECORE_ETOS_KEY_FILE:-${HOME}/.config/gamecore-studio/app-key.json}"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
evidence="${base}/artifacts/studio/evidence/P2.2/live-${stamp}"
fixtures="${evidence}/fixtures"

[[ -d "${base}" ]] || { echo "live-etos-tests.sh: no clone at ${base} (run studio/tools/sync-to-host.sh ${packet})" >&2; exit 2; }
if ! systemctl --user is-active --quiet etosd; then
  echo "live-etos-tests.sh: etosd is not active (systemctl --user status etosd)" >&2
  exit 2
fi
[[ -r "${key_file}" ]] || { echo "live-etos-tests.sh: no readable app key file at ${key_file}" >&2; exit 2; }
mkdir -p "${evidence}" "${fixtures}"
echo "-- node: etosd active; app key file: ${key_file} (contents not shown)"
echo "-- evidence: ${evidence}"

# D5: same persisted identity as EtosProjectContext; never inspect the credential settings file.
export GAMECORE_ETOS_PROJECT_ID="$(python3 - "${base}/games/hollowmere" <<'PYPROJECT'
import hashlib, json, pathlib, re, sys
root = pathlib.Path(sys.argv[1]).resolve()
identity = root / 'UserSettings/GameCoreStudio.Project.json'
stored = json.loads(identity.read_text()) if identity.exists() else {}
if stored.get('path') == str(root) and re.fullmatch('[0-9a-f]{64}', stored.get('projectId', '')):
    project_id = stored['projectId']
else:
    text = (root / 'ProjectSettings/ProjectSettings.asset').read_text()
    guid = re.search(r'(?m)^\s*productGUID:\s*([a-fA-F0-9]{32})\s*$', text)
    if not guid: raise SystemExit('project_guid_missing')
    project_id = hashlib.sha256((guid[1].lower() + '\n' + str(root)).encode()).hexdigest()
    identity.parent.mkdir(parents=True, exist_ok=True)
    identity.write_text(json.dumps({'path': str(root), 'projectId': project_id}))
print(project_id)
PYPROJECT
)"
export GAMECORE_ETOS_STAGE_SOURCE="$(git -C "${base}" rev-parse HEAD)"
if [[ -n "${GAMECORE_ETOS_STAGE_CHANGESET:-}" ]]; then
  [[ "${GAMECORE_ETOS_STAGE_CATALOG:-}" =~ ^[0-9a-f]{64}$ ]] || { echo 'stage catalog SHA-256 required' >&2; exit 2; }
  export GAMECORE_ETOS_STAGE_CHANGESET GAMECORE_ETOS_STAGE_CATALOG
fi

# The trusted host writer buffers token fragments and structured JSON before durable writes.
# Its child environment is explicit; credential contents are never command arguments.
run_logged() {
  local log="$1"
  shift
  local forwarded=()
  local name
  for name in GAMECORE_ETOS_LIVE GAMECORE_ETOS_KEY_FILE GAMECORE_ETOS_PROJECT_ID \
    GAMECORE_ETOS_STAGE_SOURCE GAMECORE_ETOS_STAGE_CHANGESET GAMECORE_ETOS_STAGE_CATALOG \
    GC_ETOS_EVIDENCE_DIR GC_ETOS_FIXTURE_OUT GC_ETOS_MIC_SECONDS GC_ETOS_MIC_DEVICE \
    GC_STUDIO_ON_HOST GC_STUDIO_HOST GC_STUDIO_REMOTE_BASE GC_STUDIO_UNITY_SLOTS \
    UNITY UNITY_TIMEOUT UNITY_SILENCE_TIMEOUT; do
    [[ -v "$name" ]] && forwarded+=("$name=${!name}")
  done
  python3 "${base}/studio/stage/run-redacted.py" --log "$log" --timeout 7200 --silence 1800 \
    -- env "${forwarded[@]}" "$@"
}

export GAMECORE_ETOS_LIVE=1
export GAMECORE_ETOS_KEY_FILE="${key_file}"
export GC_ETOS_EVIDENCE_DIR="${evidence}"
export GC_ETOS_FIXTURE_OUT="${fixtures}"
export GC_ETOS_MIC_SECONDS="${GC_ETOS_MIC_SECONDS:-14}"
export GC_STUDIO_ON_HOST=1

failures=0
loopback_pid=""
player_pid=""
cleanup() {
  [[ -n "${player_pid}" ]] && kill "${player_pid}" 2>/dev/null || true
  [[ -n "${loopback_pid}" ]] && kill "${loopback_pid}" 2>/dev/null || true
}
trap cleanup EXIT

if (( ! skip_dotnet )); then
  echo "== dotnet live tests (${dotnet_filter})"
  rc=0
  run_logged "${evidence}/dotnet-live.log" bash "${tools}/dotnet-test.sh" "${packet}" dotnet/tests/GameCore.Studio.Etos.Client.Tests -- \
    --filter "${dotnet_filter}" --logger "trx;LogFileName=${evidence}/dotnet-live.trx" \
    --logger "console;verbosity=normal" || rc=$?
  (( rc == 0 )) || { echo "-- dotnet live tests failed (exit ${rc})" >&2; failures=$((failures + 1)); }
fi

if (( ! skip_unity )); then
  if (( use_mic )) && command -v pw-loopback >/dev/null 2>&1 && command -v pw-play >/dev/null 2>&1; then
    echo "== PipeWire virtual microphone"
    pw-loopback --capture-props='media.class=Audio/Sink node.name=gc_p22_sink node.description=GC_P2.2_sink' \
      --playback-props='media.class=Audio/Source node.name=gc_p22_mic node.description=GC_P2.2_mic' \
      > "${evidence}/pw-loopback.log" 2>&1 &
    loopback_pid=$!
    sleep 2
    if kill -0 "${loopback_pid}" 2>/dev/null; then
      echo "-- pw-loopback running (sink gc_p22_sink -> source gc_p22_mic)"
      export GC_ETOS_MIC_DEVICE="GC_P2.2_mic"
      prompt="${evidence}/voice-prompt.wav"
      (
        for _ in $(seq 1 1200); do
          [[ -f "${evidence}/mic-listening" ]] && break
          sleep 1
        done
        if [[ -f "${evidence}/mic-listening" && -f "${prompt}" ]]; then
          sleep 1
          pw-play --target gc_p22_sink "${prompt}" > "${evidence}/pw-play.log" 2>&1 || echo "pw-play exit $?" >> "${evidence}/pw-play.log"
          echo "played $(date -u +%H:%M:%S)" >> "${evidence}/pw-play.log"
        else
          echo "no marker or no prompt wav; nothing played" > "${evidence}/pw-play.log"
        fi
      ) &
      player_pid=$!
    else
      echo "-- pw-loopback exited; see pw-loopback.log (microphone row will report what Unity sees)" >&2
      loopback_pid=""
    fi
  fi

  echo "== Unity live tests (${unity_filter})"
  rc=0
  UNITY_TIMEOUT="${UNITY_TIMEOUT:-3600}" UNITY_SILENCE_TIMEOUT="${UNITY_SILENCE_TIMEOUT:-1500}" \
    run_logged "${evidence}/unity-live.log" bash "${tools}/unity-compile.sh" "${packet}" games/hollowmere --tests EditMode --filter "${unity_filter}" || rc=$?
  (( rc == 0 )) || { echo "-- Unity live tests failed (exit ${rc})" >&2; failures=$((failures + 1)); }
  latest_xml="$(ls -t "${base}/.unity-logs/"*editmode*.xml 2>/dev/null | head -n 1 || true)"
  [[ -n "${latest_xml}" ]] && cp "${latest_xml}" "${evidence}/unity-live-results.xml"
fi

echo "== redaction scan"
scan_rc=0
python3 - "${evidence}" "${key_file}" <<'PY' || scan_rc=$?
import json, os, re, sys
root, key_file = sys.argv[1], sys.argv[2]
raw = open(key_file, encoding="utf-8").read().strip()
try:
    key = json.loads(raw).get("key", "")
except ValueError:
    key = raw
shape = re.compile(r"(?:etk_|ett_|etp_|eta_)[A-Za-z0-9_\-.~+/=]{4,}|(?i:bearer)\s+[A-Za-z0-9]")
hits = 0
for folder, _, files in os.walk(root):
    for name in files:
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
sys.exit(1 if hits else 0)
PY
(( scan_rc == 0 )) || { echo "-- the key appeared in evidence (redacted in place); treat as a failure" >&2; failures=$((failures + 1)); }

echo "-- evidence files:"
ls -1 "${evidence}" | sed 's/^/   /'
if (( failures == 0 )); then
  echo "RESULT live-etos-tests ${packet}: PASS (${evidence})"
else
  echo "RESULT live-etos-tests ${packet}: FAIL (${failures} stage(s); ${evidence})" >&2
  exit 1
fi
