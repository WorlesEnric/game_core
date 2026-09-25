# GC-006 HANDOFF — finite Automatic derivation and contribution policies (Wave 2)

Branch: `gc-006` (worktree `/Users/yangcao/wkspace/gc-wt/gc-006`).

**Status of every executable check below: `NotRun (pending orchestrator build host)`.** This host has no .NET SDK,
no C# compiler, no Mono and no Unity, so nothing in this change set has been compiled, imported or executed here.
The only things that *were* run on this host are interpreter-level: `python3 tools/check_game_core_csharp.py`
(99 files, `ok`) and `python3 tools/validate_game_core_docs.py` (passed) — plus the brace/forbidden-construct scan
described in §7. None of those is a build or a test result.

## 1. Summary

New Unity-free package `Packages/com.gamecore.derivation` (assembly `GameCore.Derivation`,
`noEngineReferences: true`, references only `GameCore.Contracts`) plus a reusable fixture assembly
(`GameCore.Derivation.Fixtures`) and a plain-dotnet build/test pair. It implements, end to end:

1. **Indexed immutable snapshot input** (`Runtime/Snapshot/`) — `DerivationSnapshot` over scopes, active
   installations with their manifests, target descriptors, capability contracts, ordering keys and selection
   overrides. Construction validates structure (one rooted acyclic scope tree, resolvable parents, unique
   identities) and builds the indexes P-023 requires: scope reach/ancestry, targets by scope / declared schema /
   declared capability / tag, rule buckets by stratum, canonical ordering for everything.
2. **Descriptor matching, scope reach, the mode grant predicate, isolation and exclusions** (`Runtime/Policy/`) —
   the exact 02 §5 table (LocalOnly in both modes; Automatic grants eligible descendants without imports;
   Conservative needs export+import or a complete target opt-in) and the P-016 boundary/exclusion rules, in the
   canonical check order "selector → exclusions → boundaries → mode gate → lower-stratum inputs → predicate"
   (denial wins over imports and opt-ins).
3. **All five contribution policies** (`Runtime/Policy/CompositionKernel.cs`) — `Additive` (registered reducer or
   canonical set union), `Replace`, `Ordered` (declared before/after keys, precedence as ready-set tie breaker),
   `Exclusive`, `Incompatible`, plus the P-018 total rank (priority, rule priority, provider depth, provider id,
   rule id, slot), explicit `SelectProvider` overrides (target- or scope-addressed, subtree-aware) and the
   cross-capability incompatibility-set check.
4. **The indexed runtime engine** (`Runtime/Engine/DerivationEngine.cs`) — strata 0..31 walking, candidate
   enumeration from the index, composition per (target, capability, slot) group, stratum finalization before the
   next stratum, a total rejection path (no partial closure), bounded output slots, `PropagationBudget`
   enforcement on all six dimensions of P-022, applied-cost estimation and complete provenance.
5. **The full-recompute oracle** (`Runtime/Oracle/DerivationOracle.cs`) — an independent brute-force traversal
   that shares only *semantics* (the candidate predicates and the composition kernel), plus a canonical projection
   (`Runtime/Oracle/DerivationProjection.cs`) so the differential tests compare effective capabilities, slot
   values, support identities, recipe hashes and decision sets rather than component counts.
6. **Explain records** (`Runtime/Explain/DerivationExplainReader.cs`) — the shared `IExplanationReader`/
   `ExplanationPage` shape over interned per-(target, capability) records, paged and labelled
   `PublishedComposition` or `StagedPlan`.
7. **Reusable 07 fixtures** (`Fixtures/Runtime/`) — the chapter-quest composition (07 §3) and the card-market
   composition (07 §2) as data plus a fluent builder, so GC-010/GC-011 can run those slices without re-declaring
   them.

No contract was added to or changed in `GameCore.Contracts`: every `ContributionKey`, `CapabilityContract`,
`DerivationRule`, `TargetDescriptor`, `ProviderSelectionOverride`-shaped fact already exists there, and the new
types that the frozen surface lacks (snapshot input records, ordering keys, selection overrides, budgets, results)
live in this assembly.

