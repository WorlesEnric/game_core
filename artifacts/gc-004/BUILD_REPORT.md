# GC-004 Linux build report

## Verdict

**Release build: Pass. NUnit solution run: 98 passed, 0 failed, 0 skipped. Full P-050 conformance: Blocked by the frozen cancellation response seam; a separate cancellation-identity probe fails.**

All 31 existing W0 tests remain green. The original 59 composition tests remain present; eight regression tests were added. No test was deleted, skipped, ignored, or disabled. Expected-outcome corrections are justified individually below.

Behavioral changes stopped when the cancellation seam limitation was demonstrated, per the worker brief. Nothing under `tests/GameCore.ReferenceSeams/**`, including the API snapshot, was changed. No Unity command was attempted and the Unity manifest was left unchanged.

This is build/test evidence for the pure GC-004 implementation, not a V1 conformance claim or a W1 integrated Unity gate.

## Baseline and toolchain

- Worktree: `/home/worlesenric/wkspace/gc-wt/gc-004`; branch `gc-004`.
- Requested initial synchronization executed successfully: `git fetch origin && git checkout gc-004 && git reset --hard origin/gc-004`.
- Starting revision: `c11e7a2` (`GC-004: tests, handoff and interpreter static-check evidence`). No other worktree was touched.
- Host: Ubuntu 24.04, Linux x86_64; complete kernel identification in [host.log](host.log).
- .NET SDK **8.0.425**, MSBuild **17.11.48+02bf66295**, .NET host/runtime **8.0.31**, RID `linux-x64`; [dotnet-info.log](dotnet-info.log).
- VSTest **17.11.1**. Resolved NuGet dependencies for each test project: [resolved-packages.json](resolved-packages.json).
- Production composition and reference seam assemblies target **.NET Standard 2.1**. Effective language version **C# 9.0**, nullable enabled, warnings treated as errors. No language/target relaxation was made; [compiler-settings.json](compiler-settings.json).
- The standalone smoke inspected actual compiled assembly references: `GameCore.Composition` references only `netstandard` and `GameCore.ReferenceSeams`; the latter references only `netstandard`. Neither references Unity assemblies; [smoke.log](smoke.log).
- Environment for every dotnet invocation: `DOTNET_ROOT=$HOME/.dotnet`, `$HOME/.dotnet` prepended to `PATH`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`. Exact expanded values are recorded in [build-commands.json](build-commands.json).
- SHA-256 comparison against the synchronized baseline found every frozen seam file unchanged: [frozen-seam-integrity.json](frozen-seam-integrity.json). The compiled API snapshot tests also passed.

## Commands and evidence

All build, test, toolchain and smoke argv arrays, exit codes, working directory and log paths are recorded in execution order in [build-commands.json](build-commands.json). Commands were run with captured stdout/stderr, not inferred from static checks.

From the repository root, the final required commands were:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx \
  --results-directory /home/worlesenric/wkspace/gc-wt/gc-004/artifacts/gc-004/tests-final
```

Other executed commands:

```sh
dotnet --info
uname -a
dotnet msbuild dotnet/src/GameCore.Composition/GameCore.Composition.csproj \
  -getProperty:LangVersion,TargetFramework,TreatWarningsAsErrors
```

The same full-solution build was run three times before final verification, with logs `build-initial.log`, `build-second.log`, `build-compiled.log`. Full-solution tests used the same command as above with result directories `tests-initial`, `tests-second`, `tests-third`, and `tests-reviewed` respectively. Composition-only review runs used:

```sh
dotnet test dotnet/tests/GameCore.Composition.Tests/GameCore.Composition.Tests.csproj \
  -c Release --logger trx \
  --results-directory /home/worlesenric/wkspace/gc-wt/gc-004/artifacts/gc-004/review-regressions-before

dotnet test dotnet/tests/GameCore.Composition.Tests/GameCore.Composition.Tests.csproj \
  -c Release --logger trx \
  --results-directory /home/worlesenric/wkspace/gc-wt/gc-004/artifacts/gc-004/review-regressions-after

dotnet test dotnet/tests/GameCore.Composition.Tests/GameCore.Composition.Tests.csproj \
  -c Release \
  --filter 'FullyQualifiedName~UnrelatedCleanupCannotChangeAnEarlierOperationResult|FullyQualifiedName~ReconfigurationRetiresOnlyTheOldActivationResources|FullyQualifiedName~UnmountPublishesCleanupErrorsWithRetainedReferences' \
  --logger trx \
  --results-directory /home/worlesenric/wkspace/gc-wt/gc-004/artifacts/gc-004/cleanup-before
```

