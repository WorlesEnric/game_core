# P3.1b host evidence

Runtime/build revision: **2151e274855ce7c03611bb3fbffa8246371668da**. Host: myubuntu (hostname `worlesenric`), 2026-10-06. See [the packet](../../../../games/hollowmere/PACKET.md) for changes, commands, regression mapping and explicit open items.

- Final XML: [EditMode 446 passed / 0 failed / 12 skipped](tests/editmode-final.xml), [PlayMode 22/22](tests/playmode-final.xml), [shared persistence 17/17](tests/persistence-final.xml), [isolated bake verification 1/1](tests/verify.xml). [Machine summary](validation-summary.json).
- [.NET Execution 178/178](dotnet/execution.trx); required [metadata](metadata-check.txt) and [C#](csharp-check.txt) checks pass.
- [Linux IL2CPP build and smoke summary](build/build-summary.json): build passes, 0 errors; player smoke exits 0. [Shipped-file hashes](build/data-manifest.txt).
- [Measurement summary](measurement-summary.json): exactly two attempts, one completed route, **different profiles**. Both held one allocator reservation and the allocator mutex with no Editor active. The original video was not redone and predates the fixes.

| Attempt | Outcome | p95 | >100 ms outside transitions | Belfry hitch | Worst save frame |
|---|---|---:|---:|---:|---:|
| [1: Null Device](measurement/run1/frame-stats.json) | navigation failure at Echo, 661.593 s retained | 0.692 ms | 8 | 2.587 ms | 31.840 ms |
| [2: device-enabled Xvfb](measurement/run2/frame-stats.json) | entire route, ending, restart, restore; exit 0, 612.580 s | 1.284 ms | 0 | 7.404 ms | 29.013 ms |

Attempt 2's actual Unity device/resolution was **llvmpipe OpenGLCore / 640×480**, despite requesting a 1920×1080 display and player. These offscreen numbers **do not qualify 07's RTX 4060 Ti / 1080p graphical B-FRAME gate**. Attempt 1's startup and seven logger-flush-aligned stalls are retained; the route failure is not counted as success. No third measurement was run. The exact P3.1 rehearsal launch command was not present in committed evidence; the packet records that limitation.

The statistics script reproduces the entire original P3.1 JSON exactly ([parity result](stats-parity.txt)). The first ready frame remains included. Frame CSVs are losslessly compressed; each run has `frame-log-integrity.json` with both SHA-256 values and sizes. To recalculate without modifying evidence:

```sh
mkdir -p /tmp/p31b-stats
gzip -dc artifacts/studio/evidence/P3.1b/measurement/run2/frame-log.csv.gz > /tmp/p31b-stats/frame-log.csv
python3 games/hollowmere/Tools/frame_stats.py /tmp/p31b-stats
```

Raw text logs are retained as lossless `.log.gz` archives; `raw-log-integrity.json` records their uncompressed hashes.

Initial compiler diagnostics and the initial 12/17 shared-persistence result are retained alongside the final passing XML. The persistence failures revealed synchronous migration-path compatibility, which was fixed before the final suites and build. Logs were captured through `unity-batch.sh`'s redactor. No ETOS services or paid operations were used.
