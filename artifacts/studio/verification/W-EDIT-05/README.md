# W-EDIT-05: Gizmo drag and typed value produce identical History entries

Verdict: **PASS**. Core and viewport gizmo tests compare the resulting journal entries with typed moves.

Report timestamp: 2026-10-06T12:40:21.162706+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)

Exact acceptance/component cases: `WEdit05_GizmoDragAndTypedMoveProduceIdenticalJournalEntries`, `WEdit05_ViewportGizmoDragEqualsTypedMove`. Component cases do not close any missing external workflow.