Standalone executable probes, not NUnit tests:

```sh
dotnet run --project /tmp/gc-004-smoke-qjkev9kn/Smoke.csproj -c Release
```

The second probe's exact generated temporary project path is in `build-commands.json` under `cancellation-seam-blocker.log`. Both temporary net8.0/C# 9 projects referenced the real composition project. The first exercised scope staging, publication and terminal retransmission, then inspected assembly references. The second exercised conflicting reuse of a cancellation identity, described below. Temporary projects and their build outputs were removed after execution; their output remains committed.

Evidence files are all below 2 MB individually; none needed trimming. Initial failures and failing review reproductions are retained, not replaced by green logs. No API snapshot was regenerated. No Unity `packages-lock.json` was generated. The W0 protocol fixture run regenerated `artifacts/protocol-fixtures/results.json` (58 executed data cases, all passing).

## Real results

### Final NUnit results

| Project / fixture | Pass | Fail | Skipped / NotRun | Status |
|---|---:|---:|---:|---|
| ReferenceSeams / `ApiSnapshotTests` | 3 | 0 | 0 | Pass |
| ReferenceSeams / `SeamContractTests` | 18 | 0 | 0 | Pass |
| ProtocolFixtures / `ProtocolFixtureTests` | 10 | 0 | 0 | Pass |
| Composition / `ScopeTreeTests` | 9 | 0 | 0 | Pass |
| Composition / `ConfigurationTests` | 7 | 0 | 0 | Pass |
| Composition / `ServiceResolutionTests` | 16 | 0 | 0 | Pass |
| Composition / `ControlLaneTests` | 20 | 0 | 0 | Pass |
| Composition / `ResourceGateTests` | 7 | 0 | 0 | Pass |
| Composition / `BuildHostRegressionTests` | 8 | 0 | 0 | Pass |
| **Total** | **98** | **0** | **0** | **Pass** |

Every individual test name, outcome, duration and source TRX path is in [test-results.json](test-results.json), including all earlier runs. Raw final TRX files are under [tests-final/](tests-final/); console output is [tests-final.log](tests-final.log). The 58 protocol data cases are nested within the 10 ProtocolFixture NUnit tests, **not** 58 additional NUnit tests.

### Execution history

| Run | Pass | Fail | Result |
|---|---:|---:|---|
| Initial build | — | 7 compiler errors | Fail; composition runtime |
| Second build | — | 6 compiler errors | Fail; composition tests |
| Compiled build | — | 0 errors, 0 warnings | Pass |
| First full test run | 53 | 37 | Fail; all 31 W0 tests passed |
| Second full test run | 85 | 5 | Fail; all 31 W0 tests passed |
| Third full test run | 90 | 0 | Pass; original test inventory |
| Review regression reproduction | 58 | 7 | Fail; composition only |
| Review regression confirmation | 65 | 0 | Pass; composition only |
| Cleanup reproduction, filtered | 1 | 2 | Fail; real retained-result/teardown defects |
| Reviewed full solution | 98 | 0 | Pass |
| Final Release build | — | 0 errors, 0 warnings | Pass |
| Final full solution | 98 | 0 | Pass |
| Standalone publication/reference smoke | 1 scenario | 0 | Pass, exit 0 |
| Cancellation identity seam probe | 0 | 1 scenario | **Fail, exit 1; correct completion Blocked by frozen response contract** |
| Unity EditMode / PlayMode / IL2CPP | 0 | 0 | **NotRun**, explicitly excluded |

## Fixes made

### Compiler compatibility — commit `16d30f3`