## 2. Files created

Package `Packages/com.gamecore.derivation` (each `.cs`/`.asmdef`/`package.json` with a committed `.meta`):

- `package.json`
- `Runtime/GameCore.Derivation.asmdef`
- `Runtime/Snapshot/DerivationInputs.cs` — `CanonicalDerivationOrder`, `DerivationScope`, `DerivationInstall`,
  `DerivationTarget`, `OrderKeyEdge`, `DerivationRuleKeys`, `ProviderSelectionOverride`, `DerivationScopeGrants`
- `Runtime/Snapshot/CapabilityCatalog.cs`
- `Runtime/Snapshot/DerivationSnapshot.cs` — `DerivationSnapshot`, internal `RuleSource`/`ReachDomain`
- `Runtime/Snapshot/PayloadCodec.cs`
- `Runtime/Budget/PropagationBudget.cs` — `PropagationBudget`, `CostCounters`, `BudgetDimension`
- `Runtime/Model/Contributions.cs` — `ContributionDisposition`, `CapabilityContribution`, `RankedCandidate`,
  `EffectiveSlot`, `TargetAssembly`, `EffectiveSlotChange`, `DerivationDelta`
- `Runtime/Model/Evidence.cs` — `CandidateStatus`, `CandidateRejectionReason`, `ModeGateDecision`,
  `BoundaryEvidence`, `ExclusionEvidence`, `CandidateDecision`, `DerivationExplanation`
- `Runtime/Policy/DerivationValueSource.cs` — `DerivationPredicateContext`, `IDerivationValueSource`,
  `ReducerFailureException`, `EmptyDerivationValueSource`, `EvidenceKeys`
- `Runtime/Policy/DerivationPolicy.cs` — `CapabilityLedger`, `CandidateEvaluation`, `DerivationPolicy`
- `Runtime/Policy/CompositionKernel.cs` — `CompositionFailure`, `OverrideResolution`, `SlotComposer`, `SlotHash`
- `Runtime/Validation/DerivationValidation.cs` — `DerivationValidationProblem`, `DerivationValidation`
- `Runtime/Engine/DerivationEngine.cs` — `DerivationOptions`, `DerivationRejectionKind`, `DerivationResult`,
  `DerivationEngine`, internal `SlotGroupKey`/`PairKey`
- `Runtime/Engine/AssemblyHash.cs`, `Runtime/Engine/DerivationDeltaBuilder.cs`
- `Runtime/Oracle/DerivationOracle.cs`, `Runtime/Oracle/DerivationProjection.cs`
- `Runtime/Explain/DerivationExplainReader.cs`
- `Fixtures/Runtime/GameCore.Derivation.Fixtures.asmdef`
- `Fixtures/Runtime/DerivationFixtureSupport.cs` — `FixtureIds`, `FixturePayload`, `FixtureValueSource`,
  `FixtureComposition`, `Permutation`
- `Fixtures/Runtime/FixtureBuilder.cs` — `FixtureBuilder`, `FixtureSlot`, `FixtureOrderEdge`
- `Fixtures/Runtime/NarrativeComposition.cs` — the chapter-quest descriptors of 07 §3
- `Fixtures/Runtime/CardComposition.cs` — the card-market descriptors of 07 §2
- `Tests/GameCore.Derivation.Tests.asmdef`
- `Tests/Support/DerivationTestSupport.cs`, `Tests/Support/RandomComposition.cs`
- `Tests/ModeAndReach/ModeGrantTests.cs`, `Tests/ModeAndReach/IsolationAndExclusionTests.cs`
- `Tests/Policies/CompositionPolicyTests.cs`, `Tests/Policies/PrecedenceAndSupportTests.cs`
- `Tests/Termination/TerminationAndBudgetTests.cs`
- `Tests/Provenance/ProvenanceTests.cs`
- `Tests/Differential/OracleAgreementTests.cs`
- `Tests/Reference/ReferenceCompositionTests.cs`

Plain dotnet:

