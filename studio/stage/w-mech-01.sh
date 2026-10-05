#!/usr/bin/env bash
# w-mech-01.sh - W-MECH-01 end to end on the Linux build host, without a provider (docs/studio/07, P2.4):
#   stage the pressure-plate candidate -> verdict pass -> admit into Hollowmere (batchmode, real compile + domain reload)
#   -> Hollowmere PlayMode test places a plate in the village and presses it -> undo removes the package and the catalog
#   hash returns to its value before the admission.
#
# Usage (inside a packet clone on the host, e.g. ~/wkspace/gc-studio/p2.4):
#   studio/stage/w-mech-01.sh <evidence-dir> [--reset-journal]
#
# The sample candidate has a fixed change-set id, and the journal never applies one change set twice (an undone entry
# stays undone). --reset-journal removes that one id from the project's Studio journal first (journal file, redo stack,
# stage records) only when .gamecore-stage-scratch contains the exact absolute project path.
# Create that marker explicitly on a scratch clone only; never use it on a creator's project.
#
# Every Unity Editor runs through studio/tools/unity-batch.sh (host-wide Unity lock, one Editor at a time, 600 s
# silence watchdog, one retry on the known hang). The staged slot lives under GAMECORE_STAGE_ROOT
# (default ~/.cache/gamecore-studio/stage). Writes into <evidence-dir>: stage.txt, verdict.json, admit.json,
# playmode.xml, undo.json, the redacted Unity logs, and w-mech-01.json (the summary). Exit 0 when every step passed.
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 || ( $# -eq 2 && "$2" != "--reset-journal" ) ]]; then
  sed -n '2,19p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
fi
reset_journal="${2:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "${here}/../.." && pwd)"
evidence="$(mkdir -p "$1" && cd "$1" && pwd)"
project="${repo}/games/hollowmere"
candidate="${repo}/samples/mechanisms/pressure-plate/candidate"
batch="${repo}/studio/tools/unity-batch.sh"
package_dir="${project}/Packages/com.hollowmere.mechanism.pressureplate"
logs="${evidence}/logs"
mkdir -p "${logs}"
change_set="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "${candidate}/change-set.json")"

step() { printf '\n== %s (%s)\n' "$1" "$(date -Is)"; }
fail() { echo "W-MECH-01 FAILED: $*" >&2; exit 1; }

[[ -d "${package_dir}" ]] && fail "${package_dir} exists before the admission; remove it first"
if [[ -n "${reset_journal}" ]]; then
  marker="${project}/.gamecore-stage-scratch"
  if [[ ! -f "$marker" || -L "$marker" ]] || [[ "$(cat "$marker")" != "$project" ]]; then
    fail "--reset-journal requires an operator-created .gamecore-stage-scratch marker containing the exact project path"
  fi
  step "reset the journal entry of ${change_set}"
  python3 - "${project}" "${change_set}" <<'PY'
import json, sys
from pathlib import Path
project, cs = Path(sys.argv[1]), sys.argv[2]
for entry in (project / "Studio" / "History").glob(f"*/*/{cs}.json"):
    entry.unlink()
    print("removed", entry.relative_to(project))
lib = project / "Library" / "GameCoreStudio"
redo = lib / "redo.json"
if redo.exists():
    doc = json.loads(redo.read_text())
    doc["stack"] = [x for x in doc.get("stack", []) if x != cs]
    redo.write_text(json.dumps(doc))
for name in ("verdicts.json", "admitted.json"):
    path = lib / "stage" / name
    if path.exists():
        doc = json.loads(path.read_text())
        doc = {k: v for k, v in doc.items() if k != cs and not (isinstance(v, dict) and v.get("changeSetId") == cs)}
        path.write_text(json.dumps(doc, indent=2) + "\n")
for path in (lib / "stage").glob(f"pending-{cs}.json"):
    path.unlink()
PY
fi

step "build gamecore-studio"
(cd "${repo}/studio/agent" && ~/.cargo/bin/cargo build --quiet)
bin="${repo}/studio/agent/target/debug/gamecore-studio"

step "stage ${change_set}"
stage_start=$(date +%s)
set +e
"${bin}" stage run w-mech-01 --candidate "${candidate}" --source-project "${project}" --repo "${repo}" --force \
  --verdict-out "${evidence}/verdict.json" > "${evidence}/stage.json.txt" 2> "${evidence}/stage.txt"
stage_rc=$?
set -e
stage_s=$(( $(date +%s) - stage_start ))
cat "${evidence}/stage.txt"
[[ ${stage_rc} -eq 0 ]] || fail "the stage did not pass (exit ${stage_rc})"

step "admit into games/hollowmere"
admit_start=$(date +%s)
bash "${batch}" --project "${project}" --log-dir "${logs}" --label admit --attempts 1 -- \
  -executeMethod GameCore.Studio.Edit.StageCommandLine.Admit \
  -gcCandidate "${candidate}" -gcVerdict "${evidence}/verdict.json" -gcResult "${evidence}/admit.json" || true
admit_s=$(( $(date +%s) - admit_start ))
[[ -f "${evidence}/admit.json" ]] || fail "the admission wrote no result"
cat "${evidence}/admit.json"
python3 -c 'import json,sys; sys.exit(0 if json.load(open(sys.argv[1])).get("ok") else 1)' "${evidence}/admit.json" \
  || fail "the admission did not succeed"
[[ -f "${package_dir}/package.json" ]] || fail "the package is not installed at ${package_dir}"

step "Hollowmere PlayMode: place a plate in the village and press it"
bash "${batch}" --project "${project}" --log-dir "${logs}" --label playmode --results "${evidence}/playmode.xml" -- \
  -runTests -testPlatform PlayMode -testFilter 'Hollowmere\.P2_4\..*' || fail "the Hollowmere PlayMode test failed"
grep -h "\[W-MECH-01\]" "${logs}"/playmode-*.log | tail -n 5 || true

step "undo the admission"
bash "${batch}" --project "${project}" --log-dir "${logs}" --label undo --attempts 1 -- \
  -executeMethod GameCore.Studio.Edit.StageCommandLine.Undo -gcChangeSet "${change_set}" -gcResult "${evidence}/undo.json" || true
[[ -f "${evidence}/undo.json" ]] || fail "the undo wrote no result"
cat "${evidence}/undo.json"
[[ ! -e "${package_dir}" ]] || fail "the package directory is still present after the undo"

step "summary"
python3 - "${evidence}" "${stage_s}" "${admit_s}" <<'PY'
import json, sys, re
from pathlib import Path
ev = Path(sys.argv[1])
verdict = json.loads((ev / "verdict.json").read_text())
admit = json.loads((ev / "admit.json").read_text())
undo = json.loads((ev / "undo.json").read_text())
xml = (ev / "playmode.xml").read_text()
run = re.search(r'<test-run [^>]*', xml).group(0)
attr = lambda n: re.search(n + r'="([^"]*)"', run).group(1)
summary = {
    "changeSetId": verdict["changeSetId"],
    "stage": {"pass": verdict["pass"], "durationMs": verdict["durationMs"], "budgetMs": verdict["budgetMs"],
              "wallSeconds": int(sys.argv[2]), "steps": {s["id"]: s["status"] for s in verdict["steps"]}},
    "catalogDelta": verdict["catalogDelta"],
    "admit": {"ok": admit.get("ok"), "before": admit.get("before"), "live": admit.get("live"),
              "predicted": admit.get("predicted"), "admissionMs": admit.get("milliseconds"),
              "editorWallSeconds": int(sys.argv[3])},
    "playmode": {"total": int(attr("total")), "passed": int(attr("passed")), "failed": int(attr("failed"))},
    "undo": {"ok": undo.get("ok"), "live": undo.get("live"), "before": undo.get("before")},
}
summary["catalogHashReturned"] = undo.get("live") is not None and undo.get("live") == admit.get("before")
summary["pass"] = bool(verdict["pass"] and admit.get("ok") and undo.get("ok") and summary["catalogHashReturned"]
                       and summary["playmode"]["failed"] == 0 and summary["playmode"]["passed"] > 0)
(ev / "w-mech-01.json").write_text(json.dumps(summary, indent=2) + "\n")
print(json.dumps(summary, indent=2))
sys.exit(0 if summary["pass"] else 1)
PY
echo "W-MECH-01 PASS"
