# graphics-required-tests

Verdict: **FAIL**. See XML dispositions and nonpassing case details in result.json.

Source revision: `3c5f817aa45a86d0af85cd8af6e953d128524a02`; host: `worlesenric`.
Started: 2026-10-05T17:26:41.171596+00:00; ended: 2026-10-05T17:39:33.452196+00:00; duration: 772.282 s.

Command (from repository root unless cwd specified):

```sh
bash -c 'set -euo pipefail
unity_tools_dir="$PWD/studio/tools"
source "$unity_tools_dir/unity-slot.sh"
unity_slot_acquire
trap unity_slot_release EXIT
python3 studio/stage/run-redacted.py --log "$1/editor.log" --timeout 1500 --silence 600 --   "$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" -projectPath "$PWD/games/hollowmere"   -logFile - -runTests -testPlatform EditMode -testFilter "$2" -testResults "$1/results.xml"
' p42-graphical ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/GRAPHICAL/graphics-required-tests-20261005T172641.170099Z 'R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers|D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce|Render_TargetMatchesTheViewportArea|PreviewScreenCapturesARenderTextureOrSkipsHeadless'
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
