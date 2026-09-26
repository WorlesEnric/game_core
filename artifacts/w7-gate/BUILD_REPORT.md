# W7-GATE Linux build report

**Result: PASS for the Wave 7 integration gate on `w7-gate` (`146ddd4` before this report).** The final `tools/run_w7_gate.sh` invocation exited 0. Full-duration TEST-023 timing qualification remains **Deferred by project-owner decision**; the one short diagnostic passed 19/19 correctness gates but its summarizer reported one provisional timing miss. This report distinguishes that miss from the gate's functional result.

## Host and configuration

- Ubuntu 24.04.4 LTS, Linux 7.0.0-31-generic, x86_64; Intel Core i7-12700KF; GCC 13.3.0, Clang 18.1.3, GNU ld 2.42. Build-time host facts: `toolchain/environment.txt` and `release/environment.txt`.
- .NET SDK 8.0.425, MSBuild 17.11.48, runtime 8.0.31; Unity 6000.0.75f1 (26349cd2a5c8), StandaloneLinux64 IL2CPP, High managed stripping, Burst enabled. C# 9/.NET Standard 2.1 project constraints unchanged; the pure assemblies remain without UnityEngine references (`host/gate-sources.json` and `tools/check_game_core_csharp.py`).
- Environment: `DOTNET_ROOT=$HOME/.dotnet`, `PATH=$HOME/.dotnet:$PATH`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity`, `DOTNET=$HOME/.dotnet/dotnet`, `PROBE_RUNS=2`. Unity and player invocations in the gate use `timeout` (1800 seconds full Editor/build, 600 seconds player); no timeout was counted as a pass. Headless player audio was disabled by the existing runner.
- Qualification player executable SHA-256 `aeaf13e291886fbd5a99b7dbd7c113b8ee13ed462a419a4d31a8ecbc3241ac70`; see `toolchain/environment.txt`. The release launcher's SHA-256 is the same; marker inspection of the player data/assemblies is in `release-gate-surface.json`.

## Exact commands and outcomes

From the worktree root, first synchronized with `git fetch origin && git checkout w7-gate && git reset --hard origin/w7-gate`. Ran `tools/run_w7_gate.sh` with the environment above on the initial checkout (failed Unity resolve), after repairing the lock (failed Unity compilation), after the compilation fix (EditMode passed, PlayMode failed four W7 tests), and finally after fixing those behavioral defects (exit 0). Also ran one isolated compile confirmation:

```sh
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics -quit \
  -projectPath "$PWD/unity/GameCore.Validation" \
  -logFile "$PWD/artifacts/w7-gate/unity/resolve-fixed.log"
UNITY="$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" \
  DOTNET="$HOME/.dotnet/dotnet" DOTNET_ROOT="$HOME/.dotnet" \
  PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 PROBE_RUNS=2 \
  tools/run_w7_gate.sh
