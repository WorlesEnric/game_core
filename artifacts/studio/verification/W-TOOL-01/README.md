# W-TOOL-01: Tool catalog of the clean project lists only installed plugins' tools

Verdict: **PASS**. Current clean-project catalog contains only installed production tools, excluding fixture/Hollowmere/internal admission tools; exact catalog and manifest retained.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Current-run evidence

- [UNITY-CLEANPROOF/clean-recheck-editmode-20261007T044422.332841Z/results.xml](../UNITY-CLEANPROOF/clean-recheck-editmode-20261007T044422.332841Z/results.xml)
- [W-TOOL-01/20261007T0445027928280Z/tool-catalog.json](../W-TOOL-01/20261007T0445027928280Z/tool-catalog.json)
- [W-TOOL-01/20261007T0445027928280Z/installed-packages.json](../W-TOOL-01/20261007T0445027928280Z/installed-packages.json)
