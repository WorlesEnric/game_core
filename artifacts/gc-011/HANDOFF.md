# GC-011 HANDOFF — the card-game Automatic vertical slice (Unity wiring, player probe, EditMode tests, tooling, evidence)

**Status of every executable check in this document: `NotRun (pending orchestrator build host)`.** The host this
wiring was authored on has no .NET SDK, no C# compiler, no Mono and no Unity, so no file in this change set has been
compiled, imported or executed here. The only things that ran are interpreter-level host checks, recorded verbatim in
§7: `python3 tools/check_game_core_csharp.py` (220 files, `ok`), `bash -n tools/unity/run_cards_probe.sh` (clean) and an
independent `python3` recomputation of the committed card catalog's two recorded hashes (both match). None of them is
a build or a test result.

Gate sentence implemented (verbatim, `docs/game-core/09-implementation-guide.md`, Wave 3 — Two genuinely different
running compositions):

> Run narrative and cards with the same kernel. Show zero idle command steps, automatic existing/future targets, a
> narrative state change and a card domain transfer.

Task id: `GC-011` (traceability: `docs/game-core/traceability.json`, wave 3, depends on GC-006/GC-007/GC-008/GC-009;
requirements P-001, P-003, P-013, P-015, P-024, P-034, P-036, P-037, P-042, P-043, P-044, P-056, P-059; suites
TEST-004, TEST-013, TEST-021).

## 1. Summary

This change set is the **qualification wiring** of the GC-011 card slice. The card packages themselves
(`Packages/com.gamecore.rules.cards`, `Packages/com.gamecore.gameplay.cards`) and the committed generated card catalog
(`unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs`) were authored by sibling
tasks and are treated as frozen inputs here: no file inside either package, and no file in `GeneratedCards/`, was
modified by this task.

What this task wired:

| Piece | Purpose |
|---|---|
| `unity/GameCore.Validation/Packages/manifest.json` | the two card packages are declared dependencies and testables of the qualification project |
| `Runtime/CardsScenarioHost.cs` | supplies the two catalogs the scenario runs against: the committed generated catalog (GC-011 compiler output, validated by the production `ImmutableCatalog`) and the fixture's hand-written generated-style table, and combines both runs' observations with the `fixture:` step prefix |
| `Runtime/ProbeCards.cs` | runs the scenario inside the built IL2CPP player and reports every observation plus the two facts digests into the same structured JSON result as every other probe |
| `Runtime/ProbeArguments.cs`, `Runtime/ProbeRunner.cs` | the `-probeCards` mode, its `Cards` task id and its `CompletePositive` exit-code discipline; every existing mode is untouched |
| `Runtime/GameCore.Validation.ProbeHost.asmdef` | the host assembly now references `GameCore.Gameplay.Cards`, `GameCore.Gameplay.Cards.Fixtures` and `GameCore.Validation.GeneratedCards` |
| `Tests/Cards/GameCore.Cards.Tests.asmdef`, `Tests/Cards/CardsIntegrationTests.cs` | the Unity EditMode half: one scenario execution per catalog in `[OneTimeSetUp]`, then one case per observation group asserting the observed facts by value on both catalogs |
| `tools/unity/run_cards_probe.sh` | launches the already-built player in `-probeCards` mode, repeats it `PROBE_RUNS` times through `tools/unity/probe_runs.sh`, and validates the structured result |
| `tools/check_game_core_csharp.py` | the host-side static checks now cover both card packages (the rules package as engine-free) |
| `artifacts/gc-011/cards-trace.json` | the canonical trace document: packages, assemblies, the thirteen observations, the requirement-to-evidence mapping, the seeded and expected states, and the exact host commands |
| `artifacts/gc-011/HANDOFF.md` | this note |

One process runs the whole slice twice: the same scenario over the committed generated catalog and over the fixture
catalog, each run creating and disposing its own real `Unity.Entities.World` (the scenario's last observation tears its
world down), so the two runs share no host state and `CardsScenarioHost.RunBoth` adds no cross-run caching.

## 2. Files created

Every path below was enumerated by listing the touched trees on this host (not from memory). `(new)` marks this task's
creations; the card-package and generated-catalog files are listed because the trace document and the coverage table
refer to them, and they were authored by sibling tasks.

Unity qualification project (`unity/GameCore.Validation`):

| Path | State |
|---|---|
| `Assets/GameCore.Validation/Runtime/CardsScenarioHost.cs` | new (this task) |
| `Assets/GameCore.Validation/Runtime/ProbeCards.cs` | new (this task) |
| `Assets/GameCore.Validation/Tests/Cards/GameCore.Cards.Tests.asmdef` | new (this task) |
| `Assets/GameCore.Validation/Tests/Cards/CardsIntegrationTests.cs` | new (this task) |

The card packages and their gc-011 inputs (authored by sibling tasks, frozen here):

