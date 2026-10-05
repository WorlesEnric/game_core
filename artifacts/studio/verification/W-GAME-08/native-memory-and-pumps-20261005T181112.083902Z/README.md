# native-memory-and-pumps

Verdict: **PASS**.

Source revision: `20e34d1e6a6800e4b0bd4a1e3ebd44611a968c55`; host: `worlesenric`.
Started: 2026-10-05T18:11:12.085275+00:00; ended: 2026-10-05T18:19:38.734920+00:00; duration: 506.651 s.

Command (from repository root unless cwd specified):

```sh
bash -c 'set -euo pipefail
unity_tools_dir="$PWD/studio/tools"
source "$unity_tools_dir/unity-slot.sh"
unity_slot_acquire
trap unity_slot_release EXIT
python3 studio/stage/run-redacted.py --log "$1/editor.log" --timeout 1500 --silence 600 --   "$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" -projectPath "$PWD/games/hollowmere"   -logFile - -runTests -testPlatform EditMode -testFilter "$2" -testResults "$1/results.xml"
' p42-graphical ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-GAME-08/native-memory-and-pumps-20261005T181112.083902Z R2_38_W_UI_04_W_GAME_08_TenCyclesWithSnapshotsAndOnePump
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
