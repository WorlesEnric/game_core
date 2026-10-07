# W-PLUG-12: Bake byte-identity; one line change → one revision change

Verdict: **PASS**. Both current project authoring/bake lanes pass. Exact byte-identity and single-definition revision tests pass; initial missing voice-bank authoring is retained, not hidden.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Current-run evidence

- [UNITY-HOLLOWMERE/editmode-20261007T042823.666683Z/results.xml](../UNITY-HOLLOWMERE/editmode-20261007T042823.666683Z/results.xml)
- [UNITY-CLEANPROOF/clean-recheck-editmode-20261007T044422.332841Z/results.xml](../UNITY-CLEANPROOF/clean-recheck-editmode-20261007T044422.332841Z/results.xml)
