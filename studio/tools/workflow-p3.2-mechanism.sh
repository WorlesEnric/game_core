#!/usr/bin/env bash
# workflow-p3.2-mechanism.sh - P3.2 mechanism proposal -> stage -> admit (W-MECH-01 through the agent), live, on main's
# stage seam (the companion stages; there is no verdict-file path any more):
#   1. mech-a (interactive Editor on :1): Causeway Gate (+ its interactable definition) selected, "Add a pressure plate
#      mechanism that opens the marsh gate ... while an item sits on the plate." sent to gc-mechanic (worker
#      "mechanism"); the candidate (mechanism.propose with package + proposal artifacts) is previewed and exported to
#      <run>/mech/candidate; Stage is pressed in the candidate panel (the companion runs the stage job; the verdict is
#      fetched and verified); when Admit is enabled: Admit (package written, recompile + domain reload) -> Play from
#      Boot.unity -> History Undo (StageAdmission.Undo) -> package removed;
#   2. mech-b (fallback, or --sample): the same Stage -> Admit -> Play -> Undo with P2.4's pressure-plate sample
#      candidate (samples/mechanisms/pressure-plate/candidate), recorded separately.
# Usage: studio/tools/workflow-p3.2-mechanism.sh [--packet <name>] [--sample] [--skip-agent]
# Spends: one gc-mechanic task (many turns: it runs dotnet test in its container) unless --sample/--skip-agent.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "${here}/../.." && pwd)"
packet="p3.2"
sample=0
skip_agent=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --packet) packet="$2"; shift 2 ;;
    --sample) sample=1; shift ;;
    --skip-agent) skip_agent=1; shift ;;
    *) echo "workflow-p3.2-mechanism.sh: unknown argument '$1'" >&2; exit 2 ;;
  esac
done
host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"
if [[ "${GC_STUDIO_ON_HOST:-0}" == "1" || "$(uname -s)" == "Linux" ]]; then
  host_home="${HOME}"
else
  host_home="$(ssh -o BatchMode=yes "${host}" 'echo $HOME')"
fi

rc=0
admitted=0
if (( ! sample && ! skip_agent )); then
  bash "${here}/workflow-p3.2-lib.sh" mech-a --packet "${packet}" || rc=$?
  latest="$(ls -td "${root}/artifacts/studio/workflows/P3.2/runs/mech-a-"* 2>/dev/null | head -n 1 || true)"
  if [[ -n "${latest}" && -f "${latest}/mech/admission.json" ]]; then
    admitted=1
  fi
fi

if (( sample || ! admitted )); then
  (( sample )) || echo "-- the live worker's mechanism was not admitted; running the pressure-plate sample through the same seam" >&2
  bash "${here}/workflow-p3.2-lib.sh" mech-b --packet "${packet}" \
    --env "GCS_P32_MECH_CANDIDATE=${host_home}/${remote_base}/${packet}/samples/mechanisms/pressure-plate/candidate" || rc=$?
fi
exit "${rc}"