- `dotnet/src/GameCore.Derivation/GameCore.Derivation.csproj`
- `dotnet/tests/GameCore.Derivation.Tests/GameCore.Derivation.Tests.csproj`

Evidence: `artifacts/gc-006/HANDOFF.md` (this file), `artifacts/gc-006/static-checks.log`.

## 3. Files modified (shared; each change is minimal and explained)

| Path | Change | Why |
|---|---|---|
| `dotnet/GameCore.sln` | added `GameCore.Derivation` and `GameCore.Derivation.Tests` with fresh GUIDs `{1A2B3C4D-0022-…}`/`{…-0023-…}` (checked against the existing 21 project GUIDs) and matching Debug/Release rows | the solution is the documented index of the dotnet half; entries added only |
| `dotnet/README.md` | added the two project rows | same index |
| `unity/GameCore.Validation/Packages/manifest.json` | added `com.gamecore.derivation` to `dependencies` and to `testables`; re-sorted the `testables` array alphabetically (the previous order was not alphabetical once `derivation` was inserted) | the qualification project must compile and run this package's assemblies |
| `tools/check_game_core_csharp.py` | added `Packages/com.gamecore.derivation` to `TARGETS` and to the `engine_free` tuple | the same host-side checks (brace balance, forbidden constructs, missing returns, engine-free rule) now cover the new sources; the checker was run on this host afterwards and reports `ok` for all 99 files |

Nothing else outside `Packages/com.gamecore.derivation/**`, `dotnet/{src,tests}/GameCore.Derivation*`,
`artifacts/gc-006/**` and those four shared files was touched. No `.cs` file under
`Packages/com.gamecore.contracts`, `…composition`, `…planning`, `…unity.runtime`, `…unity.adapters`,
`dotnet/src/GameCore.{Contracts,Composition,Execution,Planning}` or `tests/` was modified, and no file in any
other W2 task's folders was created.

## 4. Exact commands for the Linux build host

Run from the repository root. Nothing here has been run; the orchestrator owns the build host.

```sh
# 4.1 dotnet half (builds and runs every pure assembly incl. GameCore.Derivation)
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-006/trx

# 4.2 this task's suite alone, with the seed/step of a differential failure visible
dotnet test dotnet/tests/GameCore.Derivation.Tests/GameCore.Derivation.Tests.csproj -c Release \
  --logger "console;verbosity=detailed"

# 4.3 Unity EditMode half (fires the identical sources through the same assertions)
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Derivation.Tests \
  -testResults artifacts/gc-006/unity/derivation-editmode.xml \
  -logFile artifacts/gc-006/unity/derivation-editmode.log

# 4.4 host-side checks (run on this host already; rerun for the record)
python3 tools/check_game_core_csharp.py
python3 tools/validate_game_core_docs.py
```

Do not add `-quit` to a Unity test-run command (04 §10). The Unity run needs the package in
`unity/GameCore.Validation/Packages/manifest.json` (`com.gamecore.derivation`, added here) and in `testables`
(added here); a first import writes `.meta` files only if a GUID is missing, and none is missing (§7.3).

## 5. Requirement and test coverage mapping

