# GC-015 HANDOFF — state retention, migration and explicit reset (Wave 4)

Branch `gc-015` (worktree `/Users/yangcao/wkspace/gc-wt/gc-015`), based on `main` (`b697ff6`, Wave 3 integrated).

**Status of every build/test command in this document: `NotRun (pending orchestrator build host)`.** This host has
no .NET SDK, no C# compiler, no Mono and no Unity, so nothing in this change set has been compiled, imported or
executed here. The only things that ran are interpreter-level host checks, recorded verbatim in
`artifacts/gc-015/static-checks.log`: `python3 tools/check_game_core_csharp.py` (279 files, `ok`),
`python3 tools/check_contract_surface_parity.py` (frozen snapshot is a strict subset of production, 5 additions
listed below), `python3 tools/validate_game_core_docs.py` (passed), `bash -n tools/run_w3_gate.sh`, a
repository-wide `.meta` GUID uniqueness scan (499 metas, zero duplicates) and a "every `.cs` under `Packages/` has a
`.meta`" scan. None of those is a build or a test result.

## 0. Commits on this branch

| Commit | Contents |
|---|---|
| `GC-015: add the state-policy declaration set, generated layouts and executors` | `Packages/com.gamecore.planning/Runtime/StatePolicies/` + metas. |
| `shared: route state dispositions through the GC-015 policy executor` | The additive contract/planner/publisher/scratch changes in §3. |
| `GC-015: add the Unity state-migration module` | `Packages/com.gamecore.unity.runtime/Runtime/StateMigration/` + metas. |
| `GC-015: prove the slot layouts, every policy and owner transfer without Unity` | `Packages/com.gamecore.planning/Tests/StatePolicies/` + metas. |
| `GC-015: run every slot policy in both running families` | The four GC-015 Unity files + metas + the test-asmdef reference. |
| `GC-015: record the handoff, evidence and build commands` | `artifacts/gc-015/`. |

Each commit is self-contained: the shared-surface changes are isolated in the `shared:` commit so a reviewer can
diff them apart from GC-015's own folders.

## 1. Summary

GC-015 turns the declared per-slot state policies of [00 P-020, P-025, P-029, P-032, P-033,
P-034](../../docs/game-core/00-core-protocols.md) into executable behaviour, over real storage, in both running
families:

* **Declaration + execution (engine-free).** `GameCore.Planning/StatePolicies/` owns the declared policy projection,
  the generated per-slot layouts, owner-transfer validation and the executor for `Preserve`, `PreserveDormant`,
  `RemoveDerived`, `Migrate`, `Reset` and `TransferTo`.
* **Unity side.** `GameCore.Unity.Runtime/Runtime/StateMigration/` builds one revision's policy surface from the
  mounted manifests and runs passes over copies of live state, reporting dormancy, transfers and the pass's
  temporary-storage high-water mark.
* **Shared kernel, additive only.** `StateDispositionKind` gains `RetainDormant`/`Reset`, `StateDisposition` gains
  the destination owner P-032 requires, `MigrationScratch` gains `TryStage`, `AssemblyPlanner.Build` takes an
  optional `StatePolicyPlan`, and `AssemblyPublisher` applies the new dispositions. No existing signature changed;
  the frozen W0 seam and its API snapshot are untouched.
* **Tests.** Pure policy/layout/transfer tests that run in dotnet and Unity EditMode, plus one labelled GC-015
  scenario per family that drives every policy through that family's real world.

## 2. Files created

### 2.1 Engine-free state policies — `Packages/com.gamecore.planning/Runtime/StatePolicies/`