1. `Runtime/Configuration.cs`: return an empty provenance array, not null, through the non-null `ConfigComposeResult.Composed` parameter. Keeps nullable warnings-as-errors intact.
2. `Runtime/Services/ServiceResolver.cs`: explicitly convert stable raw IDs between `PluginInstanceId` and `ProviderInstallationId` when constructing provider lists and service bindings. The frozen types are intentionally distinct.
3. `Runtime/Operations/CompositionEditApplier.cs`: explicitly convert an imported provider identity to the installation lookup's `PluginInstanceId`.
4. `Runtime/Operations/CompositionState.cs`: encode nonnegative collection counts as `uint`, matching the frozen envelope writer.
5. `Runtime/Operations/CompositionHost.cs`: stop assigning an `AssemblyEpoch` to an `ActivationEpoch`. Initially used the installation's first epoch; the later candidate-staging fix reads the actual planned installation stamp.
6. `Tests/ServiceResolutionTests.cs`: supply the missing required null configuration argument at five mount callsites.
7. `Tests/ControlLaneTests.cs`: unwrap nullable `ModeEdit.Value` before accessing `NewMode`. No assertion expectation changed for compilation.

### Original runtime failures — commit `f33d1aa`

8. `ConfigurationCodec.TryDecodeEntry`: validate the nested `ConfigEntry` schema, not the outer `ConfigDocument` schema. This fixed configuration and edit-payload round trips.
9. `CompositionHost`: advance the staged tail's revision/epoch when publication succeeds. Previously every subsequent operation using the correct committed revision planned against revision zero and failed.
10. `CompositionHost.SubmitInternal`: settle planning failures as `Rejected(plan.Code)` immediately and do not advance the staged tail. Previously they were exposed as admitted with `None`, hiding genuine configuration, scope and service errors.
11. `CompositionHost.StagedPlan`: return only pending plans, not cancelled/terminal proposals.
12. `ServiceResolver.FindSelected`: compare the wrapped raw stable IDs, rather than boxed equality between different wrapper types; valid explicit selections previously failed.
13. Required closure: include waiting registrations when building the dependency graph, so mounting the other half of a cycle rejects the proposal. Resolve providers before consumers and propagate actual resolved availability through a chain, rather than relying on previous publication state/canonical ID order. Explicitly inactive states are not automatically activated by ordinary dependency resolution.
14. Optional fallback: preserve an explicit missing-service diagnostic when using the declared fallback. The fallback binding already existed; its diagnostic was silently omitted.
15. `ManagedResourceLease.Dispose`: distinguish an attempted release from a successful release. A throwing disposer no longer sets `IsDisposed` true and a later retirement cannot turn that failed attempt into a false successful disposal. The disposer still runs at most once.
16. Corrected conflict/self-cycle expectations and mode comparison input identities as documented below.

### Review-discovered failures and test improvements — commit `da1c4d0`

17. `OperationLedger`: retain the original submitted-against handle across settlement/retransmission; publication no longer changes handle identity.
18. `OperationLedger.IsExpired`/admission/cancellation: consult retained issuer high-water marks after bounded tombstones are forgotten. Absent lower/equal sequences, including unseen ones, report `ResultExpired` as P-050 requires. Remove step-expired IDs from the settled-order list; remove the obsolete `AdmissionKind.SequenceViolation` result variant. The sequence-violation counter remains observable.
19. `CompositionHost.Publish`: prevent direct publication from overtaking the earliest pending proposal; preserve the committed logical step if it advanced after planning.
20. Planner: reject operations stamped for another world with `StaleHandle` before a scope-tree mutation can publish.
21. Resource preparation failure: release staged acquisitions, settle the operation `Rejected`, and rebuild dependent proposals. The old implementation released the resources but left the resource-less mount pending and publishable.
22. `StageResource`: take generation/activation epoch from the planned candidate, not the old committed activation; reject non-active/mismatched owners rather than manufacture a token for an unknown installation.
23. Resume: avoid treating `Preparing -> Preparing` as a second lifecycle transition. Explicit resume can now reach resolution/publication.
24. Lifecycle closure: collect retiring activations for suspension, required-provider loss and reconfiguration, including affected consumer policies. Remove callback authority at publication; renew activation epochs when previously inactive installations become active. Inactive installations expose no active service bindings. Re-resolve bindings when candidate epochs change.
25. Resource retirement: match the old generation/activation stamp so retiring an old activation does not dispose the newly staged successor's leases. Provider/consumer and reverse-acquisition retirement ordering is retained.
26. Operation results: retain each attempt's cleanup report instead of reconstructing old results from the world's current resource ledger. An unrelated later failure no longer adds quarantine references to a previously clean result. Failed-release quarantine IDs are carried by cleanup reports, including cancellation/preparation cleanup.
27. Removal status: retain `Retiring` while resources remain owned/quarantined instead of reporting `Disposed`. Cleanup errors rebuild the staged tail against the actual committed retirement status.
28. Strengthened failed-preparation and reverse-acquisition assertions; corrected the backwards private-ancestor setup; require successful admission in the service fixture's mount helper; make subtree movement change depth; require an actual active binding before comparing modes; replace the weak nonempty plan-hash assertion with same-input/base equality and changed-base inequality.
29. Added eight behavior regressions in `BuildHostRegressionTests`: suspension/resume authority, transitive provider loss/return, terminal handle identity, admission-ordered direct publication, foreign-world rejection, forgotten/unseen expired sequences, immutable cleanup results, and old-versus-candidate resource lifetime. Seven review failures and two cleanup failures were reproduced before the corresponding fixes; the reconfiguration resource test confirmed the already-applied candidate-lifetime fix.

