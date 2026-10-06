# W-EDIT-06: Runtime-only move → "Apply to authored" → persists after exiting Play

Verdict: **BLOCKED**. No durable Apply-to-authored workflow for runtime-only moves is exposed by the current engine/UI seam; runtime-only refusal tests are not this persistence workflow.

Report timestamp: 2026-10-06T04:56:25.163981+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)

Exact acceptance/component cases: `R2_06_RuntimeActionsDoNotWriteAuthoredDataAndCannotUndo`. Component cases do not close any missing external workflow.