| File | Contents |
|---|---|
| `SlotStatePolicyDeclarations.cs` | `StatePolicyIntent`, `StatePolicyRequest`, `IInitializationPolicyRegistry` + `InitializationPolicyRegistry`, `SlotStatePolicy`, `SlotAuthorityOptionsFactory`, `SlotStatePolicySet` (built from `StateSlotSpec`s or from mounted manifests, with the revision's declared owners). |
| `SlotLayoutGenerator.cs` | `GeneratedSlotLayout`, `SlotLayoutTable`, `SlotStorageKind`, `SlotLayoutGenerator.TryGenerate`: one physical component per declared layout key with one owner and one field mapping, shared components, split storage, and refusals for a second owner or a duplicated field (P-033). |
| `OwnerTransferValidator.cs` | `OwnerTransferResult`, `OwnerTransferValidator.Validate`: the declared last-support `TransferTo` loss and the explicit owner transfer that needs the declared owner-transfer policy, with the destination key, the source-owner/declaration agreement, and the availability of the destination owner (P-025, P-032, P-034). |
| `StatePolicyExecutor.cs` | `StatePolicyDecision`, `StagedSlotValue`, `StatePolicyPlan`, `StatePolicyExecutor.Execute` / `DefaultRequestFor`: one decision per live slot, the compatibility default, staged migrations and resets on bounded scratch, release-everything on refusal. |
| `DeclaredSlotMigrationRegistry.cs` | `ISlotMigrationRegistry` view over a revision's declared migration keys (GC-007's validator and GC-015's executor read the same declaration). |

### 2.2 Unity state migration — `Packages/com.gamecore.unity.runtime/Runtime/StateMigration/`

| File | Contents |
|---|---|
| `StatePolicyCatalog.cs` | `StatePolicyCatalog.Build(manifests, migrations, initialValues)` → policy set + layout table + registries + one validator verdict per declared slot; `DormantStateRegistry` / `DormantSlotRecord` for the "retained, no active writer" fact. |
| `StateMigrationPipeline.cs` | `Execute(targets, requests[, policyOverride])`: copies live slots, runs the executor, records transfers and dormant slots; `HasActiveWriter`, `ReadSlots`, `LastPlan`, `ScratchHighWaterBytes`. |

### 2.3 Pure tests — `Packages/com.gamecore.planning/Tests/StatePolicies/`

`StatePoliciesFixture.cs` (stable identities, declarations, migration/init registries) and `StatePolicyTests.cs`
(24 cases: layouts, policy-set validation, `Preserve`, `PreserveDormant`, `RemoveDerived`, migration success/miss/
refusal/budget, reset declared/undeclared/reasonless/unregistered, transfer validation and support-set behaviour).
These run both in plain dotnet (`dotnet/tests/GameCore.Planning.Tests` globs the folder) and in Unity EditMode
(`GameCore.Planning.Tests` compiles the same sources).

### 2.4 Family scenarios and tests (Unity)

| File | Contents |
|---|---|
| `unity/…/Tests/Narrative/NarrativeStatePolicyScenario.cs` | Builds the GC-010 narrative world with the real kernel modules and runs 12 named GC-015 observations. |
| `unity/…/Tests/Narrative/NarrativeStatePolicyAssertions.cs` | EditMode fixture asserting on those observations by value. |
| `unity/…/Tests/Cards/CardStatePolicyScenario.cs` | The same, over the GC-011 card market (12 observations). |
| `unity/…/Tests/Cards/CardStatePolicyAssertions.cs` | EditMode fixture for the card observations. |
| `*.cs.meta`, `*.meta` | Fresh GUIDs for every new asset and folder; repository scan shows no duplicate GUID. |

### 2.5 Evidence

`artifacts/gc-015/static-checks.log` (the host checks above), this file.

## 3. Files modified (shared surface, additive)

| File | Change | Why it is safe |
|---|---|---|
| `Packages/com.gamecore.contracts/Runtime/Manifest/ManifestEnums.cs` | `StateDispositionKind` += `RetainDormant = 4`, `Reset = 5`. | Additions only; existing values keep their numbers. |
| `Packages/com.gamecore.contracts/Runtime/Plans/PlanDeltas.cs` | `StateDisposition` gains `DestinationOwner` plus a five-argument constructor; the four-argument one delegates with a default owner. | Additive; every existing construction site still compiles and behaves identically. |
| `Packages/com.gamecore.planning/Runtime/Plans/MigrationScratch.cs` | `TryStage(slot, value, out code)`. | New method; nothing else changed. |
| `Packages/com.gamecore.planning/Runtime/Plans/AssemblyPlanner.cs` | Optional `StatePolicyPlan? policies = null` parameter; when supplied its dispositions/migrations replace the descriptor-derived ones and its staged values are re-staged into the plan's own scratch; step 3's unmount path drops a removal whose identity the same plan installs. Also a `TryPlanSlotDispositions` helper holding the previous logic verbatim. | Default `null` keeps the previous behaviour byte-for-byte for every existing caller (all W2/W3 tests). The removal filter only ever *drops* a removal that the same plan re-installs, which is P-033's "final removal only when nothing else still requires it". |
| `Packages/com.gamecore.unity.runtime/Runtime/Assembly/AssemblyPublisher.cs` | `ApplyStructuralAndState` applies `RetainDormant`, `Reset` and `Transfer`; `MarkSlotDormant` helper; `HasEffectiveChange` counts every non-`Retain` disposition. | The `Retain`/`Retract`/`Migrate` paths are unchanged; the new kinds can only appear when a plan carries them, which today means a GC-015 policy pass. |
| `unity/…/Tests/Narrative/GameCore.Narrative.Tests.asmdef` | Adds `GameCore.Derivation.Fixtures`. | Test-only reference; the slice's scenario already uses that assembly's value source but inside the fixtures assembly. |
| W0 reference seam (`tests/GameCore.ReferenceSeams/**`) | **Untouched.** | The seam is a frozen superset source; production additions do not require it, and regenerating its snapshot is not part of GC-015. |

## 4. Contract changes (required section)

1. **`StateDispositionKind` additions** (`RetainDormant = 4`, `Reset = 5`) — P-032 names three last-support
   outcomes and an explicit reset; the previous enum had no value for two of them, so a publication could not
   express "retained dormant" or "reset" at all. Additions only.
2. **`StateDisposition.DestinationOwner`** — P-032 requires a transfer to name an *available owner*, and P-034
   requires one owner per slot, so the target alone is not enough to apply or audit a transfer. Additive
   constructor; the old one delegates.
3. **No other public signature changed.** `AssemblyPlanner.Build`'s new parameter is optional and last;
   `MigrationScratch.TryStage` and the two Unity types are new.
4. **Untouched on purpose:** the W0 reference seam, its committed API snapshot, and every gameplay package
   (narrative/cards) — the family proofs live in new, clearly labelled test files.

## 5. Requirement → implementation → test mapping

| Requirement | Where implemented | Where proven |
|---|---|---|
| P-017 (support is a set of identities; a retraction removes exactly its own contribution) | `SupportSetRegistry` (GC-007, reused) + planner's removal filter | `StatePolicyTests.RemovingOneOfTwoSupportsKeepsTheOtherSupportAndTheValue`; narrative/cards runs |
| P-020 (reconfiguration never resets mutable state) | `StatePolicyIntent.Preserve`, `DecideReset`'s separation | `ReconfigurationPreservesTheRuntimeValueAndStagesNothing`; `gc015-…-tuning-keeps-non-default-counters` (both families) |
| P-025 (reparent/publish keeps target-local state; different identities need an explicit replacement mapping) | `OwnerTransferValidator` (explicit transfer) + planner/apply | `gc015-narrative-reparent-keeps-unrelated-state`, `gc015-cards-reparent-keeps-state` |
| P-029 (copy into bounded scratch, run pure fallible migrations before the first live write, failure leaves the old assembly) | `StatePolicyExecutor` staging + `MigrationScratch` + `AssemblyPublisher.TryMigrateOnScratch` | `AMigrationThatRefusesItsInputReleasesEveryReservationItMade`, `AnUnregisteredMigrationRejectsThePassAndLeavesNoResidue`; `gc015-…-failed-migration-keeps-old-assembly` |
| P-032 (per-slot policies; `Preserve` default; version change needs a registered `Migrate`; `Reset` needs an explicit reason; last-support `RemoveDerived`/`PreserveDormant`/`TransferTo` to a named available owner) | `SlotStatePolicyDeclarations`, `StatePolicyExecutor`, `OwnerTransferValidator`, `StatePolicyCatalog` | the whole `StatePolicyTests` suite (24 cases); `gc015-…-preserve-dormant`, `-remove-derived`, `-registered-migration`, `-explicit-reset`, `-ownership-transfer` in both families |
| P-033 (one physical owner and one field mapping per component, or split storage; shared components survive a lost support) | `SlotLayoutGenerator`, planner removal filter, publisher binding removal | `OneLayoutImplementingSeveralSlotsIsASharedComponentWithOneOwner`, `TwoOwnersOfOnePhysicalLayoutAreRejected`, `TwoSlotsClaimingOneFieldOfOneComponentAreRejected`, `ASharedComponentRejectsRemovalWhileItsRecipeStillRequiresIt`; both family surfaces (narrative: 2-slot conversation / 5-slot trail layouts; cards: 5 components with the seat/table field maps) |
| P-034 (one owner per authoritative domain; a live key that contradicts its declaration is a conflict) | `SlotStatePolicySet.TryFind`, `OwnerTransferValidator` | `ALiveKeyWhoseOwnerContradictsTheDeclarationIsAnOwnershipConflict`; card runtime case in `gc015-cards-registered-migration` |
| P-054 (registered, versioned, unique migration paths — reused, not duplicated) | `DeclaredSlotMigrationRegistry` + `MigrationRegistry` | `ADeclaredVersionChangeRunsOnTheCopyInsideTheScratch`, plus both families' registered-migration observations |
| TEST-005 (composition/precedence: retraction preserves unrelated state and the surviving support) | planner removal filter (`RemoveInstalledIdentities`) | `StatePolicyTests.RemovingOneOfTwoSupports…`, `ASharedComponentRejectsRemoval…` |
| TEST-009 (prepared plans and atomic publication visibility: stale plans reject, canceled plans release staged resources) | existing GC-008 path + `StatePolicyPlan.Refuse` releasing every reservation | `AnUnregisteredMigrationRejectsThePassAndLeavesNoResidue`, `ScratchBeyondTheConfiguredBudgetRefusesThePass`; both `-failed-migration-keeps-old-assembly` observations |
| TEST-010 (state preservation, migration and reset; non-default state; target- and provider-owned records) | this task's core | `StatePolicyTests` + all twelve observations per family |
| TEST-013 (authority, direct writes, requests) | unchanged GC-007 ownership; GC-015 only adds slot dispositions | both families' worlds keep running only through the declared owners; every assertion reads the live rows |
| TEST-017 (checkpoint/schema evolution: explicit migration deterministic, incompatible data rejects) | `DeclaredSlotMigrationRegistry`, `StatePolicyExecutor.DecideMigration` (`UnsupportedVersion` for state ahead of the declaration, `MigrationRequired` for a missing one) | `AnUnregisteredMigrationRejectsThePass…`, `gc015-…-failed-migration-keeps-old-assembly` |
| GC-015 DoD: "each slot policy runs in Unity and both genres retain unrelated state through live changes; migration bounds and ownership transfer are observable" | the two family scenarios | `gc015-narrative-*` and `gc015-cards-*` observations (12 each), asserted by `NarrativeStatePolicyAssertions` / `CardStatePolicyAssertions` |

## 6. Build and test commands for the Linux build host

All of these are `NotRun (pending orchestrator build host)`.

### 6.1 Plain dotnet (engine-free half)

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/tests/GameCore.Planning.Tests/GameCore.Planning.Tests.csproj -c Release \
  --filter "FullyQualifiedName~StatePolicies|FullyQualifiedName~StatePolicy"
# the whole solution, to prove nothing else regressed:
dotnet test dotnet/GameCore.sln -c Release
# the frozen-surface comparison is unaffected but worth rerunning after the contract additions:
dotnet run --project dotnet/tools/GameCore.ApiSnapshot -c Release -- \
  --assembly dotnet/src/GameCore.ReferenceSeams/bin/Release/netstandard2.1/GameCore.ReferenceSeams.dll \
  --output /tmp/gc015-seam.api.txt --namespace GameCore.Contracts
```

### 6.2 Unity EditMode (both families' GC-015 suites)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode \
  -testFilter "GameCore.Narrative.Tests.NarrativeStatePolicyAssertions;GameCore.Cards.Tests.CardStatePolicyAssertions" \
  -testResults artifacts/gc-015/unity-statepolicies.xml -logFile artifacts/gc-015/unity-statepolicies.log
```

### 6.3 The family slices' own suites (unchanged, to prove no regression)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity tools/run_w3_gate.sh   # as recorded by the W3 gate
```

### 6.4 Probe runs (unchanged)

```sh
PROBE_RUNS=5 tools/unity/probe_runs.sh
```

GC-015 adds no probe mode: the state-policy proofs are EditMode world tests (the DoD asks for Unity, not for
IL2CPP; IL2CPP composition is GC-012's gate).

## 7. Known gaps, assumptions and doc ambiguities

1. **No manifest field for reset permission (doc/contract gap).** P-032 says a reset "requires an explicit
   manifest-supported proposal field and reason", but the 05 `StateSlotSpec` table has no reset field: only
   `InitPolicy`, `ConfigChangePolicy`, `VersionChangePolicy`, `LastSupport` and `TransferPolicy`. GC-007 already
   models the permission as a *generated* slot option (`SlotAuthorityOptions.ResetPermitted` + reason), so GC-015
   keeps that reading: `SlotAuthorityOptionsFactory.ForLastSupport` never permits a reset, which makes an
   undeclared reset reject deterministically (proven in both families), and a *declared* reset is expressed by a
   generated `SlotAuthorityOptions.Resettable(reason, …)` declaration that this task builds where it must be
   declared today (the family test fixtures). **Recommendation for the doc owner:** add the reset field to 05's
   `StateSlotSpec` (and let the GC-003 compiler emit it) in a later task, at which point the fixture-declared sets
   in the two GC-015 scenarios can be replaced by catalog data with no design change.
2. **Owner-transfer authorization has two readings in 00.** P-032 requires an owner-transfer policy *and* lists
   `TransferTo` among the last-support outcomes; GC-007's `SlotPolicyValidator.ValidateOwnerTransfer` reads an
   explicit owner transfer as legal only when the declared *last-support* policy is `TransferTo`. GC-015 keeps
   GC-007's reading (the 00-over-05 rule makes one implementation preferable to two) and additionally requires the
   destination owner to be an owner this revision declares — that is what "a named available owner" can mean at
   validation time, since the live ECS storage is never consulted during planning. Cards declare a single owner
   (`cards.owner.table`), so the card transfer observation declares an audit owner as a GC-015 test-declared slot
   owner rather than inventing a second card owner (which would change that package).
3. **The card and narrative catalogs declare no `TransferTo` last-support slot and no reset permission**, so
   "each slot policy" in a family world is proven as: the *declared* policies executed over declared slots
   (`Preserve`, `PreserveDormant`, `RemoveDerived`, `Migrate`) plus the *declared-by-this-test* variants of the
   same production executor for `Reset` and `TransferTo`, applied by the same publisher inside the same world. The
   rows involved are real ECS rows of real targets; only the declaration that authorizes them is GC-015's.
4. **GC-008's derivation→row bridge refuses a slot with more than one support** (`DerivationProposalBridge`, "a
   binding row holds one value per (capability, output slot)"). P-017's "support is a set of IDs" is therefore
   modelled where the kernel can hold it (GC-007's `SupportSetRegistry`, exercised by the pure suite) and at the
   state level by P-033's shared component: removing one support never removes a component another support or the
   base recipe still requires. Closing the multi-contribution *binding row* gap is GC-008/GC-019 work, not GC-015.
5. **`AssemblyPlanner` gained an optional parameter and a removal filter** — both are shared-file edits outside
   GC-015's folders. They are in a separate `shared:` commit, additive, and default-preserving; the W2/W3 suites
   must be rerun on the build host to confirm (they are part of the commands above).
6. **Probe/player evidence is out of scope here.** GC-015's Definition of Done asks for Unity; IL2CPP and player
   coverage for both families is GC-012's gate, and the W4 exit gate runs both families in IL2CPP.
7. **No automatic migration inference, no engine-asset conversion** (GC-015's stated non-goals): every migration
   and initialization value in this change set is declared, registered and keyed.
