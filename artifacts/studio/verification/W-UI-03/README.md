# W-UI-03: Subpart vs logical vs prefab vs scope choice on a lantern

Verdict: **PASS** on source `b2717d03`, graphical Linux Editor on `:1` through `unity-batch.sh`. No paid operations or installed-service changes.

`ScenariosR7Picking.RunLantern` instantiates the shipped Lantern prefab in a saved isolated scene. Actual Body picking opens the creator chooser. Attached UI Toolkit submit events activate all four choices, asserting:

- **part Mesh:Body**: logical Lantern owner and exactly `Mesh:Body`.
- **Lantern logical object**: same logical identity, no subpart.
- **Prefab**: originating `Assets/Hollowmere/World/Prefabs/Lantern.prefab` reference, matching asset GUID, resolved object and `AuthorScope.Prefab`; no mesh part or definition substitution.
- **Scope: Instance**: exact logical Lantern identity with `AuthorScope.Instance`, no subpart.

## Required visual evidence

- [All four creator choices visible](r7-d-20261006T215104970Z/02-lantern-chooser-ui.png).
- [Subpart selection](r7-d-20261006T215104970Z/03-lantern-subpart-ui.png), [logical selection](r7-d-20261006T215104970Z/05-lantern-logical-ui.png).
- [Prefab chooser](r7-d-20261006T215104970Z/06-lantern-prefab-chooser-ui.png), [prefab result](r7-d-20261006T215104970Z/07-lantern-prefab-ui.png).
- [Instance-scope chooser](r7-d-20261006T215104970Z/09-lantern-instance-scope-chooser-ui.png), [scope result](r7-d-20261006T215104970Z/10-lantern-instance-scope-ui.png).
- [PASS receipt](r7-d-20261006T215104970Z/receipt.json), [exact refs, scopes and attached button interactions](r7-d-20261006T215104970Z/observations.json).

Real GUIView captures are 1280×720; no synthetic image. The fixture is deleted after execution. Final wrapper run: exit 0, 42 seconds.

## Regression and reproduction

[Shared final XML](../W-UI-02/r7-d/results.xml): **71 passed / 0 failed / 0 skipped**, including prefab-root-versus-child/definition, scope replacement, logical/subpart and unavailable-prefab no-op regressions.

```sh
bash artifacts/studio/verification/W-UI-02/r7-d/run.sh
```

## Retained earlier attempts

[R7-B BLOCKED receipt](r7-b-20261006T211016902Z/receipt.json) remains historical. Initial R7-D `r7-d-20261006T214420058Z` passes selection assertions but includes restored unrelated Studio panels in its composite; only the final isolated-window captures above establish visual acceptance. Logs and both attempts remain retained.