| Path | State |
|---|---|
| `Packages/com.gamecore.rules.cards/package.json`, `README.md` | sibling task |
| `Packages/com.gamecore.rules.cards/Runtime/CardIdentity.cs`, `CardVocabulary.cs`, `CardSetRules.cs`, `CardWriteSets.cs`, `CardCommands.cs`, `CardRegistrations.cs`, `GameCore.Rules.Cards.asmdef` | sibling task |
| `Packages/com.gamecore.rules.cards/Tests/CardIdentityTests.cs`, `CardRulesTests.cs`, `GameCore.Rules.Cards.Tests.asmdef` | sibling task |
| `Packages/com.gamecore.gameplay.cards/package.json` | sibling task |
| `Packages/com.gamecore.gameplay.cards/Runtime/CardTableKeys.cs`, `CardTableComponents.cs`, `CardResultPayload.cs`, `CardTableDeclarations.cs`, `CardTableSystems.cs`, `CardTableRegistration.cs`, `CardMarketComposition.cs`, `GameCore.Gameplay.Cards.asmdef` | sibling task |
| `Packages/com.gamecore.gameplay.cards/Fixtures/Runtime/CardCatalogTable.cs`, `CardTableFixture.cs`, `CardMarketScenario.cs`, `GameCore.Gameplay.Cards.Fixtures.asmdef` | sibling task |
| `unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs`, `GameCore.Validation.GeneratedCards.asmdef` | sibling task |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Editor/CardCatalogGenerator.cs` | sibling task |
| `unity/GameCore.Validation/Catalogs/CardCatalog.catalog.json` | sibling task |
| `dotnet/src/GameCore.Rules.Cards/GameCore.Rules.Cards.csproj` | sibling task |
| `dotnet/tests/GameCore.Rules.Cards.Tests/GameCore.Rules.Cards.Tests.csproj` | sibling task |
| `dotnet/GameCore.sln`, `dotnet/README.md` | modified by the rules-package sibling task (both card dotnet projects are in the solution with Debug/Release `Build.0` entries, and both have a README row) |

Tooling and evidence:

| Path | State |
|---|---|
| `tools/unity/run_cards_probe.sh` | new (this task) |
| `artifacts/gc-011/cards-trace.json` | new (this task) |
| `artifacts/gc-011/HANDOFF.md` | new (this task) |

No `.meta` file was authored for any new file: Unity creates them on import, exactly as it does for the two card
packages, `GeneratedCards/` and `Catalogs/`, which also ship without `.meta` files. Every assembly reference in this
change set is by assembly name (never by GUID), so an import-time `.meta` is sufficient.

## 3. Files modified by this task (each minimal)

| Path | Change | Why |
|---|---|---|
| `unity/GameCore.Validation/Packages/manifest.json` | `+ "com.gamecore.gameplay.cards": "file:../../../Packages/com.gamecore.gameplay.cards"`, `+ "com.gamecore.rules.cards": "file:../../../Packages/com.gamecore.rules.cards"` in `dependencies` (both in the file's existing sorted position), and both names in `testables` (also in sorted position) | the qualification project is the only consumer that must resolve the two local packages; the existing project is not a full-repo manifest |
| `Assets/GameCore.Validation/Runtime/ProbeArguments.cs` | `+ CardsArgumentName = "-probeCards"`, `+ bool cards` positional ctor parameter (`missingRegistration, worldDispatch, w1Gate, w2Gate, cards, resultPath`), `+ Cards` property (doc-commented), `+ cards` parse branch, `+ Cards` in `IsProbeInvocation` | a new probe mode; no existing argument, default or exit code changed |
| `Assets/GameCore.Validation/Runtime/ProbeRunner.cs` | `+ arguments.Cards` branch in the mode chain (`new ProbeReport("Cards", …, "GC-011")`), `+ arguments.Cards` dispatch branch (`ProbeCards.Run(report); report.CompletePositive();`), and the mode-selection comment extended by one clause | the same player runs every mode; the new branch sits after the W2 gate branch and before the GC-001 default, so every existing mode keeps its branch and task id |
| `Assets/GameCore.Validation/Runtime/GameCore.Validation.ProbeHost.asmdef` | `+ "GameCore.Gameplay.Cards"`, `+ "GameCore.Gameplay.Cards.Fixtures"` (after `GameCore.Contracts`), `+ "GameCore.Validation.GeneratedCards"` (after `GameCore.Validation.Generated`) | `CardsScenarioHost` and `ProbeCards` name `CardTableKeys`, `CardMarketScenario`, `CardTableFixture`, `CardScenarioResult`, `CardFacts`, `CardStep` and the generated `CardCatalog`; `GameCore.Validation.GeneratedCards` declares `autoReferenced: false`, so an explicit reference is the only way in |
| `tools/check_game_core_csharp.py` | `+ "Packages/com.gamecore.rules.cards"`, `+ "Packages/com.gamecore.gameplay.cards"` in `TARGETS`; `+ ROOT / "Packages/com.gamecore.rules.cards"` in `engine_free` | the card rules package holds no Unity type at all, so it joins the engine-free set; the gameplay package legitimately references Unity types and is covered by the balance and forbidden-construct checks only (the same style as the GC-006 and W2 entries) |

## 4. Kernel changes

**There are none.** This change set contains no kernel change at all: no file under
`Packages/com.gamecore.contracts`, `Packages/com.gamecore.composition`, `Packages/com.gamecore.derivation`,
`Packages/com.gamecore.planning`, `Packages/com.gamecore.unity.runtime`, `Packages/com.gamecore.unity.adapters` or
`Packages/com.gamecore.content.compiler` was added, modified or deleted by this task, and no file outside the four
touched trees (the Unity qualification project under `unity/GameCore.Validation`, the new script under `tools/unity`,
`tools/check_game_core_csharp.py`, and `artifacts/gc-011`) was modified. The two card packages and the generated card
catalog are gameplay, not kernel: they were authored on this branch and were edited after the wiring task (see §8),
all inside `Packages/com.gamecore.rules.cards`, `Packages/com.gamecore.gameplay.cards` and the gc-011 inputs. The card slice is exercised
against the existing kernel exactly as it is; that is the P-059 claim this slice supports (the card composition runs
without a single kernel edit).

The only pre-existing file touched outside the Unity project is the host-side checker `tools/check_game_core_csharp.py`,
which is tooling, not kernel.

## 5. Exact commands for the Linux build host

Everything runs from the repository root. Nothing below has been run.

There is deliberately **no** single-command GC-011 driver in this change set (the brief enumerates no such file, and
`tools/run_w2_gate.sh` is outside the files this task was allowed to touch). Run the steps in order; a GC-011 driver
modelled on `tools/run_w2_gate.sh` — with `ARTIFACTS=<repo>/artifacts/gc-011` — is the natural follow-up for the
orchestrator.

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity     # pinned 6000.0.75f1 baseline
```

