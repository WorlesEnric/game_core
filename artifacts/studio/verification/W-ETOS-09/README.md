# W-ETOS-09: Domain reload during a task: tray shows the same task afterwards

Verdict: **BLOCKED**. Client cursor replay is real; a source recompile/domain reload while an in-flight task returns to the tray was not completed. Simulated reload tests remain component evidence.

Report timestamp: 2026-10-06T12:40:21.169033+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh reconnect
```

## Retained evidence

- [W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z/README.md](../W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z/README.md)
- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)
