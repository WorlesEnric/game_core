# W-PLUG-04: Patrol index across unload/reload and save/load

Verdict: **PASS**. Current-run Patrol index across unload/reload and save/load is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Current-run evidence

- [UNITY-HOLLOWMERE/perf-probe-1-20261007T182419.381563Z/results.xml](../UNITY-HOLLOWMERE/perf-probe-1-20261007T182419.381563Z/results.xml)
- [UNITY-HOLLOWMERE/editmode-20261007T181428.800202Z/results.xml](../UNITY-HOLLOWMERE/editmode-20261007T181428.800202Z/results.xml)
