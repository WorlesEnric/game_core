# W-EDIT-03: Generate portrait → apply → undo → redo: no second generation (usage unchanged)

Verdict: **PASS**. P4.2h one real USD0.20 portrait passes current media import, normal History undo/redo and final undo. Generated/applied/redone PNG SHA256 is identical; all post-generation charge/producer checkpoints remain unchanged. An observer initially wrongly required optional jobId; its failure is retained and corrected without regenerating. Normal History continuation reuses the original Applied journal after reopening.

Product baseline: `a77cb38ba4a2265007fa40c38983e01a17bb0914`. Historical receipts retain their original revision.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42h.py portrait --row W-EDIT-03 --method P42h.Media.Driver.RunPortrait --workflow-out artifacts/studio/verification/W-EDIT-03/p42h-portrait/workflow
```

## Retained evidence

- [W-ETOS-07/installed-media-20261005T192218.561813Z/README.md](../W-ETOS-07/installed-media-20261005T192218.561813Z/README.md)
- [W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z/README.md](../W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z/README.md)
- [W-EDIT-03/p42h-portrait/workflow/result.json](../W-EDIT-03/p42h-portrait/workflow/result.json)
- [W-EDIT-03/p42h-portrait/workflow/visual-review.json](../W-EDIT-03/p42h-portrait/workflow/visual-review.json)
- [W-EDIT-03/p42h-portrait/workflow/ledger-final.json](../W-EDIT-03/p42h-portrait/workflow/ledger-final.json)
- [W-EDIT-03/p42h-portrait/workflow/portrait-redone.json](../W-EDIT-03/p42h-portrait/workflow/portrait-redone.json)
- [W-EDIT-03/p42h-portrait/workflow/result-initial-observer-refusal.json](../W-EDIT-03/p42h-portrait/workflow/result-initial-observer-refusal.json)
