#!/usr/bin/env bash
# Move this packet's host files between the Mac checkout and the Linux host.
#
#   studio/tools/host-sync-studio.sh push   studio/ -> $STUDIO_HOST:$STUDIO_HOST_DIR/studio/
#   studio/tools/host-sync-studio.sh pull   etos.lock and artifacts/studio/environment/ back
#
# Run on the Mac. $STUDIO_HOST_DIR defaults to ~/wkspace/gc-studio/p0.1-host-etos (the packet's
# own directory on the host; ~/wkspace/game_core there is a separate clone and is not used).
# Both directions are rsync, so repeating a run transfers nothing new.
set -euo pipefail

STUDIO_HOST="${STUDIO_HOST:-myubuntu}"
STUDIO_HOST_DIR="${STUDIO_HOST_DIR:-wkspace/gc-studio/p0.1-host-etos}"
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"

case "${1:-}" in
push)
    ssh "$STUDIO_HOST" "mkdir -p ~/$STUDIO_HOST_DIR/artifacts/studio/environment"
    rsync -a --delete --itemize-changes --exclude 'bin/' --exclude 'obj/' --exclude 'target/' --exclude '/etos/etos.lock' \
        "$ROOT/studio/" "$STUDIO_HOST:$STUDIO_HOST_DIR/studio/" | sed 's/^/  /'
    # The lock is the host's output; seed it only when the host has none.
    if [ -f "$ROOT/studio/etos/etos.lock" ]; then
        rsync -a --ignore-existing "$ROOT/studio/etos/etos.lock" "$STUDIO_HOST:$STUDIO_HOST_DIR/studio/etos/etos.lock"
    fi
    ;;
pull)
    rsync -a --itemize-changes "$STUDIO_HOST:$STUDIO_HOST_DIR/studio/etos/etos.lock" "$ROOT/studio/etos/etos.lock" | sed 's/^/  /' || true
    rsync -a --itemize-changes "$STUDIO_HOST:$STUDIO_HOST_DIR/artifacts/studio/environment/" \
        "$ROOT/artifacts/studio/environment/" | sed 's/^/  /'
    ;;
*)
    echo "usage: $0 push|pull" >&2
    exit 2
    ;;
esac
