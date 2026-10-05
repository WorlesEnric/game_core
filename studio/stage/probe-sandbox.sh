#!/usr/bin/env bash
# Operator-only D3 smoke probe, NOT the stage lane. No candidate code, paid operations or live project mounts.
# Usage: probe-sandbox.sh <new-output-directory> [local-docker-image]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
[[ $# -ge 1 && $# -le 2 ]] || exit 2
out=$(realpath -m "$1")
[[ ! -e "$out" ]] || { echo 'probe output must be a new directory' >&2; exit 2; }
image="${2:-public.ecr.aws/ubuntu/ubuntu:24.04}"
editor="${HOME}/Unity/Hub/Editor/6000.0.75f1/Editor"
mkdir -p "$out/project/Assets" "$out/project/ProjectSettings" "$out/project/Packages" "$out/home/.local/share/unity3d/Unity" "$out/home/.config/unity3d" "$out/home/.cache/unity3d" "$out/logs"
printf '{"dependencies":{}}\n' > "$out/project/Packages/manifest.json"
printf 'm_EditorVersion: 6000.0.75f1\n' > "$out/project/ProjectSettings/ProjectVersion.txt"
# Only exact licence files are mounted, if present; no credential directories are exposed or inspected.
python3 - "$out" "$editor" "$image" <<'PY'
import os, pathlib, shlex, sys
out, editor, image = sys.argv[1:]
args=['docker','run','--rm','--init','--network','none','--read-only','--cap-drop','ALL','--security-opt','no-new-privileges',
      '--user',str(os.getuid())+':'+str(os.getgid()),'--workdir',out+'/project','--tmpfs','/tmp:rw,nosuid,nodev',
      '--mount',f'type=bind,src={out},dst={out}', '--mount',f'type=bind,src={editor},dst={editor},readonly',
      '--mount','type=bind,src=/usr/lib/x86_64-linux-gnu,dst=/usr/lib/x86_64-linux-gnu,readonly',
      '--env',f'HOME={out}/home']
licences=[pathlib.Path.home()/'.local/share/unity3d/Unity/Unity_lic.ulf',pathlib.Path('/var/lib/unity/Unity_lic.ulf')]
for path in licences:
    if path.is_file():
        args+=['--mount',f'type=bind,src={path},dst={out}/home/.local/share/unity3d/Unity/Unity_lic.ulf,readonly']
entitlement=pathlib.Path.home()/'.config/unity3d/Unity/licenses/UnityEntitlementLicense.xml'
if entitlement.is_file():
    destination=pathlib.Path(out)/'home/.config/unity3d/Unity/licenses/UnityEntitlementLicense.xml'
    destination.parent.mkdir(parents=True,exist_ok=True)
    args+=['--mount',f'type=bind,src={entitlement},dst={destination},readonly']
args += [image,editor+'/Unity']
wrapper=pathlib.Path(out)/'docker-editor.sh'
wrapper.write_text('#!/usr/bin/env bash\nexec '+shlex.join(args)+' "$@"\n')
wrapper.chmod(0o755)
PY
rc=0
UNITY="$out/docker-editor.sh" bash "$here/../tools/unity-batch.sh" --project "$out/project" --log-dir "$out/logs" --label sandbox-probe --attempts 1 --timeout 60 -- -quit || rc=$?
printf 'Docker Unity probe exit: %s (see redacted logs; no verdict issued)\n' "$rc"
exit "$rc"
