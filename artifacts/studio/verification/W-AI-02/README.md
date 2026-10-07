# W-AI-02: Point at a location; "add a ferryman NPC here who talks about the bell": NPC spawned with dialogue, patrol, nav

Verdict: **FAIL**. Fresh installed-worker six-operation NPC candidate is Invalid: StageFailed projecting npcs[6] because the same-change-set path Assets/Hollowmere/Npcs/Definitions/FerrymanElian.asset does not resolve. Candidate never applies; actual Play effect reports candidate was not applied. Current measured NavMesh/shared-prefab prerequisite passed.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
GAMECORE_P42G_NPC_VIEW=<current prerequisite receipt> GAMECORE_P42G_NPC_CREATION=1 python3 artifacts/studio/verification/TOOLS/live-p42i.py npc --row W-AI-02 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow p42f-npc
```

## Current-run evidence

- [W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/staged.json](../W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/staged.json)
- [W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/play-effect.json](../W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/play-effect.json)
- [W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/candidate.json](../W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/candidate.json)
- [W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/outcome.json](../W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/outcome.json)
