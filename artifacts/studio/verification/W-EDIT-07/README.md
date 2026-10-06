# W-EDIT-07: Concurrent manual rename vs agent candidate: per-op `Conflict`, rebase works

Verdict: **PASS**. Per-operation conflict/rebase tests pass; the retained real worker candidate also refuses with StaleContext after an actual registry revision change.

Report timestamp: 2026-10-06T12:40:21.163256+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)
- [W-EDIT-07/live-catalog-candidate-mode-20261005T194931.456257Z/README.md](../W-EDIT-07/live-catalog-candidate-mode-20261005T194931.456257Z/README.md)
- [W-EDIT-07/live-catalog-candidate-mode-20261005T194931.456257Z/stale-context.json](../W-EDIT-07/live-catalog-candidate-mode-20261005T194931.456257Z/stale-context.json)

Exact acceptance/component cases: `Conflict_RebaseReplansAgainstTheCurrentStamp`, `R2_05_R2_38_LiveCandidateCatalogChangeRefusesStaleContext`. Component cases do not close any missing external workflow.
