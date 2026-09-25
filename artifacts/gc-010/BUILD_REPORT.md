# GC-010 Linux build report

## Host and toolchain

- Host: Linux x86_64, kernel `7.0.0-31-generic`; Intel i7-12700KF. GCC 13.3.0, Clang 18.1.3, GNU ld 2.42.
- .NET SDK `8.0.425` (`$HOME/.dotnet/dotnet`); Unity `6000.0.75f1` (`26349cd2a5c8`), Linux Standalone IL2CPP. Unity player build reports `IL2CPP`, `High` stripping, zero build errors and warnings. The Unity API compatibility enum logs `NET_Standard_2_0` for `ApiCompatibilityLevel.NET_Standard` (the Editor's label); pure projects target `netstandard2.1`, and the player compiled and executed them.
- Source revision initially `552f4e0` from `origin/gc-010`; fixes and evidence committed on this worktree. No Unity executable was missing.

## Exact commands and results

From the repository root, after `git fetch origin && git checkout gc-010 && git reset --hard origin/gc-010`:

```sh
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet PROBE_RUNS=5 tools/run_gc010_gate.sh
python3 tools/check_game_core_csharp.py
bash -n tools/run_gc010_gate.sh
```

The final gate completed with exit 0 after the fixes. It ran these commands in order (absolute paths substituted by the script):

```sh
$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release
$HOME/.dotnet/dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-010/trx
$UNITY -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile artifacts/gc-010/unity/resolve.log
$UNITY -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testResults artifacts/gc-010/unity/editmode-results.xml -logFile artifacts/gc-010/unity/editmode.log
$UNITY -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform PlayMode -testResults artifacts/gc-010/unity/playmode-results.xml -logFile artifacts/gc-010/unity/playmode.log
UNITY=$UNITY ARTIFACTS=artifacts/gc-010/toolchain tools/unity/build_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-010/toolchain tools/unity/run_probe.sh both
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-010/toolchain tools/unity/run_world_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-010/toolchain tools/unity/run_w1_gate_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-010/toolchain tools/unity/run_w2_gate_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-010/toolchain tools/unity/run_narrative_probe.sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

The EditMode and PlayMode XML paths above are absolute in the final gate; the earlier relative test-result arguments caused Unity to resolve under the Unity project. The final gate wrote directly under `artifacts/gc-010/unity/`.

| Suite | Pass | Fail | Skipped | Result |
|---|---:|---:|---:|---|
| .NET `GameCore.Contracts.Tests` | 45 | 0 | 0 | Pass |
| .NET `GameCore.Execution.Tests` | 58 | 0 | 0 | Pass |
| .NET `GameCore.ReferenceSeams.Tests` | 21 | 0 | 0 | Pass |
| .NET `GameCore.ProtocolFixtures.Tests` | 10 | 0 | 0 | Pass |
| .NET `GameCore.ProtocolFixtures.Production.Tests` | 10 | 0 | 0 | Pass |
| .NET `GameCore.Rules.Narrative.Tests` | 118 | 0 | 0 | Pass |
| .NET `GameCore.Content.Compiler.Tests` | 40 | 0 | 0 | Pass |
| .NET `GameCore.Planning.Tests` | 129 | 0 | 0 | Pass |
| .NET `GameCore.Composition.Tests` | 105 | 0 | 0 | Pass |
| .NET `GameCore.Derivation.Tests` | 88 | 0 | 0 | Pass |
| **.NET solution** | **624** | **0** | **0** | **Pass** |
| Unity EditMode: composition / derivation / narrative / planning / narrative rules | 105 / 88 / 18 / 129 / 118 | 0 | 0 | Pass |
| Unity EditMode: assembly / messages / runtime / W1Gate / W2Gate | 10 / 10 / 53 / 8 / 11 | 0 | 0 | Pass |
| **Unity EditMode** | **550** | **0** | **0** | **Pass** |
| Unity PlayMode: adapters | 6 | 0 | 0 | Pass |

IL2CPP build: **Pass** (`StandaloneLinux64`, High stripping, `result=Succeeded`, zero Unity build errors/warnings). The committed generated catalog was byte-identical after code generation. The Unity-generated `packages-lock.json` and 62 package/declared-tree `.meta` assets are committed.

| Player mode | Executions | Exit/result | Step results | Verdict |
|---|---:|---|---:|---|
| Positive registration | 5/5 | 0 / Pass | 8 Pass | Pass |
| Missing registration | 5/5 | 3 / ExpectedNegative | 2 Pass, 1 expected-negative step | Pass |
| World dispatch | 5/5 | 0 / Pass | 7 Pass | Pass |
| W1Gate | 5/5 | 0 / Pass | 23 Pass | Pass |
| W2Gate | 5/5 | 0 / Pass | 24 Pass | Pass |
| Narrative (`-probeNarrative`) | 5/5 | 0 / Pass | 24 Pass (11 per catalog, 2 fact digests) | Pass |

The narrative player reports: chapter-one derived rows `mara=2`, `gate=1`, `encounter=1`, isolated museum `0`, incompatible crowd `0`, sibling sailor before chapter two `0`; future spawn `2` rows at binding value `1`, epoch `4`; typed choice commits fact `1@v2`, gate `0→1`, two events in one step; duplicate returns committed with zero additional steps; 600 idle frames (ten seconds at 60 Hz) commit zero steps, zero extra dispatches and zero extra images; name/component audits inspect 96 content names, 141 inventory names and one component type with zero forbidden names. These were observed in both generated and fixture catalogs under the same W2 pipeline. No actor/vitality/physics state or stage is registered by the narrative package.

Evidence: `trx/*.trx`, `unity/{resolve,editmode,playmode}.log`, `unity/{editmode-results,playmode-results}.xml`, `toolchain/{environment.txt,codegen.log,build.log,probe-*.json*,player-*.log*}`, `dotnet-{build,test}.log`, `validator*.log`, and `narrative-trace.json`. The final gate and separate host check passed; the docs validator itself says it does not run Unity, which is correct for that validator, not a statement about the gate.

## Fixes and review

1. Unity compilation initially failed on missing fixture asmdef references and namespaces (`GameCore.Derivation`, `GameCore.Execution`, `ScheduleAdaptation`, clock types), plus an unqualified `PipelineEntries()` call. Added only required references/imports and invoked `facts.PipelineEntries()`.
2. The generated-catalog host supplied two provider declarations but the scenario mounts three. Added the forward provider declaration with the same generated factory/schema.
3. The committed-trace test found no checkout above Unity's installation directory. It now searches from the working directory first, then assembly directory, without introducing a `UnityEngine` reference into the pure rules tests.
4. **Generic design gap:** GC-006 derivation correctly excluded museum and sibling scopes, but its proposal bridge lost that eligibility: `AssemblyPlanner` reapplied every proposed capability to every live target sharing the recipe, bypassing isolation and inflating rows. `ProposedCapability` now optionally carries exact eligible target IDs from derivation; the planner filters both new and existing-row candidates by those IDs and includes them in the proposal hash. Existing recipe-only callers remain unchanged. The observed failure was 8 instead of 4 first-chapter rows, museum/sailor improperly bound and no gate transition; after correction chapter-one rows are 4 and gate state commits correctly. Spawn rules remain derived from the winner's recipe and scope; future eligible village target received the binding automatically.
5. The canonical trace expected the two event schemas in causal staging order. The kernel commits events in canonical `(request, target, schema)` order; the gate target sorted before the dialogue target. Corrected the expected schema sequence in `NarrativeScenarioTrace` and its byte-for-byte committed JSON. This is the one changed expected value; the event store's canonical ordering, not a changed gameplay result, proves the old value wrong (00 P-008/P-045). No test was weakened or skipped.
6. The supplied GC-010 gate originally filtered out all previous EditMode suites and skipped world/W1/W2 player probes. It now runs every EditMode testable and every existing probe as requested; the final complete gate passed.
7. The three `kernel:` changes were reviewed: the lane seed's declared tree supplies a world definition without an unpublishable scope-only revision, `ScopeRegistry` rejects a second root, and `Root` returns the actual depth-zero root rather than numerically smallest ID. They use generic scope/identity records, no narrative schema, actor, quest, or Transform dependency. `GameCore.Composition.Tests` 105/105 in both .NET and EditMode; W1Gate and W2Gate tests and player probes stayed green.

## Limitations / status

No current build, test, player probe or documentation validator is failing. This is the GC-010 narrative initial path, not the future GC-011 card family or the 1,000-cycle teardown/cross-template proof assigned elsewhere. No Unity scene visual/UI inspection was performed; the EditMode actual-world scenario and IL2CPP headless player are the exercised surfaces.