No runtime target/framework change, new production dependency, frozen seam modification, automatic retry, Unity integration, or automatic derivation feature was added.

## Expected-value and fixture corrections: specification evidence

| Test / change | Why the original expectation/setup was wrong |
|---|---|
| `ReparentCyclesAndCrossAncestryMovesAreRejected`: self-parent `OwnershipConflict` -> `Cycle` | P-010 requires an acyclic tree; O-02 explicitly rejects ancestry cycles. A node pointing to itself is a one-edge cycle. No ownership ambiguity exists. |
| `SameScopeDuplicateSingleBindingsAlwaysConflict` and `TwoVisibleAncestorProvidersConflictWithoutAnOverride`: published Waiting -> rejected ServiceConflict | P-011 identifies these graphs as `ServiceConflict`; 00 §9 says failures before live writes return `Rejected(code)` and retain the old revision. O-03 distinguishes **missing service -> Waiting** from **invalid graph -> rejection**. New assertions also require no pending drain publication, no consumer installation, and unchanged old composition. |
| `ExplicitSelectionIsHonoredInsideTheBoundaryAndNeverFallsBack`: invisible selected provider publishes Waiting -> rejected ServiceConflict | P-011 allows explicit selection only inside the visibility boundary. O-03/00 §9 require invalid graph rejection; the visible previous composition must remain unchanged. |
| `ServiceResolutionIsIdenticalInBothPropagationModes`: identical ID seed in both runs | P-011 requires changing the mode not to change resolution. The original fixture changed the provider/scope identity domain too, then compared identities as strings. No expected provider was relaxed; the corrected test additionally requires one real binding to the intended provider. |
| `ReorderedIssuerSequenceIsRefused`: SequenceViolation/StalePlan -> ResultExpired/ResultExpired | P-050 explicitly states that **any absent ID at/below high-water returns ResultExpired, including unseen out-of-order submissions**. StalePlan is for stale planning input, not this retention rule. |
| `UnmountPublishesCleanupErrorsWithRetainedReferences`: Disposed -> Retiring | P-048 forbids false Disposed while retained/quarantined resources remain. 06 §1 permits Retiring -> Disposed only when all resources settle. Publication still succeeds with cleanup errors; no rollback is implied. |
| `PrivateProviderInAnAncestorIsInvisible`: provider moved to root, consumer to child | The old setup put the provider in a child and consumer at root, proving lack of descendant search, not privacy of an ancestor. This corrects the scenario; Waiting remains the expectation. |
| `ReparentPreservesDescendantIdentityAndRecomputesDepth`: destination now one level deeper; expected depths 2/3 -> 3/4 | The old move used equal-depth parents, so a broken implementation that never recomputed depths could pass. P-010 tree depth and P-025 movement require the changed depths for the new fixture. Identity assertions remain. |
| `PlanHashIsCanonicalForTheSameDeclarationAndBaseRevision` | Original test never compared two hashes and checked only nonempty hash plus a separate stale-revision rejection. New assertions directly enforce the P-027/05 semantic plan-identity contract. The dedicated stale-revision test remains unchanged. |