### 5.1 dotnet half

```sh
# the whole Unity-free solution (including GameCore.Rules.Cards and its tests, which the rules-package task added)
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-011/trx

# the card rules package alone (same sources as Packages/com.gamecore.rules.cards/Runtime)
dotnet build dotnet/src/GameCore.Rules.Cards/GameCore.Rules.Cards.csproj -c Release
dotnet test  dotnet/tests/GameCore.Rules.Cards.Tests/GameCore.Rules.Cards.Tests.csproj -c Release \
  --logger trx --results-directory artifacts/gc-011/trx
```

`dotnet test dotnet/GameCore.sln` is where the **committed generated card catalog is proven**: the committed
`dotnet/tests/GameCore.Content.Compiler.Tests/CardCatalogTests.cs` runs `CommittedCardCatalogMatchesAFreshGeneration`,
`CommittedCardCatalogPassesItsOwnVerification`, `CommittedCardCatalogFingerprintMatchesItsDeclarations` and
`CommittedCardCatalogKeyLiteralsMatchTheirDerivation`. That test file is the only proof that the committed
`CardCatalog.g.cs` is byte-identical with what a fresh compiler run emits; this host could not establish it, because
the committed file was authored by hand (see §8).

### 5.2 Unity half

```sh
# package resolution (refreshes packages-lock.json with the two card packages)
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation \
  -logFile artifacts/gc-011/unity/resolve.log

# the GC-011 EditMode assembly alone
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Cards.Tests \
  -testResults artifacts/gc-011/unity/cards-editmode.xml \
  -logFile artifacts/gc-011/unity/cards-editmode.log

# the whole EditMode suite (every testable package plus GameCore.Cards.Tests, GameCore.W1Gate.Tests and GameCore.W2Gate.Tests)
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode \
  -testResults artifacts/gc-011/unity/editmode-results.xml -logFile artifacts/gc-011/unity/editmode.log

# the whole PlayMode suite
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform PlayMode \
  -testResults artifacts/gc-011/unity/playmode-results.xml -logFile artifacts/gc-011/unity/playmode.log
```

Do not add `-quit` to a test-run command (`docs/game-core/04-unity-integration.md` section 10).

### 5.3 Player build and the probes

```sh
UNITY="$UNITY" tools/unity/build_probe.sh

# the card slice, PROBE_RUNS (default 5) times, in the same IL2CPP binary as every other probe
PROBE_RUNS=5 PROBE_PLAYER="$PWD/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64" \
  UNITY_PROJECT="$PWD/unity/GameCore.Validation" ARTIFACTS="$PWD/artifacts/gc-011/toolchain" \
  tools/unity/run_cards_probe.sh

# the pre-existing probes must stay green on the same revision: the new mode must not disturb their parsing or exit codes
PROBE_RUNS=5 tools/unity/run_probe.sh both
PROBE_RUNS=5 tools/unity/run_world_probe.sh
PROBE_RUNS=5 tools/unity/run_w1_gate_probe.sh
PROBE_RUNS=5 tools/unity/run_w2_gate_probe.sh
```

