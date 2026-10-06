# W-REC-01: Kill editor mid-apply; reopen: journal `Interrupted`, resume/rollback

Verdict: **PASS**. Real SIGKILL during engine mutation, then a different Editor process: both rollback and resume recover successfully. Killed-Editor nonzero exits are expected and retained.

Report timestamp: 2026-10-06T15:29:44.422301+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh recovery
```

## Retained evidence

- [W-REC-01/rollback-state-20261005T202208.950937Z/README.md](../W-REC-01/rollback-state-20261005T202208.950937Z/README.md)
- [W-REC-01/resume-state-20261005T202307.744000Z/README.md](../W-REC-01/resume-state-20261005T202307.744000Z/README.md)

Exact acceptance/component cases: `R2-03-R2-38-rollback`, `R2-03-R2-38-resume`. Component cases do not close any missing external workflow.
