# GC-026 benchmark summary

Verdict: **FAIL** — 1 budget row(s) MissedTarget.

Status: measured diagnostic evidence from the raw sample documents listed under Evidence. Check Resolved configuration and Wall-clock window per run before comparing against 08's full method; a short or partial run does not qualify the five-run gate.

Hardware: machine=worlesenric, host=Linux worlesenric 7.0.0-31-generic #31~24.04.1-Ubuntu SMP PREEMPT_DYNAMIC Mon Aug 10 09:38:02 UTC 2 x86_64 x86_64 x86_64 GNU/Linux, uname_m=x86_64, nproc=20, cpu_model=12th Gen Intel(R) Core(TM) i7-12700KF, lsb_release=Ubuntu 24.04.4 LTS, date_utc=2026-09-26T19:41:44Z, player=/home/worlesenric/wkspace/gc-wt/w7-gate/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64, player_bytes=14784, player_sha256=aeaf13e291886fbd5a99b7dbd7c113b8ee13ed462a419a4d31a8ecbc3241ac70.

## Hardware

| fact | value |
| --- | --- |
| machine | worlesenric |
| host | Linux worlesenric 7.0.0-31-generic #31~24.04.1-Ubuntu SMP PREEMPT_DYNAMIC Mon Aug 10 09:38:02 UTC 2 x86_64 x86_64 x86_64 GNU/Linux |
| uname_m | x86_64 |
| nproc | 20 |
| cpu_model | 12th Gen Intel(R) Core(TM) i7-12700KF |
| lsb_release | Ubuntu 24.04.4 LTS |
| date_utc | 2026-09-26T19:41:44Z |
| player | /home/worlesenric/wkspace/gc-wt/w7-gate/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 |
| player_bytes | 14784 |
| player_sha256 | aeaf13e291886fbd5a99b7dbd7c113b8ee13ed462a419a4d31a8ecbc3241ac70 |

## Build and configuration facts

Every measurement must name its hardware and its build/config; these are the fields the probe player recorded in its result JSON (the same block every other probe mode in this repository writes).

| fact | value |
| --- | --- |
| task | GC-026 |
| mode | Benchmark |
| result | Pass |
| unityVersion | 6000.0.75f1 |
| declaredUnityVersion | 6000.0.75f1 |
| declaredTarget | StandaloneLinux64 |
| platform | LinuxPlayer |
| architecture | X64 |
| processorType | 12th Gen Intel(R) Core(TM) i7-12700KF |
| scriptingBackend | IL2CPP |
| isIl2Cpp | true |
| managedStrippingLevel | High |
| burstCompilerEnabled | true |
| catalogFingerprint | 02bb94ab81353a2063fb1cb92d77ce3b9889885b02a95de515193e45178f37ff |
| catalogFileHash | 03bdcd23fd8ec0515d93d7c54d7f7c695f3860a850a7280b82316aaacd14c5c1 |

## Resolved configuration

| knob | resolved value |
| --- | --- |
| run count | 1 |
| workloads | idle-command-world, steady-unchanged-10000-steps, steady-execution-10000-targets, update-size-1, update-size-100, update-size-10000, whole-world-mode-switch, spawn-1000, reparent-100, inactive-target-comparison, lifecycle-cycles-1000 |
| scopes | 1000 |
| targets | 10000 |
| warmupSeconds | 1 |
| durationSeconds | 2 |
| seed | 20260926 |

## Wall-clock window per run

| workload | kind | warmup s | duration s | repetitionsRequested | repetitionsExecuted | measured window per run | warmup actually spent per run |
| --- | --- | --- | --- | --- | --- | --- | --- |
| idle-command-world | Steady | 1 | 2 | 0 | run1=0 | run1=2004759 us | run1=1003156 us |
| steady-unchanged-10000-steps | Steady | 1 | 2 | 0 | run1=0 | run1=93135 us | run1=1016275 us |
| steady-execution-10000-targets | Steady | 1 | 2 | 0 | run1=0 | run1=2001298 us | run1=1000697 us |
| update-size-1 | Change | 1 | 2 | 5 | run1=5 | run1=0 us | run1=1663520 us |
| update-size-100 | Change | 1 | 2 | 5 | run1=5 | run1=0 us | run1=1746822 us |
| update-size-10000 | Change | 1 | 2 | 5 | run1=5 | run1=0 us | run1=1119125 us |
| whole-world-mode-switch | Change | 1 | 2 | 5 | run1=5 | run1=0 us | run1=1345953 us |
| spawn-1000 | Change | 1 | 2 | 5 | run1=5 | run1=0 us | run1=1107610 us |
| reparent-100 | Change | 1 | 2 | 5 | run1=5 | run1=0 us | run1=1029638 us |
| inactive-target-comparison | Change | 1 | 2 | 5 | run1=5 | run1=0 us | run1=2032811 us |
| lifecycle-cycles-1000 | Change | 1 | 2 | 5 | run1=5 | run1=0 us | run1=1764936 us |

