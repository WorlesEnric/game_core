# GC-027 evidence index

The Linux build and execution results are in `BUILD_REPORT.md`. This document's original authoring-host static checks are historical; the current run artifacts live in `trx-final/`, `unity/`, `toolchain/`, and `release/`. Qualification `-probeRecovery` passed five clean runs with all three families and 54 passing probe steps per run. The marker-free release clone was scanned and built with the production `WorldRecovery` type retained by `Assets/link.xml`, while the fault latch and recovery fixtures are absent.

## Historical authoring-host static checks

| Check | Command | Result |
|---|---|---|
| C# brace/paren balance and forbidden-construct scan | `python3 tools/check_game_core_csharp.py` | `checked 555 C# file(s)` → `ok` |
| GC-017 release surface, source half (`--no-build`) | `python3 tools/check_release_fault_free.py --no-build` | `PASS` — no runtime source keeps a latch reference once the qualification symbol is undefined; the `[Conditional]` mask is intact at 8 call sites |
| release-clone preparation, run for real | `python3 tools/unity/prepare_gc017_release_project.py` | prepared `unity/GameCore.ReleaseCheck` (since deleted) |
| declaration audit of every new C# file | a separate read-only reviewer, cross-checking each `X.Y` against its declaration | 13 findings, **all fixed** (`compile-risk-audit.md`) |
| the observation table vs its methods (round 2) | a mechanical pass comparing `ObservationNames`, every `const string name`, and the calls in `Run()` | 21 names, 21 methods, execution order identical to the table; `ExpectedNames` filters exactly the four delivery names and the four physics names by capability |
| interface completeness (round 2) | every `IGc027Family`, `IGc018Family` and `IGc013Family` member resolved against each adapter's full set of partial files | all three adapters answer every member; none missing, none declared twice |
| the harness's step lists vs the C# table (round 2) | the three blocks parsed out of the script compared, in order, against `ObservationNames` and its two capability predicates | each family's list equals its `ExpectedNames` sequence exactly, and the blocks together cover the whole table |
| release-clone invariants, run for real | `python3 tools/check_release_clone.py` | `VERDICT: clone is clean` — 149 files, no removed-type reference, no dangling asmdef reference, constructor 14 params = 14 args, 58 C# files balanced, all six removed modes absent and all thirteen kept modes wired, manifest clean |
| probe harness shell syntax | `bash -n tools/unity/run_recovery_probe.sh` | exit 0 |
| Python tool syntax | `python3 -c "import ast; ast.parse(...)"` over both edited tools | exit 0 |
| every `replace_once` strip needle matches exactly once | interpreter check over `ARG_NEEDLES` against the current `ProbeArguments.cs` | `needles 42, bad 0` |
| frozen digest literals recomputed from the frozen name table | SHA-256 over `label/<name>=pass` lines, LF-joined | the two literals in `ProbeRecovery` reproduce exactly; the same algorithm reproduces GC-018's two published literals, so the method is validated |

The table above records authoring-host checks only. For executed compiler, Unity, player and release results, see `BUILD_REPORT.md`; no authoring-host `NotRun` status is promoted by that historical table.

## Files

| File | Kind | Status |
|---|---|---|
| `recovery-behavior-and-data-loss.md` | executed eight-point/data-loss table, including traversal physical limitation | Pass 5/5 player runs |
| `crash-restart-transcripts.md` | emitted transcript clauses and explicit non-emitted observation rows | Pass 5/5 player runs |
| `outbox-consistency.md` | recovered obligation/cursor and single-effect counters | Pass 5/5 player runs |
| `fault-injection-matrix.md` | every applicable boundary and permitted observable result | Pass 5/5 player runs |
| `inventory-proposals.md` | the inventory rows this change set proposes the build host promote **after running** | proposals only |
| `static-checks.log` | verbatim output of the host-side checks listed above | ran here |
| `compile-risk-audit.md` | the read-only declaration audit of every new C# file: twelve findings, their fixes, and the list of things only a compiler can confirm | ran here (a reading result, not a compile) |

## Commands the build host runs

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet \
  PROBE_RUNS=5 tools/run_gc027_checks.sh          # if the orchestrator adds one, or the §6 sequence in
                                                  # recovery-behavior-and-data-loss.md by hand
```

The minimum set that produces every placeholder in this directory:

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-027/trx
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
  "$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testResults "$PWD/artifacts/gc-027/unity/editmode.xml" -logFile artifacts/gc-027/unity/editmode.log
UNITY="$UNITY" ARTIFACTS=artifacts/gc-027/toolchain tools/unity/build_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-027/toolchain tools/unity/run_recovery_probe.sh
python3 tools/check_release_fault_free.py --dotnet "$DOTNET" --json artifacts/gc-027/release-surface.json
python3 tools/unity/prepare_gc017_release_project.py
python3 tools/check_release_clone.py --json artifacts/gc-027/release-clone.json
rm -rf unity/GameCore.ReleaseCheck
```
