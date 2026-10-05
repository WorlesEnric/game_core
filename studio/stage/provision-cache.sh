#!/usr/bin/env bash
# HOST ONLY: network restore inherits the host proxy; containers remain --network none.
set -euo pipefail
exec python3 "$(cd "$(dirname "$0")" && pwd)/cache.py" "$@"
