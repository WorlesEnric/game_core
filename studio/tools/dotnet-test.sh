#!/usr/bin/env bash
# dotnet-test.sh - run `dotnet test` on one packet's copy of the repository on the Linux host.
#
# Usage:
#   studio/tools/dotnet-test.sh <packet-name> [project-or-solution] [-- extra dotnet test args...]
# Examples:
#   studio/tools/dotnet-test.sh p0.2-projects-tooling
#   studio/tools/dotnet-test.sh p0.3-studio-model dotnet/tests/GameCore.Studio.Model.Tests
#   GAMECORE_OFFLINE=1 studio/tools/dotnet-test.sh p0.3-studio-model -- --filter 'FullyQualifiedName~ChangeSet'
#
# The packet's clone must already be on the host at ~/wkspace/gc-studio/<packet-name>/ (studio/tools/
# sync-to-host.sh puts it there, through git). Run from the Mac, the script re-runs itself on the host over
# non-interactive ssh; run on the host, it works directly. The default target is dotnet/GameCore.sln. Owner
# rule: dotnet never runs on the Mac.
#
# GAMECORE_OFFLINE follows the repository's documented contract (docs/operator/build-and-run.md, section 2.1):
#   0 (default) the NuGet advisory audit stays on;
#   1           adds -p:NuGetAudit=false to THIS invocation of dotnet test only, for a host whose advisory feed
#               is unreachable. Nothing is exported or written to any project or NuGet config, and the banner
#               says the audit was off.
#
# Environment:
#   GC_STUDIO_HOST         ssh host when run off-host (default: myubuntu)
#   GC_STUDIO_REMOTE_BASE  directory under the host home (default: wkspace/gc-studio)
#   DOTNET                 dotnet binary on the host (default: ~/.dotnet/dotnet)
#   DOTNET_TEST_TIMEOUT    seconds before the run is killed (default: 1800)
#
# Exit codes: the exit code of dotnet test (0 = all tests passed); 124/137 on a timeout; 2 bad usage.
set -euo pipefail

usage() {
  sed -n '2,28p' "${BASH_SOURCE[0]:-$0}" 2>/dev/null | sed 's/^# \{0,1\}//' >&2 || true
  exit 2
}

[[ $# -ge 1 ]] || usage
packet="$1"
shift
target="dotnet/GameCore.sln"
if [[ $# -gt 0 && "$1" != "--" ]]; then
  target="$1"
  shift
fi
if [[ $# -gt 0 && "$1" == "--" ]]; then
  shift
fi
extra=("$@")

if ! [[ "${packet}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
  echo "dotnet-test.sh: packet name must match [A-Za-z0-9][A-Za-z0-9._-]*: '${packet}'" >&2
  exit 2
fi
if [[ "${target}" == /* || "${target}" == *..* ]]; then
  echo "dotnet-test.sh: target must be relative to the packet root, without '..': '${target}'" >&2
  exit 2
fi
offline="${GAMECORE_OFFLINE:-0}"
if ! [[ "${offline}" =~ ^[01]$ ]]; then
  echo "dotnet-test.sh: GAMECORE_OFFLINE must be 0 or 1: '${offline}'" >&2
  exit 2
fi

host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"
default_dotnet="${HOME}/.dotnet/dotnet"

if [[ "${GC_STUDIO_ON_HOST:-0}" != "1" ]] && { [[ "$(uname -s)" != "Linux" ]] || [[ ! -x "${DOTNET:-${default_dotnet}}" ]]; }; then
  self="${BASH_SOURCE[0]}"
  remote_env="GC_STUDIO_ON_HOST=1 GAMECORE_OFFLINE=${offline} GC_STUDIO_REMOTE_BASE=$(printf '%q' "${remote_base}")"
  remote_env+=" DOTNET_TEST_TIMEOUT=$(printf '%q' "${DOTNET_TEST_TIMEOUT:-1800}")"
  if [[ -n "${DOTNET:-}" ]]; then
    remote_env+=" DOTNET=$(printf '%q' "${DOTNET}")"
  fi
  forwarded=("${packet}" "${target}" --)
  if [[ ${#extra[@]} -gt 0 ]]; then
    forwarded+=("${extra[@]}")
  fi
  rc=0
  # shellcheck disable=SC2029  # the quoting is done here on purpose (printf %q)
  ssh -o BatchMode=yes "${host}" "${remote_env} bash -s -- $(printf '%q ' "${forwarded[@]}")" < "${self}" || rc=$?
  exit "${rc}"
fi

dotnet="${DOTNET:-${default_dotnet}}"
base="${HOME}/${remote_base}/${packet}"
if [[ ! -x "${dotnet}" ]]; then
  echo "dotnet-test.sh: dotnet not executable: ${dotnet}" >&2
  exit 2
fi
if [[ ! -e "${base}/${target}" ]]; then
  echo "dotnet-test.sh: no ${target} under ${base} (run studio/tools/sync-to-host.sh ${packet} first)" >&2
  exit 2
fi

audit_args=()
if [[ "${offline}" == "1" ]]; then
  audit_args=(-p:NuGetAudit=false)
  echo "-- nuget audit: OFF for this dotnet test only (GAMECORE_OFFLINE=1; advisory feed declared unreachable)"
else
  echo "-- nuget audit: on (GAMECORE_OFFLINE=0; default)"
fi

cmd=("${dotnet}" test "${base}/${target}")
if [[ ${#audit_args[@]} -gt 0 ]]; then
  cmd+=("${audit_args[@]}")
fi
if [[ ${#extra[@]} -gt 0 ]]; then
  cmd+=("${extra[@]}")
fi
echo "-- command: ${cmd[*]}"
start="$(date +%s)"
rc=0
(cd "${base}" && DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 \
  timeout --signal=TERM --kill-after=60 "${DOTNET_TEST_TIMEOUT:-1800}" "${cmd[@]}") || rc=$?
end="$(date +%s)"
if (( rc == 0 )); then
  echo "RESULT dotnet test ${target}: PASS ($((end - start))s)"
else
  echo "RESULT dotnet test ${target}: FAIL (exit ${rc}, $((end - start))s)" >&2
fi
exit "${rc}"
