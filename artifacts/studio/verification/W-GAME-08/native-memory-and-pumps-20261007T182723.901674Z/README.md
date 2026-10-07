# native-memory-and-pumps

Verdict: **PASS**.

Source revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; host: `worlesenric`.
Started: 2026-10-07T18:27:23.903540+00:00; ended: 2026-10-07T18:29:48.151614+00:00; duration: 144.250 s.

Command (from repository root unless cwd specified):

```sh
bash -c 'set -euo pipefail
pgrep -af '"'"'Unity|ffmpeg|xvfb'"'"' || true
while pgrep -x Unity >/dev/null || pgrep -x ffmpeg >/dev/null; do
  echo '"'"'Waiting for other Editors/recorders before graphical qualification'"'"'; sleep 5
done
unity_tools_dir="$PWD/studio/tools"
source "$unity_tools_dir/unity-slot.sh"
unity_slot_acquire
trap unity_slot_release EXIT
python3 studio/stage/run-redacted.py --log "$1/editor.log" --timeout 1500 --silence 600 -- env GAMECORE_ETOS_KEY_FILE=/nonexistent/p42-offline   "$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" -projectPath "$PWD/games/hollowmere"   -logFile - -runTests -testPlatform EditMode -testFilter "$2" -testResults "$1/results.xml"
' p42-graphical ~/wkspace/gc-studio/p4.2k/artifacts/studio/verification/W-GAME-08/native-memory-and-pumps-20261007T182723.901674Z R2_38_W_UI_04_W_GAME_08_TenCyclesWithSnapshotsAndOnePump
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