`run_cards_probe.sh` requires, in the result JSON: `"task": "GC-011"`, `"mode": "Cards"`, `"result": "Pass"`, absence
of any `"status": "Fail"`, all twelve scenario observations twice (plain and `fixture:`-prefixed), the two facts
digests (`cards-generated-catalog-facts`, `cards-fixture-catalog-facts`), and a `catalogFingerprint=<hex>` in the
generated run's facts digest equal to the `CardCatalog.CatalogFingerprint` literal the script greps out of
`GeneratedCards/CardCatalog.g.cs` at run time. Exit code 0 means every run was clean; a signal death in any run fails
the whole script.

### 5.4 Host-side checks

```sh
python3 tools/check_game_core_csharp.py
bash -n tools/unity/run_cards_probe.sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

## 6. Requirement and test coverage mapping

The requirement column is `docs/game-core/traceability.json`'s own GC-011 row. "Observation" names are the scenario's
twelve step names, which the probe script and the EditMode tests both assert literally; the EditMode case column names
the Unity case that asserts the same facts by value.

| Requirement | Covered by observation | EditMode case | What is asserted (facts, by value) | Result |
|---|---|---|---|---|
| P-001 genre independence | `cards-world-and-market-seeded`, `cards-ownership-and-schedule-compiled` | `TheCompiledPlanIsFourStagesWithOnePlaybackPointAndTheDeclaredEdge`, `OwnershipHasOneOwnerPerDeclaredDomainAndEverySlotPolicyValidated` | one table runtime owns several entities across scopes (no 1:1 plugin/scope/entity mapping); every declared domain names a logical owner; no genre schema in the card package surface | NotRun (pending orchestrator build host) |
| P-003 three kinds of change | `cards-mount-reaches-existing-seats`, `cards-one-command-commits-both-sides`, `cards-future-seat-inherits-modifier` | `TheMountsReachEveryEligibleExistingSeat`, `OneCommandCommitsBothSides`, `AFutureSeatInheritsTheModifierOnFirstVisibility` | a contribution installs a derived row and awards no points (`SeatAScoreBefore == SeededSeatScore`); the committed score/hand are authoritative; the rule-library unmount does not touch state | NotRun (pending orchestrator build host) |
| P-013 mode semantics (Automatic) | `cards-world-and-market-seeded`, `cards-mount-reaches-existing-seats`, `cards-future-seat-inherits-modifier` | `TheMountsReachEveryEligibleExistingSeat`, `AFutureSeatInheritsTheModifierOnFirstVisibility` | `SeatABonusValue == SeatBBonusValue == CardVocabulary.FestivalBonus`, `SeatCBonusValue == QuietBonus`, `SpawnedSeatBonusValue == FestivalBonus`, no per-instance import | NotRun (pending orchestrator build host) |
| P-015 eligibility | `cards-mount-reaches-existing-seats` | `TheMountsReachEveryEligibleExistingSeat` | `SeatABonusRowCount == 1`, `ScoreboardRowCount == 0`, `PracticeSeatBonusRowCount == 0`, reducer and predicate registrations reported | NotRun (pending orchestrator build host) |
| P-024 spawn | `cards-future-seat-inherits-modifier` | `AFutureSeatInheritsTheModifierOnFirstVisibility` | `SpawnedSeatBonusValue == FestivalBonus`, `SpawnedSeatStampPublished`, `SpawnedSeatInView`, `SpawnedSeatStampEpoch == WorldEpochAfterSpawn`, `CountersJoinedAfterSpawn` | NotRun (pending orchestrator build host) |
| P-034 state authority | `cards-ownership-and-schedule-compiled`, `cards-one-command-commits-both-sides` | `OwnershipHasOneOwnerPerDeclaredDomainAndEverySlotPolicyValidated`, `OneCommandCommitsBothSides` | `SingleOwnerPerDomain`, `OwnershipDomainCount == 5`, `ValidatedSlotPolicyCount >= 5`, `WriterCount == CompiledStageCount == 4`, `CommittedWrites == SetCardCount + 2` | NotRun (pending orchestrator build host) |
| P-036 temporal models | `cards-idle-before-command-commits-zero-steps`, `cards-idle-world-performs-zero-steps`, `cards-world-and-market-seeded` | `AnIdleWorldPerformsZeroSteps` | `IdleStepsCommitted == 0`, `IdleDispatchRuns == 0`, `PendingDemandAfterIdle == 0`, `IdleFrames > 0`, world created CommandDriven | NotRun (pending orchestrator build host) |
| P-037 step admission | `cards-one-command-commits-both-sides`, `cards-duplicate-command-transfers-once`, `cards-rejected-settlement-changes-nothing` | `ADuplicateCommandTransfersOnce`, `ARejectedSettlementChangesNothing` | `StepsAfterCommand == 1`, `StepsAfterDuplicate == 2`, `DuplicateRejections >= 1`, `PendingDemandAfterDuplicate == 0`, the rejected request still advanced one step and produced `RejectedDecisionCount >= 1` | NotRun (pending orchestrator build host) |
| P-042 commands and requests | `cards-one-command-commits-both-sides`, `cards-rejected-settlement-changes-nothing`, `cards-duplicate-command-transfers-once` | `OneCommandCommitsBothSides`, `ARejectedSettlementChangesNothing` | admission and gameplay outcome are separate facts (`CommandAdmitted` with `CommittedEventMatchesLiveState`; `RejectedCommandAdmitted` with an untouched state); the duplicate returns a recorded result | NotRun (pending orchestrator build host) |
| P-043 buffers and bounded work | `cards-ownership-and-schedule-compiled`, `cards-one-command-commits-both-sides` | `TheCompiledPlanIsFourStagesWithOnePlaybackPointAndTheDeclaredEdge` | `BufferEdgeValidateBeforeCommit`, `InputStageIndex < CommitStageIndex`, one playback point, `ScheduleEdgeCount >= 1`, `CommittedWrites == 5 <= MaxWriteEntries == 12` | NotRun (pending orchestrator build host) |
| P-044 commit and domain transactions | `cards-one-command-commits-both-sides`, `cards-transfer-commits-both-sides`, `cards-rejected-settlement-changes-nothing` | `OneCommandCommitsBothSides`, `ATransferCommitsBothSides` | `CommittedEventCount == 1` with delta 12 and three cards matching live state; `TransferGainedCard == 1 && TransferLostCard == 0` with giver −1 and receiver +1; a rejection writes nothing | NotRun (pending orchestrator build host) |
| P-056 extension points | `cards-catalog-and-declarations` | `TheGeneratedRunReportsTheCommittedCatalogFingerprint`, `TheScenarioRecordsEveryObservationTwiceAndNoDiagnostic` | the factory lookup resolves a `PluginFactory`, every generated-style declaration is accepted, key derivation holds, an unregistered key is a `MissingDependency` miss (never constructed) | NotRun (pending orchestrator build host) |
| P-059 genre validation before freeze | all twelve observations, on both catalogs | every case in `GameCore.Cards.Tests` | the card composition runs on the same kernel binaries with no kernel edit (see §4) and no mandatory genre schema | NotRun (pending orchestrator build host) |
| TEST-004 automatic propagation (card-rule variant) | `cards-mount-reaches-existing-seats`, `cards-future-seat-inherits-modifier` | `TheMountsReachEveryEligibleExistingSeat`, `AFutureSeatInheritsTheModifierOnFirstVisibility` | eligible existing and future targets receive the row; the ineligible and the isolated target do not; the card slice has no Transform or GameObject | NotRun (pending orchestrator build host) |
| TEST-013 authority, direct writes, requests and buffers (card transfer) | `cards-ownership-and-schedule-compiled`, `cards-one-command-commits-both-sides`, `cards-duplicate-command-transfers-once`, `cards-rejected-settlement-changes-nothing`, `cards-transfer-commits-both-sides` | the four corresponding cases | direct owner writes under one owner, bounded drafts, duplicate request handling, a rejected settlement, and conservation across a transfer | NotRun (pending orchestrator build host) |
| TEST-021 genre neutrality | all twelve observations plus the generated catalog's registration surface | every case in `GameCore.Cards.Tests` | the card template registers no compulsory combat, actor, action, vitality, physics or animation state/stage; the kernel assemblies it uses are the same ones the narrative and W2 gates use | NotRun (pending orchestrator build host) |

Beyond the required list, the same cases also assert the scenario's world shape (`SeatCount == 4`,
`LiveTargetCount == SeatCount + 2`) and the ownership/slot-policy surface, because those are the facts the propagation
claims rest on.

## 7. Host checks that did run here

Verbatim, after the owner pass (all interpreter-level; none is a build or a test result):

```sh
$ python3 tools/check_game_core_csharp.py
checked 220 C# file(s)
ok
$ bash -n tools/unity/run_cards_probe.sh
$ echo $?
0
```

The generated card catalog's two recorded hashes were also recomputed independently from the committed file's own
tables on this host (a throwaway `python3` script that re-implements `CatalogEmitter.FilePrefixHash` and
`CatalogFingerprint.Compute` over the emitted registrations, not a call into the compiler):

```
CatalogFileHash  recorded 175cad2795ed8aac02ac3e70fa8676e3a1e5a3655c44cf25f248fd0a1b9b3148
                 recomputed 175cad2795ed8aac02ac3e70fa8676e3a1e5a3655c44cf25f248fd0a1b9b3148 -> MATCH
