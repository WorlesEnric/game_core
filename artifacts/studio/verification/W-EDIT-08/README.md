# W-EDIT-08: Apply timings (single and region-wide)

Verdict: **PASS**. Two current 20-sample datasets pass: single-target p95 20.7325/23.758ms; Marsh-all-NPC p95 30.0882/36.0957ms; kernel prepare p95 6.6654/10.5762ms. Limits remain 200/1000/300ms.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42l.py timing
```

## Current-run evidence

- [W-EDIT-08/p42l-timing-1-20261007T231848.106840Z/apply.json](../W-EDIT-08/p42l-timing-1-20261007T231848.106840Z/apply.json)
- [W-EDIT-08/p42l-timing-1-20261007T231848.106840Z/region-apply.json](../W-EDIT-08/p42l-timing-1-20261007T231848.106840Z/region-apply.json)
- [W-EDIT-08/p42l-timing-1-20261007T231848.106840Z/compose.json](../W-EDIT-08/p42l-timing-1-20261007T231848.106840Z/compose.json)
- [W-EDIT-08/p42l-timing-2-20261007T231935.569361Z/apply.json](../W-EDIT-08/p42l-timing-2-20261007T231935.569361Z/apply.json)
- [W-EDIT-08/p42l-timing-2-20261007T231935.569361Z/region-apply.json](../W-EDIT-08/p42l-timing-2-20261007T231935.569361Z/region-apply.json)
- [W-EDIT-08/p42l-timing-2-20261007T231935.569361Z/compose.json](../W-EDIT-08/p42l-timing-2-20261007T231935.569361Z/compose.json)
