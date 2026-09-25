# GC-013 — incremental invalidation, reparenting and live mode changes

**Every executable check named below is `NotRun (pending orchestrator build host)`.** This
host (macOS, no Unity, no .NET SDK, no Mono) cannot compile or run any of it. What *was* run
here is interpreter-level only, recorded verbatim in `static-checks.log`:

- `python3 tools/check_game_core_csharp.py` → `checked 284 C# file(s)` / `ok` (brace/paren/
  bracket balance, forbidden-construct scan, engine-free rule for the pure assemblies).
- `python3 tools/validate_game_core_docs.py` → the documentation validator, unchanged by
  this task (no doc or traceability edit was made).
- A GUID-uniqueness scan over every `*.meta` in the repository before the new `.meta` files
  were written (483 existing GUIDs, none reused).

No build, test, probe, IL2CPP player or Unity import was performed. Nothing in this change
set has been compiled.

## 1. Summary

GC-013 makes automatic composition scale with a change instead of a world, and makes live
mode changes, reparents and boundary edits report their consequence before they publish.

**`GameCore.Derivation`** (six new source folders' worth, twelve new files):

| Module | What it is |
|---|---|
| `Runtime/Index/ScopeMembershipIndex.cs` | Ancestry/membership index: depth, parent, subtree, reach containment, subtree target enumeration, with work counters on every query. |
| `Runtime/Index/DescriptorTargetIndex.cs` | Descriptor-to-target buckets in both directions (schema/capability/tag/recipe) plus the structural descriptor comparison the change detector needs. |
| `Runtime/Index/ProviderContributionIndex.cs` | Provider/rule-to-source index by output capability, by provider and by rule identity; the reverse-capability closure (`RulesCovering`, `CapabilitiesReaching`) and the declared provider set of a scope's imports. |
| `Runtime/Index/ServiceConsumerIndex.cs` | The P-011/P-012 consumer adjacency: who consumes a contract, who provides it, and which consumers a provider change can reach. |
| `Runtime/Index/DerivationIndexSet.cs` | One build of all of them plus the install-path index (`InstallsOnPath`) and the per-scope provider closure (`ProviderClosure`). |
| `Runtime/Index/CapabilityCatalogHash.cs` | The P-024 catalog dimension of a derived-variant key. |
| `Runtime/Index/InvalidationCounters.cs` | The P-022/P-023 evidence: detection, closure and evaluation work separated, whole-world flag, stable reason keys. Extends `CostCounters`, so one derivation reports one counter object. |
| `Runtime/Invalidation/DerivationChangeSet.cs` | The canonical diff of two snapshots over every fact derivation reads (scope parent/isolation/exclusions/imports, installation record and every declared rule, target scope/descriptor, contracts, rule ordering keys, overrides). |
| `Runtime/Invalidation/InvalidationClosure.cs` | The dependency closure of one change set, with the whole-world report a mode switch must state (TEST-008). |
| `Runtime/Invalidation/IncrementalDerivationEngine.cs` | The dirty-target derivation engine: same result as a full recomputation, over the invalidated part only. |
| `Runtime/Invalidation/DerivedRecipeCache.cs` | P-024 variant reuse keyed by recipe + scope inheritance fingerprint + mode + catalog hash, with explicit stale recomputation. |
| `Runtime/Invalidation/ProviderClosureDiff.cs` | P-025's old/new ancestor rule diff for a moved subtree. |

**`GameCore.Composition`**: `Runtime/Operations/CompositionChangeSet.cs` (the invalidation
view of a proposal: `ScopeFactChange` for parent/isolation/exclusions/imports, install and
config edits, mode, stable reason keys, canonical `Describe()`), an optional
`ICompositionEditValidator` seam, and small additive changes to `CompositionEditPlan`,
`CompositionEditApplier` and `CompositionHost`.

**`GameCore.Unity.Runtime`**: `Runtime/Integration/DerivationModeSwitchValidator.cs`, the
production validator behind that seam, and the derived-assembly chain now runs on the
incremental engine.

**Tests**: two pure differential sweeps (50 seeds × 500 operations, one per family), a
locality/provenance suite on the counters, a reparent/mode/boundary suite for the reference
compositions, a recipe-cache suite, a composition-side invalidation suite, and the Unity
EditMode + IL2CPP probe work described in §7.

## 2. Files created

`Packages/com.gamecore.derivation` (each `.cs` with a committed sibling `.meta`):

- `Runtime/Index/{ScopeMembershipIndex,DescriptorTargetIndex,ProviderContributionIndex,ServiceConsumerIndex,DerivationIndexSet,CapabilityCatalogHash,InvalidationCounters}.cs` (+ folder `Runtime/Index.meta`)
- `Runtime/Invalidation/{DerivationChangeSet,InvalidationClosure,IncrementalDerivationEngine,DerivedRecipeCache,ProviderClosureDiff}.cs` (+ folder `Runtime/Invalidation.meta`)
- `Tests/Support/OperationSequence.cs`
- `Tests/Differential/IncrementalAgreementTests.cs`
- `Tests/Invalidation/{InvalidationLocalityTests,DerivedRecipeCacheTests,ReferenceMoveAndModeTests}.cs` (+ folder `Tests/Invalidation.meta`)

`Packages/com.gamecore.composition`:

- `Runtime/Operations/CompositionChangeSet.cs`
- `Tests/IncrementalInvalidationTests.cs`

`Packages/com.gamecore.unity.runtime`:

- `Runtime/Integration/DerivationModeSwitchValidator.cs`

`unity/GameCore.Validation/Assets/GameCore.Validation/` (the Unity qualification surface;
each file with a committed sibling `.meta`, and the folder meta for `Tests/Gc013`):

- `Runtime/Gc013Scenario.cs`, `Runtime/Gc013NarrativeHost.cs`, `Runtime/Gc013CardsHost.cs`, `Runtime/ProbeGc013.cs`
- `Tests/Gc013/GameCore.Gc013.Tests.asmdef`, `Tests/Gc013/Gc013IntegrationTests.cs`
- `tools/unity/run_gc013_probe.sh`

Evidence: `artifacts/gc-013/HANDOFF.md` (this file), `artifacts/gc-013/static-checks.log`.

## 3. Files modified (all shared; each change is minimal and additive)

| Path | Change | Why |
|---|---|---|
| `Packages/com.gamecore.derivation/Runtime/Budget/PropagationBudget.cs` | `CostCounters` is no longer `sealed`; `Describe()` is `virtual` | `InvalidationCounters` extends it so one derivation reports one counter object instead of two (P-022 + P-023 in one report). No member changed shape; `Describe()` still starts with the same `candidates=…` text. |
| `Packages/com.gamecore.derivation/Runtime/Engine/DerivationEngine.cs` | eight private helpers became `internal` (`TryCheckDeadline`, `ReclassifyShadowed`, `SlotsOf`, `WithinBudget`, `MarkExceeded`, `TopFanOutCauses`, `Decision`, `BuildExplanations`) | The incremental engine reuses the same pure helpers instead of copying them, which is what keeps the two paths from drifting. `Derive` itself is unchanged. |
| `Packages/com.gamecore.derivation/Runtime/Model/Evidence.cs` | added `DerivationExplanation.WithToken(SnapshotToken)` | A carried explanation is the same record at a new observation image: a token names the image, not the composition (P-006, P-026). |
| `Packages/com.gamecore.derivation/Fixtures/Runtime/FixtureBuilder.cs` | added `MoveScope`, `ReplaceScopeExclusions`, `ReplaceScopeIsolation` | The reparent and boundary edits of GC-013 need them; the builder had `MoveTarget` and additive helpers only. Existing behaviour untouched. |
| `Packages/com.gamecore.composition/Runtime/Operations/CompositionEditApplier.cs` | `Plan` takes an optional validator; the subject dispatch moved to `PlanSubject`; `CompositionEditPlan` gained `ChangeSet`; `ComputeDelta` reports an `Update` scope edit for an isolation/exclusion/import change | See §4. |
| `Packages/com.gamecore.composition/Runtime/Operations/CompositionHost.cs` | the constructor and `CreateDefault` take an optional trailing `ICompositionEditValidator`; `Validator` exposes it | See §4. |
| `Packages/com.gamecore.unity.runtime/Runtime/Integration/DerivedAssemblyPipeline.cs` | `Derive` calls `IncrementalDerivationEngine`; the report gained `Invalidation`/`IncrementalCounters`; the pipeline exposes `PreviousInvalidation`; the constructor takes an optional trailing `DerivedRecipeCache` | See §4. |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeArguments.cs` | added the `-probeGc013` flag and its property | The new probe mode needs an argument and a report identity, exactly as every sibling probe has one. |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeRunner.cs` | added the `Gc013` report identity and dispatch | Same. |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/GameCore.Validation.ProbeHost.asmdef` | added references `GameCore.Derivation`, `GameCore.Derivation.Fixtures`, `GameCore.Planning` | The GC-013 scenario uses GC-006's snapshot/rule types and a fixture value source, which the probe-host assembly did not previously reference. Additions only. |

## 4. Contract changes

Additions only; no existing signature, member or semantic was removed or reinterpreted.

- `GameCore.Composition.CompositionEditPlan.ChangeSet { get; }`
  (`CompositionChangeSet`, never null). New file `CompositionChangeSet.cs` also adds
  `CompositionChangeReasons`, `ScopeFactChangeReason`, `ScopeFactChange`,
  `EditValidationResult` and `ICompositionEditValidator`.
- `GameCore.Composition.CompositionEditApplier.Plan(..., ICompositionEditValidator? validator = null)`.
- `GameCore.Composition.CompositionHost` / `CompositionHost.CreateDefault`: trailing optional
  `ICompositionEditValidator? validator = null`; new property `Validator`.
- `GameCore.Derivation`: new public types (indexes, change set, closure, incremental engine,
  recipe cache, closure diff, counters) and `DerivationExplanation.WithToken`. `CostCounters`
  is unsealed and `Describe()` virtual.
- `GameCore.Unity.Runtime.Integration.DerivedAssemblyReport.{Invalidation,IncrementalCounters}`;
  `DerivedAssemblyPipeline.PreviousInvalidation`; optional trailing `DerivedRecipeCache` ctor
  argument.
- `GameCore.Unity.Runtime.Integration.DerivationModeSwitchValidator` (new type).

All additions are trailing-optional or new members, so every existing construction site in
the repository still compiles unchanged. `ICompositionHost`, the frozen `ChangePlan` DTOs,
`CompositionDelta`, `DerivationDelta` (contracts) and every `GameCore.Contracts` type are
untouched.

### 4.1 Why the validator seam exists

P-014 says a mode switch that cannot be honoured "rejects the switch and keeps the old
mode/assembly", and 02 §5 says a Conservative→Automatic switch "may reveal an exclusive
conflict; the result is a diagnostic and the old mode intact". Whether that conflict exists
is a *derivation* fact (eligibility, contracts, descriptors, slot policies), and
`GameCore.Composition` deliberately owns none of it — it references only
`GameCore.Contracts`. Rather than move derivation facts into the composition model or let
the composition model guess, the lane accepts an optional validator and consults it while
planning, before anything is staged. A null validator is "this lane has no derivation view"
and keeps the old behaviour exactly, which is what the existing pure composition tests rely
on. `DerivationModeSwitchValidator` is the production implementation and lives in the one
assembly that sees both sides.

## 5. How the incremental path stays equal to a full recomputation

The differential sweep (§6) is the proof, and these are the invariants it rests on:

1. **Locality is derived, not assumed.** Every P-013 reach selector selects targets inside
   the provider's own subtree, so a rule can reach a target only if the provider is installed
   at the target's scope or one of its ancestors. The install-path index
   (`DerivationIndexSet.InstallsOnPath`) therefore contains *every* rule that can reach a
   target, and the engine inverts the enumeration: each dirty target asks which rules reach
   it. A rule with no dirty candidate is never enumerated (`SkippedRules` counts them).
2. **Carried means provably unchanged.** A target stays clean only when its owner scope, its
   descriptor, the mode, the contract table, the override set and every rule that can reach
   it are unchanged — because each of those is a seed of `DerivationChangeSet` and each seed
   dirties the affected targets through an index. A changed installation dirties its rules'
   populations in *both* snapshots (the old reach to retract, the new reach to apply).
3. **Nothing is carried that a full run would recompute.** Clean targets contribute their
   previous assembly, contributions, decisions and explanations (re-stamped with the new
   observation token); dirty targets are rebuilt from scratch, including their lower-stratum
   ledger entries and their candidate decisions.
4. **The whole input is still validated.** `DerivationValidation.Validate` runs on the whole
   snapshot on both paths, because a catalog problem is a property of the input rather than
   of the dirty set (P-028).
5. **The whole-world cases are explicit.** A mode switch, a contract-table change, a new
   world incarnation, a missing accepted base or a previous run without provenance delegate
   to the full engine and report `WholeWorld` with a stable reason key, which is what
   TEST-008 asks a switch to state.

Known deliberate asymmetry: budget accounting on the incremental path counts only the work it
actually performed, so a proposal that the reference budget accepts could in principle report
a different `BudgetExceeded` dimension than a full run. The sweeps use the reference budget
(1,000,000 candidates) on small worlds, so no budget path is reached here; the composition is
otherwise identical, including the apply-cost estimate, which is computed from the full
assembly list on both paths.

## 6. Requirement and test coverage mapping

| Requirement / clause | Where implemented | Where asserted |
|---|---|---|
| P-010 scope membership | `ScopeMembershipIndex`, `DerivationChangeSet.Diff` (moves, target scope) | `InvalidationLocalityTests`, `ReferenceMoveAndModeTests`, `IncrementalAgreementTests` (per-step) |
| P-013 mode semantics | `DerivationPolicy` (unchanged) + `DerivationChangeSet.ModeChanged` → whole-world closure | `BothModeDirectionsApplyToExistingAndFutureTargets`, `ACompleteOptInKeepsItsBindingInConservative`, `ModeGrantTests` (existing) |
| P-014 mode transition | `InvalidationClosure` whole-world report; `ICompositionEditValidator` + `DerivationModeSwitchValidator` | `AConflictOnTheOtherModeLeavesTheOldAssemblyPublished`, `AModeSwitchReportsTheWholeWorldAsItsCost`, `AModeSwitchPublishesAWholeWorldChangeSet`, `BothModeSwitchDirectionsPublishAndAreReported`, `AValidatorRefusalRejectsTheSwitchAndKeepsTheOldModeAndRevision` |
| P-015 eligibility | `DescriptorTargetIndex` buckets + structural comparison, `IsInPopulation` | `ADescriptorChangeOnOneTargetTouchesNoOtherTarget`, per-step sweep |
| P-016 isolation and exclusions | `DerivationChangeSet` scope facts, `InvalidationClosure` subtree seeds | `ABoundaryEditOnOneScopeLeavesSiblingsUntouched`, `AnExclusionEditAndAnImportEditAreBothReportedAsScopeFacts`, `ANoChange…` |
| P-017 contribution identity and support | `DerivationDeltaBuilder` (unchanged) driven by the carried/dirty split | `ACarriedTargetKeepsItsProvenanceAndSupportExactly`, `UnmountingTheLastProviderRetractsExactlyItsSupport`, `TheOldAndNewProviderClosureOfAMovedScopeAreDiffed`, per-step delta equality |
| P-018 precedence and overrides | `DerivationChangeSet` override domains, `ScopeMembershipIndex.DepthOf` | `ADescriptorChangeOnOneTargetTouchesNoOtherTarget`, override reversal in the sweep's provider replacement |
| P-019 composition policies | `SlotComposer` (unchanged) + `ChangedRuleKeys` | `AnOrderingKeyChangeTouchesOnlyThatRulesPopulation`, conflict tests |
| P-021 termination | stratum loop (unchanged, shared by both paths) | per-step sweep |
| P-022 budgets | `CostCounters` + `PropagationBudget` (unchanged limits; new `InvalidationCounters` report) | `TerminationAndBudgetTests` (existing) + `EvaluateApplyCostMicroseconds` on both paths |
| P-023 incrementality | the whole `Runtime/Index` + `Runtime/Invalidation` layer | `TheIncrementalEngineMatchesTheOracleOnEverySeedAndStep` (50×500), `…OnTheCardVocabularyToo` (50×500), `AProviderChangeInOneBranchDoesNotVisitTheOtherBranch`, `AModeSwitchReportsTheWholeWorldAsItsCost` |
| P-024 spawn and recipe caching | `DerivedRecipeCache`, `ScopeInheritanceFingerprint` | `DerivedRecipeCacheTests` (hit, stale recomputation, passive reuse, mode/catalog sensitivity, `IsCurrent`, bounded eviction, sibling spawn) |
| P-025 reparenting | `ProviderClosureDiff`, `InvalidationClosure` move seeds, `DerivationChangeSet.ScopeMoves` | `TheOldAndNewProviderClosureOfAMovedScopeAreDiffed`, `ReparentingASubtreeDiffsTheOldAndTheNewProviderClosure`, `ReparentingTheVillageSelectsTheNewChaptersBindings` |
| P-026 explanation | `DerivationExplanation.WithToken` + carried records | `TheIncrementalEngineAlsoAgreesWithAFullRecomputationOnExplanations` |
| P-027 change plans / P-028 validation | `CompositionChangeSet`, full-snapshot validation on both paths | `AnIsolationEditIsVisibleAsAChangeRatherThanAnEmptyDelta`, sweep (validation parity) |
| TEST-004 automatic future descendants | unchanged policy + `CreatedTargets` seed | `BothModeDirectionsApplyToExistingAndFutureTargets`, existing TEST-004 cases |
| TEST-006 isolation/modes | as P-013/P-016 | `ReferenceMoveAndModeTests`, existing `IsolationAndExclusionTests` |
| TEST-008 incremental indexes and subtree movement | all of the above; `PropagationBudget.CostCounters` evidence | `InvalidationAgreementTests`, `InvalidationLocalityTests` |
| GC-013 DoD: reparent + both mode directions in both early genres, no per-instance imports in Automatic | the incremental engine + `ICompositionEditValidator` + `DerivationModeSwitchValidator` | `ReferenceMoveAndModeTests` (pure, both reference compositions) **and** the Unity half: fifteen observations per family over both catalogs in `GameCore.Gc013.Tests`, repeated in the IL2CPP player by `run_gc013_probe.sh` |

## 7. Unity EditMode and probe surface

One scenario, two family adapters, both surfaces:

| File | What it is |
|---|---|
| `Assets/GameCore.Validation/Runtime/Gc013Scenario.cs` | The shared scripted sequence over an `IGc013Family`: a real world per family (`UnityWorldRegistry.TryCreate` + the family registration), real live targets (`TargetRegistry`, `LiveTargetIndex`, `LiveTargetSeeder`), the real lane (`CompositionHost.CreateDefault` wired to `DerivationModeSwitchValidator`), the real chain (`WorldCompositionBridge`, `DerivedAssemblyPipeline` over the incremental engine) and real storage publication. |
| `Assets/GameCore.Validation/Runtime/Gc013NarrativeHost.cs` | The narrative family: catalog declarations, the chapter tree's declared scopes, the seeded targets, the reparent/mode/conflict payloads, and the exclusive pair the narrative vocabulary lacks (a declared `CompositionPolicy.Exclusive` capability). |
| `Assets/GameCore.Validation/Runtime/Gc013CardsHost.cs` | The card family: `CardTableFixture`/`CardVocabulary` declarations, the league tree, the reparent through `CardTablePayloads.ScopeReparent` (07 s2.4's own builder, which had no caller until now), and a draw-policy pair over the real `cards.draw-policy` identity. |
| `Assets/GameCore.Validation/Runtime/ProbeGc013.cs` | The player probe: both families over both catalogs, every observation into the shared probe report, and both digest literals asserted after recomputing them from the observed steps. |
| `Assets/GameCore.Validation/Tests/Gc013/Gc013IntegrationTests.cs` (+ asmdef) | The EditMode suite: one case per family over the committed generated catalog *and* the hand-written generated-style catalog. |
| `tools/unity/run_gc013_probe.sh` | The player-probe harness: `PROBE_RUNS` runs, strict JSON validation, the fifteen names per family per catalog, both digest literals. |
| `Runtime/ProbeArguments.cs`, `Runtime/ProbeRunner.cs`, `Runtime/GameCore.Validation.ProbeHost.asmdef` | Additive wiring for the `-probeGc013` mode and three assembly references (`GameCore.Derivation`, `GameCore.Derivation.Fixtures`, `GameCore.Planning`) the new runtime code needs. |

**Fifteen named observations per family, in order** (the digest is over exactly these
`<label>/<name>=pass` lines, LF separated, no trailing newline):

1. `gc013-world-and-live-targets`
2. `gc013-mount-inherits-to-every-eligible-target`
3. `gc013-reparent-preserves-target-state-and-inheritance`
4. `gc013-isolated-branch-unchanged-across-the-reparent`
5. `gc013-opt-in-target-declared-and-derived`
6. `gc013-isolated-branch-unchanged-across-the-opt-in-target`
7. `gc013-mode-switch-automatic-to-conservative`
8. `gc013-isolated-branch-unchanged-across-the-conservative-switch`
9. `gc013-future-target-in-conservative-derives-nothing`
10. `gc013-isolated-branch-unchanged-across-the-future-spawn`
11. `gc013-mode-switch-conservative-to-automatic`
12. `gc013-isolated-branch-unchanged-across-the-automatic-switch`
13. `gc013-exclusive-conflict-preserves-mode-and-membership`
14. `gc013-isolated-branch-unchanged-across-the-conflict`
15. `gc013-teardown-settles-and-disposes`

The two digest literals the suite, the probe and the shell script all assert are
`8d0ca4d2e31cdf6e1ead4a57fa6427acb1dfb4d7c11ab9cab1590857ee81befd` (narrative) and
`ac6dc0b11d32a60328cfcc2724ce23a36919afd88aa01e6afa0a135ac470c5c9` (cards). Both were
**recomputed independently on this host** from the fifteen qualified names above and the
documented `NarrativeDigest.OfLines` formula, and both match.

### 7.1 One defect found before the build host, and fixed

The scenario qualified a step name with its family label in the isolated-branch path only, so
it recorded nine bare names and six qualified ones; the digest that produces is
`025fcf81eb8175d3cd0492a08dfc45fa6dbac32f807dda10965c3386cb7a8f5f`, not the literal the
suite, the probe and `run_gc013_probe.sh` assert, and the probe's expected names
(`narrative/gc013-…` and `fixture:narrative/gc013-…`) would not have appeared at all — three
independent CI failures. The qualification now happens at the single recording point
(`Gc013Scenario.Executor.Add`), so the sequence is exactly the qualified list and both
digests reproduce. Found by recomputing the digests from the name table rather than trusting
the implementation's own numbers.

### 7.2 What the Unity half adds over the pure tests

`ReferenceMoveAndModeTests` proves the same clauses on the pure derivation engine. The Unity
half proves them through the *real* path: a produced `CompositionHost` publication, the
incremental engine inside `DerivedAssemblyPipeline`, a compiled schedule, and binding rows in
actual ECS storage — including the parts a pure test cannot reach (the validator refusing a
staged proposal, the seeded live slot surviving the move, the rows moving with the binding).
A Unity-side failure is localised because the same clause is asserted at derivation level.

## 8. Exact commands for the Linux build host

Run from the repository root. Nothing below has been run.

```sh
# 8.1 the pure half: builds and runs every assembly incl. the new GC-013 modules
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-013/trx

# 8.2 GC-013's own suites alone, with the failing seed and operation visible
dotnet test dotnet/tests/GameCore.Derivation.Tests/GameCore.Derivation.Tests.csproj -c Release \
  --logger "console;verbosity=detailed" \
  --filter "FullyQualifiedName~IncrementalAgreementTests|FullyQualifiedName~InvalidationLocalityTests|FullyQualifiedName~DerivedRecipeCacheTests|FullyQualifiedName~ReferenceMoveAndModeTests"
dotnet test dotnet/tests/GameCore.Composition.Tests/GameCore.Composition.Tests.csproj -c Release \
  --logger "console;verbosity=detailed" --filter "FullyQualifiedName~IncrementalInvalidationTests"

# 8.3 the same derivation sources through Unity EditMode
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Gc013.Tests \
  -testResults artifacts/gc-013/unity/gc013-editmode.xml \
  -logFile artifacts/gc-013/unity/gc013-editmode.log

# 8.4 the two families' existing EditMode suites, because the chain now runs incrementally
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode \
  -testResults artifacts/gc-013/unity/all-editmode.xml \
  -logFile artifacts/gc-013/unity/all-editmode.log

# 8.5 the player probe (IL2CPP), PROBE_RUNS=5. The GC-013 mode adds one probe; the other four
#     modes must keep passing because the derived-assembly chain now runs the incremental engine.
bash tools/unity/build_probe.sh
PROBE_RUNS=5 UNITY="$UNITY" bash tools/unity/run_gc013_probe.sh
PROBE_RUNS=5 UNITY="$UNITY" bash tools/unity/run_narrative_probe.sh
PROBE_RUNS=5 UNITY="$UNITY" bash tools/unity/run_cards_probe.sh
PROBE_RUNS=5 UNITY="$UNITY" bash tools/unity/run_w3_gate_probe.sh

# 8.6 host-side checks (already run on this host; rerun for the record)
python3 tools/check_game_core_csharp.py
python3 tools/validate_game_core_docs.py
```

Do not add `-quit` to a Unity test-run command (04 §10). The Unity run needs
`com.gamecore.derivation` and `com.gamecore.composition` in
`unity/GameCore.Validation/Packages/manifest.json` and in `testables` (both already there
from GC-006/GC-004); the new `Assets/.../Tests/Gc013` assembly is Editor-only and declared
`UNITY_INCLUDE_TESTS`, so no manifest change is required for it.

**Cost warning (measured nowhere, inferred).** The sweep is 50 seeds × 500 operations ×
(oracle + incremental + canonical projections), and the reference evaluator walks every
target × installation × rule for each of the 32 strata. That is the single largest addition
to the dotnet suite in this wave; `IncrementalAgreementTests.SeedCount`/`StepsPerSeed` and
`OperationSequence.MaxTargets`/`MaxInstalls`/`ChapterRules` are the four constants that bound
it, and all four are named in the files. If the suite budget is exceeded, lower `MaxInstalls`
before lowering the seed or step counts, because the operation vocabulary is the acceptance
criterion and the world size is not.

## 9. Assumptions, decisions and doc ambiguities

1. **The composition change set is the invalidation *report*, not the invalidation *input*.**
   The incremental engine derives its own canonical diff from the two snapshots and exposes
   `DerivationChangeSet` for a caller that already knows what an edit did. A translator from
   `CompositionChangeSet` to `DerivationChangeSet` was deliberately not written: it would be a
   second, weaker change detector that could disagree with the snapshot diff, which is exactly
   the class of bug TEST-008's oracle comparison exists to catch. The composition change set
   is consumed where it is the right shape — the mode-switch validator and the proposal's own
   report.
2. **A mode switch is not derived incrementally.** P-023 permits a mode switch to invalidate
   the world; `DerivationChangeSet.ModeChanged` short-circuits to the full engine and reports
   `WholeWorld`, which is what TEST-008 asks a switch to state. Trying to bound it would mean
   re-evaluating every gated candidate anyway.
3. **A moved scope's fact changes are reported as well as its move** (§4/§3 of this file), so
   the report cannot hide one of two facts that changed together.
4. **`InstallsPath` is the reach invariant.** The install-path index assumes every reach
   selector selects inside the provider's subtree, which is what `PropagationReach` means and
   what the snapshot's own `Reach`/`TargetsInReach` implement. A future reach selector that
   selects *outside* the provider's subtree would invalidate that assumption, and it would be
   a protocol change (P-013), not a local edit. Recorded so the next reader checks it.
5. **The service-consumer closure is a sound superset.** A provider change can flip a
   consumer into `WaitingForDependencies` (P-012), which is a state change the change set
   already detects; the consumer adjacency additionally dirties the consumer's rule
   populations, which is redundant but harmless and matches 02 §7's wording.
6. **The carried explanation keeps its `Mode`.** A carried record is only produced when the
   mode did not change (a mode change is whole-world), so re-stamping the token is the only
   field that moves. `WithToken` copies every other clause verbatim.
7. **`DerivationModeSwitchValidator` accepts an unbuildable input.** An input that cannot be
   built refuses *every* proposal at the pipeline's own step, so refusing a mode switch for
   that reason would attribute an unrelated failure to the mode. The pipeline reports the
   condition with its own code.
8. **Empty scope-edit vs scope-fact:** an isolation/exclusion/import edit now yields a
   `CompositionEditKind.Update` entry in `CompositionDelta.Scopes` as well as a
   `CompositionChangeSet.ScopeFacts` entry. The delta DTO has no dedicated kind for those
   facts, and leaving the delta empty for a real change would make `IsNoChange`'s inputs
   inconsistent with the reported change; the vocabulary addition belongs to a future
   contract review, not to this task.
9. **Reparenting an installation is not expressible.** `InstallRecord.Scope` changes only
   through a fresh mount, so a provider change that moves an installation is modelled as an
   unmount plus a mount (the task's "provider replacement"). Recorded because P-025's
   "replacing a provider under the same installation identity" is a GC-014/replacement-mapping
   concern rather than this one.
10. **`CostCounters` is unsealed to avoid a second counter object.** The alternative was a
    separate invalidation report beside the cost counters; one object means one
    `DerivationResult.Counters` and no chance of the two reports disagreeing.

## 10. Known gaps

- **No measurement exists.** Every perf claim here is structural (index lookups and bounded
  closure walks), not measured. TEST-023's counters still need the standalone player run.
- **The incremental path is wired into the chain but not into `GameCore.Planning`.** The
  planner's own `AffectedCounts` come from the proposal, not from the invalidation closure;
  feeding `AffectedCounts` from `InvalidationClosureResult` is a natural follow-up and
  belongs to whoever owns the plan assembly.
- **`DerivationModeSwitchValidator` is Unity-only.** There is no plain-dotnet project for
  `com.gamecore.unity.runtime`, so the validator compiles only in Unity and is covered by the
  EditMode suite rather than by a dotnet test.
- **`DerivationChangeSet` ignores `InstallRecord.ConfigRevision`/`ConfigHash`.** Derivation
  reads a rule's `PayloadDefinition` from the manifest, never the install record's config
  hash, and the reference oracle reads the same, so both engines agree; a *real* runtime
  reconfiguration that changes an effective value without changing the declared rule payload
  would be invisible to derivation today. That is GC-006's declared model (payload revision
  lives in the rule), not something this task changed — recorded as the most likely place a
  reviewer would expect a difference.
- **`RuleKeysChanged` is detected per rule identity, not per installation.** An ordering-key
  change dirties that rule's populations in both snapshots; a rule identity declared by two
  installations is therefore handled, but the closure cannot distinguish which installation's
  key set changed, because `DerivationRuleKeys` is keyed by rule identity (GC-006's decision
  6.2). Conservative in the dirty direction.
- **The state-slot support index of P-023 is not here.** It belongs to the ownership/state
  model (GC-007/GC-008) and GC-013 was not given that data; the other five P-023 indexes are.
- **The recipe cache is wired into the derivation path, not the spawn publication's variant
  cache.** `DerivedRecipeCache` resolves the reaching rules for a dirty target and
  `ScopeInheritanceFingerprint` is the value `DerivedVariantKey` (declared in GC-008's
  `SpawnRecipeCatalog.cs` and, until this task, never constructed by anything) was waiting
  for. Passing that fingerprint into the spawn path means editing `AssemblyPublisher.Spawn`,
  which every existing probe and gate exercises; the resolver and the key now exist side by
  side, and connecting them is the follow-up that would make a spawn reuse a derived variant
  instead of recomputing it (P-024's "fast repeated spawning").
- **`IncrementalDerivationEngine` is on the production path, but `GameCore.Planning` is not
  told about it.** The plan's `AffectedCounts` and `ValidityAndCost` still come from the
  proposal rather than from the invalidation closure, so a plan's reported cost is not yet
  the incremental cost. Wiring it is a small change in the plan assembly, which this task
  does not own.
- **Two scenario steps assert a reading, not a bare boolean, where the vocabulary does not
  reach.** Step D of the Unity scenario asserts the *refused switch* (the validator's
  rejection with the old mode, membership and revision intact) rather than a conflict raised
  by a mount, because in Conservative the descendant-reach rules of the pair are denied and
  so no mount can conflict there; the reverse direction cannot conflict either, since a grant
  that makes the rule applicable in Conservative makes it applicable in Automatic too. The
  step detail says so in prose. The same clause is asserted on the pure engine in
  `AConflictOnTheOtherModeLeavesTheOldAssemblyPublished`.
