# layout-1280x720

Verdict: **PASS**.

Source revision: `d26494a498d422c9495ef503d88afbd1551d79c3`; host: `worlesenric`.
Started: 2026-10-05T19:36:53.368549+00:00; ended: 2026-10-05T19:41:03.512079+00:00; duration: 250.144 s.

Command (from repository root unless cwd specified):

```sh
bash -c 'set -euo pipefail
pgrep -af '"'"'Unity|ffmpeg|xvfb'"'"' || true
if pgrep -x Unity >/dev/null; then echo '"'"'BLOCKED: another Editor is active'"'"'; exit 2; fi
unity_tools_dir="$PWD/studio/tools"
source "$unity_tools_dir/unity-slot.sh"
unity_slot_acquire
trap unity_slot_release EXIT
out="$1"; shift
python3 studio/stage/run-redacted.py --log "$out/editor.log" --timeout 1500 --silence 600 -- env "$@"
' p42-graphics ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-UI-01/layout-1280x720-20261005T193653.367571Z DISPLAY=:1 GAMECORE_P42_EVIDENCE=~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-UI-01/layout-1280x720-20261005T193653.367571Z GAMECORE_ETOS_KEY_FILE=/nonexistent/p42-layout-offline GAMECORE_ETOS_AUTOSTART=0 ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity -projectPath ~/wkspace/gc-studio/p4.2/games/hollowmere -logFile - -runTests -testPlatform EditMode -testFilter R2_30_1280By720WorkspaceContainsEveryStudioPanel -testResults ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-UI-01/layout-1280x720-20261005T193653.367571Z/results.xml
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
