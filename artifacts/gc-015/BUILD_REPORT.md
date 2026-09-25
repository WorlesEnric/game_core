# GC-015 Linux build and test report

## Host and tools

- Branch: `gc-015`, fetched and reset to `origin/gc-015` at `aec7c42` before work. Final source commits below.
- Linux x86_64, kernel `7.0.0-31-generic`; .NET SDK `8.0.425`; Unity Editor `6000.0.75f1` (batchmode, nographics); GCC `13.3.0`, Clang `18.1.3`, GNU ld `2.42`. Toolchain details and player SHA-256: `gate/toolchain/environment.txt`.
- Pure assemblies compiled for `netstandard2.1` with the repository's C# language settings; Unity package imports and tests compiled by the pinned Editor. No pure-assembly UnityEngine reference was added.

## Commands and final outcomes

Commands ran from the repository root with `DOTNET_ROOT=$HOME/.dotnet`, `PATH=$HOME/.dotnet:$PATH`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, and `UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity` (absolute executable path).

| Command | Result | Evidence |
|---|---|---|
| `dotnet build dotnet/GameCore.sln -c Release` | Pass; zero warnings/errors | W3 gate process output |
| `dotnet test dotnet/tests/GameCore.Planning.Tests/GameCore.Planning.Tests.csproj -c Release --filter 'FullyQualifiedName~StatePolicies\|FullyQualifiedName~StatePolicy'` | Pass 28/28 | Focused command output |
| `dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-015/gate/trx` | Pass 683/683 across 11 suites | `gate/trx/*.trx` (final run only) |
| `dotnet run --project dotnet/tools/GameCore.ApiSnapshot -c Release -- --assembly dotnet/src/GameCore.ReferenceSeams/bin/Release/netstandard2.1/GameCore.ReferenceSeams.dll --output /tmp/gc015-seam.api.txt --namespace GameCore.Contracts` | Pass; frozen seam unchanged | Generated `/tmp/gc015-seam.api.txt`; production snapshot `production-contracts.api.txt` |
| `$UNITY -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile artifacts/gc-015/gate/unity/resolve.log` | Pass; lockfile and existing metas unchanged | `gate/unity/resolve.log`, tracked `unity/GameCore.Validation/Packages/packages-lock.json` |
| `$UNITY -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testFilter 'GameCore.Narrative.Tests.NarrativeStatePolicyAssertions;GameCore.Cards.Tests.CardStatePolicyAssertions' -testResults <absolute>/artifacts/gc-015/unity-statepolicies.xml -logFile <absolute>/artifacts/gc-015/unity-statepolicies.log` | Pass 10/10: narrative 5/5, cards 5/5 | `unity-statepolicies.xml`, `unity-statepolicies.log` |
| `UNITY=$UNITY ARTIFACTS=<absolute>/artifacts/gc-015/gate PROBE_RUNS=5 tools/run_w3_gate.sh` | Pass: solution build/tests, Unity resolve, 640 EditMode, 6 PlayMode, both catalog generators, StandaloneLinux64 IL2CPP High-stripping build, all player probes, documentation validator | `gate/unity/*.xml`, `gate/unity/*.log`, `gate/toolchain/*`, `gate/trx/*.trx` |
| `python3 tools/validate_game_core_docs.py --self-test` | Pass 9 self-test fixtures; gate also passed 14-document validation | `gate/validator-self-test.log`, `gate/validator.log` |

The gate's Unity EditMode breakdown (all Pass, zero skipped/failures): Cards 20, Composition 106, Derivation 88, Narrative 23, Planning 157, Rules.Cards 26, Rules.Narrative 118, Unity.Assembly 10, Unity.Messages 10, Unity.Runtime 53, W1Gate 8, W2Gate 11, W3Gate 10. Total **640/640**. PlayMode Unity.Adapters **6/6**. Dotnet: Composition 106, Content.Compiler 44, Contracts 45, Derivation 88, Execution 58, Planning 157, ProtocolFixtures.Production 10, ProtocolFixtures 10, ReferenceSeams 21, Rules.Cards 26, Rules.Narrative 118; total **683/683**.

Player probe modes: GC-001 positive, GC-001 expected-negative, GC-005 world dispatch, W1Gate, W2Gate, narrative, cards, W3Gate. Each mode ran in five separate player processes; **40/40 clean expected results**, including the expected-negative outcome. Canonical JSON and `.run2`–`.run5` plus corresponding player logs retained under `gate/toolchain/`. The player itself is generated under the ignored Unity project `Builds/Linux64/`; its reproducible hash is in `gate/toolchain/environment.txt`. No GC-015-specific IL2CPP state-policy probe exists: the family state-policy assertions ran in Unity EditMode, and the unchanged eight IL2CPP probe modes ran in the gate.

## Fixes and reasons

1. `StatePolicyExecutor`: assign success-path `out` diagnostic/detail on migrate/reset; C# definite-assignment errors prevented compilation.
2. Pure policy fixtures/tests: correct `FieldOwnership` identity type, typos in slot/policy symbols and registry argument, initialize support records before retracting; use the real `SupportSetDelta.EndedActiveLife` contract. The same-target transfer projects a default `TransferTo` as specified by publisher fallback; assert the resolved decision target rather than changing the contract. Remove an assertion pinned to diagnostic wording, not behavior. No expected state value changed.
3. `OwnerTransferValidator`: actually check `IsAvailableOwner` before accepting the destination; otherwise a transfer to an undeclared owner succeeded despite P-032.
4. `ContractTests`: explicitly allow only the documented additive GC-015 `StateDisposition` fields/constructors and `StateDispositionKind` members. Frozen W0 API snapshot remains untouched; compatibility test remains strict for all other drift.
5. Both Unity family scenarios: fix incorrect namespaces/type identities/API arguments and route the policy plan through the real derivation, planner and publisher **without first consuming the same lane publication in `DerivedAssemblyPipeline.Derive`**. Handle an unchanged binding delta with effective state dispositions, disposed-install toggles, and failure/catch-up boundaries; find the specific slot decision rather than assuming canonical list index zero. Preserve all 12 named observations per family. Narrative reset checks the actual successfully migrated pre-reset value, not a stale pre-migration fixture value; this is required by the live preceding migration, not a weakened expectation.
6. `StateDisposition.ToVersion` and publisher: apply staged migrated/reset values at the policy's declared destination version rather than the static world descriptor's older schema version. Legacy dispositions retain the old descriptor fallback. This was a real runtime bug exposed by the card v1→v2 migration in Unity; the narrow additive contract change and API-compatibility allowance are recorded in the production API snapshot.

Initial dotnet build failed on compiler errors; initial complete dotnet run had one frozen-surface allowance failure. Initial Unity resolve failed on scenario compiler errors; first full EditMode run was 632/640 (eight GC-015 assertion failures); focused runs exposed consumed lane publications and disposed-install toggles. These were fixed and the **final full gate passed**. No test was skipped, ignored or deleted. No generated lock or `.meta` change was required; both were already tracked and unchanged after Unity resolve. Design gap remains as recorded in `HANDOFF.md` §7: catalog `StateSlotSpec` lacks manifest reset-permission metadata; fixture-declared reset/transfer authorization is used in the two family worlds. This does not block the documented GC-015 test-declared policy execution, but production manifests cannot yet express that permission directly.

## Logical commits

- `35b88ce` — compile policy fixtures and validate transfer owners.
- `438270b` — publish migrated slot schema versions from policy decisions.
- `a8ebd12` — publish card policy passes through one lane revision.
- `f5ef536` — publish narrative policy passes without consuming stale epochs.
- This report and executed evidence are committed separately.
