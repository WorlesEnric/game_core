# W-AI-02: Point at a location; "add a ferryman NPC here who talks about the bell": NPC spawned with dialogue, patrol, nav

Verdict: **BLOCKED**. PARTIAL: the installed R4-C validator accepts the scoped move/patrol and ferryman candidates; both apply. The P3.2 driver has no in-Play NPC/nav assertion and tries to select Odd in Village. Ferryman Undo reports Undone but leaves the created NPC (roster 20→21→21); owner request retained.

Report timestamp: 2026-10-06T09:08:50.172955+00:00 UTC.

Acceptance baseline: merged main `f787829289ea7402c08917a78553ff6c3838bda8`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42c text2
```

## Retained evidence

- [W-AI-02/p42c-text2-20261006T071746.894107Z/README.md](../W-AI-02/p42c-text2-20261006T071746.894107Z/README.md)
- [W-AI-02/p42c-text2-20261006T071746.894107Z/workflow/ferryman2/roster-undone.json](../W-AI-02/p42c-text2-20261006T071746.894107Z/workflow/ferryman2/roster-undone.json)
- [W-AI-02/p42c-text2-20261006T071746.894107Z/workflow/ferryman2/undo-result.json](../W-AI-02/p42c-text2-20261006T071746.894107Z/workflow/ferryman2/undo-result.json)

Historical references: Earlier P4.2/P4.2b attempts remain retained; this disposition uses the installed P4.2c release and final-main product source.