## Per-workload phase distributions

Samples are pooled across every run and the percentiles are re-derived from the pooled sample array with the nearest-rank definition: sort ascending, index `ceil(p/100 * N) - 1`, clamped into `[0, N-1]`; p <= 0 returns the minimum and p >= 100 the maximum. Per-run percentiles are never averaged — an average of p95s is not a p95.

| workload | kind | phase | samples | p50 us | p95 us | p99 us | max us | mean us | total us |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| idle-command-world | Steady | Step | 407 | 4910 | 7113 | 12551 | 14124 | 4925.698 | 2004759 |
| steady-unchanged-10000-steps | Steady | Step | 10 | 6679 | 16022 | 16022 | 16022 | 9313.5 | 93135 |
| steady-execution-10000-targets | Steady | Step | 1306 | 1526 | 1633 | 1710 | 2123 | 1532.387 | 2001298 |
| update-size-1 | Change | Warmup | 2 | 806459 | 857061 | 857061 | 857061 | 831760 | 1663520 |
| update-size-1 | Change | Prepare | 5 | 861840 | 867066 | 867066 | 867066 | 853178.4 | 4265892 |
| update-size-100 | Change | Warmup | 2 | 867234 | 879588 | 879588 | 879588 | 873411 | 1746822 |
| update-size-100 | Change | Prepare | 6 | 871332 | 909987 | 909987 | 909987 | 774567.167 | 4647403 |
| update-size-100 | Change | Apply | 1 | 663 | 663 | 663 | 663 | 663 | 663 |
| update-size-100 | Change | EndToEnd | 1 | 248831 | 248831 | 248831 | 248831 | 248831 | 248831 |
| update-size-10000 | Change | Warmup | 1 | 1119125 | 1119125 | 1119125 | 1119125 | 1119125 | 1119125 |
| update-size-10000 | Change | Prepare | 5 | 1112297 | 1155911 | 1155911 | 1155911 | 1108963.2 | 5544816 |
| whole-world-mode-switch | Change | Warmup | 2 | 554970 | 790983 | 790983 | 790983 | 672976.5 | 1345953 |
| whole-world-mode-switch | Change | Prepare | 5 | 515786 | 765012 | 765012 | 765012 | 596633.8 | 2983169 |
| spawn-1000 | Change | Warmup | 5 | 226167 | 232891 | 232891 | 232891 | 221522 | 1107610 |
| spawn-1000 | Change | Prepare | 6 | 150647 | 166954 | 166954 | 166954 | 131807.5 | 790845 |
| spawn-1000 | Change | Apply | 1 | 2170 | 2170 | 2170 | 2170 | 2170 | 2170 |
| spawn-1000 | Change | EndToEnd | 1 | 1329275 | 1329275 | 1329275 | 1329275 | 1329275 | 1329275 |
| reparent-100 | Change | Warmup | 7 | 149097 | 158679 | 158679 | 158679 | 147091.143 | 1029638 |
| reparent-100 | Change | Prepare | 5 | 97035 | 99000 | 99000 | 99000 | 90979.4 | 454897 |
| inactive-target-comparison | Change | Warmup | 11 | 133161 | 1084807 | 1084807 | 1084807 | 290288.364 | 3193172 |
| inactive-target-comparison | Change | Prepare | 10 | 213679 | 1406298 | 1406298 | 1406298 | 718550.2 | 7185502 |
| lifecycle-cycles-1000 | Change | Warmup | 1 | 1764936 | 1764936 | 1764936 | 1764936 | 1764936 | 1764936 |
| lifecycle-cycles-1000 | Change | Change | 10 | 797062 | 977293 | 977293 | 977293 | 872569.2 | 8725692 |

## Counter deltas

