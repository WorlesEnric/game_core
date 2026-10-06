# W-AI-05: Change the lantern quest to require two oil flasks; verify consequences in Play

Verdict: **BLOCKED**. PARTIAL: the live scoped quest.addObjective candidate applies a Collect requirement of 2 against the real OilFlask definition, exercising R3-F/R4-C validation. The legacy simulator uses oil_flask and refuses GP-QST-004; no in-Play consequence is established.

Report timestamp: 2026-10-06T09:08:50.175633+00:00 UTC.

Acceptance baseline: merged main `f787829289ea7402c08917a78553ff6c3838bda8`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42c narrative
```

## Retained evidence

- [W-AI-03/p42c-narrative-20261006T073303.291662Z/workflow/quest/candidate.json](../W-AI-03/p42c-narrative-20261006T073303.291662Z/workflow/quest/candidate.json)
- [W-AI-03/p42c-narrative-20261006T073303.291662Z/workflow/quest/apply-report.json](../W-AI-03/p42c-narrative-20261006T073303.291662Z/workflow/quest/apply-report.json)
- [W-AI-03/p42c-narrative-20261006T073303.291662Z/workflow/quest/quest-simulate-applied.json](../W-AI-03/p42c-narrative-20261006T073303.291662Z/workflow/quest/quest-simulate-applied.json)

Historical references: Earlier P4.2/P4.2b attempts remain retained; this disposition uses the installed P4.2c release and final-main product source.
