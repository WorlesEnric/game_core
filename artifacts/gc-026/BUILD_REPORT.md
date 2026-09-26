# GC-026 Linux build and test report

**Status: partial; full TEST-023 benchmark qualification is Blocked.** The current branch builds and the editor suites pass. One short all-workload diagnostic run passed its correctness gates, but the specified five independent full-catalogue runs did not finish. No provisional target has been revised or accepted.

## Host and toolchain

- Host: Ubuntu 24.04.4 LTS, Linux `7.0.0-31-generic`, x86-64; Intel Core i7-12700KF, 12 physical cores / 20 logical CPUs; 31 GiB RAM, 8 GiB swap. CPU governor: `powersave`.
- .NET SDK `8.0.425`, runtime `8.0.31`; C# 9 and .NET Standard 2.1 pure assemblies; `tools/check_game_core_csharp.py`: 559 files checked, pass.
- Unity `6000.0.75f1` (`26349cd2a5c8`), StandaloneLinux64 IL2CPP, Release compiler configuration, High managed stripping. Locked Burst `1.8.28`, Entities `1.4.6`, Collections `2.6.6`; benchmark player reports Burst enabled. Unsupported platforms are not qualified.
- Builds are traced in `qualification/{environment.txt,codegen.log,build.log}` and `release/{environment.txt,codegen.log,build.log}`. Note: both standalone launcher executables are 14,784 bytes; the associated `_Data` directories are part of the build, not captured by that launcher checksum alone.

## Exact commands and results

