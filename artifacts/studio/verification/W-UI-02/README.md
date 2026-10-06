# W-UI-02: Box-select three NPCs behind a fence; overlap list; choose NPCs

Verdict: **BLOCKED** — the creator's marquee-result overlap chooser is absent from `studio.ui` (R7-A-owned).

R7-B adds and executes `ScenariosR7Picking.RunFence` on source `0909e4a2`. It creates a saved isolated fixture with the shipped Maren/Odd/Pip definitions and NPC prefab instances behind visible fence rails/posts. Actual marquee includes all three authored identities. Actual point-picking sees the nearer fence and occluded NPCs; controller choices resolve each NPC and an exact additive three-NPC set without fence geometry. These checks complete without assertion errors, but point-click/controller choices do **not** substitute for the requested marquee-result creator sequence.

## Evidence

- [receipt.json](r7-b-20261006T210920229Z/receipt.json): `BLOCKED`, `error: null`, batch Editor, graphics device Null.
- [observations.json](r7-b-20261006T210920229Z/observations.json): exact fixture refs, marquee, depth/occlusion results and selected identity sets; controller-only steps explicitly marked `creatorInteraction: false`.
- [Editor log](r7-b-logs/r7b-fence-20261007T050847-4029716-a1.log): expected driver exit 2; wrapper reports failure rather than promoting BLOCKED to PASS.

## Exact R7-A request

`Packages/com.gamecore.studio.ui/Editor/Viewport/StudioViewportWindow.cs`, `MarqueeSelect(Rect, SelectionOp, bool?)`: present resulting marquee candidates through the overlap chooser and allow choosing the three NPC identities while excluding fence geometry. It currently delegates to `Picker.Marquee` without opening that chooser. Retain stable button names for acceptance interaction.

## Reproduce

```sh
GAMECORE_ETOS_AUTOSTART=0 GAMECORE_ETOS_LIVE=0 \
studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/artifacts/studio/verification/W-UI-02/rerun-logs" \
  --label r7b-fence --attempts 1 -- \
  -executeMethod Hollowmere.P3_2.Workflows.ScenariosR7Picking.RunFence
```

Capture limitation: the required batch wrapper hardcodes `-nographics`; existing real ViewportRenderer/GUIView pixel capture cannot run with a Null graphics device. The driver records this explicitly and creates no synthetic PNG. With a graphics-enabled batch launch it uses the existing real offscreen viewport and GUIView capture paths, never desktop `:1`. No paid operations or installed-service changes. Fixture scene/assets are deleted after the run.
