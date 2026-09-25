# W4-GATE Linux build and test report

## Verdict

**Pass — Wave 4 integration gate.** `tools/run_w4_gate.sh` completed end to end with `PROBE_RUNS=5`. The integrated `W4GateScenario` passed in Editor and the Linux64 IL2CPP player for narrative and cards, each over the committed generated catalog and the fixture catalog. This is not a claim that all V1 requirements or later stress/fault suites pass.

## Host and reproducible command

- Starting revision: `9acd160c3f7a333e5d59ec48a44af407ccb8ef08` (`origin/w4-gate`); fixes and this report committed subsequently on `w4-gate`.
- Linux x86_64, kernel `7.0.0-31-generic`, Intel Core i7-12700KF (12 cores, 20 logical CPUs); .NET SDK `8.0.425`; Unity `6000.0.75f1`; Python `3.12.3`; GCC `13.3.0`, Clang `18.1.3`, GNU ld `2.42`. See `toolchain/environment.txt` for host, date, manifest hash, player hash and compiler versions.
- Unity project: `unity/GameCore.Validation`; Entities `1.4.6`, Burst `1.8.28`, Collections `2.6.6`. Pure assemblies retain C# 9 and .NET Standard 2.1; no UnityEngine reference was added to them.
- Executed from repository root with `DOTNET_ROOT=$HOME/.dotnet`, `PATH=$HOME/.dotnet:$PATH`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity`, `DOTNET=$HOME/.dotnet/dotnet`, `PROBE_RUNS=5`:

```sh
tools/run_w4_gate.sh
```

The script executed these commands in order (absolute artifact paths resolve under the repository root):

```sh
$DOTNET build dotnet/GameCore.sln -c Release
$DOTNET test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/w4-gate/trx
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile artifacts/w4-gate/unity/resolve.log
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testResults artifacts/w4-gate/unity/editmode-results.xml -logFile artifacts/w4-gate/unity/editmode.log
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform PlayMode -testResults artifacts/w4-gate/unity/playmode-results.xml -logFile artifacts/w4-gate/unity/playmode.log
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog -logFile artifacts/w4-gate/unity/card-codegen.log
UNITY="$UNITY" ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/build_probe.sh
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_probe.sh both
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_world_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_w1_gate_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_w2_gate_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_narrative_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_cards_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_w3_gate_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_w4_profile_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_gc013_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_w4_gate_probe.sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

`build_probe.sh` independently bounds both Unity invocations with `timeout --signal=TERM --kill-after=60 1800`; `probe_runs.sh` bounds each player invocation at 600 seconds. No timeout or ten-minute no-progress hang occurred; no GDB attachment was needed. Focused pre-gate EditMode qualification used `timeout --signal=TERM --kill-after=60 600 "$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testFilter GameCore.W4Gate.Tests -testResults artifacts/w4-gate/unity/w4gate-editmode.xml -logFile artifacts/w4-gate/unity/w4gate-editmode.log` and passed 3/3. Test runs omit `-quit` as required by the runner.

## Results on the final run

| Suite / artifact | Pass | Fail | Skipped / not run |
| --- | ---: | ---: | ---: |
| .NET solution build | 0 warnings, 0 errors | 0 | — |
| .NET tests, 11 TRX projects | 750 | 0 | 0 |
| Unity resolve and import | completed | 0 errors | — |
| Unity EditMode, 17 assemblies | 781 | 0 | 0 |
| Unity PlayMode | 6 | 0 | 0 |
| Card and narrative catalog regeneration | 2 byte-identical | 0 | — |
| Linux64 IL2CPP player, High stripping | built | 0 build failures | — |
| Documentation validator self-test | 9 fixtures | 0 | 0 |
| Documentation validator | 14 documents | 0 | 0 |

The .NET TRX counts by assembly: Composition 144, Content.Compiler 44, Contracts 45, Derivation 114, Execution 58, Planning 160, ProtocolFixtures.Production 10, ProtocolFixtures 10, ReferenceSeams 21, Rules.Cards 26, Rules.Narrative 118. `GameCore.Derivation.Tests` includes the two 50-seed × 500-operation incremental/oracle differential sweeps (narrative and cards). Unity EditMode includes `GameCore.W4Gate.Tests` 3/3, `GameCore.Gc013.Tests` 2/2, Lifecycle 58/58, Planning 160/160 and the earlier gates. TRX files under `trx/` are the final invocation only; earlier failed-attempt TRX files were removed to avoid ambiguous evidence. The Unity compiler emitted existing `CS8604` nullable warnings in `ProbeWorldDispatch.cs`; the .NET build had zero warnings.

