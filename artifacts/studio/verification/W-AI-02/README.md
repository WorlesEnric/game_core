# W-AI-02: Point at a location; "add a ferryman NPC here who talks about the bell": NPC spawned with dialogue, patrol, nav

Verdict: **FAIL**. Fresh unchanged installed-worker Ferryman candidate is refused before apply: GP-LOG-002, FerrymanElian is listed twice in Assets/Hollowmere/Rules/HollowmereContent.asset. No NPC Play/nav/dialogue or successful admission is claimed; original roster remains 20. Candidate is not repaired or regenerated.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
GAMECORE_P42G_NPC_CREATION=1 GAMECORE_P42G_NPC_VIEW=artifacts/studio/verification/W-AI-02/p42k-npc-view-20261007T191235.565763Z/npc-view-prerequisite.json python3 artifacts/studio/verification/TOOLS/live-p42k.py npc --row W-AI-02 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow p42f-npc
```

## Current-run evidence

- [W-AI-02/p42k-npc-20261007T191317.157379Z/workflow/ferryman2/outcome.json](../W-AI-02/p42k-npc-20261007T191317.157379Z/workflow/ferryman2/outcome.json)
- [W-AI-02/p42k-npc-20261007T191317.157379Z/workflow/ferryman2/staged.json](../W-AI-02/p42k-npc-20261007T191317.157379Z/workflow/ferryman2/staged.json)
- [W-AI-02/p42k-npc-20261007T191317.157379Z/workflow/ferryman2/candidate.json](../W-AI-02/p42k-npc-20261007T191317.157379Z/workflow/ferryman2/candidate.json)
- [W-AI-02/p42k-npc-20261007T191317.157379Z/workflow/ferryman2/play-effect.json](../W-AI-02/p42k-npc-20261007T191317.157379Z/workflow/ferryman2/play-effect.json)
- [W-AI-02/p42k-npc-20261007T191317.157379Z/workflow/ferryman2/roster-before.json](../W-AI-02/p42k-npc-20261007T191317.157379Z/workflow/ferryman2/roster-before.json)
- [W-AI-02/p42k-npc-20261007T191317.157379Z/workflow/ferryman2/roster-undone.json](../W-AI-02/p42k-npc-20261007T191317.157379Z/workflow/ferryman2/roster-undone.json)
