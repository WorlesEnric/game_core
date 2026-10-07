# W-REC-01: Kill editor mid-apply; reopen: journal `Interrupted`, resume/rollback

Verdict: **PASS**. Current-run Kill editor mid-apply; reopen: journal `Interrupted`, resume/rollback is verified by the linked named XML cases and/or exact JSON assertions; no prior-run result is used.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh recovery
```

## Current-run evidence

- [W-REC-01/resume-state-20261007T123105.394286Z/recovery-result.json](../W-REC-01/resume-state-20261007T123105.394286Z/recovery-result.json)
- [W-REC-01/rollback-state-20261007T122851.849146Z/recovery-result.json](../W-REC-01/rollback-state-20261007T122851.849146Z/recovery-result.json)
