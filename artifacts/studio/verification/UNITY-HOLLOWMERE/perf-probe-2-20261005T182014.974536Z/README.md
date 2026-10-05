# perf-probe-2

Verdict: **PASS**.

Source revision: `20e34d1e6a6800e4b0bd4a1e3ebd44611a968c55`; host: `worlesenric`.
Started: 2026-10-05T18:20:14.975791+00:00; ended: 2026-10-05T18:20:50.497314+00:00; duration: 35.523 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/UNITY-HOLLOWMERE/perf-probe-2-20261005T182014.974536Z/logs --label perf-probe-2 --results ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/UNITY-HOLLOWMERE/perf-probe-2-20261005T182014.974536Z/results.xml -- -runTests -testPlatform PlayMode -testFilter 'Hollowmere\.P1_1\..*|Hollowmere\.P1_3\..*|Hollowmere\.P1_7a\..*'
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
