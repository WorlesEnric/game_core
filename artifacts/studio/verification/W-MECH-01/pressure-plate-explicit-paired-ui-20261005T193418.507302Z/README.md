# pressure-plate-explicit-paired-ui

Verdict: **FAIL**.

Source revision: `d26494a498d422c9495ef503d88afbd1551d79c3`; host: `worlesenric`.
Started: 2026-10-05T19:34:18.508577+00:00; ended: 2026-10-05T19:36:32.994992+00:00; duration: 134.488 s.

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
' p42-graphics ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-MECH-01/pressure-plate-explicit-paired-ui-20261005T193418.507302Z DISPLAY=:1 GAMECORE_P42_EVIDENCE=~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-MECH-01/pressure-plate-explicit-paired-ui-20261005T193418.507302Z GAMECORE_ETOS_LIVE=1 GAMECORE_ETOS_AUTOSTART=1 GAMECORE_ETOS_PROJECT_ID=fd465bc5bd5491d4f887faf017ddb99ac90cba9ce717877fc5f6a53fbbfaa6d8 GAMECORE_ETOS_KEY_FILE=~/.config/gamecore-studio/app-key.json GC_ETOS_EVIDENCE_DIR=~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-MECH-01/pressure-plate-explicit-paired-ui-20261005T193418.507302Z/live GC_ETOS_FIXTURE_OUT=~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-MECH-01/pressure-plate-explicit-paired-ui-20261005T193418.507302Z/fixtures GCS_P32_WORKFLOW=mech-b GCS_P32_OUT=~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-MECH-01/pressure-plate-explicit-paired-ui-20261005T193418.507302Z/workflow GCS_P32_MECH_CANDIDATE=~/wkspace/gc-studio/p4.2/samples/mechanisms/pressure-plate/candidate ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity -projectPath ~/wkspace/gc-studio/p4.2/games/hollowmere -logFile - -executeMethod Hollowmere.P4_2.EvidenceEntry.RunStage
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