## Frozen seam blocker: cancellation identity admission

**Observed failure**, not a conjecture: [cancellation-seam-blocker.log](cancellation-seam-blocker.log), exit 1.

Reproduction against the actual host:

1. Admit two independent scope edits with `(world, issuer, sequence 1)` and `(world, issuer, sequence 2)`; leave both pending.
2. Call `Cancel(cancellationId=(world, issuer, sequence 3), firstEdit)` -> `Cancelled`.
3. Reuse that **same cancellation ID** with a different target: `Cancel(cancellationId, secondEdit)` -> `Cancelled` again. The second edit is now cancelled too.
4. `Read` for the cancellation identity returns `Unknown`.

P-050 applies to every mutating public operation: step 3 must reject `IdempotencyConflict` without executing the different request, and the original cancellation attempt must remain retrievable. `CompositionHost.Cancel` currently discards `cancellationOperation`; the original cutoff test repeats one cancellation ID against several different targets and does not test this contract.

The frozen surface in `tests/GameCore.ReferenceSeams/Contracts/HostContracts.cs` is:

```csharp
CancelOutcome Cancel(OperationId cancellationOperation, OperationId target);
```

The frozen `CancelOutcome` enum in `Manifest/ManifestEnums.cs` contains only `Cancelled`, `TooLate`, `Unknown`, `ResultExpired`. It has no `IdempotencyConflict`/general admission rejection or attempt handle. Pretending conflicting input is `TooLate`, returning a false `Cancelled`, throwing instead of the nonthrowing operation protocol, or overwriting the original attempt would not be a correct fix. An agreed refusal/status channel is required at the interface gate before completing cancellation admission. No frozen enum, interface, stub, or API snapshot was edited; no workaround silently weakens the requirement.

**Required decision:** the seam owner must specify how cancellation-operation admission failures (conflict, capacity, invalid session) are returned separately from the target's cutoff outcome. Then the host can ledger the cancellation ID/hash and test changed-target reuse, same-input retransmission, capacity and sequence admission. This is a conformance blocker, **not a compiler blocker**; the requested frozen surface compiles as-is.

## Test meaningfulness review

All four original composition test files and their fixture helpers were read, not merely their names. The following records what the tests establish and what they do not. Passing a test below is not evidence for the broader unexercised behavior in its name or the handoff.

### P-010 scope membership / TEST-004, TEST-008

- `RootScopeIsTheOnlyScopeWithoutAParent` checks the initial root and an unknown lookup, but never attempts a second root or removal/reparenting of the root.
- `CreatedScopeCarriesMembershipAndInheritsItsParentDepth` checks scope count/depth/revision, **not installation or target membership**, despite its name.
- `ReparentCyclesAndCrossAncestryMovesAreRejected` covers descendant and self cycles, not an actual cross-world scope move. The new foreign-operation test covers a foreign operation stamp, not target/scope-handle collision between two populated worlds.
- `ReparentPreservesDescendantIdentityAndRecomputesDepth` now genuinely changes and verifies depths. It still does not verify service rebinding after moving a subtree or preserved mutable target state.
- `RemovingANonemptyScopeRequiresAnExplicitSubtreeDisposition` contains child scopes, not targets or installed plugins. `RetirementRunsConsumersBeforeTheirProviders` separately covers installed plugins removed with a subtree. No ReparentTo removal path is exercised.
- `IsolationSetCannotNameContractsWhileDeclaringAll` checks the rejection code but not a subsequent drain/unchanged snapshot. `IsolationIsStoredPerScopeAndVisibleInTheSnapshot` is useful storage evidence; it is not capability-derivation isolation evidence.
- `DuplicateScopeIdentityIsRejectedAsAnOwnershipConflict` meaningfully checks rejection and unchanged revision.

### P-011 service visibility / TEST-003, TEST-006

