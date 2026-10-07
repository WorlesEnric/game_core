# p42k-graphics

Verdict: **PASS**.

Source revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; host: `worlesenric`.
Started: 2026-10-07T18:33:59.633254+00:00; ended: 2026-10-07T18:34:51.575004+00:00; duration: 51.943 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2k/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2k/artifacts/studio/verification/GRAPHICAL/p42k-graphics-20261007T183359.631957Z/logs --label p42k-graphics --attempts 1 --timeout 1800 --results ~/wkspace/gc-studio/p4.2k/artifacts/studio/verification/GRAPHICAL/p42k-graphics-20261007T183359.631957Z/results.xml -- -runTests -testPlatform EditMode -testFilter 'R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers|D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce|Render_TargetMatchesTheViewportArea|PreviewScreenCapturesARenderTextureOrSkipsHeadless'
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