Workload-level totals from each document's `counters` object, pooled across runs with the schema's own aggregation policy (sum for an additive counter, max for a gauge). A counter absent from the object is a recorded zero; the correctness gates below pair a zero reading with a positive control so a zero caused by a compiled-out counter cannot pass as a measurement.

The 08-named counters appear as `name=delta`; a counter named by a budget row for that workload is always listed, zero included. Every other counter is listed only when it moved, so the table stays readable. The full column order of the raw CSV is: `control-nodes-visited`, `candidates-matched`, `contributions-added`, `contributions-retracted`, `strata-evaluated`, `plan-prepared-bytes`, `apply-us`, `assembly-epoch`, `steps-advanced`, `service-string-lookups`, `stage-us`, `stage-samples`, `job-wait-us`, `job-wait-samples`, `structural-operations`, `request-high-water`, `request-overflow`, `stale-results`, `live-leases`, `outstanding-callbacks`, `retained-event-bytes`, `retained-event-count`, `quarantine-bytes`, `quarantine-entries`, `lease-bytes`, `cache-bytes`, `cache-entries`, `discarded-callbacks`, `apply-samples`.

| workload | listed counters (name=delta) | counters that moved |
| --- | --- | --- |
| idle-command-world | steps-advanced=0, stage-samples=0 | <none> |
| steady-unchanged-10000-steps | control-nodes-visited=0, service-string-lookups=0 | <none> |
| steady-execution-10000-targets | steps-advanced=1306 | steps-advanced=1306 |
| update-size-1 | control-nodes-visited=645200, candidates-matched=155005, contributions-added=5, contributions-retracted=5, strata-evaluated=5 | control-nodes-visited=645200, candidates-matched=155005, contributions-added=5, contributions-retracted=5, strata-evaluated=5 |
| update-size-100 | control-nodes-visited=645200, candidates-matched=155005, contributions-added=500, contributions-retracted=500, strata-evaluated=5, apply-us=663, assembly-epoch=2, structural-operations=500, live-leases=9, lease-bytes=4320, apply-samples=1 | control-nodes-visited=645200, candidates-matched=155005, contributions-added=500, contributions-retracted=500, strata-evaluated=5, apply-us=663, assembly-epoch=2, structural-operations=500, live-leases=9, lease-bytes=4320, apply-samples=1 |
| update-size-10000 | control-nodes-visited=645200, candidates-matched=155005, contributions-added=50000, contributions-retracted=50000, strata-evaluated=5 | control-nodes-visited=645200, candidates-matched=155005, contributions-added=50000, contributions-retracted=50000, strata-evaluated=5 |
| whole-world-mode-switch | control-nodes-visited=105555, candidates-matched=105005, contributions-added=41918, contributions-retracted=62877, strata-evaluated=5 | control-nodes-visited=105555, candidates-matched=105005, contributions-added=41918, contributions-retracted=62877, strata-evaluated=5 |
| spawn-1000 | control-nodes-visited=35020, candidates-matched=10000, contributions-added=10000, contributions-retracted=8000, strata-evaluated=5, apply-us=2170, assembly-epoch=3, structural-operations=5000, live-leases=9, lease-bytes=4320, apply-samples=1 | control-nodes-visited=35020, candidates-matched=10000, contributions-added=10000, contributions-retracted=8000, strata-evaluated=5, apply-us=2170, assembly-epoch=3, structural-operations=5000, live-leases=9, lease-bytes=4320, apply-samples=1 |
| reparent-100 | control-nodes-visited=6240, candidates-matched=1250, contributions-added=180, contributions-retracted=270, strata-evaluated=5 | control-nodes-visited=6240, candidates-matched=1250, contributions-added=180, contributions-retracted=270, strata-evaluated=5 |
| inactive-target-comparison | control-nodes-visited=1514285, candidates-matched=425505, contributions-added=10, strata-evaluated=10 | control-nodes-visited=1514285, candidates-matched=425505, contributions-added=10, strata-evaluated=10 |
| lifecycle-cycles-1000 | control-nodes-visited=1035435, candidates-matched=260010, contributions-added=5, contributions-retracted=5, strata-evaluated=10 | control-nodes-visited=1035435, candidates-matched=260010, contributions-added=5, contributions-retracted=5, strata-evaluated=10 |

## Correctness gates

