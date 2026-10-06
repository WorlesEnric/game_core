#!/usr/bin/env bash
# HOST ONLY: network restore inherits the host proxy; containers remain --network none.
# Full stage: --stage-root ROOT --source-project PROJECT --unity-library LIBRARY --upm-from PUBLIC_UPM [--offline-from NUGET]
# Recheck that exact identity: --stage-root ROOT --source-project PROJECT --verify.
# A positional cache directory is the low-level dependency-only API; add --complete for the full prerequisite.
set -euo pipefail
exec python3 "$(cd "$(dirname "$0")" && pwd)/cache.py" "$@"
