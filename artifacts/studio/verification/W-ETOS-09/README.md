# W-ETOS-09: Domain reload during a task: tray shows the same task afterwards

Verdict: **PASS**. P4.2h real changed-source compilation/domain reload in the same Editor preserves running task tec77a34b9f6f55d58ed934f5 and one attached tray row with unchanged creation identity. Loaded source token and compilation/reload callbacks are retained; session starts 11-to-12. Durable cursor 760 continues with exact production/independent replay [761,762], no gap/duplicate and one terminal event; creator tray cancellation then bounds task lifetime.

Product baseline: `a77cb38ba4a2265007fa40c38983e01a17bb0914`. Historical receipts retain their original revision.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42h.py reload --row W-ETOS-09 --method P42h.Tasks.TaskDriver.SourceReload
```

## Retained evidence

- [W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z/README.md](../W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z/README.md)
- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)
- [W-ETOS-09/p42h-reload-20261006T234520.220306Z/workflow/result.json](../W-ETOS-09/p42h-reload-20261006T234520.220306Z/workflow/result.json)
- [W-ETOS-09/p42h-reload-20261006T234520.220306Z/workflow/cursor-replay.json](../W-ETOS-09/p42h-reload-20261006T234520.220306Z/workflow/cursor-replay.json)
- [W-ETOS-09/p42h-reload-20261006T234520.220306Z/workflow/source-compilation-request.json](../W-ETOS-09/p42h-reload-20261006T234520.220306Z/workflow/source-compilation-request.json)
- [W-ETOS-09/p42h-reload-20261006T234520.220306Z/workflow/reload-same-running-row.png](../W-ETOS-09/p42h-reload-20261006T234520.220306Z/workflow/reload-same-running-row.png)
