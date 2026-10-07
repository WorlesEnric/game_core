# W-PLUG-07: Quest branches; failure closes dependents

Verdict: **PASS**. Both quest branches, transitive dependent failure, cycle refusal and actual three endings/LanternLost failure pass at this revision.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Current-run evidence

- [UNITY-HOLLOWMERE/editmode-20261007T042823.666683Z/results.xml](../UNITY-HOLLOWMERE/editmode-20261007T042823.666683Z/results.xml)
- [UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/results.xml](../UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/results.xml)
- [UNITY-HOLLOWMERE/playmode-20261007T043423.985881Z/results.xml](../UNITY-HOLLOWMERE/playmode-20261007T043423.985881Z/results.xml)