| Player mode (`toolchain/probe-*.json`) | Repetitions | Canonical result / step count |
| --- | ---: | --- |
| GC-001 Positive and MissingRegistration | 5 each | Pass 8/8; ExpectedNegative 1/1 (two ancillary Pass steps) |
| GC-005 WorldDispatch | 5 | Pass 7/7 |
| W1Gate | 5 | Pass 23/23 |
| W2Gate | 5 | Pass 24/24 |
| GC-010 Narrative | 5 | Pass 24/24 |
| GC-011 Cards | 5 | Pass 28/28 |
| W3Gate | 5 | Pass 11/11 |
| GC-012 W4Profile | 5 | Pass 7/7 |
| GC-013 transition | 5 | Pass 62/62 |
| W4Gate | 5 | Pass 70/70 |

The gate has ten probe scripts, with GC-001 executing two player modes; all 55 player invocations produced the expected result and exit code. W4Gate's 70 steps are 17 observations × 2 catalogs × 2 families plus both digest checks. Digests: narrative `d73e1a15e3d5f997b47087d02ea73ed809b73692b35900c3f2feeeff65cebaab`; cards `4a1bdb460ab366c5a0ffed77f09aaca881b4fdfea142195290ee5ad73283b018`. Player evidence records both mode directions over existing and future targets; provider-changing subtree move with identity and live state preserved; required-service loss/wait and return/resume; suspend/resume; actual reverse-order unmount teardown; Preserve, PreserveDormant, RemoveDerived, TransferTo and manifest-supported Reset; and 0 lane/world publication mismatches (17 narrative / 18 card checked publications per catalog). The player JSON reports IL2CPP, x64, High stripping and Burst enabled; `Assets/link.xml` has no kernel/gameplay `preserve="all"` entries.

Generated catalog SHA-256: narrative `5298bbc853b35424c7b7e8300b7c39afe6e27ec493027a22a47336a5cb567625`; cards `4761d745e8386cd5831005cd532ae26615c13f252ef064f45bb0888ca9e764a7`. `packages-lock.json` SHA-256 `9a243cfbdb35d1d901102cd68219448f8189ba9d9dc8c2ba86f32ada22dc7597` and remained unchanged by resolve. Player executable SHA-256 `aeaf13e291886fbd5a99b7dbd7c113b8ee13ed462a419a4d31a8ecbc3241ac70`; `toolchain/environment.txt` records it. Generated catalogs, package lock and .meta files did not change and therefore require no new commit.

## Fixes made on the Linux host

1. `W4GateFamily.cs`, `W4GateScenario.cs`: added missing composition/integration/ownership namespace imports and qualified the planning proposal type; supplied missing effective-provider/int32 helpers; fixed undefined case names, enum/string comparison and provider generation formatting. These were real Unity compiler errors not detectable on the authoring host.
2. `W4GateScenario.cs`: mount required provider before its consumer, seed and publish the explicit opted-in target before Conservative mode, and capture the isolated baseline after the providers exist. Otherwise consumer/opt-in observations falsely had no active subjects and the isolated comparison used an uninitialized baseline.
3. `W4GateScenario.cs`, `Gc013NarrativeHost.cs`: published spawn's derivation report covers pre-spawn targets, so the future target must be read from published binding rows. Added a real villager in GroveScope to materialize the matching `(VillagerRecipe, GroveScope)` rule before spawning the future villager. The new existing target joins the mode-switch assertions; no assertion was weakened.
4. `W4GateScenario.cs`: mount the unload installation once, with its two staged leases, then publish its actual unmount via `LifecycleController.Submit`. A bare `Unload` only disposes resources but leaves the installation Active in the lane; the unmount publishes the terminal Disposed state and matching world assembly.
5. `W4GateCardsHost.cs`: four lifecycle installations sharing one capability produced only winner-attributed binding rows; the consumer had zero rows even while Active. Give each real installation its own capability/schema and keep the same required service binding, scopes, selectors, values and registered reducer. Provider loss now retracts positive consumer rows in the same publication; return restores them.
6. `ProbeArguments.cs`, `ProbeRunner.cs`: wire `-probeW4Gate` into flag parsing, report identity (`W4-GATE`/`W4Gate`) and dispatch. Before the fix the player silently ran GC-001 Positive 5/5 for that flag, while the strict W4 probe verifier correctly rejected 84 missing identity/observation checks.
7. `tools/run_w4_gate.sh`, `tools/unity/build_probe.sh`: bound all Unity invocations, including the two nested Unity build invocations, with watchdogs. Full editor suites and IL2CPP build use 1800 seconds; player probes and focused tests use 600 seconds.
8. `inventory.json`, `inventory.md`: promote only `O-02` and `O-08` from Partial to Implemented+Evidenced, citing the distinguishing W4 and GC-013 Editor/player observations. Other partial requirements retain named unproven subclauses; P-053 and O-20..O-22 remain Not yet.

No tests were ignored, weakened, skipped or deleted; no expected values or normative design documents changed. The initial gate failed at Unity compilation; the next full attempt found 779/781 EditMode passing and exposed the integration defects above; a later full run reached the player and exposed the missing `-probeW4Gate` dispatch. The final full invocation passed. No required suite remains failing or blocked. Later V1 stress/fault, checkpoint/recovery, action-family and performance work is outside this Wave 4 gate and is not claimed here.
