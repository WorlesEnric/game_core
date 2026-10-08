# p42l-graphics

Verdict: **PASS**.

Source revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; host: `worlesenric`.
Started: 2026-10-07T23:15:07.718382+00:00; ended: 2026-10-07T23:16:01.392824+00:00; duration: 53.675 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2l/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2l/artifacts/studio/verification/GRAPHICAL/p42l-graphics-20261007T231507.717364Z/logs --label p42l-graphics --attempts 1 --timeout 1800 --results ~/wkspace/gc-studio/p4.2l/artifacts/studio/verification/GRAPHICAL/p42l-graphics-20261007T231507.717364Z/results.xml -- -runTests -testPlatform EditMode -testFilter 'R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers|D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce|Render_TargetMatchesTheViewportArea|PreviewScreenCapturesARenderTextureOrSkipsHeadless'
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
