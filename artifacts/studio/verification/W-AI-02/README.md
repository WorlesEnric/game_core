# W-AI-02: Point at a location; "add a ferryman NPC here who talks about the bell": NPC spawned with dialogue, patrol, nav

Verdict: **FAIL**. Creation/undo portion passes: exact roster identity set returns 20→21→20. The untouched live ferryman candidate also edits Odd’s graph entry, leaving nodes 0–7 unreachable. The corrected driver fails its Play gate at bake with GP-DLG-005; NPC navigation/dialogue in Play is not established.

Report timestamp: 2026-10-06T12:40:21.171231+00:00 UTC.

Acceptance baseline: merged main `40fb91fa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42d text2
```

## Retained evidence

- [W-AI-02/p42d-text2-20261006T113349.342620Z/workflow/ferryman2/play-effect.json](../W-AI-02/p42d-text2-20261006T113349.342620Z/workflow/ferryman2/play-effect.json)
- [W-AI-02/p42d-text2-20261006T113349.342620Z/workflow/ferryman2/roster-undone.json](../W-AI-02/p42d-text2-20261006T113349.342620Z/workflow/ferryman2/roster-undone.json)
- [W-AI-02/p42d-text2-20261006T113349.342620Z/workflow/ferryman2/undo-result.json](../W-AI-02/p42d-text2-20261006T113349.342620Z/workflow/ferryman2/undo-result.json)
- [W-AI-02/p42d-text2-20261006T113349.342620Z/workflow/ferryman2/candidate.json](../W-AI-02/p42d-text2-20261006T113349.342620Z/workflow/ferryman2/candidate.json)

Historical references: P4.2d installed release 0.1.0-e8a72b2d6eb3aad9 on main 40fb91fa. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
