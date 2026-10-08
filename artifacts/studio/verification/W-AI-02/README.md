# W-AI-02: Point at a location; "add a ferryman NPC here who talks about the bell": NPC spawned with dialogue, patrol, nav

Verdict: **PASS**. Fresh installed-worker Ferryman candidate stages and applies unchanged; graph enrollment, committed movement, NavMesh and bell dialogue pass in real Play. Scene-entity roster is 20 → 21 → 20 after normal Undo. No retained candidate reuse, repair or regeneration.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/workflows-p42l.py
```

## Current-run evidence

- [W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/staged.json](../W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/staged.json)
- [W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/apply-report.json](../W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/apply-report.json)
- [W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/play-effect.json](../W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/play-effect.json)
- [W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/roster-before.json](../W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/roster-before.json)
- [W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/roster-applied.json](../W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/roster-applied.json)
- [W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/roster-undone.json](../W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/roster-undone.json)
- [W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/undo-result.json](../W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/undo-result.json)
- [W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/candidate.json](../W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/candidate.json)
- [W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/request.json](../W-AI-02/p42l-npc-20261007T235545.087554Z/workflow/ferryman2/request.json)
