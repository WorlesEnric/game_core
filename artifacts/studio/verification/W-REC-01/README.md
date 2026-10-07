# W-REC-01: Kill editor mid-apply; reopen: journal `Interrupted`, resume/rollback

Verdict: **PASS**. Current-run Kill editor mid-apply; reopen: journal `Interrupted`, resume/rollback is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh recovery
```

## Current-run evidence

- [W-REC-01/resume-state-20261007T185206.701320Z/recovery-result.json](../W-REC-01/resume-state-20261007T185206.701320Z/recovery-result.json)
- [W-REC-01/rollback-state-20261007T185026.180708Z/recovery-result.json](../W-REC-01/rollback-state-20261007T185026.180708Z/recovery-result.json)
