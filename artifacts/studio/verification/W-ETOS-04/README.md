# W-ETOS-04: Worker `etos query` returns the selected NPC's dialogue nodes

Verdict: **PASS** on product `fccf3f8c44e08bd3c6925ef99a8fcd15991d8703`, Linux build host, 2026-10-07.

## Observed workflow

Batch Editor selects the real `Assets/Hollowmere/Npcs/Definitions/Bram.asset`. The production ETOS session publishes the real project index; no harness-seeded rows or replacement publisher. A task on the scratch node's actual Docker worker executes `etos query`, discovers the owner-scoped graph from `gc_definition`, and returns **8 Bram nodes** and **53 current dialogue nodes** with the graph filter removed. Every selected node index and text matches the actual graph asset. The worker obtains owner/query guidance from the production `request.md` and graph reference from its actual selection/index inputs.

The scratch node and companion were freshly installed for this run. A deterministic loopback tool selector instructs the real worker to run the query script; it supplies no query responses or graph data. There were **zero external provider calls**. The installed node and companion were untouched.

## Evidence

- [Worker query receipt](r8-a/worker-query-receipt.json.gz): exact commands, authenticated stdout, raw JSONL, discovered graph, selected and unfiltered rows.
- [Real node worker tool trace](r8-a/worker-tool-trace.json.gz).
- [Actual selection and graph nodes](r8-a/selection.json.gz).
- [Editor result](r8-a/editor-result.json.gz), [complete scratch result](r8-a/result.json.gz).
- [Baseline regression failures](r8-a/baseline-index-tests.log): unchanged new tests against baseline `7f5cacb8`, **0 passed / 3 failed**. Integrated tests pass.
- [EditMode result XML](r8-a/editmode-results.xml.gz): **199 passed / 0 failed / 0 skipped**, including lifecycle open/apply/undo/redo/rebuild/removal/disposal coverage.
- Full redacted logs and checksums: [R8_A scratch-02 manifest](../../../../games/hollowmere/Assets/Hollowmere/Tests/R8_A/Evidence~/scratch-02/manifest.json).

The first scratch attempt returned the same real 8/53 rows but exposed a harness parser defect: it counted the JSONL provenance header as a row. The fresh passing run uses the CLI's structured `rows` array, validates completeness and row count, and preserves raw JSONL separately. No assertion or product rule was relaxed.

## Reproduce

See [R8-A packet](../../../../docs/studio/packets/R8-A-publication-cancellation.md) for build and scratch runner commands. All Editors run sequentially through `studio/tools/unity-batch.sh`; no graphical qualification is claimed.

Historical P4.2h zero-row receipts remain in this directory under their original revision and names.
