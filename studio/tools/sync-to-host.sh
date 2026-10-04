#!/usr/bin/env bash
# sync-to-host.sh - put this worktree's committed branch on the Linux build host, through git.
#
# Usage:   studio/tools/sync-to-host.sh [--wip] <packet-name>
# Example: studio/tools/sync-to-host.sh p0.2-projects-tooling
#
# Owner rule: nothing is compiled, built or tested on the Mac. Code reaches the host through git (never rsync),
# and studio/tools/unity-compile.sh and studio/tools/dotnet-test.sh run there.
#
# What it does:
#   1. requires a clean tree (no staged, unstaged or untracked changes). With --wip it instead snapshots the
#      uncommitted state as ONE temporary commit on branch wip/<packet-name> (parent: HEAD), built in a private
#      index, so the current branch, the index and the working tree are left exactly as they were. It never
#      commits on the current branch;
#   2. ensures the git remote `host` (default myubuntu:wkspace/gc-studio/hub.git) exists, creating the bare hub
#      repository on the host if it is missing, and force-pushes the branch (the current branch, or
#      wip/<packet-name> with --wip) to it;
#   3. on the host, makes ~/wkspace/gc-studio/<packet-name> a clone of ~/wkspace/gc-studio/hub.git checked out at
#      that branch: `git clone --reference-if-able ~/wkspace/game_core` when absent (objects the host checkout
#      already has are borrowed through git alternates, so the big artifacts/ history never crosses the link
#      twice); otherwise `fetch`, `checkout -B <branch>`, `reset --hard origin/<branch>` and `git clean -fd`
#      (untracked, non-ignored files only). Library/, Temp/, UserSettings/, obj/ and .unity-logs/ are ignored
#      or excluded, so a reset or clean keeps them and the next Unity compile stays incremental. A directory
#      left by the earlier rsync-based version of this script is replaced by a fresh clone, with its Library/
#      directories and .unity-logs/ moved into the clone.
#
# Environment:
#   GC_STUDIO_HOST         ssh host (default: myubuntu; non-interactive ssh must work)
#   GC_STUDIO_REMOTE_BASE  directory under the host home (default: wkspace/gc-studio)
#   GC_STUDIO_REFERENCE    host checkout under the host home used as --reference-if-able for new clones
#                          (default: wkspace/game_core; empty = no reference)
#
# Exit codes: 0 synced; 2 bad usage, dirty tree without --wip, or detached HEAD; anything else is the failing
# git/ssh exit code.
set -euo pipefail

