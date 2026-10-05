# W-MECH-01: Pressure-plate mechanism: staged, admitted, world resumed from checkpoint

Verdict: **FAIL**. Installed paired UI Stage returns 404 no owned resource for the app-origin pressure-plate and semantic-negative samples; no signed verdict, coldCache, cold/warm stage duration, Admit or undo exists. No host fallback.

Report timestamp: 2026-10-05T21:03:25.262881+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh stage
```

## Retained evidence

- [W-MECH-01/pressure-plate-explicit-paired-ui-20261005T193418.507302Z/README.md](../W-MECH-01/pressure-plate-explicit-paired-ui-20261005T193418.507302Z/README.md)
- [W-MECH-01/negative-semantic-explicit-paired-ui-20261005T193633.001803Z/README.md](../W-MECH-01/negative-semantic-explicit-paired-ui-20261005T193633.001803Z/README.md)
- [W-MECH-01/semantic-analyzer-unit-20261005T192618.998527Z/README.md](../W-MECH-01/semantic-analyzer-unit-20261005T192618.998527Z/README.md)

Exact acceptance/component cases: `R2_13_R3_D23_AppOriginCandidateHasSignedClientIntake`. Component cases do not close any missing external workflow.
