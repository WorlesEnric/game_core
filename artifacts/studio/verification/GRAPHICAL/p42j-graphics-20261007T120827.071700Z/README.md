# p42j-graphics

Verdict: **PASS**.

Source revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; host: `worlesenric`.
Started: 2026-10-07T12:08:27.082116+00:00; ended: 2026-10-07T12:10:42.610119+00:00; duration: 135.530 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2j/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2j/artifacts/studio/verification/GRAPHICAL/p42j-graphics-20261007T120827.071700Z/logs --label p42j-graphics --attempts 1 --timeout 1800 --results ~/wkspace/gc-studio/p4.2j/artifacts/studio/verification/GRAPHICAL/p42j-graphics-20261007T120827.071700Z/results.xml -- -runTests -testPlatform EditMode -testFilter 'R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers|D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce|Render_TargetMatchesTheViewportArea|PreviewScreenCapturesARenderTextureOrSkipsHeadless'
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
