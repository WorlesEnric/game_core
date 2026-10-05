#!/usr/bin/env bash
# Trusted analyzer tests and both samples through the production Docker launcher.
set -euo pipefail
[[ $# == 2 ]] || { echo 'usage: offline-check.sh <new-work-directory> <provisioned-versioned-cache>' >&2; exit 2; }
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../../.." && pwd)"
work=$(realpath -m "$1")
cache=$(realpath "$2")
[[ ! -e "$work" ]] || { echo 'work directory must be new' >&2; exit 2; }
python3 "$here/../cache.py" "$cache" --verify
python3 - "$repo" "$work" "$cache" <<'PY'
import json,pathlib,shutil,socket,sys
repo,work,cache=map(pathlib.Path,sys.argv[1:])
shutil.copytree(repo/'studio/stage/analyzer',work/'studio/stage/analyzer',ignore=shutil.ignore_patterns('bin','obj','TestResults'))
for name in ('pressure-plate','negative-semantic'):
    shutil.copytree(repo/'samples/mechanisms'/name,work/'samples/mechanisms'/name)
editor=pathlib.Path.home()/'Unity/Hub/Editor/6000.0.75f1/Editor'
refs=[str(p) for p in sorted((editor/'Data/Managed/UnityEngine').glob('*.dll')) if p.name.startswith(('UnityEngine','UnityEditor'))]
lock=json.loads((repo/'studio/stage/cache/unity-metadata-lock.json').read_text())
sys.path.insert(0,str(repo/'studio/stage'))
import analysis_context
for p,digest in lock.items(): analysis_context.checked(cache/'analysis-context',p,digest)
refs += [str(cache/'analysis-context'/p) for p in lock]
(work/'request.json').write_text(json.dumps({'schema':'gamecore.stage.analyze/1','references':refs,'supportSources':[str(repo/'Packages')],'policy':{'mode':'D1'}}))
(work/'sandbox.json').write_text(json.dumps({'mode':'docker','image':'gamecore-stage:6000.0.75f1-v1','editor':str(editor),'home':str(pathlib.Path.home()),'hostname':socket.gethostname(),'licences':[str(pathlib.Path.home()/'.config/unity3d/Unity'),str(pathlib.Path.home()/'.local/share/unity3d/Unity'),'/var/lib/unity'],'slot':str(work),'cache':str(cache),'packages':str(repo/'Packages')}))
PY
binary="$repo/studio/agent/target/debug/gamecore-studio"
run=("$binary" stage sandbox-exec "$work/sandbox.json" dotnet)
"${run[@]}" test "$work/studio/stage/analyzer/Tests/StageAnalyzer.Tests.csproj" --nologo
analyzer="$work/studio/stage/analyzer/bin/Debug/net8.0/StageAnalyzer.dll"
"${run[@]}" "$analyzer" --root "$work/samples/mechanisms/pressure-plate" --rules "$work/request.json" --out "$work/pressure-findings.json"
rc=0
"${run[@]}" "$analyzer" --root "$work/samples/mechanisms/negative-semantic" --rules "$work/request.json" --out "$work/negative-findings.json" || rc=$?
[[ $rc == 3 ]]
python3 - "$work" <<'PY'
import json,pathlib,sys
root=pathlib.Path(sys.argv[1])
positive=json.loads((root/'pressure-findings.json').read_text())
assert positive['pass'] and not positive['findings']
expected=json.loads((root/'samples/mechanisms/negative-semantic/expected.json').read_text())
negative=json.loads((root/'negative-findings.json').read_text())
assert not negative['pass']
assert set(expected)<={f['rule'] for f in negative['findings']}
print('PASS: production Docker launcher, network none, analyzer tests, pressure-plate and negative-semantic')
PY
