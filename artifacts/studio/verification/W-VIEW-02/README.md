# W-VIEW-02: Dialogue: Graph tools, conditions, preview, journaled edits/undo and real-Play bridge

Verdict: **FAIL**. Current full-row dialogue edit test rejects AddLine/Connect/Rename with GP-DLG-005: ViewsTestGraph node 1 is unreachable. Reproduced in broad EditMode and current 40-pass/1-fail view suite; graphical preview does not replace failed editing/undo.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh final-views-tests; studio/tools/verify-all.sh views
```

## Current-run evidence

- [UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/result.json](../UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/result.json)
- [UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/results.xml](../UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/results.xml)
- [W-VIEW-01/captures-20261007T045200Z/capture.json](../W-VIEW-01/captures-20261007T045200Z/capture.json)
