#!/usr/bin/env bash
# sync-to-host.sh - mirror this worktree to the Linux build host for one packet.
#
# Usage:   studio/tools/sync-to-host.sh <packet-name>
# Example: studio/tools/sync-to-host.sh p0.2-projects-tooling
#
# Copies the worktree that contains this script to
#   <host>:~/wkspace/gc-studio/<packet-name>/
# with rsync (archive mode, --delete), excluding .git and every generated tree: Library, Temp, Logs, obj,
# bin, target, UserSettings, .claude and the host-side .unity-logs directory. Excluded paths are never
# deleted on the host, so a project's Library cache and earlier logs survive a re-sync, which keeps the next
# Unity compile incremental.
#
# Environment:
#   GC_STUDIO_HOST         ssh host (default: myubuntu; non-interactive ssh must work)
#   GC_STUDIO_REMOTE_BASE  directory under the remote home (default: wkspace/gc-studio)
#   GC_STUDIO_SEED         checkout under the remote home used to seed a packet's FIRST sync with a host-local
#                          copy, so only the delta crosses the link (default: wkspace/game_core; empty = off).
#                          The rsync that follows makes the copy identical to this worktree either way.
#
# Exit codes: 0 synced; 2 bad usage; anything else is the failing ssh/rsync exit code.
set -euo pipefail

usage() {
  sed -n '2,21p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

[[ $# -eq 1 ]] || usage
packet="$1"
if ! [[ "${packet}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
  echo "sync-to-host.sh: packet name must match [A-Za-z0-9][A-Za-z0-9._-]*: '${packet}'" >&2
  exit 2
fi

host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

if [[ ! -f "${root}/tools/check_package_metadata.py" ]]; then
  echo "sync-to-host.sh: ${root} does not look like a game_core checkout" >&2
  exit 2
fi

start="$(date +%s)"
# First sync of a packet: seed the host directory from the host's own checkout (a local copy on the host),
# so the link only carries the delta. rsync then compares by checksum blocks, never trusting the seed.
seed="${GC_STUDIO_SEED-wkspace/game_core}"
ssh -o BatchMode=yes "${host}" "set -e; dest=\"\$HOME/${remote_base}/${packet}\"; seed=\"\$HOME/${seed}\";
  if [ ! -d \"\$dest\" ] && [ -n '${seed}' ] && [ -d \"\$seed/Packages\" ]; then
    mkdir -p \"\$dest\";
    rsync -a --exclude '.git' --exclude 'Library/' --exclude 'Temp/' --exclude 'Logs/' --exclude 'UserSettings/' \
      --exclude 'obj/' --exclude 'bin/' --exclude 'target/' --exclude '__pycache__/' \"\$seed/\" \"\$dest/\";
    echo \"seeded \$dest from \$seed (host-local copy)\";
  fi;
  mkdir -p \"\$dest\""
rsync -a --delete \
  --exclude '.git' \
  --exclude '.claude/' \
  --exclude '.DS_Store' \
  --exclude '.unity-logs/' \
  --exclude 'Library/' \
  --exclude 'Temp/' \
  --exclude 'Logs/' \
  --exclude 'UserSettings/' \
  --exclude 'obj/' \
  --exclude 'bin/' \
  --exclude 'target/' \
  --exclude '__pycache__/' \
  -e 'ssh -o BatchMode=yes' \
  "${root}/" "${host}:${remote_base}/${packet}/"
end="$(date +%s)"
echo "synced ${root} -> ${host}:~/${remote_base}/${packet}/ ($((end - start))s)"