| Requirement | Where implemented | Where asserted |
|---|---|---|
| P-010 scope membership | `DerivationSnapshot` (root/parent/descendants, one-owner targets), `DerivationScope` | `NarrativeComposition`/`CardComposition` trees, `ReferenceCompositionTests.RefN01/RefN04`, snapshot constructor rejects a second root or a detached parent |
| P-013 mode semantics | `DerivationPolicy.EvaluateModeGate`, `ImportsCapability`, `HasTargetOptIn` | `ModeGrantTests` (all nine cases), `ProvenanceTests.AModeSwitchMovesTheStoryIntoOneCoherentExplanation`, `ReferenceCompositionTests.RefC05/RefN01` |
| P-014 mode transition | re-derivation of the same snapshot in the other mode; nothing mode-specific is persisted | `ModeGrantTests.SwitchingModes…`, `RefC05_ModeSwitch…`, `RefC06` (a switch that would expose a conflict rejects and leaves the old assembly) |
| P-015 eligibility | `TargetsInReach` (descriptor index), `DerivationPolicy.Evaluate` selector/input/predicate checks, `CandidateStatus.SelectorNotAdvertised/SelectorVersionMismatch/InputMissing/PredicateRejected` | `ProvenanceTests` (version mismatch, boundary, no-invented-explanation), `ReferenceCompositionTests.RefN01` (prop ineligible, future villager eligible) |
| P-016 isolation and exclusions | `DerivationScope.BlocksCapability`, `DerivationPolicy.CollectBoundaries`/`CollectExclusions`, `ExclusionAppliesFromDescriptor/FromScope` | `IsolationAndExclusionTests` (all eight cases, incl. sibling isolation, wildcard vs named boundary, capability/rule/provider exclusions, opt-in cannot reopen a boundary), `ReferenceCompositionTests.RefP02` |
| P-017 contribution identity and support | `CapabilityContribution.Key`, `EffectiveSlot.Support/Shadowed`, `DerivationDeltaBuilder` (surviving vs lost support) | `PrecedenceAndSupportTests` (shared support retraction, last supporter removal, reconfiguration keeps the key), `ReferenceCompositionTests.RefC04` |
| P-018 precedence and overrides | `RankedCandidate.Compare`, `SlotComposer.ResolveOverride` | `PrecedenceAndSupportTests` (priority > depth, depth > id, explicit selection, ineligible selection rejects, ambiguous overrides, illegal policy rejected), `ReferenceCompositionTests.RefC04` |
| P-019 the five policies | `SlotComposer.TryCompose`, `TryCheckIncompatibility` | `CompositionPolicyTests` (each policy, all five in the four-provider fixture, per-policy permutation sweep), `PrecedenceAndSupportTests` |
| P-020 configuration and overrides | payload revision vs contribution identity; overrides are versioned data | `PrecedenceAndSupportTests.ReconfiguringAPayload…`, `CardComposition` reconfigure path (`RefC04` variant in `OracleAgreementTests`) |
| P-021 termination and strata | 32-stratum buckets, stratum finalization, bounded slots, `Inputs` strictly lower | `TerminationAndBudgetTests` (finite chain, diamond duplicate suppression, mutual dependency, self-producing rule, out-of-range stratum, over-declared slot bound, missing prerequisite) |
| P-022 budgets | `PropagationBudget`, `CostCounters`, `DerivationEngine.WithinBudget`/`TryCheckDeadline`/`EstimateApplyCostMicroseconds` | `TerminationAndBudgetTests` (candidate/contribution/byte/deadline/apply-cost exhaustion, larger budget is an explicit change, no partial closure), `ReferenceCompositionTests.RefP03` |
| P-023 incrementality oracle | `DerivationSnapshot` indexes + `DerivationOracle` + `DerivationProjection` | `OracleAgreementTests` (50 seeds × 40 operation steps, 50 random compositions, both reference compositions, delta agreement) |
| P-026 explanation | `CandidateDecision`, `DerivationExplanation`, `DerivationExplainReader` | `ProvenanceTests` (all clauses incl. paging, source labelling, an empty page for an unknown pair), `ReferenceCompositionTests.RefP02` |
| P-028 validation | `DerivationValidation.Validate` | `DerivationValidation` cases inside `TerminationAndBudgetTests` (stale revision, strata, slot bounds, reducer/predicate registration, illegal override) |
| TEST-004 | Automatic propagation to existing and future descendants | `ModeGrantTests.AutomaticGrants…`, `AutomaticAppliesToAFutureDescendant…`, `PrecedenceAndSupportTests.ReplayingTheSameDerivation…`, `ReferenceCompositionTests.RefN01` |
| TEST-005 | composition, precedence, permutations, shared support | `CompositionPolicyTests`, `PrecedenceAndSupportTests` |
| TEST-006 | modes, isolation, exclusions | `ModeGrantTests`, `IsolationAndExclusionTests`, `RefC05_ModeSwitch…` |
| TEST-007 | termination and bounded expansion | `TerminationAndBudgetTests` |
| TEST-008 | runtime vs oracle over randomized sequences | `OracleAgreementTests` |
| TEST-009 (derivation half) | a rejected proposal publishes nothing and leaves the old result usable | `TerminationAndBudgetTests.ARejectedProposalLeavesThePreviouslyPublishedResultUsable`, `ReferenceCompositionTests.RefC06` |