- `PrivateProviderInAnAncestorIsInvisible` had a reversed topology; corrected as above. `SiblingScopesNeverSeeEachOthersProviders` meaningfully checks waiting and empty bindings in sibling scopes.
- `ExportedProviderReachesDescendantsIncludingTheProvidersOwnScopeTargets` mounts **installations**, not targets; it checks both activation states but inspects only the descendant binding. It does not prove TEST-004 target propagation.
- `ServiceIsolationBoundaryBlocksAncestorProvidersButNotBoundaryProviders` is meaningful for the named contract and boundary-local replacement. It does not check an unrelated contract remains available or run this isolation topology in both modes.
- The three corrected conflict/explicit-selection tests now verify rejection and preservation. `SameScopeDuplicateSingleBindingsAlwaysConflict` checks the conflict when a consumer is mounted after both providers; it does not establish conflict handling when adding a duplicate to an already-active consumer, or when an explicit selection accompanies duplicate providers.
- `NearestAncestorProviderWinsWhenItDeclaresTheOverride` tests the nearest provider **at the consumer's scope** plus an outer provider, not a longer strict-ancestor-only chain.
- `MultiBindingReturnsEveryVisibleProviderInCanonicalOrder` checks exact provider identities/order for three same-scope providers. It does not test multi-binding across isolation boundaries, mixed single/multi declarations, or optional multi-binding availability changes.
- `ExplicitSelectionIsHonoredInsideTheBoundaryAndNeverFallsBack` now tests allowed and invisible selections, but not an unknown selected ID or an optional selected dependency with fallback.
- `IncompatibleContractVersionLeavesTheConsumerWaiting` checks states, not empty bindings, version diagnostics, or inclusive version-range endpoints.
- `SelfOnlyDomainNeverResolvesAnAncestorProvider` has both negative ancestor and positive same-scope cases; no `WorldImported`-specific case exists.
- `ServiceResolutionIsIdenticalInBothPropagationModes` now requires a real binding before comparison. It covers one exported-provider topology, not every visibility/conflict/isolation variant in both modes.
- `ResolutionIsIndependentOfMountInsertionOrderForFixedSeeds` compares eight permutations through a semantic projection. The mount helper now asserts admission. The projection deliberately does not compare diagnostic payloads, configuration, activation history or leases; do not call it full snapshot equality.

### P-012 dependency closure / TEST-003, TEST-015

- `WaitingInstallationActivatesWhenItsProviderAppearsAndWaitsAgainWhenItLeaves` originally checked only lifecycle state and final empty bindings. The new transitive-chain regression adds callback-authority removal, provider return and stale-activation rejection.
- `RequiredDependencyCycleRejectsTheWholeProposal` is meaningful and caught the waiting-node graph defect. It tests a two-node required cycle, not longer/multi-binding cycles or policy/migration refusal in a dependency closure.
- `OptionalAbsenceIsExplicitAndRebindsToItsDeclaredFallback` tests an **initial absence only**. There is no real provider mounted and then lost, so “rebinds” is not proven. Its original diagnostic assertion was only `Count > 0`; no fallback identity/provenance is asserted.
- `RetirementRunsConsumersBeforeTheirProviders` genuinely records consumer/provider disposal order and zero retained counts, but only for a two-node subtree removal, not 1,000 churn cycles or provider loss while unrelated consumers survive.

### P-013 modes and grant data

- `ModeSwitchPublishesOneSettingAndKeepsAutomaticAsTheDefault` checks default Automatic, staging visibility and one switch to Conservative. It does not exercise switching back, stored grants surviving both directions, conflicts retaining the prior mode, or existing/future target derivation.
- `ConservativeGrantDataIsStoredAndValidatedAgainstRealProviders` actually runs the host in Automatic and checks stored imports plus an unknown provider rejection. This is useful GC-004 storage evidence, not a Conservative eligibility predicate test or proof of provider capability export validity.
- Automatic/Conservative target derivation remains the later derivation task's scope under 09 GC-004's explicit non-goals. None of these tests establishes full TEST-004 or TEST-006.

### P-046 lifecycle / TEST-015, TEST-016

