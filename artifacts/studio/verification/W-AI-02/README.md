# W-AI-02: Point at a location; "add a ferryman NPC here who talks about the bell": NPC spawned with dialogue, patrol, nav

Verdict: **PASS**. Fresh unchanged installed-worker new Ferryman candidate applies. Real Play proves enrolled bell dialogue starts and ends, committed patrol motion, and NavMesh binding; normal History Undo succeeds and restores the roster.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
GAMECORE_P42G_NPC_CREATION=1 GAMECORE_P42G_NPC_VIEW=artifacts/studio/verification/W-AI-02/p42j-npc-view-20261007T125422.879147Z/npc-view-prerequisite.json python3 artifacts/studio/verification/TOOLS/live-p42j.py npc --row W-AI-02 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow p42f-npc
```

## Current-run evidence

- [W-AI-02/p42j-npc-20261007T125627.367021Z/workflow/ferryman2/play-effect.json](../W-AI-02/p42j-npc-20261007T125627.367021Z/workflow/ferryman2/play-effect.json)
- [W-AI-02/p42j-npc-20261007T125627.367021Z/workflow/ferryman2/undo-result.json](../W-AI-02/p42j-npc-20261007T125627.367021Z/workflow/ferryman2/undo-result.json)
