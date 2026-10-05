#!/usr/bin/env bash
# Create a content-free Unity project. Existing files are never overwritten.
# Usage: studio/tools/new-project.sh games/<name> [DisplayName]
# Then run unity-batch.sh --project <abs> --log-dir <dir> --label setup --
#   -quit -executeMethod GameCoreProjectSetup.Configure
# Template: games/cleanproof/Template~ (no Hollowmere content or lock cache).
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
python3 - "$repo" "${1:-games/cleanproof}" "${2:-Saltmarsh}" <<'PY'
import json, os, pathlib, re, sys
from xml.sax.saxutils import quoteattr
repo=pathlib.Path(sys.argv[1]); target=(repo/sys.argv[2]).resolve(); title=sys.argv[3]
if target.parent != repo/'games' or not re.fullmatch(r'[A-Za-z0-9_-]+',target.name):
    raise SystemExit('project must be a direct games/<name> child')
def write(path, data):
    path=target/path; path.parent.mkdir(parents=True,exist_ok=True)
    if not path.exists(): path.write_text(data)
for src in sorted((repo/'games/cleanproof/Template~').rglob('*')):
    if src.is_file(): write(src.relative_to(repo/'games/cleanproof/Template~'),src.read_text())
deps={p.parent.name:'file:'+os.path.relpath(p.parent,target/'Packages') for p in sorted((repo/'Packages').glob('com.gamecore.*/package.json'))}
deps.update({'com.unity.entities':'1.4.6','com.unity.burst':'1.8.28','com.unity.collections':'2.6.6','com.unity.mathematics':'1.3.2','com.unity.inputsystem':'1.19.0','com.unity.ai.navigation':'2.0.12','com.unity.render-pipelines.universal':'17.0.4','com.unity.test-framework':'1.6.0','com.unity.nuget.newtonsoft-json':'3.2.1','com.unity.toolchain.linux-x86_64':'2.0.11'})
for module in ['ai','animation','audio','imageconversion','imgui','jsonserialize','physics','screencapture','ui','uielements','unitywebrequest','video']: deps['com.unity.modules.'+module]='1.0.0'
write('Packages/manifest.json',json.dumps({'dependencies':dict(sorted(deps.items()))},indent=2)+'\n')
write('ProjectSettings/ProjectSettings.asset','''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  serializedVersion: 27
  companyName: GameCore
  productName: '''+json.dumps(title, ensure_ascii=False)+'''
  m_ActiveColorSpace: 1
  activeInputHandler: 1
  scriptingBackend:
    Standalone: 1
  defaultScreenWidth: 1280
  defaultScreenHeight: 720
  runInBackground: 1
''')
write('.gitignore','Library/\nTemp/\nObj/\nLogs/\nUserSettings/\nBuilds/\nMemoryCaptures/\n*.csproj\n*.sln\n')
write('Assets/link.xml','<linker>\n  <assembly fullname='+quoteattr(title)+' preserve="all" />\n</linker>\n')
print('Scaffold ready:',target,'(existing files preserved; Unity resolves its own lock)')
PY
