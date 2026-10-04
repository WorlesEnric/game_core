#!/usr/bin/env bash
# Put the etos source used by GameCore Studio on the Linux host, with git (docs/studio/04-etos-integration.md §8.1).
#
#   studio/tools/host-sync-etos.sh
#
# Run on the Mac. Pushes the checked-out branch of $ETOS_SRC (default ~/wkspace/etos; it must be
# committed and clean) to the bare hub $STUDIO_HOST:~/wkspace/gc-studio/etos-hub.git (remote
# `host`, added when missing), then makes ~/wkspace/etos-studio on the host a clone of the hub
# at exactly that commit (`git checkout -B <branch> origin/<branch>`). Build outputs (target/)
# are untracked and survive. host-build-etos.sh pins `git rev-parse HEAD` of that clone in
# studio/etos/etos.lock. Idempotent: an unchanged branch fetches and checks out nothing new.
#
# Never touches ~/wkspace/etos on the host (an unrelated codebase).
set -euo pipefail

ETOS_SRC="${ETOS_SRC:-$HOME/wkspace/etos}"
STUDIO_HOST="${STUDIO_HOST:-myubuntu}"
HUB="wkspace/gc-studio/etos-hub.git"
DEST="wkspace/etos-studio"
BASE="6c2c3f4"

git_() { git -C "$ETOS_SRC" "$@"; }
branch="$(git_ rev-parse --abbrev-ref HEAD)"
sha="$(git_ rev-parse HEAD)"
[ "$branch" != HEAD ] || { echo "host-sync-etos: $ETOS_SRC is not on a branch" >&2; exit 1; }
[ -z "$(git_ status --porcelain --untracked-files=no)" ] ||
    { echo "host-sync-etos: $ETOS_SRC has uncommitted changes; commit them first" >&2; exit 1; }
git_ merge-base --is-ancestor "$BASE" HEAD ||
    { echo "host-sync-etos: HEAD does not contain the pinned base $BASE" >&2; exit 1; }

ssh "$STUDIO_HOST" "test -d ~/$HUB || git init -q --bare ~/$HUB"
git_ remote get-url host >/dev/null 2>&1 || git_ remote add host "$STUDIO_HOST:$HUB"
git_ push -q host "$branch"
ssh "$STUDIO_HOST" bash -s -- "$HUB" "$DEST" "$branch" "$sha" <<'EOF'
set -euo pipefail
hub="$HOME/$1" dest="$HOME/$2" branch="$3" sha="$4"
if [ ! -d "$dest/.git" ]; then
    git clone -q -b "$branch" "$hub" "$dest"
    echo "cloned $hub into $dest"
fi
cd "$dest"
git fetch -q origin
[ -z "$(git status --porcelain --untracked-files=no)" ] || { echo "$dest has local changes" >&2; exit 1; }
before="$(git rev-parse HEAD)"
git checkout -q -B "$branch" "origin/$branch"
now="$(git rev-parse HEAD)"
[ "$now" = "$sha" ] || { echo "the hub's $branch is $now, not $sha" >&2; exit 1; }
if [ "$before" = "$now" ]; then echo "etos-studio unchanged at $now ($branch)"; else echo "etos-studio $before -> $now ($branch)"; fi
EOF
