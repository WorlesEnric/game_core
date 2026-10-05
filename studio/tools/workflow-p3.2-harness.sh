#!/usr/bin/env bash
# workflow-p3.2-harness.sh - the headless (no window) variants of the P3.2 workflows: the EditMode tests in
# games/hollowmere/Assets/Hollowmere/Tests/P3_2/EditMode (Hollowmere.P3_2.Headless) drive the Studio runtime and P2.2's
# EtosAgentGateway against the REAL node: H1 typed edit on a selection (candidate -> apply -> journal -> undo), H2 media
# (generate.image with max_cost_usd 0.25 -> asset.import -> assign -> undo; tts -> import -> undo; image with
# max_cost_usd 0.001), H3 cancel while running.
#
# Usage: studio/tools/workflow-p3.2-harness.sh [--packet <name>] [--filter <regex>]
#   default filter: Hollowmere\.P3_2\.Headless\..*
# From the Mac it re-runs itself on the host (after sync-to-host.sh) and copies the evidence back to
# artifacts/studio/workflows/P3.2/runs/harness-<stamp>/; on the host it leaves it in ~/wkspace/gc-studio/<packet>-runs/.
# The Unity run goes through studio/tools/unity-compile.sh (one host-wide Unity slot). The key file path comes from
# GAMECORE_ETOS_KEY_FILE (default ~/.config/gamecore-studio/app-key.json) and is only passed on, never read here.
# Spends: one gc-designer task (H1), one cancelled task (H3), two images and one tts line (H2).
set -euo pipefail

packet="p3.2"
filter='Hollowmere\.P3_2\.Headless\..*'
while [[ $# -gt 0 ]]; do
  case "$1" in
    --packet) packet="$2"; shift 2 ;;
    --filter) filter="$2"; shift 2 ;;
    *) echo "workflow-p3.2-harness.sh: unknown argument '$1'" >&2; exit 2 ;;
  esac
done
[[ "${packet}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]] || { echo "bad packet" >&2; exit 2; }
host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"

if [[ "${GC_STUDIO_ON_HOST:-0}" != "1" && "$(uname -s)" != "Linux" ]]; then
  root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
  log="$(mktemp "${TMPDIR:-/tmp}/workflow-p3.2-harness.XXXXXX")"
  rc=0
  # shellcheck disable=SC2029
  ssh -o BatchMode=yes "${host}" "GC_STUDIO_ON_HOST=1 GC_STUDIO_REMOTE_BASE=$(printf '%q' "${remote_base}") bash -s -- --packet $(printf '%q' "${packet}") --filter $(printf '%q' "${filter}")" < "${BASH_SOURCE[0]}" | tee "${log}" || rc=$?
  out_dir="$(grep -E '^RUN_DIR ' "${log}" | tail -n 1 | cut -d' ' -f2- || true)"
  if [[ -n "${out_dir}" ]]; then
    dest="${root}/artifacts/studio/workflows/P3.2/runs/$(basename "${out_dir}")"
    mkdir -p "${dest}"
    scp -q -r "${host}:${out_dir}/." "${dest}/"
    echo "-- copied the run folder to ${dest}"
  fi
  exit "${rc}"
fi

base="${HOME}/${remote_base}/${packet}"
key_file="${GAMECORE_ETOS_KEY_FILE:-${HOME}/.config/gamecore-studio/app-key.json}"
export ETOS_ROOT="${ETOS_ROOT:-${HOME}/.local/share/etos-studio}"
etos_bin="${HOME}/.local/opt/etos/bin/etos"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
out="${HOME}/${remote_base}/${packet}-runs/harness-${stamp}"
mkdir -p "${out}"
echo "RUN_DIR ${out}"
[[ -r "${key_file}" ]] || { echo "no readable app key file at ${key_file}" >&2; exit 2; }
systemctl --user is-active --quiet etosd || { echo "etosd is not active" >&2; exit 2; }
sha="$(git -C "${base}" rev-parse HEAD </dev/null)"

rc=0
GAMECORE_ETOS_LIVE=1 GAMECORE_ETOS_KEY_FILE="${key_file}" GCS_P32_OUT="${out}" GC_STUDIO_ON_HOST=1 \
  UNITY_TIMEOUT="${UNITY_TIMEOUT:-3600}" UNITY_SILENCE_TIMEOUT="${UNITY_SILENCE_TIMEOUT:-1500}" \
  "${base}/studio/tools/unity-compile.sh" "${packet}" games/hollowmere --tests EditMode --filter "${filter}" </dev/null 2>&1 | tee "${out}/unity.log" || rc=$?
latest_xml="$(ls -t "${base}/.unity-logs/"*editmode*.xml 2>/dev/null | head -n 1 || true)"
[[ -n "${latest_xml}" ]] && cp "${latest_xml}" "${out}/results.xml"

mkdir -p "${out}/etos"
python3 - "${out}" <<'PY' > "${out}/etos/task-ids.txt"
import json, os, re, sys
ids = set()
for name in os.listdir(sys.argv[1]):
    if name.startswith("headless-") and name.endswith(".json"):
        ids.update(re.findall(r'"(t[0-9a-f]{24})"', open(os.path.join(sys.argv[1], name)).read()))
print("\n".join(sorted(ids)))
PY
while read -r task; do
  [[ -n "${task}" ]] || continue
  { echo '{"show":'; "${etos_bin}" task show "${task}" --json </dev/null 2>/dev/null || echo null; echo ',"budget":'; "${etos_bin}" budget --task "${task}" --json </dev/null 2>/dev/null || echo null; echo '}'; } > "${out}/etos/${task}.json"
done < "${out}/etos/task-ids.txt"

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
        path = os.path.join(folder, name)
        text = open(path, encoding="utf-8", errors="replace").read()
        new = shape.sub("[redacted]", text.replace(key, "[redacted]") if key else text)
        if new != text:
            print("redacted credential material in " + os.path.relpath(path, root))
            open(path, "w", encoding="utf-8").write(new)
            hits += 1
sys.exit(1 if hits else 0)
PY
(( scan_rc == 0 )) || rc=1
printf '{"workflow":"harness","revision":"%s","host":"%s","exit":%s,"stamp":"%s","filter":"%s"}\n' "${sha}" "$(hostname)" "${rc}" "${stamp}" "${filter}" > "${out}/run.json"
echo "RUN_DIR ${out}"
exit "${rc}"
