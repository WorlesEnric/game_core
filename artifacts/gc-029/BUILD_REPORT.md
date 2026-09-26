# GC-029 build and reproduction report

## Verdict

**Pass for the GC-029 clean-checkout reproduction and operator contract** at `7d1e305` (the final clean clone's input revision). The later commits contain only archived evidence and this report; no production C# or Unity asset source changed. The final clone ran all 54 recorded steps with exit code 0 and no tracked-file drift, then was deleted. The first fresh clone (`62539e2`) also finished its suites and probes, but Unity rewrote the committed package lock and the budget checker rewrote a tracked host-specific JSON file; those reproducibility defects were corrected before the final clone. Both transcripts are retained under `artifacts/reproducibility/{first-clone,final-clone}/`.

## Host and profile

- Linux x86_64, kernel `7.0.0-31-generic`, 20 logical processors; Python 3.12.3, .NET SDK 8.0.425, Unity 6000.0.75f1. Editor build reports `6000.0.75f1 (26349cd2a5c8)` in its log. Qualification target: StandaloneLinux64 x86_64, IL2CPP Release, High managed stripping, Burst enabled, headless and audio disabled. Other targets are unqualified.
- .NET assemblies retain C# 9 and .NET Standard 2.1; `tools/check_game_core_csharp.py` inspected 623 files and passed. Package audit inspected 21 packages and 46 assemblies; eight kernel packages declare zero `com.gamecore.gameplay.*` dependencies.
- NuGet's advisory endpoint was unavailable to `dotnet` on the initial worktree build, yielding 19 `NU1900` warnings-as-errors despite a successful HTTP request from `curl`. The successful builds/restores used process-local `NuGetAudit=false`; this does **not** change compiler warnings-as-errors or disable tests. NuGet vulnerability-audit results are therefore **NotRun** on this host. No repository-wide audit policy was weakened.

## Exact commands exercised

From the worktree (the indicated environment applied to the .NET and reproduction commands):

```sh
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 NuGetAudit=false
$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release -p:NuGetAudit=false
$HOME/.dotnet/dotnet test dotnet/GameCore.sln -c Release --no-build --logger 'trx;LogFilePrefix=gc029' --results-directory artifacts/reproducibility/host/trx
python3 tools/check_package_metadata.py --self-test
python3 tools/emit_failure_codes.py --self-test
python3 tools/check_operator_docs.py --self-test
python3 tools/validate_game_core_docs.py --self-test
python3 tools/check_release_clone.py --self-test
python3 tools/compare_registration_fingerprints.py --self-test
python3 tools/check_package_metadata.py --sync-lock
python3 tools/emit_failure_codes.py --output /tmp/gc029-failure-codes.md
cmp docs/operator/failure-codes.md /tmp/gc029-failure-codes.md
python3 tools/check_operator_docs.py
python3 tools/validate_game_core_docs.py
python3 tools/check_game_core_csharp.py
bash -n tools/reproduce.sh
```

The first unmodified `dotnet build ... -bl:artifacts/reproducibility/host/dotnet-build.binlog` failed on `NU1900`; the audited-off repeat passed with zero warnings/errors. The worktree .NET suite passed. The package `--sync-lock` command produced no tracked diff after the fix. The failure-code table compared byte-for-byte; the committed and generated SHA-256 both equal `8ec01538546102ea61521d1a6a8c37798d6066902ea62b391a503e6d6575fde3`.

Two genuinely fresh clones, each made with the command below and with **no copied Library directory or project cache**:

```sh
git clone --branch gc-029 git@github.com:WorlesEnric/game_core.git ~/wkspace/repro-gc029
# in the new clone, with DOTNET_ROOT/PATH/DOTNET_CLI_TELEMETRY_OPTOUT/NuGetAudit as above:
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet PROBE_RUNS=2 tools/reproduce.sh
```

After the first run, the disposable clone was removed, fixes were committed/pushed in `gc-029`, and the branch was freshly cloned and reproduced a second time. The final clone was removed after archiving results. All Unity and player processes launched by `tools/reproduce.sh`/`tools/unity/build_probe.sh`/probe harnesses used `timeout` watchdogs (1800 seconds per full Editor invocation, 600 seconds per player probe sequence). No observed log silence reached ten minutes; no GDB backtrace was needed. Neither run timed out.

## Results (final fresh clone)

| Suite / contract | Status | Observed result | Evidence |
| --- | --- | --- | --- |
| Complete `.NET` solution build | **Pass** | 0 warnings, 0 errors | `final-clone/transcript.log`, `final-clone/steps.tsv` |
| Complete `.NET` solution tests | **Pass** | 17 TRX assemblies, **1280 passed, 0 failed, 0 skipped** | `final-clone/host/trx/` |
| Unity EditMode, all testables | **Pass** | **1270 passed, 0 failed, 0 skipped** | `final-clone/unity/editmode-results.xml` |
| Unity PlayMode, all testables | **Pass** | **84 passed, 0 failed, 0 skipped** | `final-clone/unity/playmode-results.xml` |
| Qualification player IL2CPP build | **Pass** | Executable built with High stripping | `final-clone/transcript.log`, steps 39–42 |
| Qualification narrative/cards/traversal | **Pass** | 3 families × 2 runs = **6 Pass JSON results** | `final-clone/probe/probe-*.json*` |
| Marker-free release clone and player | **Pass** | Clone, fault, telemetry, latch and gate surface checks passed; player built | `final-clone/release/*surface.json`, transcript |
| Release narrative/cards/traversal | **Pass** | 3 families × 2 runs = **6 Pass JSON results** | `final-clone/release/probe-*.json*` |
| Five catalog generation/bake methods and committed bytes | **Pass** | All methods exited zero; `git diff --exit-code` over all four generated trees was empty | `final-clone/steps.tsv`, transcript |
| Package metadata / no kernel→gameplay | **Pass** | 21 packages, 46 assemblies, eight kernel packages, no reverse dependency; resolved lock byte-identical | `final-clone/host/package-metadata-after-resolve.json`, `git diff --exit-code` at end of clone |
| Failure-code generator | **Pass** | 22 operation codes, 17 catalog codes, 11 narrative refusals; generated operator page byte-identical | generator `--check`, `cmp` above |
| Operator and design docs | **Pass** | 11 operator pages/49 local links/26 probe modes; 14 design docs and nine validator fixtures | transcript, `check_operator_docs.py`, validator |
| TEST-023 full-duration timing | **NotRun (owner decision)** | No p95/p99 qualification claimed; existing short diagnostic and open prepare-cost miss remain documented | `docs/operator/deferred-scope.md` |
| Other platform/player targets | **NotRun (unqualified)** | No macOS, Windows, Linux ARM64, mobile, console or WebGL player run | `docs/operator/profile.md` |
| Standalone omitted-registration negative probe / exhaustive probe matrix | **NotRun by this reproduction entry point** | The clean-checkout script runs the three family probes, not every mode; do not infer full TEST-001/TEST-021 exhaustive coverage from this result | `tools/reproduce.sh` header, `docs/operator/build-and-run.md` §5 |

The first clone had the same actual suite counts and 12/12 family probe JSON verdicts. Its transcript/ledger also reported success because the script checks catalog byte identity and package dependency consistency, not byte identity of the *entire* Unity lock or unrelated tracked host JSON. The tracked lock and budget JSON drift was detected by inspecting `git diff` and corrected instead of calling that checkout clean.

## Fixes and limits

1. `unity/GameCore.Validation/Packages/packages-lock.json`: committed the pinned Editor's actual resolved dependency graph. The old lock omitted Unity-engine dependencies in several local packages and had a late `com.gamecore.recovery` entry. The clean clone had modified the lock on resolve even though the earlier metadata audit passed because that audit compares only the GameCore dependency subset. No package manifest or production assembly was weakened.
2. `tools/reproduce.sh`: wrote the host-dependent budget check to untracked `host/reproduce-budget-record.json`, not the committed Mac-host `host/budget-record.json`. `docs/operator/build-and-run.md` names the corrected artifact. The committed historical budget artifact is not rewritten by a clean checkout.
3. `docs/operator/catalog-generation.md`: added `timeout 1800` to each directly quoted Unity invocation, matching the watchdog policy; removed an unused shell variable. No runtime code changed.

All non-destructive commands quoted in the operator runbooks that are suitable as stand-alone checks were spot-run (metadata audit/sync-lock, generator/check, docs and self-tests, catalog verification/emitter/reachability/baked checks, source syntax and `git diff`). All 22 referenced executable/script/solution paths exist; the six referenced `.sh` paths are executable. Destructive release preparation and direct player commands were instead exercised inside the fresh-clone reproduction, with watchdogs. The docs qualify **only** Linux x86_64 IL2CPP High stripping and explicitly label other platforms unqualified.

Because there were **no production C# or Unity asset source edits** between `62539e2` and the final reproduction (only lock, script, operator docs and evidence), the full final-clone EditMode/PlayMode suites and both player probe sets cover the accepted implementation; no separate worktree Unity run was required by the brief. The final clone began on `7d1e305`, which already contained all functional corrections. Its later evidence/report commits do not change the reproduced behavior.

The script's human display has some duplicated step labels at manually recorded boundaries (for example, it prints `step 37` for byte identity while the ledger assigns that action `38`). The machine-readable ledger contains 54 successful entries and is the authoritative sequence. The toolchain record's `revision_dirty=3` includes transcript/ledger/environment files created by the script itself before taking that measurement; it is **not** evidence of tracked-source drift. Explicit post-run `git diff --exit-code` confirmed no tracked drift in the corrected clone. Neither presentation issue was used to claim a failed test had passed.
