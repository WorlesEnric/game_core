# W-VIEW-02: Dialogue: Graph tools, conditions, preview, journaled edits/undo and real-Play bridge

Verdict: **FAIL**. R9-A final combined suite records 161 PASS/1 FAIL, no skips: the exact named P2_3 workflow is refused at empty graph creation with GP-DLG-001. It also commits an orphan before a separate Connect. Its test path is outside R9-A ownership; the valid combined owned replacement passes but does not substitute for that named criterion. [Current XML](r9-a-final-regressions/results.xml).

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
P4.2i's evidence below remains historical. R9-A preserves the current failure and the exact required owner correction rather than weakening graph validity.

## Reproduce

```sh
studio/tools/verify-all.sh final-views-tests; studio/tools/verify-all.sh views
```

## Historical P4.2i evidence

- [UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/result.json](../UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/result.json)
- [UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/results.xml](../UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/results.xml)
- [W-VIEW-01/captures-20261007T045200Z/capture.json](../W-VIEW-01/captures-20261007T045200Z/capture.json)

## R9-A final-state workflow

Owned combined AddLine+Connect+Rename workflow and three retained true-refusal cases pass: [4/4 XML](r9-a-regressions-20261007T101610.953797Z/regressions.xml). The exact named P2_3 fixture remains outside R9-A's exclusive paths and commits invalid intermediate change sets. This row remains FAIL; replacement-test evidence does not silently satisfy the named acceptance criterion. Exact owner request: [R9-A packet](../../../../docs/studio/packets/R9-A.md#requests-to-other-packets).
