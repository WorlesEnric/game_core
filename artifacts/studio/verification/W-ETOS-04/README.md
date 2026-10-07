# W-ETOS-04: Worker `etos query` returns the selected NPC's dialogue nodes

Verdict: **BLOCKED**. P4.2h selected Bram worker task actually executes etos query; exact node tool-call trace and authenticated stdout are retained. Selected-graph and unfiltered gc_dialogue_node queries return zero rows. Production Editor has no PostIndexDeltaAsync caller, and companion ingestion does not expand dialogue.graph nodes. Requires real lifecycle-bound node publication and discoverable owner-scoped graph identity; no harness-seeded RG substitute.

Product baseline: `a77cb38ba4a2265007fa40c38983e01a17bb0914`. Historical receipts retain their original revision.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42h.py query --row W-ETOS-04 --method P42h.Tasks.TaskDriver.SelectedNpcQuery
```

## Retained evidence

- [W-UI-01/ui-capture-20261005T192323.970290Z/README.md](../W-UI-01/ui-capture-20261005T192323.970290Z/README.md)
- [W-ETOS-04/p42h-query-20261006T233851.413333Z/workflow/result.json](../W-ETOS-04/p42h-query-20261006T233851.413333Z/workflow/result.json)
- [W-ETOS-04/p42h-query-20261006T233851.413333Z/workflow/worker-query-receipt.json](../W-ETOS-04/p42h-query-20261006T233851.413333Z/workflow/worker-query-receipt.json)
- [W-ETOS-04/p42h-query-20261006T233851.413333Z/workflow/worker-tool-trace.json](../W-ETOS-04/p42h-query-20261006T233851.413333Z/workflow/worker-tool-trace.json)
