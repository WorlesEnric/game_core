# W-EDIT-06: Runtime-only move → "Apply to authored" → persists after exiting Play

Verdict: **PASS**. Current-run Runtime-only move → "Apply to authored" → persists after exiting Play is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42l.py lifecycle
```

## Current-run evidence

- [W-EDIT-06/p42l-runtime-promotion-20261007T232248.267192Z/workflow/promotion.json](../W-EDIT-06/p42l-runtime-promotion-20261007T232248.267192Z/workflow/promotion.json)
