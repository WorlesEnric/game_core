# W3-GATE Linux build report

Date: 2026-09-25. Branch: `w3-gate`. Source initially reset to `origin/w3-gate` at `cfa19ec`; fixes: `b1da9b2`, regenerated lock: `6911410`. Final verification was run with both fixes in the working tree; the lock was subsequently committed unchanged.

## Host and toolchain

- Ubuntu 24.04 x86-64, Linux `7.0.0-31-generic`, Intel i7-12700KF.
- .NET SDK `8.0.425`, runtime `8.0.31`, MSBuild `17.11.48`, VSTest `17.11.1`.
- Unity Editor `6000.0.75f1`, Linux Standalone x64 IL2CPP, High managed stripping; Unity build reported `Succeeded`, zero errors, one warning. The player reported IL2CPP and 64-bit Linux at runtime.
- Invocation environment: `DOTNET_ROOT=$HOME/.dotnet`, `PATH=$HOME/.dotnet:$PATH`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity`, `DOTNET=$HOME/.dotnet/dotnet`, `PROBE_RUNS=5`.

## Commands and results

From the repository root, first ran `git fetch origin && git checkout w3-gate && git reset --hard origin/w3-gate`. Then ran the complete `tools/run_w3_gate.sh` twice with the environment above. The first run failed at the W3 probe (5/5 attempts) after all earlier build, Unity, and player steps passed; see **Fixes**. The second run exited zero and performed these steps in order:

1. `dotnet build dotnet/GameCore.sln -c Release`: **Pass**, zero warnings/errors.
2. `dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/w3-gate/trx`: **Pass**, 655/655 across 11 test projects; zero failed/skipped. Final-run TRX files have timestamps `13_00_26`/`13_00_27` (older `12_53_24`/`12_53_25` files preserve the first run).
3. `Unity -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile artifacts/w3-gate/unity/resolve.log`: **Pass**; package lock regenerated with `com.gamecore.gameplay.cards` and `com.gamecore.rules.cards`.
4. `Unity -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testResults artifacts/w3-gate/unity/editmode-results.xml -logFile artifacts/w3-gate/unity/editmode.log`: **Pass**, 602/602, zero failed/skipped.
5. Same Unity test command with `-testPlatform PlayMode`, `playmode-results.xml`, `playmode.log`: **Pass**, 6/6, zero failed/skipped. Neither test command used `-quit`.
6. `Unity -batchmode -nographics -quit -projectPath unity/GameCore.Validation -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog -logFile artifacts/w3-gate/unity/card-codegen.log`; `UNITY="$UNITY" ARTIFACTS=artifacts/w3-gate/toolchain tools/unity/build_probe.sh`: **Pass**, both catalogs regenerated and `git diff --exit-code` against both tracked generated files returned zero. StandaloneLinux64 IL2CPP player built; `artifacts/w3-gate/toolchain/build.log` reports result `Succeeded`, totalErrors=0, totalWarnings=1.
7. Seven player probe scripts (`tools/unity/run_probe.sh both`, `run_world_probe.sh`, `run_w1_gate_probe.sh`, `run_w2_gate_probe.sh`, `run_narrative_probe.sh`, `run_cards_probe.sh`, `run_w3_gate_probe.sh`) with `PROBE_RUNS=5`, `PROBE_PLAYER=unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64`, `UNITY_PROJECT=unity/GameCore.Validation`, `ARTIFACTS=artifacts/w3-gate/toolchain`: **Pass**. The first script executes separate positive and expected-negative cases: eight result families, five independent player invocations each, 40/40 clean and no crash. Expected-negative result is `ExpectedNegative`, not `Pass`.
8. `python3 tools/validate_game_core_docs.py --self-test` and `python3 tools/validate_game_core_docs.py`: **Pass**; nine self-test fixtures and 14 Markdown documents checked.

### .NET test projects, final run

| Suite | Pass | Fail | Skipped |
| --- | ---: | ---: | ---: |
| GameCore.ProtocolFixtures.Tests | 10 | 0 | 0 |
| GameCore.Rules.Narrative.Tests | 118 | 0 | 0 |
| GameCore.Rules.Cards.Tests | 26 | 0 | 0 |
| GameCore.Content.Compiler.Tests | 44 | 0 | 0 |
| GameCore.ReferenceSeams.Tests | 21 | 0 | 0 |
| GameCore.Composition.Tests | 106 | 0 | 0 |
| GameCore.Execution.Tests | 58 | 0 | 0 |
| GameCore.Derivation.Tests | 88 | 0 | 0 |
| GameCore.ProtocolFixtures.Production.Tests | 10 | 0 | 0 |
| GameCore.Contracts.Tests | 45 | 0 | 0 |
| GameCore.Planning.Tests | 129 | 0 | 0 |
| **Total** | **655** | **0** | **0** |

### Unity test assemblies, final run

| EditMode assembly | Pass | Fail | Skipped |
| --- | ---: | ---: | ---: |
| GameCore.Cards.Tests | 15 | 0 | 0 |
| GameCore.Composition.Tests | 106 | 0 | 0 |
| GameCore.Derivation.Tests | 88 | 0 | 0 |
| GameCore.Narrative.Tests | 18 | 0 | 0 |
| GameCore.Planning.Tests | 129 | 0 | 0 |
| GameCore.Rules.Cards.Tests | 26 | 0 | 0 |
| GameCore.Rules.Narrative.Tests | 118 | 0 | 0 |
| GameCore.Unity.Assembly.Tests | 10 | 0 | 0 |
| GameCore.Unity.Messages.Tests | 10 | 0 | 0 |
| GameCore.Unity.Runtime.Tests | 53 | 0 | 0 |
| GameCore.W1Gate.Tests | 8 | 0 | 0 |
| GameCore.W2Gate.Tests | 11 | 0 | 0 |
| GameCore.W3Gate.Tests | 10 | 0 | 0 |
| **EditMode total** | **602** | **0** | **0** |

PlayMode: `GameCore.Unity.Adapters.Tests` **6 passed, 0 failed, 0 skipped**.

### Player probe results, final run

| Result file prefix under `artifacts/w3-gate/toolchain/` | Result per run | Clean runs | Observations per run |
| --- | --- | ---: | ---: |
| `probe-result.json` (GC-001 positive) | Pass | 5/5 | 8 |
| `probe-negative.json` (GC-001 negative) | ExpectedNegative | 5/5 | 3 |
| `probe-world-dispatch.json` (GC-005) | Pass | 5/5 | 7 |
| `probe-w1-gate.json` | Pass | 5/5 | 23 |
| `probe-w2-gate.json` | Pass | 5/5 | 24 |
| `probe-narrative.json` | Pass | 5/5 | 24 |
| `probe-cards.json` | Pass | 5/5 | 28 |
| `probe-w3-gate.json` | Pass | 5/5 | 11 |

The W3 probe has zero failed observations. In **one IL2CPP player process**, its two generated-catalog compositions complete 11 narrative and 13 card checks with distinct world sessions and registry returned to the same baseline after each. Eight kernel assemblies inspected, zero forbidden gameplay references, zero duplicate assemblies, zero inspection failures; all six loaded gameplay/rules assemblies reference the kernel. Narrative: 600 idle frames/zero steps, three existing derived targets, two future rows, gate `0→1` through committed fact version 2 and two committed events. Cards: eight idle frames/zero steps, existing seat bonuses `2,2,1`, future bonus 2, giver `4→3`, receiver `3→4`, moved card gained once and absent from giver. The Editor `.asmdef` audit ran in the W3 EditMode assembly. Separate narrative/card probes and EditMode suites exercise each family's generated and fixture catalogs.

## Fixes and first-run failure

- `Packages/com.gamecore.gameplay.narrative/Fixtures/Runtime/GameCore.Gameplay.Narrative.Fixtures.asmdef`: removed stale `GameCore.Execution` reference. No Unity asmdef provides that name; the referenced runtime types are in the already-listed `GameCore.Unity.Runtime` assembly. No test expectation changed.
- `Packages/com.gamecore.gameplay.narrative/Fixtures/Runtime/NarrativeScenario.cs`: `CreateWorldAndTargets` read `registryBeforeCreate` but never assigned it to `facts.RegistryBeforeCreate`. In the first player run, actual registry baseline was 1, after creation 2, after teardown 1; the facts incorrectly reported before-create 0, making `w3-two-worlds-on-one-kernel-image` fail 5/5 despite both compositions passing. Copy the actual measured baseline to facts immediately when sampled. Final W3 probe and EditMode gate pass without weakening any assertion.
- `unity/GameCore.Validation/Packages/packages-lock.json`: committed the two card-package entries produced by real Unity resolution, not manually synthesized.

No implementation contract or test expected values changed. There is no remaining failing or blocked gate step. Unity logged a licensing-client access-token refresh warning while Editor operations, tests, build and player runs completed; the build itself reported one warning (Unity Entities FastEquality extra-type warning). Headless test/player execution was performed; no visual scene inspection was performed.

## Evidence and hashes

- Unity XML and Editor logs: `artifacts/w3-gate/unity/`; final and first-run .NET TRX: `artifacts/w3-gate/trx/`; player JSON/logs, build/codegen logs and environment: `artifacts/w3-gate/toolchain/`. Probe repeats use `.run2` through `.run5` suffixes. Every evidence file is below 2 MB.
- Lock SHA-256: `9a243cfbdb35d1d901102cd68219448f8189ba9d9dc8c2ba86f32ada22dc7597`.
- Probe catalog SHA-256: `2f0e85d0d7c96b0b05404a0b5c7cc1639e625fe44a13e5d83e5f3c2d2406c005`; card catalog SHA-256: `5841acb2404e0c6a3369c1e4e42609e5439fad7c722bde5ba1fffc3cfdf3a43c`.
