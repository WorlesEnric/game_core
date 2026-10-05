# B-REGION bounded probes

Verdict: **BLOCKED** for full acceptance. Headless real-scene travel/pose/pump probes; combined transition elapsed is not separate load/unload timing. Native texture/audio peak-relative release snapshots are missing. No graphical B-FRAME claim.

Measured UTC timestamps and commands are in the two linked run READMEs. Machine: Core i7-12700KF, 20 logical CPUs, RTX 4060 Ti; Linux 7.0.0-31-generic. Packet Editors serialized. No other Editor observed at run boundaries; no continuous host-process sampler was installed.

| Measurement | Run 1 ms | Run 2 ms | Median ms |
|---|---:|---:|---:|
| Blackmere Marsh -> Drowned Belfry | 15 | 10 | 12.5 |
| Drowned Belfry -> Thornwick Village | 10 | 11 | 10.5 |
| Thornwick Village -> Blackmere Marsh | 29 | 21 | 25.0 |
| boot | 831 | 687 | 759.0 |

- [UNITY-HOLLOWMERE/perf-final-1-20261005T201110.316067Z/results.xml](../UNITY-HOLLOWMERE/perf-final-1-20261005T201110.316067Z/results.xml)
- [UNITY-HOLLOWMERE/perf-final-2-20261005T201658.304250Z/results.xml](../UNITY-HOLLOWMERE/perf-final-2-20261005T201658.304250Z/results.xml)

Reproduce: `PROBE_RUNS=2 studio/tools/verify-all.sh perf`. Fixed limits: load ≤2000 ms; unload ≤1000 ms; release ≤5% peak. No budget changed.
