# W-ETOS-06: Kill companion mid-task; restart; same task id resumes; one outcome; kill etosd mid-task: delayed completion attributed correctly

Verdict: **BLOCKED**. Socket reconnect/cursor replay passes; killing/restarting etosd is expressly forbidden, so the complete companion-death/node-death attribution scenario cannot run.

Report timestamp: 2026-10-05T21:03:25.252494+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh reconnect
```

## Retained evidence

- [W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z/README.md](../W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z/README.md)