## 6. Doc ambiguities and the decisions taken

00 wins over 05, which wins over 09. Each decision below is the simplest reading consistent with 00 and is
recorded because a reviewer may reasonably want a different one.

1. **A rule's candidate population.** P-023 says derivation examines *indexed* candidates, while P-015 says an
   unrecognized target "remains unchanged and gets an `Ineligible` explanation". Resolved as: a candidate is a
   target in the rule's reach domain that advertises **either** an accepted selector contract (by schema id, so a
   version mismatch is explainable) **or** the rule's output capability (so a target that natively provides it is
   told why it was refused). A target advertising neither is outside every rule's population, gets no invented
   explanation, and keeps exactly its base recipe. When a rule declares no selector at all, the reach domain is
   the population, taken from the scope index rather than by scanning targets.
2. **Ordering keys.** The frozen `DerivationRule` carries one opaque `FrozenPayload` and no before/after fields,
   so P-019's "declared before/after keys" travel as `DerivationRuleKeys` in the derivation input (a versioned
   composition declaration owned by the contract's package). A rule declares its key set once; the snapshot
   rejects a duplicate, and a fixture builder call for the same rule replaces the earlier one.
3. **Selection overrides are per capability, not per slot.** `ProviderSelectionOverride` names a capability, a
   provider, and either a scope (optionally subtree) or a target. Every fixture capability declares one slot, so
   the distinction is not observable here; a future multi-slot contract should extend the record rather than
   reinterpreting it. An override for a policy other than `Replace`/`Exclusive` is rejected as a catalog error
   (P-018 "legal only for Replace or Exclusive") instead of being silently ignored.
4. **Incompatibility is set membership.** P-019 describes "cross-capability incompatibility sets"; a declaration
   from either side forms the set, so a one-sided list still rejects when both members would be active. Reading it
   symmetrically is the safer option ("priority cannot destroy an incompatible capability") and is what the
   fixtures assert.
5. **An `Incompatible` slot policy accepts at most one member and no selection override.** P-018 allows
   `SelectProvider` for `Replace`/`Exclusive` only, and P-019 says the incompatible case is rejected rather than
   resolved, so an override cannot dissolve it.
6. **`LocalOnly` reach.** P-013 says a `LocalOnly` rule "never propagates"; the domain is the installation's own
   scope, which is also what makes the same-scope half of the mode table meaningful.
7. **Capability isolation only.** Services never pass the propagation gate (P-013), so `DerivationScope` carries
   the capability isolation set and the Conservative imports, not `ServiceIsolation`. Service isolation remains
   the composition package's data and is unread here.
8. **Policy agreement.** `DerivationRule.Policy` must equal the contract's per-slot policy; P-019 calls a mixed
   policy a catalog error and a silent mismatch would make `EffectiveSlot.Policy` a lie.
9. **Reducer failure.** A registered reducer that detects overflow/range throws and the engine turns it into a
   whole-proposal rejection (P-019 "error rejects whole proposal"); a *missing* registration is reported by
   validation (P-028) rather than being treated as an identity fold.
10. **The oracle shares semantics, not traversal.** It calls the same `DerivationPolicy`/`SlotComposer` (02 §4:
    "share schema definitions") but has its own brute-force enumeration, its own group collection, its own
    assembly/loop and no budget accounting. Its decision set is a superset of the engine's, so the differential
    test asserts semantic equality *and* that the engine invented no decision the oracle cannot reproduce.
11. **Wall-clock deadline.** P-022 includes a preparation deadline; pure code cannot read a clock, so the host
    supplies `DerivationOptions.ElapsedMilliseconds`. Null means "not enforced in this run", which is what the
    deterministic tests rely on; the deadline path is tested with an injected value.
