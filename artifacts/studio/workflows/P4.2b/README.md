# P4.2b workflow re-acceptance

Final-main baseline `1752ca8a`; R4-A/B/C and P3.1c merged. The live-run guard remains
occupied by P3.1d. The required immutable release cannot be activated, so **no live
workflow was started** and no old P3.2 result is relabelled as a current pass.

[Per-workflow dispositions](workflows.json), [paid-operation ledger](paid-operations.json),
[packet](../../../../docs/studio/packets/P4.2b-live-acceptance.md),
[matrix receipts](../../verification/SUMMARY.md).

The `pass` / `partial` / `fail` / `blocked` vocabulary follows P3.2. These are prerequisite
`blocked` outcomes, with empty task lists and zero submitted operations. Intended R3/R4
coverage is recorded per row. Cold/warm stage, verified verdict, creator Admit, running
tri-state smoke, undo and negative-semantic UI refusal remain unmeasured.

The driver source is the existing `Hollowmere.P3_2.Workflows.WorkflowRunner`, selected by
`studio/tools/workflow-p3.2-*.sh`; no executable scripts exist inside the P3.2 artifact
folder itself. The retained destructive WAV is reused by the P4.2 virtual-source lane;
its fixture was not synthesized again. No microphone request was made while guarded.
