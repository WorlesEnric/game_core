# W-ETOS-05: Cancel a running task from the tray: `cancelled`, no candidate, no duplicate task

Verdict: **PASS**. P4.2h actual enabled attached tray Cancel button is activated while request, task and tray report running. One original task becomes cancelled, no candidate or duplicate row appears; acknowledgement 427 ms, stable observation 3585 ms.

Product baseline: `a77cb38ba4a2265007fa40c38983e01a17bb0914`. Historical receipts retain their original revision.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42h.py cancel --row W-ETOS-05 --method P42h.Tasks.TaskDriver.TrayCancel
```

## Retained evidence

- [W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z/README.md](../W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z/README.md)
- [W-ETOS-05/p42h-cancel-20261006T234228.357309Z/workflow/result.json](../W-ETOS-05/p42h-cancel-20261006T234228.357309Z/workflow/result.json)
- [W-ETOS-05/p42h-cancel-20261006T234228.357309Z/workflow/cancelled.json](../W-ETOS-05/p42h-cancel-20261006T234228.357309Z/workflow/cancelled.json)
- [W-ETOS-05/p42h-cancel-20261006T234228.357309Z/workflow/running-before-cancel.png](../W-ETOS-05/p42h-cancel-20261006T234228.357309Z/workflow/running-before-cancel.png)
- [W-ETOS-05/p42h-cancel-20261006T234228.357309Z/workflow/cancelled-tray.png](../W-ETOS-05/p42h-cancel-20261006T234228.357309Z/workflow/cancelled-tray.png)
