#!/usr/bin/env bash
# workflow-p3.2-mechanism.sh - P3.2 mechanism proposal -> stage -> admit (W-MECH-01 through the agent), live:
#   1. mech-a (interactive Editor on :1): Causeway Gate selected, "Add a pressure plate mechanism that opens the marsh
#      gate ... while an item sits on the plate." sent to gc-mechanic; the candidate (mechanism.propose with package +
#      proposal artifacts) is previewed; Stage is pressed in the candidate panel (POST /v1/stage through the installed
#      companion; its outcome is recorded as it is); the candidate is exported as a stage-lane candidate directory;
#   2. the stage lane CLI on the host (gamecore-studio stage run, built from this clone's studio/agent; Unity under the
#      shared lock): verdict.json;
#   3. mech-b (interactive Editor on :1, a fresh session): Record verdict in the panel -> Admit (package written,
#      recompile + domain reload, catalog re-bake check) -> Play from Boot.unity -> History Undo (StageAdmission.Undo,
#      recompile) -> package removed;
#   4. when the live worker produced no candidate or its stage failed: the same 2-3 with P2.4's pressure-plate sample
#      candidate (samples/mechanisms/pressure-plate/candidate), recorded separately (--sample forces it).
# Usage: studio/tools/workflow-p3.2-mechanism.sh [--packet <name>] [--sample] [--skip-agent]
# Spends: one gc-mechanic task (many turns: it runs dotnet test in its container).
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
on_host=0
[[ "${GC_STUDIO_ON_HOST:-0}" == "1" || "$(uname -s)" == "Linux" ]] && on_host=1

remote() {
  # Runs a host command (directly on the host, over ssh from the Mac).
  if (( on_host )); then
    bash -c "$1"
  else
    # shellcheck disable=SC2029
    ssh -o BatchMode=yes "${host}" "$1"
  fi
}

clone="\${HOME}/${remote_base}/${packet}"
stage_run() {
  # $1 candidate dir on the host, $2 verdict path on the host, $3 log prefix
  remote "set -e; cd ${clone}/studio/agent && ~/.cargo/bin/cargo build --quiet </dev/null 2>&1 | tail -n 20; \
    start=\$(date +%s); set +e; \
    ./target/debug/gamecore-studio stage run p32-mech --candidate '$1' \
      --source-project ${clone}/games/hollowmere --repo ${clone} --force --verdict-out '$2' </dev/null > '$3.stdout.txt' 2> '$3.stderr.txt'; \
    rc=\$?; echo \"stage exit \${rc} after \$(( \$(date +%s) - start )) s\" | tee '$3.exit.txt'; tail -n 5 '$3.stderr.txt'; exit 0"
}

last_run_dir() {
  ls -td "${root}/artifacts/studio/workflows/P3.2/runs/$1-"* 2>/dev/null | head -n 1
}

rc=0
agent_ok=0
if (( ! sample && ! skip_agent )); then
  bash "${here}/workflow-p3.2-lib.sh" mech-a --packet "${packet}" || rc=$?
  run_a="$(remote "ls -td \${HOME}/${remote_base}/${packet}-runs/mech-a-* | head -n 1")"
  echo "-- mech-a run: ${run_a}"
  if remote "test -f '${run_a}/mech/candidate/change-set.json'"; then
    stage_run "${run_a}/mech/candidate" "${run_a}/mech/verdict.json" "${run_a}/mech/stage-cli"
    if remote "test -f '${run_a}/mech/verdict.json'"; then
      if remote "python3 -c 'import json,sys; sys.exit(0 if json.load(open(\"${run_a}/mech/verdict.json\")).get(\"pass\") else 1)'"; then
        agent_ok=1
      fi
    fi
    if (( ! on_host )); then
      dest="$(last_run_dir mech-a)"
      [[ -n "${dest}" ]] && scp -q "${host}:${run_a}/mech/verdict.json" "${host}:${run_a}/mech/stage-cli."* "${dest}/mech/" 2>/dev/null || true
    fi
  fi
  if (( agent_ok )); then
    bash "${here}/workflow-p3.2-lib.sh" mech-b --packet "${packet}" --env "GCS_P32_VERDICT=${run_a}/mech/verdict.json" || rc=$?
  else
    echo "-- the live worker's mechanism did not reach a passing verdict; falling back to the pressure-plate sample" >&2
  fi
fi

if (( sample || (! agent_ok) )); then
  work="$(remote "d=\${HOME}/${remote_base}/${packet}-runs/mech-sample-\$(date -u +%Y%m%dT%H%M%SZ); mkdir -p \$d; echo \$d")"
  sample_dir="${clone}/samples/mechanisms/pressure-plate/candidate"
  remote "python3 - '${work}/mech.json' ${clone} <<'PY'
import json, os, sys
clone = os.path.expanduser(sys.argv[2])
cand = os.path.join(clone, 'samples/mechanisms/pressure-plate/candidate')
cs = json.load(open(os.path.join(cand, 'change-set.json')))
json.dump({'changeSetId': cs['id'], 'requestId': 'p2.4-sample', 'dir': cand, 'catalogRevision': None, 'sample': True}, open(sys.argv[1], 'w'), indent=2)
PY"
  stage_run "${sample_dir}" "${work}/verdict.json" "${work}/stage-cli"
  bash "${here}/workflow-p3.2-lib.sh" mech-b --packet "${packet}" --env "GCS_P32_VERDICT=${work}/verdict.json" --env "GCS_P32_MECH=${work}/mech.json" || rc=$?
  if (( ! on_host )); then
    dest="${root}/artifacts/studio/workflows/P3.2/runs/$(basename "${work}")"
    mkdir -p "${dest}"
    scp -q -r "${host}:${work}/." "${dest}/" || true
  fi
fi
exit "${rc}"
