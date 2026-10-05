# installed-media

Verdict: **FAIL**. See XML dispositions and nonpassing case details in result.json.

Source revision: `12d7e5c7ababe95a1e251343584755e6c38fd454`; host: `worlesenric`.
Started: 2026-10-05T19:22:18.562973+00:00; ended: 2026-10-05T19:26:05.263070+00:00; duration: 226.701 s.

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
' p42-graphics ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-ETOS-07/installed-media-20261005T192218.561813Z DISPLAY=:1 GAMECORE_P42_EVIDENCE=~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-ETOS-07/installed-media-20261005T192218.561813Z GAMECORE_ETOS_LIVE=1 GAMECORE_ETOS_AUTOSTART=1 GAMECORE_ETOS_PROJECT_ID=fd465bc5bd5491d4f887faf017ddb99ac90cba9ce717877fc5f6a53fbbfaa6d8 GAMECORE_ETOS_KEY_FILE=~/.config/gamecore-studio/app-key.json GC_ETOS_EVIDENCE_DIR=~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-ETOS-07/installed-media-20261005T192218.561813Z/live GC_ETOS_FIXTURE_OUT=~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-ETOS-07/installed-media-20261005T192218.561813Z/fixtures ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity -projectPath ~/wkspace/gc-studio/p4.2/games/hollowmere -logFile - -runTests -testPlatform EditMode -testFilter C_D_E_F_I_MediaOps -testResults ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-ETOS-07/installed-media-20261005T192218.561813Z/results.xml
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