CatalogFingerprint recorded/recomputed e74c3be5264c100622f6fe122bf8d2e9711ae6944a183bd230205a662ac742a6 -> MATCH
(8 factory registrations, 1 schema, 0 declared features)
```

That is an independent recomputation, but it is still not the real gate: the committed
`dotnet/tests/GameCore.Content.Compiler.Tests/CardCatalogTests.cs` regenerates the file with the production compiler
and compares it byte for byte on the Linux host, which is what proves the committed catalog is fresh.

### What was verified on this host

```text
$ bash -n tools/unity/run_cards_probe.sh
bash -n tools/unity/run_cards_probe.sh: OK

$ python3 tools/check_game_core_csharp.py
checked 220 C# file(s)
ok
```

`check_game_core_csharp.py` covers every C# file this task created (they live under
`unity/GameCore.Validation/Assets`, which is a checker target) plus both card packages; it checks the C# 9 forbidden
constructs (file-scoped namespaces, global usings, records, `init` accessors, required members, static abstract
members, raw string literals, list patterns), `#nullable enable`, brace balance, the non-void-without-return shape and
engine types in engine-free assemblies. The observation-name contract was checked separately by extracting every
`new CardStep("…")` name from `CardMarketScenario.cs` and comparing it member by member with the list in
`cards-trace.json`, in `run_cards_probe.sh` and in `CardsIntegrationTests.cs`: all three match the scenario's own
twelve names, in the scenario's own order.