The worktree was initialized with `git fetch origin && git checkout gc-026 && git reset --hard origin/gc-026`. Dotnet commands ran with `DOTNET_ROOT=$HOME/.dotnet`, `PATH=$HOME/.dotnet:$PATH`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`.

| Command / suite | Result | Evidence |
| --- | --- | --- |
| `dotnet build dotnet/GameCore.sln -c Release --nologo -v:q` | **Pass**, 0 warnings, 0 errors | final build stdout |
| `dotnet test dotnet/GameCore.sln -c Release --no-build --nologo -v:q` | **Pass**, 1,205/1,205, 0 skipped across 15 assemblies | final test stdout; benchmark assembly 89/89 |
| `timeout 1800 Unity -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testResults artifacts/gc-026/editmode-final.xml -logFile artifacts/gc-026/editmode-final.log` | **Pass**, 1,164/1,164, 0 skipped | `editmode-final.xml`; includes 89 benchmark tests, randomized 10,000-entry live-index oracle, W2–W5 and GC-013/018/019 |
| Same command with `PlayMode`, `playmode-final.xml`, `playmode-final.log` | **Pass**, 53/53, 0 skipped | `playmode-final.xml`; W6 gate 27/27 |
| `UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity UNITY_TIMEOUT=1800 ARTIFACTS=artifacts/gc-026/qualification tools/unity/build_probe.sh` | **Pass**, IL2CPP/High-stripping player built | qualification build logs and generated catalog |
| `PROBE_RUNS=5 ARTIFACTS=artifacts/gc-026/probes tools/unity/run_probe.sh both` | **Pass**, positive 5/5, expected-negative 5/5 | probe result JSON and logs |
| `PROBE_RUNS=5 ARTIFACTS=artifacts/gc-026/probes/<script> tools/unity/<script>.sh`, for world, narrative, cards, W1–W6, W4 profile, GC-013/017 faults/018/019/021, traversal, replay, lifecycle stress | **Pass**, 18 scripts, five invocations per script (90/90) | `artifacts/gc-026/probes/` |
| `BENCH_RUNS=1 BENCH_WARMUP=1 BENCH_DURATION=2 BENCH_REPETITIONS=5 BENCH_TIMEOUT=600 ARTIFACTS=artifacts/gc-026/catalogue-smoke tools/run_benchmarks.sh` | **Fail** overall: 11/11 workload gates pass; 2 provisional budget misses. This is deliberately *not* full-duration acceptance. | `artifacts/gc-026/catalogue-smoke/raw/run1/` and `artifacts/performance/summary.md` |
| `BENCH_RUNS=5 BENCH_TIMEOUT=1800 ARTIFACTS=artifacts/performance tools/run_benchmarks.sh` | **Blocked/Fail**: repeated first-run no-log-progress >10 minutes; terminated under the required hang rule. No qualifying five-run catalogue, 30-second per-workload warmups, or 28,000 change repetitions. | `artifacts/performance/raw/run1/{invocation.txt,player-benchmark.log}`; GDB attach refused by host ptrace policy |
| `BENCH_RUNS=5 BENCH_WORKLOADS=steady-execution-10000-targets BENCH_TIMEOUT=1800 ARTIFACTS=artifacts/gc-026/steady-measurement tools/run_benchmarks.sh` | **Fail** as isolated *mode*: five 30-second warmups and 120-second per-sample `Step` totals completed, but global authority fixture was not run because the selector excludes the live world; the older runner also serialized `windowMicroseconds=0` (metadata defect fixed subsequently). Five raw samples retained; not full TEST-023 acceptance. | `artifacts/gc-026/steady-measurement/raw-samples.tar.zst` (extract with `tar --zstd -xf`) |
| `python3 tools/summarize_benchmarks.py --raw artifacts/gc-026/catalogue-smoke/raw --out artifacts/performance/summary.md --decisions artifacts/performance/BUDGET_DECISIONS.md --machine worlesenric --json artifacts/performance/summarize.json` | **Fail**, 10 rows, 2 diagnostic misses; summary produced | `summary.md`, `summarize.json`; `python3 tools/summarize_benchmarks.py --self-test` **Pass**, 57/57 |
| `python3 tools/unity/prepare_gc017_release_project.py`; `python3 tools/check_release_clone.py` before Unity import | **Pass** clone inspection (149 files); rerun *after* import reports 27 false positives in generated `Library/PackageCache` sources and restored `packages-lock.json` | prior check stdout; see release scan below |
| `UNITY_PROJECT=$PWD/unity/GameCore.ReleaseCheck UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity ARTIFACTS=artifacts/gc-026/release tools/unity/build_probe.sh` | **Pass** marker-free IL2CPP player | release build logs |
| `python3 tools/check_player_fault_free.py --player unity/GameCore.ReleaseCheck/Builds/Linux64 --il2cpp unity/GameCore.ReleaseCheck/Library/Bee/artifacts/LinuxPlayerBuildProgram/il2cppOutput/cpp --json artifacts/gc-026/release/surface.json` | **Pass**, 78 managed assemblies and 744 generated sources | `release/surface.json` |
| Byte scan of release player files and generated IL2CPP sources for `probeBenchmark`, `BenchmarkScenario`, `GameCore.Benchmarks` | **Pass**, 1,106 files scanned; zero hits | scan stdout |
| Release `run_probe.sh both` plus world, narrative, cards, W1–W4/profile, GC-013/018/019 and traversal, with `UNITY_PROJECT=unity/GameCore.ReleaseCheck PROBE_RUNS=5` | **Pass**, 10 positive/negative plus 60 family invocations | `artifacts/gc-026/release/probes/` |

Every Unity Editor launch used `timeout 1800`; scripts wrap each player in `timeout 600`, except the full benchmark's per-run `timeout 1800`. No Unity test result is inferred from the `-quit` compile-only invocation: the actual test commands above omit `-quit` and wrote XML.

## Benchmark facts and open findings

The diagnostic 1,000-scope/10,000-target seed `20260926` passed 19 correctness gates across 11 workloads. The unchanged-composition window committed 10,000 steps with zero control-node visits and zero string service lookups. Idle command world advanced zero steps/stages; owner-state mutation read 11 then 12 from ECS without changing derived bindings. Its short windows (1 s warmup, 2 s steady, five change repetitions) cannot substantiate p95 at the declared duration or lifecycle plateau. Its pre-fix allocation counter included sample-recording allocations: 336.49 bytes/step; corrected kernel-only short probe reads 0 bytes/step on its declared single thread. The five 120-second steady-only runs captured complete samples, but fail the global gate because their workload selector omitted the authority fixture. Do not treat them as five all-workload passes.

Diagnostic provisional targets: execution p95 1,587 us versus 4,000; unchanged scans 0; string lookups 0; apply pause 605 us versus 2,000; idle steps/stages 0. Whole-world preparation p95 **15,425,585 us versus 100,000 us**; per-change index rebuilds and candidate enumeration are implicated by 129,040 control nodes visited across five updates, but there is no profiler attribution sufficient to approve a particular optimization. The target remains unchanged; a revised target is **proposed for orchestrator acceptance only after full measurements**. Spawn and lifecycle are report-only, and five cycles cannot establish a 1,000-cycle plateau. `BUDGET_DECISIONS.md` records every row and the separate P-022 correctness quotas.

The full catalogue's per-run cost is not within the 1,800-second watchdog on this host: even a five-repetition diagnostic of all 11 workloads took 565 seconds, with update-size-one/100 derivations around 12 seconds each and whole-world updates around 15 seconds each. The required 28,000 repetitions cannot be represented as a passing run by lowering counts or widening the watchdog. Multiple 10-minute no-log-progress observations were treated as hangs; attempted `gdb -p PID -batch -ex 'thread apply all bt'` returned `ptrace: Inappropriate ioctl for device` under the host policy. No raw full-duration document was produced. Root cause and full benchmark remain unresolved.

## Fixes on this branch

1. Repaired missing `WholeWorldPreparationP95` identifier, `IReadOnlyList.Count` use, ambiguous `DerivationDelta`, and Unity probe API/flag compile errors. Dotnet and Unity compilation now pass without weakening tests.
2. Added deterministic `LiveTargetIndex` append+Sort oracle test: seeds 7, 83 and 20260926, duplicate registrations/retirements, and a 10,000-target case. It passed on the final EditMode run.
3. Fixed benchmark's tag-selected unaffected witness, two-token `-probeResult` parsing, absent per-run timeout default, and live-world initial publication alignment.
4. Fixed non-monotonic fixed-step and idle host clocks, accidental fixed-world scale truncation, duplicate spawned target IDs, missing next composition publication for live spawn, and fixture-appropriate configured P-022 derivation budget. Live apply and spawn gates pass in the focused probe; no protocol quota was silently changed globally.
5. Measured publisher apply pause using its own microsecond clock and registered the publisher as a telemetry owner; used a declared authoritative narrative slot so the mutation fixture survives a real plan; separated kernel allocation measurement from benchmark sample objects; recorded the measured steady window; batched idle pump samples without shortening duration. Focused live probe and all-workload short diagnostic pass correctness gates.
6. Clarified that `summary.md` is measured *diagnostic* data, not full acceptance; documented all provisional rows and the unresolved miss. Kept existing test assertions and acceptance values unchanged.

## Still not qualified

- Full GC-026 five-run catalogue at 30 s warmup, 120 s steady window and 28,000 change repetitions: **Blocked** by no-progress/runtime budget; `TEST-023` is not promoted.
- Whole-world 100 ms provisional target: **Fail** in short diagnostic run; no accepted revision and no proven optimization.
- Final player replays after the last benchmark-only allocation/metadata edits: earlier qualification and marker-free family probes passed on prior build revisions; final Editor suites and final dotnet suites passed. Benchmark-only changes are not claimed to have been requalified in every player probe mode.
- The release-clone checker assumes a fresh clone; after build its generated `Library` is in scope and Unity restores a package lock. The actual marker-free player surface scan passed; the post-import textual checker failure is not recorded as a shipping binary failure.
