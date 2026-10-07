# W-PERSIST-01: Save in the Ruin with items; load: all restored; region re-entered

Verdict: **PASS**. Production Belfry save/fresh boot restores canonical slots, items/facts, residency and in-flight work exactly once; midquest restore continues to ending C.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Current-run evidence

- [UNITY-HOLLOWMERE/perf-probe-1-20261007T043718.908062Z/results.xml](../UNITY-HOLLOWMERE/perf-probe-1-20261007T043718.908062Z/results.xml)
- [UNITY-HOLLOWMERE/playmode-20261007T043423.985881Z/results.xml](../UNITY-HOLLOWMERE/playmode-20261007T043423.985881Z/results.xml)
