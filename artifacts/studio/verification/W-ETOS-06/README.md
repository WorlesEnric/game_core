# W-ETOS-06: Kill companion mid-task; restart; same task id resumes; one outcome; kill etosd mid-task: delayed completion attributed correctly

Verdict: **BLOCKED**. Companion-only portion PASS: permitted restart preserves the original task and cursor replay with one cancellation outcome. Owner rule: never stop/restart etosd; node-death/reconnect portion is forbidden and not run.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
Not run: Owner rule: never stop/restart etosd; node-death/reconnect part is not run. Companion-only restart cannot close this row.
```

## Current-run evidence

- [W-ETOS-06/p42l-owner-rule/result.json](../W-ETOS-06/p42l-owner-rule/result.json)
- [W-ETOS-06/p42l-companion-restart-20261008T002740.659906Z/result.json](../W-ETOS-06/p42l-companion-restart-20261008T002740.659906Z/result.json)
