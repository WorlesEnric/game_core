# W-REC-01: Kill editor mid-apply; reopen: journal `Interrupted`, resume/rollback

Verdict: **PASS**. Both current real SIGKILL paths reopen in distinct Editor PIDs: uncertain in-operation rollback and completed-operation resume recover correctly. Intentional killed-Editor nonzero attempts are preserved, not product failures.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh recovery
```

## Current-run evidence

- [W-REC-01/resume-state-20261007T050804.537383Z/recovery-result.json](../W-REC-01/resume-state-20261007T050804.537383Z/recovery-result.json)
- [W-REC-01/rollback-state-20261007T050623.898305Z/recovery-result.json](../W-REC-01/rollback-state-20261007T050623.898305Z/recovery-result.json)
- [W-REC-01/resume-state-20261007T050804.537383Z/result.json](../W-REC-01/resume-state-20261007T050804.537383Z/result.json)
- [W-REC-01/rollback-state-20261007T050623.898305Z/result.json](../W-REC-01/rollback-state-20261007T050623.898305Z/result.json)