Asserted by the probe, not inferred here: zero stable control-tree scans, zero string service lookups, an idle world advancing zero steps, no duplicated authoritative state, and a live-instrumentation positive control (a counter that moved somewhere in the run, so a zero reading is not a compiled-out counter). A single failed gate fails this whole summary.

| workload | gate | runs | verdict | detail |
| --- | --- | --- | --- | --- |
| idle-command-world | idle-command-world-advances-zero-steps | 1 | pass | frames=3334144; pumpsPerSample=8192; steps=0; stepsAdvanced=0; stageSamples=0; dispatchRuns=0; windowUs=2004759; budgetUs=2000000 |
| idle-command-world | idle-command-world-does-zero-control-work | 1 | pass | controlNodes=0; serviceStringLookups=0; candidatesMatched=0; strataEvaluated=0; planPreparedBytes=0 |
| steady-unchanged-10000-steps | unchanged-composition-commits-the-declared-steps | 1 | pass | declared=10000; committed=10000; pumps=10; step=142000; windowUs=93135 |
| steady-unchanged-10000-steps | zero-stable-control-tree-scans | 1 | pass | steps=10000; controlNodes=0; candidatesMatched=0; strataEvaluated=0; planPreparedBytes=0 |
| steady-unchanged-10000-steps | zero-string-service-lookups | 1 | pass | serviceStringLookups=0 |
| steady-unchanged-10000-steps | unchanged-composition-publishes-nothing | 1 | pass | publicationsBefore=1; after=1 |
| steady-execution-10000-targets | core-execution-window | 1 | pass | steps=1306; windowUs=2001298; budgetUs=2000000; windowEndedByCap=false; targets=10000; commands=1306000 |
| steady-execution-10000-targets | steady-execution-allocation-is-thread-complete | 1 | pass | managedBytes=0;declaredThreads=1;sampledThreads=1;threadComplete=true;coverage=AllThreadsSampled; managedBytesPerStep=0 |
| update-size-1 | update-affects-the-declared-target-count | 1 | pass | size=1; affectedTargets=1; expected=1; repetitions=5; warmupCycles=2; accepted=True |
| update-size-100 | update-affects-the-declared-target-count | 1 | pass | size=100; affectedTargets=100; expected=100; repetitions=5; warmupCycles=2; accepted=True |
| update-size-100 | apply-affects-the-declared-target-count | 1 | pass | outcome=Published; code=None; detail=; eligibleTargets=100; declaredApplyTargets=100; installedRows=200; retractedRows=0; applyUs=663; applySamples=1; waitUs=0; endToEndUs=248831; prepareUs=248168; joined=True |
| update-size-100 | apply-pause-is-measured-by-the-publisher | 1 | pass | applySamples=1; applyUs=663; note=a zero apply sample count would make the apply-pause row unmeasurable rather than fast |
| update-size-10000 | update-affects-the-declared-target-count | 1 | pass | size=10000; affectedTargets=10000; expected=10000; repetitions=5; warmupCycles=1; accepted=True |
| whole-world-mode-switch | mode-switch-invalidates-the-whole-world | 1 | pass | switches=5; reportedLocal=0; warmupSwitches=2; accepted=True |
| spawn-1000 | spawn-first-visibility-is-fully-assembled | 1 | pass | spawnedPerRepetition=1000; assembledOnFirstVisibility=1000; capability=CapabilityId(0774f192d6754a88c3215e38cceaf022); accepted=True |
| spawn-1000 | live-spawn-publishes-one-complete-publication | 1 | pass | spawned=1000; code=None; detail=; assembled=1000; outcome=Published; installedRows=2000; seedUs=12156; publishUs=1329275; applyUs=2170 |
| reparent-100 | reparent-moves-the-declared-subtree | 1 | pass | subtreeTargets=100; accepted=True |
| inactive-target-comparison | inactive-targets-do-not-add-a-per-change-scan | 1 | pass | smallTargets=1000; largeTargets=10000; smallUs=921152; largeUs=6264350; ratio=6.801 |
| lifecycle-cycles-1000 | lifecycle-counts-return-to-baseline | 1 | pass | baselineAssemblies=10000; finalAssemblies=10000; baselineContributions=20959; finalContributions=20959; cycles=5; accepted=True |

## Memory categories

