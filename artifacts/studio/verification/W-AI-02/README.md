# W-AI-02: Point at a location; "add a ferryman NPC here who talks about the bell": NPC spawned with dialogue, patrol, nav

Verdict: **FAIL**. P4.2f deploys the exact R6-C designer contract with a USD 0.50 worker ceiling. The unchanged text2 run returns needs_clarification for missing confirmed navigation. A fresh NPC-only request receives actual sampled NavMesh points and complete paths as the creator follow-up, then produces an unchanged six-operation candidate: apply succeeds and roster rises 20 to 21. Actual WorkflowPlayChecks.Effect("W-AI-02", ...) enters Play but fails dialogue start refused. R6-C AppliedOr consults the durable Applied journal; normal History undo succeeds, journals Undone and restores 21 to 20 despite post-Play candidate UI invalidity. The retained unreachable-entry witness still refuses before writes in the passing R6 regression XML. No candidate bytes are repaired and no full NPC Play pass is claimed.

Product revision: `4ac7ba858b91e73e2d5de9dc6f02852c13feec56`. Earlier attempts remain historical evidence.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42f.py text2
python3 artifacts/studio/verification/TOOLS/live-p42f.py npc
```

## Retained evidence

- [W-AI-02/p42f-text2-20261006T173140.444632Z](../W-AI-02/p42f-text2-20261006T173140.444632Z)
- [W-AI-02/p42f-npc-20261006T174113.356793Z](../W-AI-02/p42f-npc-20261006T174113.356793Z)
- [R6-P4.2f/p42f-regression-20261006T175113.703121Z](../R6-P4.2f/p42f-regression-20261006T175113.703121Z)
