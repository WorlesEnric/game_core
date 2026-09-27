# GC-030 Linux build and completion-gate report

**Result:** Pass for the requested GC-030 checks on source revision `2b720ffb04a8f4105a4c544443ce3f97b6850251` (before this evidence commit). The release-readiness tool returned `V1 complete (with owner-approved exception: TEST-023 timing deferred)` with exit 0. This is a tool decision over the previously accepted Wave 8 evidence, not a claim that the full suites were rerun here.

## Host and profile

- Date: 2026-09-27 UTC. Host: Ubuntu 24.04, Linux x86_64, Intel Core i7-12700KF.
- .NET SDK 8.0.425, runtime 8.0.31; Python 3.12.3; Unity 6000.0.75f1.
- Qualification player: StandaloneLinux64 x64, IL2CPP, High managed stripping, Burst enabled. Built player SHA-256: `aeaf13e291886fbd5a99b7dbd7c113b8ee13ed462a419a4d31a8ecbc3241ac70` (identical to the recorded player launcher digest). Editor invocations used `-batchmode -nographics -quit`, `-logFile`, and `timeout --signal=TERM --kill-after=60 1800`; probes used `-batchmode -nographics`, `-logFile`, and `timeout --signal=TERM --kill-after=10 600`. Player runs were capped at `PROBE_RUNS=2` per mode.
- Environment for SDK commands: `DOTNET_ROOT=$HOME/.dotnet`, `PATH=$HOME/.dotnet:$PATH`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`. The build set `DOTNET=$HOME/.dotnet/dotnet`, `UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity`, `UNITY_TIMEOUT=1800`, `PROBE_RUNS=2`.

## Exact commands and results

Ran `git fetch origin && git checkout gc-030 && git reset --hard origin/gc-030` before verification. Original HEAD was `2b720ffb04a8f4105a4c544443ce3f97b6850251`.

| Command or verification | Result |
| --- | --- |
| `~/.dotnet/dotnet build dotnet/GameCore.sln --nologo` | **Pass**: 0 warnings, 0 errors; 7.66 s. Build compiles tests but does not execute them. |
| `python3 tools/release_readiness/check_revision_consistency.py --self-test` | **Pass**: all 16 mutations detected. |
| `python3 tools/release_readiness/check_revision_consistency.py` | **Pass**: 139 ok, 4 declared skips, 0 failures; accepted revision `3895d0c632c867ba87d783cd1a1654643dedfeb1` is an ancestor of HEAD. Disposable release-clone manifest digests are recorded-only, not recomputed. |
| `python3 tools/release_readiness/build_release_readiness.py --self-test` | **Pass**: all 10 mutations refused. |
| `python3 tools/release_readiness/build_release_readiness.py` | **Pass**, exit 0; exact status `V1 complete (with owner-approved exception: TEST-023 timing deferred)`. |
| `python3 tools/release_readiness/build_release_readiness.py --build` then `python3 tools/release_readiness/build_release_readiness.py --check` and `git diff --exit-code -- artifacts/release-readiness/evidence-manifest.json artifacts/release-readiness/evidence-manifest.md artifacts/release-readiness/status.json` | **Pass**: three regenerated records byte-identical to committed copies. |
| Scratch negative: copied only `artifacts/w8-gate/matrix/evidence-index.json`, flipped required `P-001` from Pass to NotRun and updated summary; `python3 tools/release_readiness/build_release_readiness.py --repo /tmp/gc030-negative-hwy73n0q --out-dir /tmp/gc030-negative-hwy73n0q/output --build` | **Pass (expected rejection)**: exit 1, generated status `incomplete`, `complete=false`; reported NotRun requirement and disagreement between accepted trees. Scratch directory removed. |
| `python3 tools/validate_game_core_docs.py --self-test` and `python3 tools/validate_game_core_docs.py` | **Pass**: 9 self-test fixtures; 14 Markdown documents validated. |
| `python3 tools/check_operator_docs.py` | **Pass**: 11 pages, 65 local links, 26 probe modes. |
| `python3 tools/check_package_metadata.py --self-test` and `python3 tools/check_package_metadata.py` | **Pass**: 12 self-test cases; 21 packages, 46 assemblies, 4 engine pins. |
| `python3 tools/emit_failure_codes.py --self-test` and `python3 tools/emit_failure_codes.py --check` | **Pass**: 8 self-test cases; committed table current (23 operation codes, 17 catalog codes, 11 narrative refusals). |
| `ARTIFACTS=$PWD/artifacts/gc-030/player-build tools/unity/build_probe.sh` | **Pass**: catalog regeneration and StandaloneLinux64 IL2CPP/High qualification player build. Build/codegen logs and environment captured in `player-build/`. `git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/Generated unity/GameCore.Validation/Packages/packages-lock.json` passed: no catalog or lock drift. |
| `PROBE_RUNS=2 ARTIFACTS=$PWD/artifacts/gc-030/probes PROBE_PLAYER=$PWD/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 tools/unity/run_cards_probe.sh` | **Pass**, 2/2 player exits 0, 28/28 steps Pass each run. |
| Same environment, `tools/unity/run_narrative_probe.sh` | **Pass**, 2/2 player exits 0, 24/24 steps Pass each run. |
| Same environment, `tools/unity/run_traversal_probe.sh` | **Pass**, 2/2 player exits 0, 15/15 steps Pass each run. |
| Same environment, `tools/unity/run_conformance_probe.sh` | **Pass**, 2/2 player exits 0, 133/133 steps Pass each run; four committed trace comparisons empty and repeated-run digests match. |

All eight freshly produced probe JSONs and player logs are under `artifacts/gc-030/probes/`; editor logs and build environment are under `artifacts/gc-030/player-build/`. No evidence file exceeds 2 MB.

## Suite and completion accounting

| Suite or gate | Fresh result here | Previously accepted evidence |
| --- | --- | --- |
| TEST-001 | **Partial / NotRun as a complete suite**: player built and four smoke modes executed; omitted-registration fixture and full toolchain suite not rerun. | Accepted matrix reports Pass. |
| TEST-021 | **Partial / NotRun as a complete suite**: three family probes and combined conformance, 2/2 each; whole .NET/Unity acceptance suite not rerun. | Accepted matrix reports Pass. |
| TEST-023 | **Deferred by project-owner decision**: no full-duration timing runs or timing benchmarks. | Accepted short diagnostic reported 19 correctness gates Pass and one provisional prepare-cost target MissedTarget; timing remains deferred, not Pass. |
| TEST-024 | **Pass for mechanical documentation check**: validator, operator, package, failure-code, readiness checks above; human semantic review not newly performed. | Accepted matrix reports Pass. |
| Full .NET test suite and Unity EditMode/PlayMode suites | **NotRun here**, deliberately: GC-030 changed no production code and task requests build plus focused smoke rather than full-suite rerun. | Readiness tool checked both accepted trees: 24/24 suites Pass each; 60 requirements and 26 operations each, with the sole non-pass row P-060 Deferred (owner decision). |

No source, test, expected-value, or package-lock changes were necessary. No fresh defect or hang was observed. The known post-V1 whole-world/local prepare-cost miss and deferred TEST-023 timing qualification remain open as recorded in `artifacts/release-readiness/outstanding-defects.md` and ADR-017. The new qualification build/probe evidence corroborates the current checkout but does not substitute for the accepted full conformance matrix.
