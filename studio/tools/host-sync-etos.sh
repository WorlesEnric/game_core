#!/usr/bin/env bash
# Sync the etos source used by GameCore Studio to the Linux host (docs/studio/04-etos-integration.md §8.1).
#
#   studio/tools/host-sync-etos.sh [--allow-dirty]
#
# Run on the Mac. Copies $ETOS_SRC (default ~/wkspace/etos) at its checked-out commit to
# $STUDIO_HOST:~/wkspace/etos-studio/ with rsync (no .git, no build outputs) and writes
# ETOS_COMMIT there (commit sha, branch, base) so that host-build-etos.sh can pin it in
# studio/etos/etos.lock. Refuses a dirty tree unless --allow-dirty (then the commit is marked
# "-dirty" and must not be locked). Idempotent: rsync transfers only what changed.
#
# Never touches ~/wkspace/etos on the host (an unrelated codebase).
set -euo pipefail

ETOS_SRC="${ETOS_SRC:-$HOME/wkspace/etos}"
STUDIO_HOST="${STUDIO_HOST:-myubuntu}"
DEST="wkspace/etos-studio"
BASE="6c2c3f4"
allow_dirty=0
[ "${1:-}" = "--allow-dirty" ] && allow_dirty=1

git_() { git -C "$ETOS_SRC" "$@"; }
sha="$(git_ rev-parse HEAD)"
branch="$(git_ rev-parse --abbrev-ref HEAD)"
if [ -n "$(git_ status --porcelain --untracked-files=no)" ]; then
    if [ "$allow_dirty" -eq 0 ]; then
        echo "host-sync-etos: $ETOS_SRC has uncommitted changes; commit them or pass --allow-dirty" >&2
        exit 1
    fi
    sha="${sha}-dirty"
fi
git_ merge-base --is-ancestor "$BASE" HEAD || {
    echo "host-sync-etos: HEAD does not contain the pinned base $BASE" >&2
    exit 1
}

ssh "$STUDIO_HOST" "mkdir -p ~/$DEST"
rsync -a --delete --itemize-changes \
    --exclude '/.git/' --exclude 'target/' --exclude 'node_modules/' --exclude '.venv/' \
    --exclude '__pycache__/' --exclude '/ETOS_COMMIT' --exclude '/.claude/' \
    "$ETOS_SRC/" "$STUDIO_HOST:$DEST/" | sed 's/^/  /'
printf 'commit=%s\nbranch=%s\nbase=%s\n' "$sha" "$branch" "$(git_ rev-parse "$BASE")" |
    ssh "$STUDIO_HOST" "cat > ~/$DEST/ETOS_COMMIT.new && if cmp -s ~/$DEST/ETOS_COMMIT.new ~/$DEST/ETOS_COMMIT; then rm ~/$DEST/ETOS_COMMIT.new; echo 'ETOS_COMMIT unchanged'; else mv ~/$DEST/ETOS_COMMIT.new ~/$DEST/ETOS_COMMIT; echo 'ETOS_COMMIT updated'; fi"
echo "synced etos $sha ($branch) to $STUDIO_HOST:~/$DEST"
