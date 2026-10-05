#!/usr/bin/env bash
# unity-compile.sh - batchmode compile (and optional test run) of one project copy on the Linux host.
#
# Usage:
#   studio/tools/unity-compile.sh <packet-name> <project-rel-path> [--tests EditMode|PlayMode] [--filter <regex>]
# Examples:
#   studio/tools/unity-compile.sh p0.2-projects-tooling games/hollowmere
#   studio/tools/unity-compile.sh p0.2-projects-tooling unity/GameCore.Validation --tests EditMode \
#       --filter 'GameCore\.Composition\..*'
#
# The project must already be on the host at ~/wkspace/gc-studio/<packet-name>/<project-rel-path>, inside the
# packet's clone of the host hub (studio/tools/sync-to-host.sh <packet-name> puts it there). Run from the Mac,
# the script re-runs itself on the host over non-interactive ssh; run on the host, it works directly. Owner
# rule: Unity never runs on the Mac.
#
# Delegates all host execution to unity-batch.sh: one shared atomic reservation across interactive and
# batch Editors, at most three host-wide, timeout/silence retry, TERM forwarding and streaming redaction.
# Without --tests it compiles with -quit; with --tests it preserves the test runner's XML disposition.
# Repeat --require-test <fullname> to require designated acceptance cases to appear and pass.
# Skipped/inconclusive/zero cases are partial or NotRun, never PASS. Logs/XML live in .unity-logs/.
# Operator environment: GC_STUDIO_HOST, GC_STUDIO_REMOTE_BASE, GC_STUDIO_UNITY_SLOTS, UNITY,
# UNITY_TIMEOUT and UNITY_SILENCE_TIMEOUT. See unity-batch.sh for defaults and exit codes.
set -euo pipefail

usage() {
  sed -n '2,/^set -/p' "${BASH_SOURCE[0]:-$0}" | sed '/^set -/d' 2>/dev/null | sed 's/^# \{0,1\}//' >&2 || true
  exit 2
}

if [[ $# -lt 2 ]]; then
  usage
fi
packet="$1"
project="$2"
shift 2
tests=""
filter=""
required_tests=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --tests)
      [[ $# -ge 2 ]] || usage
      tests="$2"
      shift 2
      ;;
    --require-test) [[ $# -ge 2 ]] || usage; required_tests+=(--require-test "$2"); shift 2 ;;
    --filter)
      [[ $# -ge 2 ]] || usage
      filter="$2"
      shift 2
      ;;
    *)
      echo "unity-compile.sh: unknown argument '$1'" >&2
      usage
      ;;
  esac
done
if ! [[ "${packet}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
  echo "unity-compile.sh: packet name must match [A-Za-z0-9][A-Za-z0-9._-]*: '${packet}'" >&2
  exit 2
fi
if [[ "${project}" == /* || "${project}" == *..* ]]; then
  echo "unity-compile.sh: project path must be relative to the packet root, without '..': '${project}'" >&2
  exit 2
fi
if [[ -n "${tests}" && "${tests}" != "EditMode" && "${tests}" != "PlayMode" ]]; then
  echo "unity-compile.sh: --tests takes EditMode or PlayMode, not '${tests}'" >&2
  exit 2
fi
if [[ -n "${filter}" && -z "${tests}" ]]; then
  echo "unity-compile.sh: --filter needs --tests" >&2
  exit 2
fi

host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"
default_unity="${HOME}/Unity/Hub/Editor/6000.0.75f1/Editor/Unity"

# Off the host: re-run this very script on the host, forwarding the arguments and the tunables.
if [[ "${GC_STUDIO_ON_HOST:-0}" != "1" ]] && { [[ "$(uname -s)" != "Linux" ]] || [[ ! -x "${UNITY:-${default_unity}}" ]]; }; then
  self="${BASH_SOURCE[0]}"
  remote_env="GC_STUDIO_ON_HOST=1 GC_STUDIO_REMOTE_BASE=$(printf '%q' "${remote_base}")"
  remote_env+=" GC_STUDIO_UNITY_SLOTS=$(printf '%q' "${GC_STUDIO_UNITY_SLOTS:-3}")"
  remote_env+=" UNITY_TIMEOUT=$(printf '%q' "${UNITY_TIMEOUT:-1500}")"
  remote_env+=" UNITY_SILENCE_TIMEOUT=$(printf '%q' "${UNITY_SILENCE_TIMEOUT:-600}")"
  if [[ -n "${UNITY:-}" ]]; then
    remote_env+=" UNITY=$(printf '%q' "${UNITY}")"
  fi
  forwarded=("${packet}" "${project}" "${required_tests[@]}")
  [[ -n "${tests}" ]] && forwarded+=(--tests "${tests}")
  [[ -n "${filter}" ]] && forwarded+=(--filter "${filter}")
  rc=0
  # shellcheck disable=SC2029  # the quoting is done here on purpose (printf %q)
  ssh -o BatchMode=yes "${host}" "${remote_env} bash -s -- $(printf '%q ' "${forwarded[@]}")" < "${self}" || rc=$?
  exit "${rc}"
fi

base="${HOME}/${remote_base}/${packet}"
project_dir="${base}/${project%/}"
logs="${base}/.unity-logs"
slug="$(printf '%s' "${project%/}" | tr '/ ' '__')"
mode="compile"
args=(-quit)
result_args=()
if [[ -n "$tests" ]]; then
  mode="${tests,,}"
  args=(-runTests -testPlatform "$tests")
  [[ -z "$filter" ]] || args+=(-testFilter "$filter")
  result_args=(--results "${logs}/${slug}-${mode}-$(date +%Y%m%dT%H%M%S)-$$.xml")
fi
exec bash "${base}/studio/tools/unity-batch.sh" --project "$project_dir" --log-dir "$logs" \
  --label "${slug}-${mode}" "${result_args[@]}" "${required_tests[@]}" -- "${args[@]}"