- `RemountingAStableInstanceIdentityAdvancesItsInstallationGeneration` tests generation increment and Active state, but not dispatch of an old remount token. The new suspend/resume and reconfiguration tests cover stale activation tokens on those separate paths.
- No original test called Suspend/Resume or exhaustively exercised `InstallationStateMachine`; the handoff's broad lifecycle-edge claim was unsupported. The new regression found and fixed both leaked authority and broken resume. Exhaustive legal/illegal/repeated-edge coverage still does not exist.
- `ReconfigurePreservesStateAndIncrementsOnlyTheActivationEpoch` checks generation/config revision/activation and world counters. It has no mutable runtime state to preserve. New reconfiguration coverage adds actual old/candidate resource lifetime, not ECS slot migration.
- `PreparedLeaseStaysInertUntilPublication` meaningfully checks gated dispatch before/after publication for the initial activation. Candidate-epoch resource tokens now have a separate reconfiguration regression. It does not test all foreign-world/fence/stale callback permutations.
- `FailedPreparationReleasesEarlierStagedAcquisitions` originally stopped after checking resource release; it missed the still-publishable failed mount. It now verifies terminal rejection, no publication, no installation, and unchanged epoch.
- `RetiringAnInstanceDisposesEachLeaseOnceInReverseAcquisitionOrder` originally checked counts only and could pass with forward disposal. It now compares the actual complete reverse disposal log. It drives `ResourcePreparationSet.ReleaseStaged`, not `ResourceLedger.RetireInstance`; host retirement order has separate coverage.
- `FailedReleaseQuarantinesTheResourceInsteadOfReportingADisposal` caught false successful retirement; `UnmountPublishesCleanupErrorsWithRetainedReferences` now correctly expects Retiring and retained quarantine. Neither establishes native-job quarantine/release safety.
- No 1,000-cycle churn, 100 late callbacks, unfinished jobs, world restart/recovery, or complete TEST-016 failure-boundary matrix was run.

### P-050 / P-051 operation admission

- `SameOperationIdAndSameInputCoalesceToOnePublication` covers a pending retransmission. The new terminal-retransmission test was necessary: original handle identity changed after settlement.
- `ConflictingReuseIsRejectedAndKeepsTheOriginalResult` meaningfully checks the original row/revision. It tests the package-local `SubmitEdit` result, not every frozen public mutation/error channel.
- Count/step expiry tests cover one retained-result window. New forgotten/unseen-ID coverage catches the high-water rule after tombstone eviction. No long-run bounded registry/issuer-registration test is present.
- `LaneCapacityRefusesFurtherAdmission` tests a full lane with one pending row, not capacity recovery after terminal retention or many distinct issuers. No bounded-admission churn claim is made.
- `CancelBeforeTheCutoffIsCancelledAndImmediatelyAfterIsTooLate` tests before publication and after the entire drain. It does not place a latch exactly at Applying, test race permutations, or ledger cancellation identity; its changed-target reuse hides the blocker above.
- `CancellingARequiredStepRejectsTheDependentProposal` meaningfully tests a cancelled parent-scope dependency, not resourceful dependent candidates whose plan changes but remains valid.
- `SeveralAdmittedProposalsPublishInAdmissionOrder` tests Drain order; the new direct-Publish test was necessary to prevent overtaking through the other public method.
- `StagedProposalIsInspectableWhileTheCommittedRevisionIsUnchanged` is useful pure staging/visibility evidence. It does not establish concurrent observer atomicity across ECS bindings/schedules/state.
- `NoChangeIncrementsNothing`, `PublicationMovesRevisionAndEpochButNeverTheLogicalStep`, `StaleExpectedRevisionIsRejectedWithoutPublishing`, `UndecodableProposalIsRejectedThroughTheFrozenSeam`, and `SeamSubmissionRequiresTheDeclaredKindToMatchTheSubject` meaningfully assert their narrow outcomes. They do not prove all unsupported variants, immutable request buffers, or counter-exhaustion behavior in the actual host.
- The original `PlanHashIsCanonicalForTheSameDeclarationAndBaseRevision` asserted too little; now directly corrected. Other semantic hash inputs (e.g. catalog changes, exclusions with equal counts) are not covered by it.
- `PayloadEncodingRoundTripsEveryField` checks several nested collection **counts**, not every nested value; full encoded-input hash equality is useful but does not replace independent expected nested-value assertions. Comparing byte zero is redundant envelope evidence, not payload correctness.

