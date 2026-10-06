# W-PLUG-12: Bake byte-identity; one line change → one revision change

Verdict: **PASS**. Both final-tree rebakes/Entry.Verify pass without output changes; byte identity and single-definition revision tests pass. Structural recipe revisions did not change, preserving existing save compatibility.

Report timestamp: 2026-10-06T12:40:21.180253+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh bake
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)
- [W-PLUG-12/bake-hollowmere-20261005T185517.764081Z/README.md](../W-PLUG-12/bake-hollowmere-20261005T185517.764081Z/README.md)
- [W-PLUG-12/bake-cleanproof-20261005T185554.359006Z/README.md](../W-PLUG-12/bake-cleanproof-20261005T185554.359006Z/README.md)

Exact acceptance/component cases: `BakeTwice_IsByteIdentical_AndVerifyPasses`, `OneFieldChange_ChangesOnlyThatDefinitionsRevision`. Component cases do not close any missing external workflow.
