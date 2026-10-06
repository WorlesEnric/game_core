# W-DOC-01: New user adds an NPC with dialogue from the creator guide

Verdict: **PASS**. The creator-guide NPC/dialogue boundary is exercised through npc.addAt in Context and Add line in the Dialogue view, using the existing Maren definition and its bound graph. Placement and dialogue edit apply; save and normal journal undo restore the 20-entity/13-node baseline. Named guide test passes in XML. This is the documented existing-definition flow, not creation of a new unique NPC definition.

Report timestamp: 2026-10-06T15:29:44.437511+00:00 UTC.

Acceptance baseline: merged main `40fb91fa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42d guide
```

## Retained evidence

- [W-DOC-01/p42d-guide-20261006T123449.140659Z/results.xml](../W-DOC-01/p42d-guide-20261006T123449.140659Z/results.xml)
- [W-DOC-01/p42d-guide-20261006T123449.140659Z/guide.json](../W-DOC-01/p42d-guide-20261006T123449.140659Z/guide.json)
- [W-DOC-01/p42d-guide-20261006T123449.140659Z/guide-applied.png](../W-DOC-01/p42d-guide-20261006T123449.140659Z/guide-applied.png)

Exact acceptance/component cases: `P42d.Live.NoviceGuideTests.R2_38_Guide_NpcAndDialogueContextToolsUndo`. Component cases do not close any missing external workflow.

Historical references: P4.2d installed release 0.1.0-e8a72b2d6eb3aad9 on main 40fb91fa. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
