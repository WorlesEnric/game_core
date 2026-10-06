# W-UI-03: Subpart vs logical vs prefab vs scope choice on a lantern

Verdict: **BLOCKED** — the creator's prefab/scope choices are absent from `studio.ui` (R7-A-owned).

R7-B executes `ScenariosR7Picking.RunLantern` on source `0909e4a2`. Actual picking on the shipped lantern prefab produces `Mesh:Body` with its authored logical owner. Subpart selection retains exactly that part/owner; logical selection clears the part. The real prefab and Instance-scoped lantern refs resolve. The driver deliberately does not programmatically select those refs and call that a creator chooser sequence.

## Evidence

- [receipt.json](r7-b-20261006T211016902Z/receipt.json): `BLOCKED`, `error: null`, batch Editor, graphics device Null.
- [observations.json](r7-b-20261006T211016902Z/observations.json): real logical/subpart identities, expected prefab/scope references and the absent creator choices; controller-only actions explicitly identified.
- [Editor log](r7-b-logs/r7b-lantern-20261007T050948-4039836-a1.log): expected driver exit 2; no false passing disposition.

## Exact R7-A request

- `Packages/com.gamecore.studio.ui/Editor/Viewport/ViewportSupport.cs`: `OverlapPopup.Show(IReadOnlyList<PickCandidate>, Vector2, Func<AuthoringRef,string>)`.
- `Packages/com.gamecore.studio.ui/Editor/Viewport/ViewportPicker.cs`: `Choose(PickCandidate, bool, SelectionOp)`.

Add explicit prefab and authoring-scope choices alongside logical/subpart choices. Prefab must resolve to the originating Lantern prefab; Instance scope preserves the logical lantern identity with `AuthorScope.Instance`; subpart retains `Mesh:Body`; logical clears it. Expose stable button names so the driver can activate the actual attached UI buttons.

## Reproduce

```sh
GAMECORE_ETOS_AUTOSTART=0 GAMECORE_ETOS_LIVE=0 \
studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/artifacts/studio/verification/W-UI-03/rerun-logs" \
  --label r7b-lantern --attempts 1 -- \
  -executeMethod Hollowmere.P3_2.Workflows.ScenariosR7Picking.RunLantern
```

Capture limitation: the required batch wrapper hardcodes `-nographics`, so actual offscreen viewport/GUIView pixels are unavailable. This is retained explicitly; no synthetic PNG or graphical pass is claimed. The driver's graphics-capable path uses real Editor pixels without desktop `:1`. No paid operations or installed-service changes. The isolated fixture is removed after the run.
