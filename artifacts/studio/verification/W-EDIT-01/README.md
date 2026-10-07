# W-EDIT-01: Change set in History shows etos task id and GameCore operation ids

Verdict: **PASS**. P4.2h fresh installed-worker Instance-scoped runtime.move candidate applies unchanged in real Play. History joins task t9450022d9a74ef2db96f895e with the exact committed GameCore operation; production receipt and pose confirm Bram moved to (11.5,0,-13), with authored scene bytes unchanged.

Product baseline: `a77cb38ba4a2265007fa40c38983e01a17bb0914`. Historical receipts retain their original revision.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42h.py move --row W-EDIT-01 --method P42h.Tasks.TaskDriver.JoinedMove
```

## Retained evidence

- [W-UI-01/ui-capture-20261005T192323.970290Z/README.md](../W-UI-01/ui-capture-20261005T192323.970290Z/README.md)
- [W-EDIT-01/p42h-move-20261006T233010.771874Z/workflow/result.json](../W-EDIT-01/p42h-move-20261006T233010.771874Z/workflow/result.json)
- [W-EDIT-01/p42h-move-20261006T233010.771874Z/workflow/joined-history.json](../W-EDIT-01/p42h-move-20261006T233010.771874Z/workflow/joined-history.json)
- [W-EDIT-01/p42h-move-20261006T233010.771874Z/workflow/joined-task-gamecore-history.png](../W-EDIT-01/p42h-move-20261006T233010.771874Z/workflow/joined-task-gamecore-history.png)
