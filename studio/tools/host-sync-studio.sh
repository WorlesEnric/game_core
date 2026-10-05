#!/usr/bin/env bash
# Move this packet's branch between the Mac checkout and the Linux host, with git.
#
#   studio/tools/host-sync-studio.sh push   commit-only: push the current branch to the hub and
#                                           check it out in $STUDIO_HOST_DIR on the host
#   studio/tools/host-sync-studio.sh pull   on the host, commit studio/etos/etos.lock and
#                                           artifacts/studio/environment/ if they changed, push
#                                           them to the hub, and fast-forward the Mac branch
#
# Run on the Mac. The hub is the bare repository $STUDIO_HOST:~/wkspace/gc-studio/hub.git (remote
# `host`). $STUDIO_HOST_DIR (default ~/wkspace/gc-studio/p0.1-host-etos) is a clone of the hub;
# untracked build outputs there (studio/agent/target, studio/etos/agent/bin) survive.
# ~/wkspace/game_core on the host is a separate clone and is not used. Idempotent: an unchanged
# branch pushes, checks out and commits nothing.
set -euo pipefail

STUDIO_HOST="${STUDIO_HOST:-myubuntu}"
STUDIO_HOST_DIR="${STUDIO_HOST_DIR:-wkspace/gc-studio/p0.1-host-etos}"
HUB="wkspace/gc-studio/hub.git"
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
git_() { git -C "$ROOT" "$@"; }
branch="$(git_ rev-parse --abbrev-ref HEAD)"
[ "$branch" != HEAD ] || { echo "not on a branch" >&2; exit 1; }
git_ remote get-url host >/dev/null 2>&1 || git_ remote add host "$STUDIO_HOST:$HUB"

case "${1:-}" in
push)
    [ -z "$(git_ status --porcelain --untracked-files=no)" ] ||
        { echo "uncommitted changes: commit before pushing" >&2; exit 1; }
    git_ push -q host "$branch"
    ssh "$STUDIO_HOST" bash -s -- "$HUB" "$STUDIO_HOST_DIR" "$branch" "$(git_ rev-parse HEAD)" <<'EOF'
set -euo pipefail
hub="$HOME/$1" dest="$HOME/$2" branch="$3" sha="$4"
[ -d "$dest/.git" ] || { git clone -q -b "$branch" "$hub" "$dest"; echo "cloned into $dest"; }
cd "$dest"
git fetch -q origin
before="$(git rev-parse HEAD)"
git checkout -q -B "$branch" "origin/$branch"
[ "$(git rev-parse HEAD)" = "$sha" ] || { echo "the hub's $branch is not $sha" >&2; exit 1; }
if [ "$before" = "$sha" ]; then echo "host checkout unchanged at $sha"; else echo "host checkout $before -> $sha"; fi
EOF
    ;;
pull)
    name="$(git_ config user.name)" email="$(git_ config user.email)"
    ssh "$STUDIO_HOST" bash -s -- "$STUDIO_HOST_DIR" "$branch" "$name" "$email" <<'EOF'
set -euo pipefail
cd "$HOME/$1"; branch="$2"
git add -A studio/etos/etos.lock artifacts/studio/environment
if git diff --cached --quiet; then
    echo "nothing to bring back"
else
    git -c user.name="$3" -c user.email="$4" commit -q \
        -m "evidence: host etos lock and environment verification (P0.1)" \
        -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
    echo "committed $(git rev-parse --short HEAD) on the host"
fi
git push -q origin "HEAD:$branch"
EOF
    git_ pull -q --ff-only host "$branch"
    git_ log --oneline -1
    ;;
*)
    echo "usage: $0 push|pull" >&2
    exit 2
    ;;
esac
