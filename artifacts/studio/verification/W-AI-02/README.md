# W-AI-02: Point at a location; "add a ferryman NPC here who talks about the bell": NPC spawned with dialogue, patrol, nav

Verdict: **PASS (R9-A retained-candidate replay)**. The unchanged six-operation candidate passes Stage/Apply, actual graphical Play patrol/NavMesh/dialogue, and complete-byte journal Undo. Exact retained catalog context is preserved. Product `6a67e287`; exact source hashes and receipts: [R9-A result](r9-a-npc-20261007T103755.217153Z/result.json), [Play effect](r9-a-npc-20261007T103755.217153Z/ferryman2/play-effect.json), [source hashes](r9-a-npc-20261007T103755.217153Z/source-sha256.json). No fresh model call or repaired candidate.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
The P4.2i failure evidence below remains historical. R9-A's successful replay supersedes that projection failure; initial R9-A catalog/path refusals are retained separately.

## Reproduce

```sh
GAMECORE_P42G_NPC_VIEW=<current prerequisite receipt> GAMECORE_P42G_NPC_CREATION=1 python3 artifacts/studio/verification/TOOLS/live-p42i.py npc --row W-AI-02 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow p42f-npc
```

## Historical P4.2i evidence

- [W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/staged.json](../W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/staged.json)
- [W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/play-effect.json](../W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/play-effect.json)
- [W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/candidate.json](../W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/candidate.json)
- [W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/outcome.json](../W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/outcome.json)

## R9-A reproduction

On fresh project state: `python3 artifacts/studio/verification/W-AI-06/r9-a/run.py --lane npc`. It refuses existing applied journals rather than deleting them. See [packet](../../../../docs/studio/packets/R9-A.md) for failed-attempt recovery and source provenance.
