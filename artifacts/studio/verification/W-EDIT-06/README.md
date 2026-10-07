# W-EDIT-06: Runtime-only move → "Apply to authored" → persists after exiting Play

Verdict: **PASS**. Current-run Runtime-only move → "Apply to authored" → persists after exiting Play is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42k.py lifecycle
```

## Current-run evidence

- [W-EDIT-06/p42k-runtime-promotion-20261007T184240.138879Z/workflow/promotion.json](../W-EDIT-06/p42k-runtime-promotion-20261007T184240.138879Z/workflow/promotion.json)
