# W-AI-06: Undo/redo the above, close and reopen the project, verify consistency

Verdict: **FAIL**. Saved asset hashes and all three Applied journals survive a real Editor close/reopen. HUD and quest undo/redo/final undo pass; Odd undo refuses Conflict against the first operation’s intermediate after-stamp although the second operation’s final stamp matches. Final backToBefore=false; no forced undo or candidate edit.

Report timestamp: 2026-10-06T09:08:50.176943+00:00 UTC.

Acceptance baseline: merged main `f787829289ea7402c08917a78553ff6c3838bda8`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42c narrative
studio/tools/verify-all.sh p42c reopen
```

## Retained evidence

- [W-AI-06/p42c-reopen-20261006T073813.299352Z/README.md](../W-AI-06/p42c-reopen-20261006T073813.299352Z/README.md)
- [W-AI-06/p42c-reopen-20261006T073813.299352Z/workflow/reopen/after-reopen.json](../W-AI-06/p42c-reopen-20261006T073813.299352Z/workflow/reopen/after-reopen.json)
- [W-AI-06/p42c-reopen-20261006T073813.299352Z/workflow/odd-line/undo-result.json](../W-AI-06/p42c-reopen-20261006T073813.299352Z/workflow/odd-line/undo-result.json)
- [W-AI-06/p42c-reopen-20261006T073813.299352Z/workflow/reopen/final.json](../W-AI-06/p42c-reopen-20261006T073813.299352Z/workflow/reopen/final.json)

Historical references: Earlier P4.2/P4.2b attempts remain retained; this disposition uses the installed P4.2c release and final-main product source.
