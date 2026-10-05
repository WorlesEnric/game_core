#!/usr/bin/env bash
# D7: stable warm-cache key from the Unity version and kernel/gameplay package versions.
# Usage: cache-key.sh <source-project>. Output: stage-cache-v1-<sha256>.
set -euo pipefail
[[ $# == 1 ]] || { echo 'usage: cache-key.sh <source-project>' >&2; exit 2; }
python3 - "$1" <<'PY'
import hashlib,json,re,sys
from pathlib import Path
project=Path(sys.argv[1]).resolve()
version=(project/'ProjectSettings/ProjectVersion.txt').read_text()
unity=re.search(r'^m_EditorVersion:\s*(\S+)',version,re.M)
if not unity: raise SystemExit('cache_key_invalid: missing Unity version')
deps=json.loads((project/'Packages/manifest.json').read_text())['dependencies']
versions={}
for name,pin in sorted(deps.items()):
    if not name.startswith('com.gamecore.') or name.startswith('com.gamecore.studio.'): continue
    if not pin.startswith('file:'): raise SystemExit('cache_key_invalid: kernel/gameplay package must be local')
    path=(project/'Packages'/pin[5:]).resolve()
    meta=json.loads((path/'package.json').read_text())
    if meta['name']!=name or not isinstance(meta.get('version'),str): raise SystemExit('cache_key_invalid: package identity')
    versions[name]=meta['version']
data=json.dumps({'schema':1,'unity':unity.group(1),'packages':versions},sort_keys=True,separators=(',',':')).encode()
print('stage-cache-v1-'+hashlib.sha256(data).hexdigest())
PY