usage() {
  sed -n '2,34p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

wip=0
if [[ $# -ge 1 && "$1" == "--wip" ]]; then
  wip=1
  shift
fi
[[ $# -eq 1 ]] || usage
packet="$1"
if ! [[ "${packet}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
  echo "sync-to-host.sh: packet name must match [A-Za-z0-9][A-Za-z0-9._-]*: '${packet}'" >&2
  exit 2
fi

host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"
reference="${GC_STUDIO_REFERENCE-wkspace/game_core}"
hub_rel="${remote_base}/hub.git"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${root}"

if [[ ! -f tools/check_package_metadata.py ]] || ! git rev-parse --git-dir >/dev/null 2>&1; then
  echo "sync-to-host.sh: ${root} is not a game_core git checkout" >&2
  exit 2
fi
current="$(git symbolic-ref --quiet --short HEAD || true)"
if [[ -z "${current}" ]]; then
  echo "sync-to-host.sh: HEAD is detached; check out a branch first" >&2
  exit 2
fi

start="$(date +%s)"
dirty="$(git status --porcelain --untracked-files=all)"
branch="${current}"
if [[ -n "${dirty}" ]]; then
  if (( wip == 0 )); then
    echo "sync-to-host.sh: the working tree has uncommitted changes; commit them, or pass --wip to sync a" >&2
    echo "temporary snapshot on wip/${packet}:" >&2
    printf '%s\n' "${dirty}" | head -n 40 | sed 's/^/   /' >&2
    exit 2
  fi
  echo "WARNING: syncing UNCOMMITTED changes as a temporary commit on wip/${packet} (the current branch is untouched):"
  printf '%s\n' "${dirty}" | head -n 40 | sed 's/^/   /'
  tmp_index="$(mktemp "${TMPDIR:-/tmp}/gc-sync-index.XXXXXX")"
  trap 'rm -f "${tmp_index}"' EXIT
  GIT_INDEX_FILE="${tmp_index}" git read-tree HEAD
  GIT_INDEX_FILE="${tmp_index}" git add -A
  tree="$(GIT_INDEX_FILE="${tmp_index}" git write-tree)"
  commit="$(git commit-tree "${tree}" -p HEAD -m "wip(${packet}): uncommitted snapshot of ${current} for host compile")"
  git update-ref "refs/heads/wip/${packet}" "${commit}"
  branch="wip/${packet}"
elif (( wip == 1 )); then
  echo "-- --wip given but the tree is clean; syncing ${current} itself"
fi

# The hub: a bare repository on the host, reached through the `host` remote.
if ! git remote get-url host >/dev/null 2>&1; then
  git remote add host "${host}:${hub_rel}"
  echo "-- added git remote host -> ${host}:${hub_rel}"
fi
ssh -o BatchMode=yes "${host}" "set -e; hub=\"\$HOME/${hub_rel}\"; if [ ! -d \"\$hub\" ]; then git init -q --bare \"\$hub\"; echo \"-- created bare hub \$hub\"; fi"
git push --force --quiet host "refs/heads/${branch}:refs/heads/${branch}"
sha="$(git rev-parse "refs/heads/${branch}")"
echo "-- pushed ${branch} (${sha}) to host"

# The packet clone on the host. Quoted values are validated above (packet regex) or come from this script.
# shellcheck disable=SC2029
ssh -o BatchMode=yes "${host}" "bash -s -- $(printf '%q ' "${remote_base}" "${packet}" "${branch}" "${sha}" "${reference}")" <<'HOST'
set -euo pipefail
remote_base="$1"; packet="$2"; branch="$3"; sha="$4"; reference="$5"
hub="${HOME}/${remote_base}/hub.git"
dest="${HOME}/${remote_base}/${packet}"

clone_into() {
  local target="$1"
  local ref_args=()
  if [[ -n "${reference}" && -d "${HOME}/${reference}/.git" ]]; then
    ref_args=(--reference-if-able "${HOME}/${reference}")
  fi
  git clone --quiet "${ref_args[@]}" --branch "${branch}" "${hub}" "${target}"
}

if [[ ! -e "${dest}" ]]; then
  clone_into "${dest}"
  echo "-- cloned ${hub} into ${dest}"
elif [[ ! -d "${dest}/.git" ]]; then
  # Left by the rsync-based version of this script: replace it with a clone, keeping the Unity caches.
  fresh="${dest}.clone-$$"
  clone_into "${fresh}"
  while IFS= read -r library; do
    rel="${library#"${dest}"/}"
    if [[ -d "${fresh}/$(dirname "${rel}")" && ! -e "${fresh}/${rel}" ]]; then
      mv "${library}" "${fresh}/${rel}"
      echo "-- kept ${rel}"
    fi
  done < <(find "${dest}" -maxdepth 4 -type d -name Library -prune)
  if [[ -d "${dest}/.unity-logs" ]]; then
    mv "${dest}/.unity-logs" "${fresh}/.unity-logs"
  fi
  rm -rf "${dest}"
  mv "${fresh}" "${dest}"
  echo "-- replaced the rsync copy at ${dest} with a clone of ${hub}"
fi

cd "${dest}"
git remote set-url origin "${hub}"
git fetch --quiet --force --prune origin
git checkout --quiet -f -B "${branch}" "origin/${branch}"
git reset --quiet --hard "origin/${branch}"
git clean -fdq -e Library/ -e .unity-logs/ -e UserSettings/ -e Temp/ -e Logs/
head="$(git rev-parse HEAD)"
if [[ "${head}" != "${sha}" ]]; then
  echo "sync-to-host.sh: host clone is at ${head}, expected ${sha}" >&2
  exit 1
fi
echo "-- ${dest} at ${branch} ${head}"
HOST
end="$(date +%s)"
echo "synced ${branch} (${sha}) -> ${host}:~/${remote_base}/${packet}/ ($((end - start))s)"