Nothing else ran. In particular no build, no test, no formatter, no linter and no git command was executed, and no
claim in this document is a run result.

## 8. Post-wiring corrections (owner pass, after the wiring task)

The wiring task treated the card packages as frozen inputs, and they were **not** frozen: after it finished, the slice
owner (this branch) reviewed them, and an independent reviewer verified every kernel member, argument order, enum
member and nullability against its defining declaration. Five defects were found and fixed, the scenario gained the
observation that puts the declared batch envelope on the real pipeline, and the dead surface was pruned.

### Compile blockers (found by the independent review; two were introduced by an owner edit)

| File | Defect | Fix |
|---|---|---|
| `Packages/com.gamecore.gameplay.cards/Runtime/CardTableComponents.cs` | `CardDecisionRow` declared `Kind` twice (CS0102) because an owner edit replaced the `WriteCount` slot instead of adding beside it | one `Kind` plus a distinct `WriteCount` |
| `CardTableComponents.cs` | `WriteCount` was then read (`TryWrite`, `RecordWrites`, the commit loop) but declared nowhere, and a settled set had no declared bound | `public byte WriteCount;` with `W0..W5` and `TrySetWrite`/`TryWrite` enforcing it |
| `Fixtures/Runtime/CardMarketScenario.cs` | `facts.CountersJoinedAfterSetup = report.CountersJoined;` named a `report` that did not exist in that scope, and `PipelineDescriptorReport` has no `CountersJoined` member at all | recomputed the join from the real authorities, `AssemblyPublisher.MatchesPublishedAssembly(lane.Committed.Revision, lane.Committed.Epoch, publisher.PublishedRevision, host.CurrentEpoch)`, exactly as the sibling step does |

### Runtime defects on the happy path

| File | Defect | Fix |
|---|---|---|
| `CardTableComponents.cs` | `CardPayloadCodec.BatchBytes` was 72 while `WriteBatch`/`TryReadBatch` address four 12-byte candidate slots from offset 28, so the last slot wrote and read 4 bytes past the array (`IndexOutOfRangeException` through the registered `CardBatchReader`) | `BatchBytes = 76` (`24 + 4 + 4 * 12`); the batch lane's byte capacity is computed from it, so both move together |
| `CardTableSystems.cs` | the input stage bounded its draft with `draft.Length >= draft.Capacity`, i.e. the ECS allocation capacity instead of the declared bound, so the declared bound was never enforced and input could be refused arbitrarily | bounded by `CardTableKeys.DraftCapacity`, the bound the lanes are declared with and the bound `DrainOwnerBatch` makes meaningful |

### Scenario defects found by the owner

| Defect | Fix |
|---|---|
| `PublishEdits` submitted and drained composition edits without publishing the world's assembly for them, leaving the lane one publication ahead, so every later `TryAdoptLanePublication` would have failed `StalePlan` (P-006) | `PublishEdit` publishes the world's assembly for every edit and requires `CountersJoined`; the spawn step uses `ApplyEditAndReport`, so it reads the unmount publication's own derivation instead of deriving a second time for a consumed number |
| `SeedMarket` never installed the table entity's card storage, so the first `GetBuffer<CardMarketRow>` would have thrown | calls `CardTableAccess.InstallTableStorage` right after binding the table entity |
| the practice seat was bound to the ordinal the future `seat-d` claims | the new `CardTableKeys.PracticeOrdinal` (4) |
| the market and deck rows reused `SeatCard(...)` identities, so the market held the same cards as seat C, contradicting 07 s2.2's "one authoritative assignment per card" | the disjoint `MarketCard(int)` (900..) and `DeckCard(int)` (800..) sources |
| `facts.MountInstalledRows` was set from a constant and then compared against the same constant | accumulated from the two mount publications' own `DerivedAssemblyReport.InstalledRows` and compared against `CardTableKeys.MountInstalledRowCount` |