12. **`SnapshotToken` step.** An explanation is produced at the snapshot's epoch; the logical step is `Zero`
    because composition publication may keep the same step (P-006) and derivation never advances it.

## 7. What was run on this host, and the first likely failures

### 7.1 Commands actually run (not build or test results)

- `python3 tools/check_game_core_csharp.py` → `checked 99 C# file(s)` / `ok` (includes the new package, after the
  `TARGETS`/`engine_free` addition).
- `python3 tools/validate_game_core_docs.py` → passed.
- An independent brace/paren/bracket balance and forbidden-construct scan over
  `Packages/com.gamecore.derivation/**/*.cs` (31 files) → balanced, no forbidden construct, no engine type.
- A duplicate-member scan over the same files (overloads only where intended: `IndexContributions`, `Select`
  renamed to `Selector` to avoid an overload pair).

All four are recorded verbatim in `artifacts/gc-006/static-checks.log`.

### 7.2 What could fail first on the build host

1. A nullable-reference or unused-variable diagnostic under `TreatWarningsAsErrors` in files no host compiler
   touched. Highest-risk spots: the fixture builder's `out`/`!` usages, `SlotComposer.TryCompose`'s explicit
   `schema!`/`policy!` (validation guarantees them) and the nullable-annotated internal structs.
2. A Unity asmdef reference error in the new test assembly or the fixtures assembly.
3. An assembly-name collision if a later W2 task also declares `GameCore.Derivation.Fixtures`.
4. The randomized differential sweep's runtime: it is 50 seeds × 40 steps × (runtime + oracle) plus 50 random
   compositions on small fixtures. The oracle is deliberately slow. If it is too slow for the suite budget, the
   seeds/steps constants are at the top of `OracleAgreementTests` and the sweep is deterministic, so lowering
   them keeps the comparison meaningful.

### 7.3 Build-host housekeeping

`unity/GameCore.Validation/Packages/packages-lock.json` is expected to be stale relative to the manifest (the
same situation the W1 gate reported): the resolve step regenerates it, and the regenerated lock should be
committed with the result. `.meta` files for every new file and folder were written here with deterministic
GUIDs derived from the path (unique against the repository's existing GUIDs, checked mechanically); if Unity
rewrites any of them on first import, commit the rewritten file.

## 8. Known gaps

- **No incremental invalidation.** GC-006 builds the *indexed* input and the oracle; reusing an index across
  publications, dirty-closure computation, cached recipe variants and the cost evidence of P-023's "does the
  engine avoid rescanning" claim belong to GC-013 (as 09 GC-006 states: "optimize invalidation in GC-013"). The
  counters `IndexBucketsVisited`/`IndexTargetsVisited` exist so GC-013 can prove that claim; they are not a
  substitute for it.
- **No live publication.** Derivation returns an immutable delta; binding/component publication, world fences and
  epoch advancement are GC-008's.
- **No schedule or ownership validation.** The ownership map, stage DAG and buffer contracts are GC-007/GC-009's;
  the derivation result intentionally exposes slots/supports/recipe hashes only.
- **Per-slot selection overrides** (decision §6.3) and **per-slot ordering keys** are modelled per capability.
- **`Explain` state dispositions.** `ExplanationPage.StateDispositions` is passed as null: state-slot dispositions
  are computed from the ownership/state model (GC-007, GC-008), not from derivation. The derivation record that
  *is* available (recipe hash, support ids, stratum, mode gate) is populated.
- **The `Ineligible` explanation gap** for a target that advertises neither a selector nor the output capability
  (decision §6.1). If the orchestrator prefers an explicit record for every untouched target, the change is in
  `DerivationSnapshot.TargetsInReach` + `DerivationPolicy.Evaluate` and the oracle already does it.
- **Local-only rule granularity.** Rules whose reach is `LocalOnly` are evaluated for the installation scope
  only; 07's chapter story uses descendant rules everywhere, so no fixture depends on a narrower reading.
