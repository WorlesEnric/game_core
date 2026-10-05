#!/usr/bin/env bash
# clean-clone-verify.sh - W-PLUG-12 on a fresh checkout: clone a branch of the host hub into a new directory (no
# Library/, nothing imported yet), open games/hollowmere once in batchmode and run
# Hollowmere.P1_7b.EditMode.Tests.CleanCloneBakeCheck.Run: Entry.Verify against the committed bake outputs (reported),
# Entry.Bake (no asset refresh), Entry.Verify again (must pass: two bakes are byte-identical).
#
# Usage:   studio/tools/clean-clone-verify.sh <branch> [--keep]
# Example: studio/tools/clean-clone-verify.sh worktree-agent-a05edc7a6e52c5648
#
# The branch must be on the hub (studio/tools/sync-to-host.sh pushes it). Run from the Mac, the script re-runs itself
# on the host over non-interactive ssh (owner rule: Unity never runs on the Mac). The Editor is launched through
# studio/tools/unity-batch.sh of the fresh clone, so it takes one of the host-wide Unity slots (at most
# GC_STUDIO_UNITY_SLOTS, default 3) and holds only that one. The clone is deleted afterwards unless --keep.
#
# Output (host): ~/wkspace/gc-studio/.clean-clones/logs/<label>-*.log, <label>.json (the check's report) and
# <label>.status (git status of the clone after the bake: the outputs a re-bake rewrites).
#
# Environment:
#   GC_STUDIO_HOST         ssh host when run off-host (default: myubuntu)
#   GC_STUDIO_REMOTE_BASE  directory under the host home (default: wkspace/gc-studio)
#   GC_STUDIO_REFERENCE    host checkout used as --reference-if-able (default: wkspace/game_core; empty = none)
#   UNITY_TIMEOUT          seconds per Editor attempt (default: 3600: a fresh import of the whole project)
#
# Exit codes: 0 the bake is reproducible on a fresh clone (bake and the verify after it pass); 1 it is not, or the
# Editor failed; 124 timed out; 2 bad usage or missing branch.
set -euo pipefail

usage() {
  sed -n '2,27p' "${BASH_SOURCE[0]:-$0}" 2>/dev/null | sed 's/^# \{0,1\}//' >&2 || true
  exit 2
}

[[ $# -ge 1 ]] || usage
branch="$1"
shift
keep=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --keep) keep=1; shift ;;
    *) echo "clean-clone-verify.sh: unknown argument '$1'" >&2; usage ;;
  esac
done
if ! [[ "${branch}" =~ ^[A-Za-z0-9][A-Za-z0-9._/-]*$ ]]; then
  echo "clean-clone-verify.sh: bad branch name '${branch}'" >&2
  exit 2
fi

host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"
reference="${GC_STUDIO_REFERENCE-wkspace/game_core}"

if [[ "${GC_STUDIO_ON_HOST:-0}" != "1" && "$(uname -s)" != "Linux" ]]; then
  remote_env="GC_STUDIO_ON_HOST=1 GC_STUDIO_REMOTE_BASE=$(printf '%q' "${remote_base}")"
  remote_env+=" GC_STUDIO_REFERENCE=$(printf '%q' "${reference}")"
  remote_env+=" UNITY_TIMEOUT=$(printf '%q' "${UNITY_TIMEOUT:-3600}")"
  forwarded=("${branch}")
  (( keep == 1 )) && forwarded+=(--keep)
  rc=0
  # shellcheck disable=SC2029  # quoting done here on purpose (printf %q)
  ssh -o BatchMode=yes "${host}" "${remote_env} bash -s -- $(printf '%q ' "${forwarded[@]}")" < "${BASH_SOURCE[0]}" || rc=$?
  exit "${rc}"
fi

hub="${HOME}/${remote_base}/hub.git"
work="${HOME}/${remote_base}/.clean-clones"
logs="${work}/logs"
label="clean-verify-$(printf '%s' "${branch}" | tr '/' '_')-$(date +%Y%m%dT%H%M%S)"
mkdir -p "${work}" "${logs}"
if ! git --git-dir="${hub}" rev-parse --verify --quiet "refs/heads/${branch}" >/dev/null; then
  echo "clean-clone-verify.sh: branch '${branch}' is not on ${hub} (run studio/tools/sync-to-host.sh first)" >&2
  exit 2
fi

clone="$(mktemp -d "${work}/clone-XXXXXX")"
cleanup() {
  if (( keep == 0 )); then
    rm -rf "${clone}"
  else
    echo "-- kept ${clone}"
  fi
}
trap cleanup EXIT

start="$(date +%s)"
ref_args=()
if [[ -n "${reference}" && -d "${HOME}/${reference}/.git" ]]; then
  ref_args=(--reference-if-able "${HOME}/${reference}")
fi
git clone --quiet "${ref_args[@]}" --branch "${branch}" "${hub}" "${clone}"
echo "-- fresh clone of ${branch} at $(git -C "${clone}" rev-parse --short HEAD) in $(( $(date +%s) - start ))s: ${clone}"
if [[ -d "${clone}/games/hollowmere/Library" ]]; then
  echo "clean-clone-verify.sh: the fresh clone already has a Library/ directory" >&2
  exit 1
fi

rc=0
P17B_BAKE_REPORT="${logs}/${label}.json" "${clone}/studio/tools/unity-batch.sh" \
  --project "${clone}/games/hollowmere" --log-dir "${logs}" --label "${label}" \
  --timeout "${UNITY_TIMEOUT:-3600}" -- -executeMethod Hollowmere.P1_7b.EditMode.Tests.CleanCloneBakeCheck.Run || rc=$?

git -C "${clone}" status --porcelain --untracked-files=all > "${logs}/${label}.status" || true
echo "-- report ${logs}/${label}.json:"
cat "${logs}/${label}.json" 2>/dev/null || echo "   (no report: the check did not run)"
echo "-- files the bake rewrote in the fresh clone (${logs}/${label}.status):"
sed 's/^/   /' "${logs}/${label}.status" | head -n 60
echo "RESULT clean-clone-verify ${branch}: $([[ ${rc} -eq 0 ]] && echo PASS || echo FAIL) (exit ${rc}, $(( $(date +%s) - start ))s)"
exit "${rc}"