python3 tools/validate_game_core_docs.py
```

The final gate command actually ran these steps, in order (exact argv and default paths in `tools/run_w7_gate.sh`):

| Step | Result and archived evidence |
| --- | --- |
| `dotnet build dotnet/GameCore.sln -c Release --nologo -v:q` | **Pass**, zero warnings/errors. |
| `dotnet test dotnet/GameCore.sln -c Release --no-build --nologo -v:q --logger 'trx;LogFilePrefix=w7' --results-directory artifacts/w7-gate/trx` | **Pass**, 1,280/1,280, 0 failed/skipped, 17 test projects. This includes ReferenceConformance 27/27, Recovery.Fixtures 47/47, Derivation 114/114, Replay 57/57, Benchmarks 90/90. Final TRX set: `w7_net8.0_20260927031346.trx` through `w7_net8.0_20260927031411.trx` (17 files). |
| Named derivation agreement filter; named `BenchmarkFixtureDerivationTests` filter | **Pass**, respectively 8/8 and 13/13, both nonempty TRX (`w7-equivalence_net8.0_20260927031422.trx`, `w7-ten-thousand_net8.0_20260927031446.trx`). These are reruns, not additional unique solution tests. |
| Host-side C# source, frozen table, contract, four catalog/coverage mirrors, reachability, fingerprint, budget, link.xml, release-clone self-test and six shell syntax checks | **Pass**. Budget record validates 10/10 declared rows, 20 existing evidence paths and owner deferral (`host/budget-record.json`). |
| Unity package resolve | **Pass** after repairing the duplicate lock entries; Unity imported with the committed lock. |
| Unfiltered Unity `-runTests -testPlatform EditMode` | **Pass**, 1,270/1,270, 0 failed/skipped (`unity/editmode-results.xml`), above each previous task's merged-scope total. |
| Unfiltered Unity `-runTests -testPlatform PlayMode` | **Pass**, 84/84, 0 failed/skipped (`unity/playmode-results.xml`), above the earlier 53/65/53/53 task totals. W7Gate fixture: 19/19. |
| Unity codegen for probe, cards, checkpoint, traversal, coverage bake; `git diff --exit-code` over four generated trees | **Pass**, byte-identical committed catalogs and coverage companions. `unity/codegen-*.log` and host-side mirror checks. |
| Qualification StandaloneLinux64 IL2CPP player (`tools/unity/build_probe.sh`) | **Pass** with High stripping (`toolchain/build.log`, `toolchain/environment.txt`). |
| All qualification probe modes | **Pass**: 24 result JSONs (23 Pass plus the GC-001 ExpectedNegative) with **two clean runs each**, 870 named assertions per run, 0 failing assertions. `toolchain/probe-*.json` and matching `.run2`; conformance 133/133 probe steps and 72/72 transcribed reference rows including the combined narrative+cards world; W7 gate 18/18; GC-027 54/54; catalog coverage 14/14. `toolchain/traces/` contains the generated conformance traces. |
| Release clone preparation, pre-build clone/link checks | **Pass**: `release/clone-surface.json` reports 180 own-source files inspected, zero problems, and lock absent before import; `release/link-xml.json` passed. |
| Marker-free IL2CPP release build and player inspections | **Pass**: `release/build.log`, `release-player-surface.json`, `release-gate-surface.json`, `release/telemetry-release-surface.json` report no forbidden qualification markers/latches and require the three kept modes. |
| Release probes | **Pass**, eight modes with **two clean runs each**, 217 named assertions per run, 0 failing: world dispatch, narrative, cards, GC-018, GC-019, traversal, catalog coverage (14/14), recovery smoke (17/17). Smoke used production `WorldRecovery.Recover`/`Restart` and `FileCheckpointStore`; real `release/recovery-smoke-{narrative,cards}.checkpoint` files exist. JSON and `.run2` in `release/`. |
| One short benchmark correctness diagnostic | **Pass for functional gates**: one player run, 11 workloads, 19/19 correctness gates, zero failing probe steps; one 1-second warmup and 2-second steady windows, five change repetitions. **Timing summarizer FAIL (exit 1), one provisional `budget.whole-world-preparation-p95` MissedTarget; full-duration timing NotRun/Deferred.** `benchmark/raw/run1/`, `benchmark/summary.md`, `benchmark/summarize.json`. The gate explicitly records the summarizer exit and judges the functional player separately. |
| Documentation validator self-test and full validator | **Pass**: 9 isolated fixtures and 14 Markdown documents, plus an explicit post-inventory `python3 tools/validate_game_core_docs.py` pass. |

No full-duration p95/p99, five-run benchmark or cross-platform player qualification was performed. The current diagnostic's provisional timing miss is not described as a passed timing budget. The audio observation is engine-free; no live audio device was qualified. No player/Editor watchdog expiration was used to claim success.

## Defects found and fixed

1. The merged Unity `packages-lock.json` repeated `com.gamecore.replay` and `com.gamecore.telemetry-qualification`; Unity Package Manager rejected duplicate keys. Removed only the duplicate second entries and kept valid JSON (`2cf5022`). No test expectation changed.
2. `W7GateScenario.GenreAuditStep` called LINQ `ToArray()` on `IReadOnlyList<string>` without importing LINQ. Used `string.Join(",", audit.Findings)` directly; Unity import compiled. Unity normalized `tests/GameCore.ReferenceConformance/README.md.meta`, which was committed (`4d17c38`).
3. The unfiltered first PlayMode run was 80/84; W7's catalog coverage rerun saw an instance legitimately created by the earlier CatalogCoverage assembly in the same Editor process. `CatalogCoverageScenario` now accounts only its own prior late mounts: fresh-process zero, no unexpected mount before each run, exactly one new mount, unchanged handler and other catalog assertions. The scenario does not reset or ignore the factory count (`5351817`).
4. W7's budget observation treated zero-valued correctness targets as absent, failing five rows despite 08 declaring zero as their actual target. It now distinguishes comparable zero targets from the two report-only baseline/plateau rows and checks the declared/recorded ID join; the budget definitions and expected values were not changed (`5351817`). The two other W7 PlayMode failures were the digest and aggregate verdict cascading from these two actual failures. The final full suites pass.

## Inventory and limits

`artifacts/gates/w4-generic-profile/inventory.{md,json}` promotes **only O-22** from Not yet to Implemented+Evidenced: the qualification GC-027/W7 probes passed faulted recovery across all three genres, and the marker-free release player recovered new sessions from real narrative/card checkpoints. Counts are now 42 evidenced, 44 partial, 0 not-yet across 86 rows. Other proposed promotions were deliberately left Partial where inventory clauses extend beyond this gate; the timing deferral is not a waiver. The three-genre conformance and cross-template observations passed, but complete V1-wide evidence remains Wave 8/9 work.

Raw logs/results under `artifacts/w7-gate/` were retained; any individual artifact over 2 MB is excluded or trimmed before commit. The last successful full run used the fixed production sources already pushed as `5351817`; this report and inventory update follow the successful run without changing compiled code.
