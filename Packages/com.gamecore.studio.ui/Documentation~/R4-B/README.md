# R4-B verification

Linux host, branch `codex/r4-b`, base `c79a3832`. Implementation checkpoint:
`c1bb1f0c` (final product code). No paid operation, service restart, credential-file inspection or
sibling checkout change. Text evidence replaces the host home directory with
`~`; XML dispositions are preserved. Counts below come from XML, not stdout.

| Run | Passed | Failed | Skipped | Interpretation |
|---|---:|---:|---:|---|
| `r4-b-before.xml` | 0 | 3 | 0 | Expected original-code failures: late placement, missing tray status (two cases) |
| `r4-b-headless.xml` | 62 | 0 | 3 | UI 55 + P2.1 3 + R3-B 4 passed; three cases require graphics |
| `graphical-first.xml` | 7 | 2 | 0 | Rig incorrectly expected Tab key-up at the image after focus changed; initial placement loop exposed a 29-pixel X11 translation and exhausted observations before settling |

The unchanged base memory source fails the new 15% budget guard once, retained
in `memory-before.log`; both host tests pass after the assertion change.

The final graphical run is **12 passed, 0 failed, 0 skipped** in
`r4-b-graphical-final.xml`. It confirms the key rig fix against unchanged viewport
routing, exact placement at a feasible native origin, creator movement after
settlement, and the precise timeout for the original y=40 clamped request.

The trace runs (`layout-trace`, `layout-ack`, `layout-native`, `layout-workarea`)
are retained failures, not passes. Native readback ruled out a simple offset;
the final implementation retries the requested rectangle without drift. The
positive fixture's former creator y=60 was also below the native minimum and
its single yield preceded acknowledgement; it now asserts exact y=140 after
one second. The original y=40 request has its own mandatory timeout test, not
an ignore or a loose successful-placement assertion.

Unity's [6000.0 EditorWindow reference](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0/Editor/Mono/EditorWindow.cs#L1140)
shows that `position` first updates a managed rectangle before driving its
parent window. Therefore immediate getter equality is insufficient to establish
native settlement. The retained native-frame traces establish the actual clamp
on this host. No Unity source was copied into the product.

Final `r4-b-final.xml`: **71 total, 67 passed, 0 failed, 4 graphical skips**.
This is 65 passing UI/P2.1/R3-B cases plus the two P3.1 hooks. All four skips pass
in the separate graphical run. The wrapper correctly marks the headless run
partial. Ten-cycle allocated growth is **-27.18%**, reserved growth **+2.05%**;
`memory-cycles.json` is the new report and `admission-refusal.json` is the hook's
fail-closed journal witness. The existing shared artifacts/settings were
restored after retaining these copies.

The before run used regression checkpoint `82e83ba8`. Final product code is
`c1bb1f0c`; the final graphical run predates only the additional integral-layout
unit test, which passes in the final headless run. Host tests: 2 passed. Metadata:
42 packages / 91 assemblies; C# policy: 1,199 files. Logs retain intermediate
failures; no skipped or failed case is relabeled as a pass.

## Commands

Run from this clone's root. Every invocation holds at most one host-wide slot.
The batch wrapper supplies `-batchmode -nographics`, `-projectPath`, and
`-testResults` exactly as `unity-compile.sh` does.

```sh
bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/.unity-logs" --label r4-b-before \
  --results "$PWD/.unity-logs/r4-b-before.xml" -- \
  -runTests -testPlatform EditMode \
  -testFilter 'GameCore\.Studio\.UI\.Tests\.R4UiRegressionTests'

bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/.unity-logs" --label r4-b-headless \
  --results "$PWD/.unity-logs/r4-b-headless.xml" -- \
  -runTests -testPlatform EditMode \
  -testFilter 'GameCore\.Studio\.UI.*|Hollowmere\.P2_1.*|Hollowmere\.R2_C.*|Hollowmere\.R3_B.*|Hollowmere\.R4_B.*'

GC_STUDIO_UNITY_SLOTS=3 \
UNITY="$PWD/Packages/com.gamecore.studio.ui/Tests/Host/r4-unity-display.sh" \
bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/.unity-logs" --label r4-b-graphical \
  --results "$PWD/.unity-logs/r4-b-graphical.xml" \
  --require-test GameCore.Studio.UI.Tests.R2UiRegressionTests.R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers \
  --require-test GameCore.Studio.UI.Tests.R3UiRegressionTests.D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce -- \
  -runTests -testPlatform EditMode \
  -testFilter 'R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers|D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce|Render_TargetMatchesTheViewportArea|GameCore\.Studio\.UI\.Tests\.R4UiRegressionTests'

PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover \
  -s Packages/com.gamecore.studio.ui/Tests/Host -p 'test_*.py'
python3 tools/check_package_metadata.py
python3 tools/check_game_core_csharp.py
git diff --check
```

The graphical adapter removes only `-batchmode`/`-nographics`, uses `DISPLAY=:1`,
and retains the wrapper's redactor, watchdog, test results and reservation. It
locks the allocator mutex, lets existing Editors finish, then starts Unity only
with zero other Editors. It holds that mutex until its Editor exits. An earlier
`GC_STUDIO_UNITY_SLOTS=1` invocation was stopped while still waiting, before any
Editor/reservation; it produced no XML and is not a test run.

No test namespace matching Hollowmere.R2_C or Hollowmere.R4_B exists in this
checkout. The requested filter alternatives were retained unchanged; the new
regressions live in the owned UI package's existing test assembly.

Final combined UI and P3.1 command (same batch arguments as the earlier UI run):

```sh
bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/.unity-logs" --label r4-b-final \
  --results "$PWD/.unity-logs/r4-b-final.xml" -- \
  -runTests -testPlatform EditMode \
  -testFilter 'GameCore\.Studio\.UI.*|Hollowmere\.P2_1.*|Hollowmere\.R2_C.*|Hollowmere\.R3_B.*|Hollowmere\.R4_B.*|TenPlayEditCycles|AdmissionFromPlayModeCaptures'
```

The final graphical invocation used label/results `r4-b-graphical-final` and the
same graphical filter above, with both mandatory cases confirmed present and
passing in XML. The intermediate focused layout runs selected D12 alone, or
`D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce|P42_UI_02`.
`SHA256SUMS` covers sanitized retained bytes, excluding this index and itself.
