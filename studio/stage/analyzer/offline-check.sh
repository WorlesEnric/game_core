#!/usr/bin/env bash
# Reproduce offline restore, unit tests and sample scans in a network-disabled container.
# This executes trusted analyzer tests only; it does not execute a candidate or run the stage lane.
set -euo pipefail
[[ $# == 1 ]] || { echo 'usage: offline-check.sh <new-work-directory>' >&2; exit 2; }
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../../.." && pwd)"
work=$(realpath -m "$1")
[[ ! -e "$work" ]] || { echo 'work directory must be new' >&2; exit 2; }
mkdir -p "$work"
python3 - "$repo" "$work" <<'PY'
import json,pathlib,shutil,sys
repo,work=map(pathlib.Path,sys.argv[1:])
shutil.copytree(repo/'studio/stage/analyzer',work/'analyzer',ignore=shutil.ignore_patterns('bin','obj','TestResults'))
for name in ('pressure-plate','negative-semantic'):
    shutil.copytree(repo/'samples/mechanisms'/name,work/'samples/mechanisms'/name)
for path in (repo/'Packages').rglob('*.cs'):
    dest=work/'support'/path.relative_to(repo/'Packages')
    dest.parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(path,dest)
(work/'assemblies').mkdir()
# Read-only analysis metadata from the existing warm cache; stage execution must use R2-F's versioned cache.
for path in (pathlib.Path.home()/'.cache/gamecore-studio/stage/_warm/Library/ScriptAssemblies').glob('Unity.*.dll'):
    shutil.copyfile(path,work/'assemblies'/path.name)
managed=pathlib.Path.home()/'Unity/Hub/Editor/6000.0.75f1/Editor/Data/Managed'
refs=['/unity/Data/Managed/'+p.relative_to(managed).as_posix() for p in managed.rglob('*.dll') if p.name.startswith(('UnityEngine','UnityEditor'))]
refs+=['/slot/assemblies/'+p.name for p in (work/'assemblies').glob('*.dll')]
refs+=['/nuget/nunit/3.14.0/lib/netstandard2.0/nunit.framework.dll']
(work/'rules.json').write_text(json.dumps({'Schema':1,'References':refs,'SupportSources':['/slot/support']}))
(work/'run.sh').write_text('''#!/bin/sh
set -eu
/dotnet/dotnet test /slot/analyzer/Tests/StageAnalyzer.Tests.csproj --nologo
/dotnet/dotnet /slot/analyzer/bin/Debug/net8.0/StageAnalyzer.dll --root /slot/samples/mechanisms/pressure-plate --rules /slot/rules.json --out /slot/pressure-findings.json
rc=0
/dotnet/dotnet /slot/analyzer/bin/Debug/net8.0/StageAnalyzer.dll --root /slot/samples/mechanisms/negative-semantic --rules /slot/rules.json --out /slot/negative-findings.json || rc=$?
test "$rc" = 3
''')
PY
docker run --rm --network none --read-only --cap-drop ALL --security-opt no-new-privileges \
  --user "$(id -u):$(id -g)" --tmpfs /tmp:rw,nosuid,nodev \
  --mount "type=bind,src=$work,dst=/slot" \
  --mount "type=bind,src=$HOME/.dotnet,dst=/dotnet,readonly" \
  --mount "type=bind,src=$HOME/.nuget/packages,dst=/nuget,readonly" \
  --mount "type=bind,src=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor,dst=/unity,readonly" \
  --mount type=bind,src=/usr/lib/x86_64-linux-gnu,dst=/usr/lib/x86_64-linux-gnu,readonly \
  --env HOME=/slot/home --env DOTNET_CLI_HOME=/slot/home --env DOTNET_ROOT=/dotnet --env NUGET_PACKAGES=/nuget \
  --env DOTNET_NOLOGO=1 --env DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 --env DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  public.ecr.aws/ubuntu/ubuntu:24.04 sh /slot/run.sh
python3 - "$work" <<'PY'
import json,pathlib,sys
root=pathlib.Path(sys.argv[1])
assert not json.loads((root/'pressure-findings.json').read_text())['findings']
expected=json.loads((root/'samples/mechanisms/negative-semantic/expected.json').read_text())
observed={f['RuleId'] for f in json.loads((root/'negative-findings.json').read_text())['findings']}
assert set(expected)<=observed,(expected,observed)
print('Offline analyzer: restore + tests + pressure pass + negative findings PASS (network none)')
PY
