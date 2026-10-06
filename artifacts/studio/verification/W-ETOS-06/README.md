# W-ETOS-06: Kill companion mid-task; restart; same task id resumes; one outcome; kill etosd mid-task: delayed completion attributed correctly

Verdict: **BLOCKED**. Companion portion PASS: actual etos agent restart, same task, one cancellation outcome and exact four-event cursor replay; reconnect 143.1 ms, cancel ack 188.0 ms. Node-death portion remains BLOCKED: etosd stop/restart is forbidden, and a companion restart cannot prove delayed attribution after node death.

Report timestamp: 2026-10-06T15:29:44.391002+00:00 UTC.

Acceptance baseline: merged main `f787829289ea7402c08917a78553ff6c3838bda8`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42c restart
```

## Retained evidence

- [W-ETOS-06/p42c-companion-restart-20261006T082527.197356Z/result.json](../W-ETOS-06/p42c-companion-restart-20261006T082527.197356Z/result.json)
- [W-ETOS-06/p42c-companion-restart-20261006T082527.197356Z/live/dotnet-companion-restart.json](../W-ETOS-06/p42c-companion-restart-20261006T082527.197356Z/live/dotnet-companion-restart.json)
- [W-ETOS-06/p42c-companion-restart-20261006T082527.197356Z/live/dotnet-g-h-events-resume-cancel.json](../W-ETOS-06/p42c-companion-restart-20261006T082527.197356Z/live/dotnet-g-h-events-resume-cancel.json)

Exact acceptance/component cases: `GameCore.Studio.Etos.Client.Tests.LiveTests.R2_38_W_ETOS_06_CompanionRestartResumesCursorAndKeepsTask`. Component cases do not close any missing external workflow.

Historical references: Earlier P4.2/P4.2b attempts remain retained; this disposition uses the installed P4.2c release and final-main product source.
