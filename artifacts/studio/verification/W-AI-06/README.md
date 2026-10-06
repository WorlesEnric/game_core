# W-AI-06: Undo/redo the above, close and reopen the project, verify consistency

Verdict: **FAIL**. All three live entries survive close/reopen with matching saved hashes; undo/redo/final undo succeeds. The separate retained two-operation Odd witness also passes in a different Editor with Play/Edit domain reload (1/1 XML), closing R5-A’s final-postimage conflict. Full byte consistency still fails: backToBefore=false, with only contentStamp differences in Odd and DrownedBell among the four compared assets. Driver exit 0 is not treated as a full-row pass.

Report timestamp: 2026-10-06T12:40:21.174475+00:00 UTC.

Acceptance baseline: merged main `40fb91fa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42d reopen
studio/tools/verify-all.sh p42d history-prepare
studio/tools/verify-all.sh p42d history-reopen
```

## Retained evidence

- [W-AI-06/p42d-reopen-20261006T114958.044810Z/workflow/reopen/after-reopen.json](../W-AI-06/p42d-reopen-20261006T114958.044810Z/workflow/reopen/after-reopen.json)
- [W-AI-06/p42d-reopen-20261006T114958.044810Z/workflow/reopen/final.json](../W-AI-06/p42d-reopen-20261006T114958.044810Z/workflow/reopen/final.json)
- [W-AI-06/p42d-history-reopen-20261006T115409.820866Z/results.xml](../W-AI-06/p42d-history-reopen-20261006T115409.820866Z/results.xml)
- [W-AI-06/p42d-reopen-20261006T114958.044810Z/workflow/odd-line/undo-result.json](../W-AI-06/p42d-reopen-20261006T114958.044810Z/workflow/odd-line/undo-result.json)

Exact acceptance/component cases: `Hollowmere.R5_A.HistoryReopenTests.R5_02_OddWitness_UndoesAfterEditorReopenAndDomainReload`. Component cases do not close any missing external workflow.

Historical references: P4.2d installed release 0.1.0-e8a72b2d6eb3aad9 on main 40fb91fa. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