08 requires managed heap, native containers, retained catalogs/caches, asset leases, events and quarantined work to be reported separately, and an aggregate process total alone is never evidence. Values are the range across the runs; the managed-allocation reading states whether it covered every declared thread, because a main-thread-only zero cannot prove worker allocation is zero.

| workload | category | value | entries | thread-complete | note |
| --- | --- | --- | --- | --- | --- |
| idle-command-world | managed heap start | 59346944 |  | n/a | process total, observation only |
| idle-command-world | managed heap end | 59346944 |  | n/a | process total, observation only |
| idle-command-world | managed allocated | 59346944 |  | thread-complete=false | covered threads; thread completeness stated below |
| idle-command-world | native containers | 4320 |  | n/a | the world's own resource ledger |
| idle-command-world | lease bytes | 4320 |  | n/a | live staged/asset leases |
| idle-command-world | retained events | 0 | 0 | n/a | committed events still retained |
| idle-command-world | cache | 0 | 0 | n/a | retained derived caches |
| idle-command-world | quarantine | 0 | 0 | n/a | unfinished work still reaches these |
| idle-command-world | live leases | 9 |  | n/a | count |
| idle-command-world | outstanding callbacks | 0 |  | n/a | live activations plus tracked jobs |
| steady-unchanged-10000-steps | managed heap start | 98181120 |  | n/a | process total, observation only |
| steady-unchanged-10000-steps | managed heap end | 98181120 |  | n/a | process total, observation only |
| steady-unchanged-10000-steps | managed allocated | 98181120 |  | thread-complete=false | covered threads; thread completeness stated below |
| steady-unchanged-10000-steps | native containers | 4320 |  | n/a | the world's own resource ledger |
| steady-unchanged-10000-steps | lease bytes | 4320 |  | n/a | live staged/asset leases |
| steady-unchanged-10000-steps | retained events | 0 | 0 | n/a | committed events still retained |
| steady-unchanged-10000-steps | cache | 0 | 0 | n/a | retained derived caches |
| steady-unchanged-10000-steps | quarantine | 0 | 0 | n/a | unfinished work still reaches these |
| steady-unchanged-10000-steps | live leases | 9 |  | n/a | count |
| steady-unchanged-10000-steps | outstanding callbacks | 0 |  | n/a | live activations plus tracked jobs |
| steady-execution-10000-targets | managed heap start | 98684928 |  | n/a | process total, observation only |
| steady-execution-10000-targets | managed heap end | 98684928 |  | n/a | process total, observation only |
| steady-execution-10000-targets | managed allocated | 0 |  | thread-complete=true | covered threads; thread completeness stated below |
| steady-execution-10000-targets | native containers | 0 |  | n/a | the world's own resource ledger |
| steady-execution-10000-targets | lease bytes | 0 |  | n/a | live staged/asset leases |
| steady-execution-10000-targets | retained events | 0 | 0 | n/a | committed events still retained |
| steady-execution-10000-targets | cache | 0 | 0 | n/a | retained derived caches |
| steady-execution-10000-targets | quarantine | 0 | 0 | n/a | unfinished work still reaches these |
| steady-execution-10000-targets | live leases | 0 |  | n/a | count |
| steady-execution-10000-targets | outstanding callbacks | 0 |  | n/a | live activations plus tracked jobs |
| update-size-1 | managed heap start | 0 |  | n/a | process total, observation only |
| update-size-1 | managed heap end | 0 |  | n/a | process total, observation only |
| update-size-1 | managed allocated | 0 |  | thread-complete=false | covered threads; thread completeness stated below |
| update-size-1 | native containers | 0 |  | n/a | the world's own resource ledger |
| update-size-1 | lease bytes | 0 |  | n/a | live staged/asset leases |
| update-size-1 | retained events | 0 | 0 | n/a | committed events still retained |
| update-size-1 | cache | 0 | 0 | n/a | retained derived caches |
| update-size-1 | quarantine | 0 | 0 | n/a | unfinished work still reaches these |
| update-size-1 | live leases | 0 |  | n/a | count |
| update-size-1 | outstanding callbacks | 0 |  | n/a | live activations plus tracked jobs |
| update-size-100 | managed heap start | 0 |  | n/a | process total, observation only |
| update-size-100 | managed heap end | 0 |  | n/a | process total, observation only |
| update-size-100 | managed allocated | 0 |  | thread-complete=false | covered threads; thread completeness stated below |
| update-size-100 | native containers | 0 |  | n/a | the world's own resource ledger |
| update-size-100 | lease bytes | 0 |  | n/a | live staged/asset leases |
| update-size-100 | retained events | 0 | 0 | n/a | committed events still retained |
| update-size-100 | cache | 0 | 0 | n/a | retained derived caches |
| update-size-100 | quarantine | 0 | 0 | n/a | unfinished work still reaches these |
| update-size-100 | live leases | 0 |  | n/a | count |
| update-size-100 | outstanding callbacks | 0 |  | n/a | live activations plus tracked jobs |
| update-size-10000 | managed heap start | 0 |  | n/a | process total, observation only |
| update-size-10000 | managed heap end | 0 |  | n/a | process total, observation only |
| update-size-10000 | managed allocated | 0 |  | thread-complete=false | covered threads; thread completeness stated below |
| update-size-10000 | native containers | 0 |  | n/a | the world's own resource ledger |
| update-size-10000 | lease bytes | 0 |  | n/a | live staged/asset leases |
| update-size-10000 | retained events | 0 | 0 | n/a | committed events still retained |
| update-size-10000 | cache | 0 | 0 | n/a | retained derived caches |
| update-size-10000 | quarantine | 0 | 0 | n/a | unfinished work still reaches these |
| update-size-10000 | live leases | 0 |  | n/a | count |
| update-size-10000 | outstanding callbacks | 0 |  | n/a | live activations plus tracked jobs |
| whole-world-mode-switch | managed heap start | 0 |  | n/a | process total, observation only |
| whole-world-mode-switch | managed heap end | 0 |  | n/a | process total, observation only |
| whole-world-mode-switch | managed allocated | 0 |  | thread-complete=false | covered threads; thread completeness stated below |
| whole-world-mode-switch | native containers | 0 |  | n/a | the world's own resource ledger |
| whole-world-mode-switch | lease bytes | 0 |  | n/a | live staged/asset leases |
| whole-world-mode-switch | retained events | 0 | 0 | n/a | committed events still retained |
| whole-world-mode-switch | cache | 0 | 0 | n/a | retained derived caches |
| whole-world-mode-switch | quarantine | 0 | 0 | n/a | unfinished work still reaches these |
| whole-world-mode-switch | live leases | 0 |  | n/a | count |
| whole-world-mode-switch | outstanding callbacks | 0 |  | n/a | live activations plus tracked jobs |
| spawn-1000 | managed heap start | 135847936 |  | n/a | process total, observation only |
| spawn-1000 | managed heap end | 135847936 |  | n/a | process total, observation only |
| spawn-1000 | managed allocated | 135847936 |  | thread-complete=false | covered threads; thread completeness stated below |
| spawn-1000 | native containers | 4320 |  | n/a | the world's own resource ledger |
| spawn-1000 | lease bytes | 4320 |  | n/a | live staged/asset leases |
| spawn-1000 | retained events | 0 | 0 | n/a | committed events still retained |
| spawn-1000 | cache | 0 | 0 | n/a | retained derived caches |
| spawn-1000 | quarantine | 0 | 0 | n/a | unfinished work still reaches these |
| spawn-1000 | live leases | 9 |  | n/a | count |
| spawn-1000 | outstanding callbacks | 0 |  | n/a | live activations plus tracked jobs |
| reparent-100 | managed heap start | 0 |  | n/a | process total, observation only |
| reparent-100 | managed heap end | 0 |  | n/a | process total, observation only |
| reparent-100 | managed allocated | 0 |  | thread-complete=false | covered threads; thread completeness stated below |
| reparent-100 | native containers | 0 |  | n/a | the world's own resource ledger |
| reparent-100 | lease bytes | 0 |  | n/a | live staged/asset leases |
| reparent-100 | retained events | 0 | 0 | n/a | committed events still retained |
| reparent-100 | cache | 0 | 0 | n/a | retained derived caches |
| reparent-100 | quarantine | 0 | 0 | n/a | unfinished work still reaches these |
| reparent-100 | live leases | 0 |  | n/a | count |
| reparent-100 | outstanding callbacks | 0 |  | n/a | live activations plus tracked jobs |
| inactive-target-comparison | managed heap start | 0 |  | n/a | process total, observation only |
| inactive-target-comparison | managed heap end | 0 |  | n/a | process total, observation only |
| inactive-target-comparison | managed allocated | 0 |  | thread-complete=false | covered threads; thread completeness stated below |
| inactive-target-comparison | native containers | 0 |  | n/a | the world's own resource ledger |
| inactive-target-comparison | lease bytes | 0 |  | n/a | live staged/asset leases |
| inactive-target-comparison | retained events | 0 | 0 | n/a | committed events still retained |
| inactive-target-comparison | cache | 0 | 0 | n/a | retained derived caches |
| inactive-target-comparison | quarantine | 0 | 0 | n/a | unfinished work still reaches these |
| inactive-target-comparison | live leases | 0 |  | n/a | count |
| inactive-target-comparison | outstanding callbacks | 0 |  | n/a | live activations plus tracked jobs |
| lifecycle-cycles-1000 | managed heap start | 0 |  | n/a | process total, observation only |
| lifecycle-cycles-1000 | managed heap end | 0 |  | n/a | process total, observation only |
| lifecycle-cycles-1000 | managed allocated | 0 |  | thread-complete=false | covered threads; thread completeness stated below |
| lifecycle-cycles-1000 | native containers | 0 |  | n/a | the world's own resource ledger |
| lifecycle-cycles-1000 | lease bytes | 0 |  | n/a | live staged/asset leases |
| lifecycle-cycles-1000 | retained events | 0 | 0 | n/a | committed events still retained |
| lifecycle-cycles-1000 | cache | 0 | 0 | n/a | retained derived caches |
| lifecycle-cycles-1000 | quarantine | 0 | 0 | n/a | unfinished work still reaches these |
| lifecycle-cycles-1000 | live leases | 0 |  | n/a | count |
| lifecycle-cycles-1000 | outstanding callbacks | 0 |  | n/a | live activations plus tracked jobs |

