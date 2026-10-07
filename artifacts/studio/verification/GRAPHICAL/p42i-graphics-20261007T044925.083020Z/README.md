# p42i-graphics

Verdict: **PASS**.

Source revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; host: `worlesenric`.
Started: 2026-10-07T04:49:25.084697+00:00; ended: 2026-10-07T04:50:27.308114+00:00; duration: 62.225 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2i/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2i/artifacts/studio/verification/GRAPHICAL/p42i-graphics-20261007T044925.083020Z/logs --label p42i-graphics --attempts 1 --timeout 1800 --results ~/wkspace/gc-studio/p4.2i/artifacts/studio/verification/GRAPHICAL/p42i-graphics-20261007T044925.083020Z/results.xml -- -runTests -testPlatform EditMode -testFilter 'R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers|D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce|Render_TargetMatchesTheViewportArea|PreviewScreenCapturesARenderTextureOrSkipsHeadless'
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
