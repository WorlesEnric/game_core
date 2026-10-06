# W-UI-02: Box-select three NPCs behind a fence; overlap list; choose NPCs

Verdict: **PASS** on source `b2717d03`, graphical Linux Editor on `:1` through `unity-batch.sh`. No paid operations or installed-service changes.

`ScenariosR7Picking.RunFence` creates saved, isolated shipped Maren/Odd/Pip NPC instances behind visible fence rails/posts. The full-containment marquee includes three NPCs and three fence posts. The creator's actual resulting chooser has six initially checked toggles. Attached UI Toolkit submit events uncheck all fence candidates and activate **Select chosen objects**. Assertions require exactly the three NPC identities, no fence, no subparts and the unchanged marquee rectangle. Subsequent point choices independently confirm each NPC is occluded behind the nearer fence.

## Required visual evidence

- [Resulting marquee chooser](r7-d-20261006T215026225Z/02-marquee-chooser-ui.png).
- [Only Odd, Pip and Maren checked; fence excluded](r7-d-20261006T215026225Z/03-marquee-npc-choices-ui.png).
- [Applied three-NPC selection](r7-d-20261006T215026225Z/04-marquee-three-npcs-result-ui.png).
- [PASS receipt](r7-d-20261006T215026225Z/receipt.json), [exact identities and creator interactions](r7-d-20261006T215026225Z/observations.json).

The real GUIView captures are 1280×720; all chooser controls are visible. Separate viewport PNGs are actual RenderTexture readbacks, not substitutes for the chooser capture. The fixture is deleted after execution. Final wrapper run: exit 0, 36 seconds.

## Regression and reproduction

[Final EditMode XML](r7-d/results.xml): **71 passed / 0 failed / 0 skipped** (69 UI, including six R7-D regressions; two Hollowmere R7-B). The requested R7_D and R2_38 namespace alternatives match no separate classes in this baseline; R7-D cases live in the UI suite. Source checkpoint `b2717d03`.

```sh
bash artifacts/studio/verification/W-UI-02/r7-d/run.sh
```

This runs the requested suite and both real drivers serially under the shared allocator. UI's actual namespace is uppercase `UI`; the regex accepts `Ui` and `UI`.

## Retained earlier attempts

R7-B's [BLOCKED receipt](r7-b-20261006T210920229Z/receipt.json) remains historical proof of the missing seam; no controller-only result is promoted. [Initial R7-D XML](r7-d/initial-results.xml) is 69/71: a test pinned the wrong prefab path representation and another omitted the submit event target. Both were corrected without weakening selection identity assertions. Initial graphical driver `r7-d-20261006T214245193Z` passes assertions but has overlapping restored Studio windows in its composite; it is not the visual acceptance. Final driver closes only restored Studio panels in its isolated owned Editor before capturing. All attempts and logs remain retained.
