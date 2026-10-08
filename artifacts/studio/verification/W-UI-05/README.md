# W-UI-05: Selection/hover and marquee timings

Verdict: **PASS**. Two current 500-candidate datasets contain 100 picks and 100 marquees: pick p95 1.3656/0.8867ms, marquee p95 0.4024/0.2487ms. Unchanged limits are 16/50ms.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42l.py timing
```

## Current-run evidence

- [W-EDIT-08/p42l-timing-1-20261007T231848.106840Z/selection.json](../W-EDIT-08/p42l-timing-1-20261007T231848.106840Z/selection.json)
- [W-EDIT-08/p42l-timing-2-20261007T231935.569361Z/selection.json](../W-EDIT-08/p42l-timing-2-20261007T231935.569361Z/selection.json)