## Budget table

The 08 table row for row (encoded from PerformanceBudgets.cs). NotMeasured is not a pass: a row whose measurement is absent is reported as NotMeasured. ReportOnly rows are baselines 08 asks to be established, not numbers to be met.

| budget id | workload | phase | metric | target | measured | verdict | 08 reference |
| --- | --- | --- | --- | --- | --- | --- | --- |
| budget.execution-p95 | steady-execution-10000-targets | Step | p95 | at most 4000 us | 1633 us | WithinTarget | 10,000 integer-rule targets, 1,000 active commands per fixed step: Core execution p95 at most 4 ms. |
| budget.execution-managed-bytes | steady-execution-10000-targets | Step | managed-bytes-per-step | at most 0 bytes | 0 bytes | WithinTarget | Stable execution after warmup: 0 managed bytes per logical step in the kernel hot path. |
| budget.unchanged-control-nodes | steady-unchanged-10000-steps | Step | control-nodes-visited | exactly 0 | 0 | WithinTarget | No composition changes over 10,000 steps: 0 control-tree visits. |
| budget.unchanged-service-lookups | steady-unchanged-10000-steps | Step | service-string-lookups | exactly 0 | 0 | WithinTarget | No composition changes over 10,000 steps: 0 string service resolutions. |
| budget.apply-pause-p95 | update-size-100 | Apply | p95 | at most 2000 us | 663 us | WithinTarget | Valid plan affecting 100 existing targets: Apply pause p95 at most 2 ms. |
| budget.whole-world-preparation-p95 | update-size-10000 | Prepare | p95 | at most 100000 us | 1155911 us | MissedTarget | Whole-world derivation for 10,000 targets: Preparation p95 at most 100 ms. |
| budget.spawn-baseline | spawn-1000 | Prepare | p95 | report-only: establish a baseline (no target to compare against) | 166954 us | ReportOnly | 1,000-target spawn under active capabilities: report prepare/apply p95, native/managed bytes and recipe reuse; establish a baseline. |
| budget.lifecycle-plateau | lifecycle-cycles-1000 | Change | p99 | report-only: establish a baseline (no target to compare against) | 977293 us | ReportOnly | 1,000 lifecycle cycles with fixed retained data: active counts return to baseline; bounded cache/native/managed growth plateaus. |
| budget.idle-steps | idle-command-world | Step | steps-advanced | exactly 0 | 0 | WithinTarget | Idle command-driven world: 0 simulation steps. |
| budget.idle-stage-updates | idle-command-world | Step | stage-samples | exactly 0 | 0 | WithinTarget | Idle command-driven world: 0 simulation-stage updates. |

