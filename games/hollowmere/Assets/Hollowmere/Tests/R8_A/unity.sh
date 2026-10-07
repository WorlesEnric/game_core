#!/usr/bin/env bash
# Keep unity-batch.sh's -batchmode/-nographics intact. Forward only the owned scratch pairing path.
set -euo pipefail
: "${GAMECORE_R8A_PAIRING_PATH:?scratch pairing path required}"
export GAMECORE_ETOS_KEY_FILE="${GAMECORE_R8A_PAIRING_PATH}"
export GAMECORE_ETOS_AUTOSTART=1
exec "${HOME}/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" "$@"
