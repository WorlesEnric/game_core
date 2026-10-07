# W-ETOS-06: Kill companion mid-task; restart; same task id resumes; one outcome; kill etosd mid-task: delayed completion attributed correctly

Verdict: **BLOCKED**. Companion-only portion PASS: actual permitted agent restart preserves the original task and cursor replay with one cancellation outcome. Owner rule: never stop/restart etosd; node-death/reconnect portion is forbidden and not run, so the full row remains BLOCKED.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
Not run: Owner rule: never stop/restart etosd; node-death/reconnect part is not run. Companion-only restart cannot close this row.
```

## Current-run evidence

- [W-ETOS-06/p42j-owner-rule/result.json](../W-ETOS-06/p42j-owner-rule/result.json)
- [W-ETOS-06/p42j-companion-restart-20261007T134234.435304Z/result.json](../W-ETOS-06/p42j-companion-restart-20261007T134234.435304Z/result.json)
