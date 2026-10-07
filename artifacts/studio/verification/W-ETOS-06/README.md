# W-ETOS-06: Kill companion mid-task; restart; same task id resumes; one outcome; kill etosd mid-task: delayed completion attributed correctly

Verdict: **BLOCKED**. Companion-only portion PASS: actual etos agent restart preserves original task and exact cursor replay, with one cancellation outcome. Owner rule: never stop/restart etosd; node-death/reconnect portion is forbidden and not run, so the complete row remains BLOCKED.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
Not run: Owner rule: never stop/restart etosd; node-death/reconnect part is not run. Companion-only restart cannot close this row.
```

## Current-run evidence

- [W-ETOS-06/p42i-owner-block/result.json](../W-ETOS-06/p42i-owner-block/result.json)
- [W-ETOS-06/p42i-companion-restart-20261007T062720.972531Z/result.json](../W-ETOS-06/p42i-companion-restart-20261007T062720.972531Z/result.json)
- [W-ETOS-06/p42i-companion-restart-20261007T062720.972531Z/live/dotnet-g-h-events-resume-cancel.json](../W-ETOS-06/p42i-companion-restart-20261007T062720.972531Z/live/dotnet-g-h-events-resume-cancel.json)
- [W-ETOS-06/p42i-companion-restart-20261007T062720.972531Z/live/dotnet-exchanges-R2_38_W_ETOS_06_CompanionRestartResumesCursorAndKeepsTask.json](../W-ETOS-06/p42i-companion-restart-20261007T062720.972531Z/live/dotnet-exchanges-R2_38_W_ETOS_06_CompanionRestartResumesCursorAndKeepsTask.json)
- [W-ETOS-06/p42i-companion-restart-20261007T062720.972531Z/live/dotnet-companion-restart.json](../W-ETOS-06/p42i-companion-restart-20261007T062720.972531Z/live/dotnet-companion-restart.json)
