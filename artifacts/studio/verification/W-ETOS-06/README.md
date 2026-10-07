# W-ETOS-06: Kill companion mid-task; restart; same task id resumes; one outcome; kill etosd mid-task: delayed completion attributed correctly

Verdict: **BLOCKED**. Companion-only portion PASS: permitted restart preserves the original task and cursor replay with one cancellation outcome. Owner rule: never stop/restart etosd; node-death/reconnect portion is forbidden and not run.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
Not run: Owner rule: never stop/restart etosd; node-death/reconnect part is not run. Companion-only restart cannot close this row.
```

## Current-run evidence

- [W-ETOS-06/p42k-owner-rule/result.json](../W-ETOS-06/p42k-owner-rule/result.json)
- [W-ETOS-06/p42k-companion-restart-20261007T195622.701526Z/result.json](../W-ETOS-06/p42k-companion-restart-20261007T195622.701526Z/result.json)
