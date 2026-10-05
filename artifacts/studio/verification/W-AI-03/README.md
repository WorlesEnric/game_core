# W-AI-03: "Add a line Odd only says after the shrine is lit": conditional dialogue works in Play

Verdict: **BLOCKED**. R3-F fact handoff regressions pass, but no current real-worker conditional Odd line was produced and played; the bounded live task failed earlier on target scope.

Report timestamp: 2026-10-05T21:03:25.255314+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)
- [W-UI-01/ui-capture-20261005T192323.970290Z/README.md](../W-UI-01/ui-capture-20261005T192323.970290Z/README.md)

Exact acceptance/component cases: `D10a_EarlierCandidateFactStagesAppliesAndReplaysThroughEngine`. Component cases do not close any missing external workflow.
