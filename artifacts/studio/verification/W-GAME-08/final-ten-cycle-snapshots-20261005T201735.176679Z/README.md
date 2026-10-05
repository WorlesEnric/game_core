# final-ten-cycle-snapshots

Verdict: **PASS**.

Source revision: `c532b77edd93099ddd1e45d20f48db5d627413b3`; host: `worlesenric`.
Started: 2026-10-05T20:17:35.178007+00:00; ended: 2026-10-05T20:20:42.509155+00:00; duration: 187.332 s.

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
' p42-graphics ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-GAME-08/final-ten-cycle-snapshots-20261005T201735.176679Z DISPLAY=:1 GAMECORE_P42_EVIDENCE=~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-GAME-08/final-ten-cycle-snapshots-20261005T201735.176679Z GAMECORE_ETOS_KEY_FILE=/nonexistent/p42-memory-offline GAMECORE_ETOS_AUTOSTART=0 ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity -projectPath ~/wkspace/gc-studio/p4.2/games/hollowmere -logFile - -runTests -testPlatform EditMode -testFilter R2_38_W_UI_04_W_GAME_08_TenCyclesWithSnapshotsAndOnePump -testResults ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-GAME-08/final-ten-cycle-snapshots-20261005T201735.176679Z/results.xml
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
