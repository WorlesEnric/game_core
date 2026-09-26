# GC-027 evidence — index and placeholders

**Nothing in this directory is a run result.** This host has no Unity, no .NET SDK, no Mono and no C# compiler, so
this change set has not been compiled, imported, executed or built. Every file here is either (a) a *template* the
orchestrator's build host fills in by running the commands below, or (b) a host-side static check that really did run
here, labelled as such.

## What actually ran on this host

| Check | Command | Result |
|---|---|---|
| C# brace/paren balance and forbidden-construct scan | `python3 tools/check_game_core_csharp.py` | `checked 555 C# file(s)` → `ok` |
| GC-017 release surface, source half (`--no-build`) | `python3 tools/check_release_fault_free.py --no-build` | `PASS` — no runtime source keeps a latch reference once the qualification symbol is undefined; the `[Conditional]` mask is intact at 8 call sites |
| release-clone preparation, run for real | `python3 tools/unity/prepare_gc017_release_project.py` | prepared `unity/GameCore.ReleaseCheck` (since deleted) |
| declaration audit of every new C# file | a separate read-only reviewer, cross-checking each `X.Y` against its declaration | 12 findings, **all fixed** (`compile-risk-audit.md`) |
| release-clone invariants, run for real | `python3 tools/check_release_clone.py` | `VERDICT: clone is clean` — 149 files, no removed-type reference, no dangling asmdef reference, constructor 14 params = 14 args, 58 C# files balanced, all six removed modes absent and all thirteen kept modes wired, manifest clean |
| probe harness shell syntax | `bash -n tools/unity/run_recovery_probe.sh` | exit 0 |
| Python tool syntax | `python3 -c "import ast; ast.parse(...)"` over both edited tools | exit 0 |
| every `replace_once` strip needle matches exactly once | interpreter check over `ARG_NEEDLES` against the current `ProbeArguments.cs` | `needles 42, bad 0` |
| frozen digest literals recomputed from the frozen name table | SHA-256 over `label/<name>=pass` lines, LF-joined | the two literals in `ProbeRecovery` reproduce exactly; the same algorithm reproduces GC-018's two published literals, so the method is validated |

None of that is a build, an import, a test or a player run.

## Files

| File | Kind | Status |
|---|---|---|
| `recovery-behavior-and-data-loss.md` | the design + the data-loss boundary table the task requires, with the evidence column to be filled | authored; §6 names the filling commands |
| `crash-restart-transcripts.md` | the crash/restart transcript template and its fill-in instructions | `NotRun (pending orchestrator build host)` |
| `outbox-consistency.md` | the outbox consistency report template and its fill-in instructions | `NotRun (pending orchestrator build host)` |
| `fault-injection-matrix.md` | the eight-point matrix with where each is asserted and what the build host copies out | authored from the source; observations `NotRun` |
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