Measurement provenance (what each measured number was derived from):

- `budget.execution-p95`: pooled 1306 sample(s) over 1 run(s); p95 from the sample arrays.
- `budget.execution-managed-bytes`: 0 managed byte(s) over 1306 committed step(s); thread-complete=true.
- `budget.unchanged-control-nodes`: counters['control-nodes-visited'] pooled delta over 1 run(s) (absent key means the writer recorded a zero).
- `budget.unchanged-service-lookups`: counters['service-string-lookups'] pooled delta over 1 run(s) (absent key means the writer recorded a zero).
- `budget.apply-pause-p95`: pooled 1 sample(s) over 1 run(s); p95 from the sample arrays.
- `budget.whole-world-preparation-p95`: pooled 5 sample(s) over 1 run(s); p95 from the sample arrays.
- `budget.spawn-baseline`: pooled 6 sample(s) over 1 run(s); p95 from the sample arrays.
- `budget.lifecycle-plateau`: pooled 10 sample(s) over 1 run(s); p99 from the sample arrays.
- `budget.idle-steps`: counters['steps-advanced'] pooled delta over 1 run(s) (absent key means the writer recorded a zero).
- `budget.idle-stage-updates`: counters['stage-samples'] pooled delta over 1 run(s) (absent key means the writer recorded a zero).

