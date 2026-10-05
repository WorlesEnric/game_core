#!/usr/bin/env bash
# stage.sh - the legacy staging-shell entry of the companion (P0.5 `[stage] command`), wrapping the P2.4 staging lane.
#
# Usage (called by the companion; Linux build host only):
#   studio/stage/stage.sh <slot> <package-dir> [<change-set-id>]
#
# Runs `gamecore-studio stage run legacy-<slot> --package-dir <package-dir> --change-set-id <id> --force`: a slot made
# from the bare package directory (no stage inputs, so no world catalog delta), the seven steps of the lane, and the
# StageVerdict. The verdict's canonical JSON, with `ok` added (= `pass`, the field the legacy shell reads), is the last
# stdout line. The staging lane proper is `POST /v1/stage {changeSetId, slot?, steps?}` (studio/stage/README.md).
#
# Environment: GAMECORE_STUDIO_BIN (default: the repository's studio/agent/target/{release,debug}/gamecore-studio),
# plus everything `gamecore-studio stage run` reads (GAMECORE_STAGE_ROOT, GAMECORE_STAGE_SOURCE_PROJECT,
# GAMECORE_STAGE_BUDGET_S, UNITY, ...). Exit: 0 pass, 1 fail, 2 no verdict.
set -euo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
  sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
fi
slot="$1"
package_dir="$2"
change_set="${3:-cs_00000000000000000000000000}"
if [[ $# -lt 3 ]]; then
  echo "stage.sh: no change-set id given (old companion); using ${change_set}" >&2
fi

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "${here}/../.." && pwd)"
bin="${GAMECORE_STUDIO_BIN:-}"
if [[ -z "${bin}" ]]; then
  for candidate in "${repo}/studio/agent/target/release/gamecore-studio" "${repo}/studio/agent/target/debug/gamecore-studio"; do
    if [[ -x "${candidate}" ]]; then
      bin="${candidate}"
      break
    fi
  done
fi
if [[ -z "${bin}" || ! -x "${bin}" ]]; then
  echo '{"ok":false,"error":"gamecore-studio is not built (cargo build in studio/agent) and GAMECORE_STUDIO_BIN is unset"}'
  exit 2
fi

safe_slot="legacy-$(printf '%s' "${slot}" | tr -c 'a-z0-9._-' '-' | cut -c1-50)"
set +e
output="$("${bin}" stage run "${safe_slot}" --package-dir "${package_dir}" --change-set-id "${change_set}" --repo "${repo}" --force)"
rc=$?
set -e
verdict="$(printf '%s\n' "${output}" | tail -n 1)"
python3 -c 'import json,sys
v = json.loads(sys.argv[1])
if isinstance(v, dict) and "pass" in v:
    v["ok"] = bool(v["pass"])
print(json.dumps(v, sort_keys=True, separators=(",", ":")))' "${verdict}"
exit "${rc}"
