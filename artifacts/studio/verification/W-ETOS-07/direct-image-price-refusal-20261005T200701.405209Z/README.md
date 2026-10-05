# direct-image-price-refusal

Verdict: **PASS**.

Source revision: `c532b77edd93099ddd1e45d20f48db5d627413b3`; host: `worlesenric`.
Started: 2026-10-05T20:07:01.413070+00:00; ended: 2026-10-05T20:16:09.561362+00:00; duration: 548.155 s.

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
' p42-graphics ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z DISPLAY=:1 GAMECORE_P42_EVIDENCE=~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z GAMECORE_ETOS_LIVE=1 GAMECORE_ETOS_AUTOSTART=1 GAMECORE_ETOS_PROJECT_ID=fd465bc5bd5491d4f887faf017ddb99ac90cba9ce717877fc5f6a53fbbfaa6d8 GAMECORE_ETOS_KEY_FILE=~/.config/gamecore-studio/app-key.json GC_ETOS_EVIDENCE_DIR=~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z/live GC_ETOS_FIXTURE_OUT=~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z/fixtures ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity -projectPath ~/wkspace/gc-studio/p4.2/games/hollowmere -logFile - -runTests -testPlatform EditMode -testFilter R2_38_DirectImagePriceRefusalIsRecorded -testResults ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z/results.xml
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
