# W-E2E-01: All rows resolved on one revision; completion report

Verdict: **BLOCKED**. P4.2c ran the installed final-tree companion and disposed the requested rows, but live admission, history and destructive voice have retained failures; several workflow assertions and node-death scenarios remain unqualified. Inherited rows keep their original revision-specific evidence; this is not all-row product acceptance.

Report timestamp: 2026-10-06T09:08:50.192691+00:00 UTC.

Acceptance baseline: merged main `f787829289ea7402c08917a78553ff6c3838bda8`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh all
# Display/node jobs are separate documented subcommands.
```

## Retained evidence

- [STATIC/final-evidence-regressions-20261005T205458.709656Z/README.md](../STATIC/final-evidence-regressions-20261005T205458.709656Z/README.md)