## Provisional targets

> The following numbers are initial engineering targets, not measured results or universal shipping requirements. Apply them on the recorded baseline machine; retain the measurements if later product evidence justifies revising a target. A quota in the protocol is a correctness bound and remains mandatory even when a performance target changes.

A quota in the protocol (P-022's candidate/contribution/target/byte counts, the apply-cost estimate) is a correctness bound enforced by the derivation engine; it is reported with the fixture configuration and a performance revision may never move it.

Budget rows MissedTarget (each is a decision for `BUDGET_DECISIONS.md` — either an accepted revision with the retained measurement, or a fix):

- `budget.whole-world-preparation-p95` (`update-size-10000`/Prepare/p95): measured 1155911 against at most 100000 us. See BUDGET_DECISIONS.md.

## Raw document self-consistency

Every raw document's declared per-phase distributions equal what its own `samples` array gives.

## Evidence

Every raw file this summary read, so each number above is recomputable from these alone:

- `/home/worlesenric/wkspace/gc-wt/w7-gate/artifacts/w7-gate/benchmark/environment.txt`
- `/home/worlesenric/wkspace/gc-wt/w7-gate/artifacts/w7-gate/benchmark/raw/run1/idle-command-world.samples.json`
- `/home/worlesenric/wkspace/gc-wt/w7-gate/artifacts/w7-gate/benchmark/raw/run1/inactive-target-comparison.samples.json`
- `/home/worlesenric/wkspace/gc-wt/w7-gate/artifacts/w7-gate/benchmark/raw/run1/lifecycle-cycles-1000.samples.json`
- `/home/worlesenric/wkspace/gc-wt/w7-gate/artifacts/w7-gate/benchmark/raw/run1/probe-benchmark.json`
- `/home/worlesenric/wkspace/gc-wt/w7-gate/artifacts/w7-gate/benchmark/raw/run1/reparent-100.samples.json`
- `/home/worlesenric/wkspace/gc-wt/w7-gate/artifacts/w7-gate/benchmark/raw/run1/spawn-1000.samples.json`
- `/home/worlesenric/wkspace/gc-wt/w7-gate/artifacts/w7-gate/benchmark/raw/run1/steady-execution-10000-targets.samples.json`
- `/home/worlesenric/wkspace/gc-wt/w7-gate/artifacts/w7-gate/benchmark/raw/run1/steady-unchanged-10000-steps.samples.json`
- `/home/worlesenric/wkspace/gc-wt/w7-gate/artifacts/w7-gate/benchmark/raw/run1/update-size-1.samples.json`
- `/home/worlesenric/wkspace/gc-wt/w7-gate/artifacts/w7-gate/benchmark/raw/run1/update-size-100.samples.json`
- `/home/worlesenric/wkspace/gc-wt/w7-gate/artifacts/w7-gate/benchmark/raw/run1/update-size-10000.samples.json`
- `/home/worlesenric/wkspace/gc-wt/w7-gate/artifacts/w7-gate/benchmark/raw/run1/whole-world-mode-switch.samples.json`