### The declared batch envelope now really runs (observation 10)

The brief requires a command batch, and the slice declares one (P-037: "a domain can declare an atomic batch envelope
as one command"). It was declared but never exercised, so the observation
`cards-batch-envelope-resolves-one-winner` was added: two seats bid for one card the holder has, the envelope travels
`cards.route.batch`, is decoded by `CardBatchReader`, is resolved by the pure `CardSetRules.TryResolveContest`, and
exactly one bid consumes the card. That single observation exercises `CardPayloadCodec.WriteBatch`, the batch lane's
declared row and byte capacity, the registered batch reader, the validate stage's contest branch, the contest
resolution rule and the holder-to-winner transfer, none of which any other observation touched.

### Dead code removed

Removed because nothing read them, and this repo's convention is to delete weightless code rather than keep a
speculative public surface: `CardTableKeys.ProviderOf`, `CardResultPayload.NamesCard`,
`CardTableRegistration`'s recipe-catalog `Describe`, `CardMarketComposition.SeatScope`,
`CardTableFixture.FestivalRule`/`QuietRule`, `CardTableState.IsSeated`, and the never-assigned duplicate fact
`CardFacts.SpawnedSeatInPublishedView`. Three members were kept and given a real reader instead of deleted, because
the declared data is load-bearing: `CardTableKeys.HandRowsField` is now a declared field of the seat slot's layout,
`CardTableKeys.DeckCapacity` bounds the seeded deck loop, and `CardPayloadCodec.WriteBatch` encodes the batch envelope
observation 10 submits. Five unused `using` directives and four unused asmdef references
(`Unity.Collections`/`Unity.Jobs` in both card asmdefs) were removed.

## 9. Known gaps, assumptions and doc ambiguities

Recorded as facts verified by reading the code, unless marked as an assumption.

1. **The additive `cards.set-bonus` slot can only be published into a binding row when exactly ONE contribution
   supports it.** `DerivedCompositionProposal.Build` in
   `Packages/com.gamecore.unity.runtime/Runtime/Integration/DerivationProposalBridge.cs` refuses a multi-supporter
   slot. Consequence: the nested-festival `+3` composition of `docs/game-core/07-reference-compositions.md` §2.1
   ("A nested festival with `+3` produces `+5` where both providers match") is **not** exercised end to end by this
   slice; the multi-contribution arithmetic is covered by the pure reducer path
   (`CardSetRules.TryReduceBonus`, and the card rules package's own tests) instead. The slice mounts exactly one
   scoring provider per league, which is why each binding row has one supporter.
2. **The card slice declares no schema migration**, so every state slot is schema version 1: the scenario builds its
   `OwnershipSchedulePipeline` with `new MigrationRegistry(new List<ISlotMigration>())`, and there is no
   `ISlotMigration` implementation anywhere in the card packages. The migrated-slot assertions of the W2 gate
   therefore have no card counterpart in this slice.
3. **`CommandEnvelope.ExpectedDomainVersion` is never read by the plane.** The card scenario's `SubmitCommand`
   passes `null` for it, so the table-version guard is enforced by the domain rules and the commit stage's own
   recheck instead (`CardSetRules.TryBuildSettlement` compares the expected table version, and the commit stage
   rechecks it before writing). The rejection observation proves the guard works, not the envelope field.
4. **The assembled generated catalog was authored by hand to be byte-identical with a fresh compiler run.** This host
   could not run the compiler. The proof belongs to the build host: `dotnet/tests/GameCore.Content.Compiler.Tests/
   CardCatalogTests.cs` (4 cases, committed by the codegen task) regenerates the catalog from
   `unity/GameCore.Validation/Catalogs/CardCatalog.catalog.json` and compares bytes, verification, fingerprint and key
   literals. Until that test has run, `CardCatalog.g.cs` is a hand-written file that claims to be generated output.
   Regenerate it only with
   `-executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog`; any byte edit invalidates
   `CatalogFileHash` (`175cad2795ed8aac02ac3e70fa8676e3a1e5a3655c44cf25f248fd0a1b9b3148`).
5. **Signature differences from the brief (quoted as found).**
   - `ProbeReport`'s constructor is
     `public ProbeReport(string mode, string declaredUnityVersion, string declaredTarget, string task = "GC-001")`
     — the task id is an optional fourth parameter, not a second overload. The `Cards` mode chain passes `"GC-011"`.
   - `CardStep`'s constructor is `public CardStep(string name, bool passed, string detail)`; `CardScenarioResult`'s is
     `public CardScenarioResult(IReadOnlyList<CardStep> steps, CardFacts facts)`.
   - There is **no `CardScenarioTable` type and no `AbsentPluginFactoryKey` member** anywhere in the card packages
     (searched). The brief's own fallback is what was implemented: `ProbeKeys.AbsentFixturePluginKey` for the absent
     factory key, and `public static readonly PluginTypeId AbsentCardsPluginType =
     CardTableKeys.PluginType("cards.absent-plugin");` declared in `CardsScenarioHost`.
   - The brief's asmdef instructions are already satisfied by the `GameCore.W2Gate.Tests.asmdef` copy:
     `GameCore.Derivation` and `GameCore.Validation.ProbeHost` are both already present in its reference list (nothing
     had to be added for them). One reference beyond the enumerated list was added —
     `GameCore.Validation.GeneratedCards` — because the EditMode case must assert the generated run's facts against
     `CardCatalog.CatalogFingerprint` **by value** (no other file exposes that literal, and hardcoding it in a test
     would be a value nothing proves).
   - `CardFacts.MountInstalledRows` is assigned from a cumulative `installedRows` counter fed by
     `report.InstalledRows` of **every** publication in the setup, and the mount step's own pass condition compares it
     with `CardTableKeys.MountInstalledRowCount == 3`. The equality holds because the eight scope creations, the table
     mount and the rule-library mount derive no target change at all (neither the table runtime nor the rule library
     declares a derivation rule), so the only rows any publication installs are the festival provider's two and the
     quiet provider's one.
   - `cards-idle-world-performs-zero-steps` performs the teardown itself (it calls `TearDownSafely()` after its
     assertions), so `cards-teardown-settles-and-disposes` is recorded even when the idle observation fails. The
     teardown case therefore asserts the teardown facts only; the idle case owns the idle facts.
6. **Assumption:** the fixture run's `CatalogFingerprint` differs from the generated run's, and the tests assert that
   difference. Both catalogs register the same key literals (the generated one and `CardCatalogTable` agree on
   `cards.factory.card-table-plugin`, the four system keys, the reducer, the predicate and the configuration schema),
   so if the fingerprint algorithm is a pure function of the registration set the two could in principle coincide;
   the scenario's own first observation compares each catalog against the fingerprint it was declared with, so a
   coincidence would be visible there rather than hidden. If the build host shows the two fingerprints equal, that
   single assertion (`Is.Not.EqualTo`) is the one to revisit, and the case's comment says why.
7. **Assumption:** `OwnershipDomainCount == 5` in the EditMode tests is derived from
   `CardTableDeclarations`'s five `Slot(...)` declarations (table, seat, command-draft, decision-draft, output), each
   with its own `SchemaRef` domain. The scenario itself only asserts `SlotPolicies.Count >= 5` with every policy
   succeeded, so if the ownership validator folds two of those domains into one map entry, this assertion is the one
   to relax to the scenario's own bound.
8. **Assumption:** `TableVersionAfterRejection == TableVersionAfterCommit + 1U`. The scenario never exposes the
   table version after the duplicate observation's committed transfer, but it does assert that the duplicate's first
   admission executed (its settlement count reaches 2) and that a transfer advances the version by exactly one
   (`TableVersionAfterTransfer == table.TableVersion + 1U`). The rejection case derives the value from those two
   facts instead of hardcoding 3, and additionally pins the absolute values (`StepsAfterCommand == 1`,
   `StepsAfterDuplicate == 2`) so a step-count regression cannot hide.
9. **Doc ambiguity:** `07-reference-compositions.md` §2.4 lists the mount's before-state as "Seats A/B have bonus 0
   and existing totals 4/8" while §2.3's example and the fixture both seed every seat at 4. The fixture follows §2.3
   (`CardTableKeys.SeededSeatScore == 4`, "its score is 4"), and the tests assert 4, not 8.
10. **Doc ambiguity:** the brief names the compiled edge a "validate→commit buffer edge", and the scenario exposes it
    as `BufferEdgeValidateBeforeCommit`; the code computes it as `schedule.HasEdge(inputStageIndex, commitStageIndex)`
    over the declared `DecisionBuffer` (whose declared producer is the validate system). The tests assert the fact,
    not a re-derivation, so either reading passes.
11. **Not covered by this slice** (out of the brief's scope, tracked here so it is not mistaken for done): the mode
    switch directions (`Automatic`→`Conservative` and back), reparenting a seat between leagues, provider
    reconfiguration (`+2`→`+4`), provider unmount retraction of an existing seat's derived row, and the exclusive
    `cards.draw-policy` conflict are declared or reachable in the packages but not exercised by the scenario. The
    atomic batch envelope route **is** exercised (observation 10, `cards-batch-envelope-resolves-one-winner`). The
    narrative half of the Wave 3 gate sentence is GC-010's; this slice covers the cards half plus the shared "zero idle
    command steps, automatic existing/future targets, a card domain transfer" clauses.