### P-052 diagnostics

No composition test establishes the full structured diagnostic contract. Negative tests generally assert only `DiagnosticCode`/`CodeText` or a positive diagnostic count. Missing assertions include world/operation/plan identity, involved provider/schema/rule IDs, minimal conflicting sets, provenance/retrieval keys, phase, count/budget values and retry classification. The generic closure rejection and optional/waiting diagnostics are not proof of complete P-052 payloads. W0 DTO/API tests prove the fields exist, not that this host populates them correctly. Full diagnostic conformance remains **NotRun / not established**, despite code-level rejection tests passing.

### Additional configuration-test weaknesses

Although P-020 was not the requested focus, its tests were part of the full run:

- `FieldMaskDistinguishesNullFromMissingAndUnionsSets` checks a union's size rather than exact members, and cleared provenance only by positive count; it does not independently verify the untouched omitted field/provenance.
- `ComposedDocumentHasAStableCanonicalHashAndRoundTrips` uses one text field, not all value kinds, reordered multi-field documents, malformed schema versions or duplicate encoded keys.
- `MountStoresTheComposedEffectiveConfiguration` compares a hash produced through shared composition helpers rather than independently asserting effective values.
- `ConfigDocumentRejectsDuplicateFieldKeys`, `UndeclaredConfigurationFieldIsRejected`, and `DeclaredConfigurationHashMustDescribeTheComposedDocument` meaningfully test their stated rejection boundaries. They do not exhaust manifest/configuration validation.

## Named 08 suites: qualified status

| Suite | Executed GC-004 evidence | Status of broader suite |
|---|---|---|
| TEST-002 | IDs/handles, revision/epoch, retries/expiry, foreign operation, activation staleness; W0 identity fixtures | Pure subset Pass; native handle reuse/world restart and host counter exhaustion NotRun; cancellation identity probe Fail/Blocked |
| TEST-003 | 16 service tests plus chain/configuration regressions | Pure subset Pass; complete replacement/fencing/version/manifest matrix NotRun |
| TEST-004 | Scope structure/storage only | Target derivation NotRun; no claim of full suite Pass |
| TEST-006 | Isolation storage/resolution, one-way mode change, simple service-mode equivalence | Pure subset Pass; capability exclusion/Conservative target grants/future targets NotRun |
| TEST-008 | Scope reparent/cycle/nonempty removal and subtree installation retirement | Pure subset Pass; 50 x 500 oracle/index/membership/state differential workload NotRun |
| TEST-009 | Staging visibility, stale/no-op/retry/publication order, pre/post cutoff | Pure subset Pass; concurrent observer/ECS fence/migration/structural application NotRun |
| TEST-015 | Managed gate/lease teardown, order, suspension/resume, dependency authority, quarantine | Pure subset Pass; exhaustive lifecycle edges/native jobs/churn/late-callback matrix NotRun |
| TEST-016 | Acquisition failure, cancellation cleanup, throwing disposer, old/candidate activation | Pure subset Pass; complete live-write/fault/recovery matrix NotRun |

Unity is **NotRun by explicit task instruction**. The composition Unity manifest dependency cannot resolve production `GameCore.Contracts` on this branch before the W1 integration gate. Neither Editor availability nor licensing was tested; neither is represented as a passed or failed check. No Unity manifest/lockfile edit was attempted.

## Commits and publication

Logical fix commits:

- `16d30f3` — compile against frozen reference seam types.
- `f33d1aa` — original publication, configuration codec, service closure and disposal defects.
- `da1c4d0` — review regressions: activation authority, resource/result lifetime, admission identity and test improvements.

Build/test evidence and this report are committed separately after those fixes. Publication command: `git push origin gc-004`. The push outcome is reported by the worker's final response rather than pre-claimed here.

The implementation handoff remains historical; this report and its executable evidence supersede its “nothing compiled” status and qualify its coverage/assumption claims. Frozen seam reopening is required to resolve the cancellation blocker; it is not hidden behind the green NUnit count.
