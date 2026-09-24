# GC-004 HANDOFF — scopes, services and serialized control admission (Wave 1)

Branch: `gc-004` (worktree `/Users/yangcao/wkspace/gc-wt/gc-004`).
**Status of every executable check: NotRun (pending orchestrator build host).** This machine has no .NET SDK,
no Unity and no Mono, so nothing here has been compiled or executed. Only interpreter-level static checks ran;
they are recorded in `artifacts/gc-004/static-checks.log` and are explicitly not a build result.

## 1. Summary

`Packages/com.gamecore.composition` (assembly `GameCore.Composition`, `noEngineReferences: true`, referencing
`GameCore.Contracts`) implements the composition host of one world: scope membership, manifest install records,
deterministic service resolution, the required dependency closure, immutable configuration patches, the
serialized control lane with its ledger/capacity/expiry/cancellation cutoffs, inert managed resource gates and
the resource ledger, and the `CompositionHost` facade over the frozen W0 `ICompositionHost` seam.

Layering, all Unity-free (so it also compiles under plain dotnet):

| File | Contents |
|---|---|
| `Runtime/Documents.cs` | Canonical document codec (envelope framing, checked lists, id lists), the package's own document schema identities, canonical ordering helpers |
| `Runtime/Configuration.cs` | `ConfigFieldValue`/`ConfigField`/`ConfigDocument`, configuration layers and per-field provenance, the layered composer (mask, null-vs-missing, id-set union, no deep merge) |
| `Runtime/ConfigurationCodec.cs` | Canonical encode/decode of configuration documents plus `HashOf` |
| `Runtime/Scopes/ScopeRecords.cs` | `ScopeRecord`, `ScopeGrants`, `ScopeRegistry` (root, depth, ancestors, descendants, cycle test, add, reparent) |
| `Runtime/Services/ServiceResolver.cs` | `ServiceNode`, `DependencyResolution`, `ServiceNodeResolution`, `ServiceResolution` and the pure resolver: visibility, isolation boundaries, conflicts, explicit selection, multi-binding, fallback, closure order/cycle |
| `Runtime/Operations/CompositionState.cs` | `InstallEntry`, immutable `CompositionState`, snapshot assembly, definition fingerprint, `IPluginManifestSource` |
| `Runtime/Operations/CompositionEditPayload.cs` | `CompositionEditSubject`, `CompositionEditPayload` and its canonical codec |
| `Runtime/Operations/CompositionEditApplier.cs` | `CompositionEditPlan` and the pure applier for O-02…O-08 (plus suspend/resume), state dispositions and delta |
| `Runtime/Operations/OperationLedger.cs` | Bounded ledger: admission kinds, idempotency, issuer sequence high-water, retention/expiry, phases and cancellation cutoffs |
| `Runtime/Operations/CompositionHost.cs` | `EditAdmission`, `PublishedOperation`, the host: submit, drain/publish, cancel, staged resources, results, counters |
| `Runtime/Lifecycle/ManagedResources.cs` | `ManagedResourceGate`, `ManagedResourceLease`, `ResourceLedger`, `ResourcePreparationSet`, `CleanupReport`, `ResourcePreparationException` |
| `Runtime/Lifecycle/CallbackGate.cs` | `CallbackGate` (world → fence → liveness → generation/epoch), `GatedCallbackPath`, `ActivationStamp` |
| `Runtime/Lifecycle/InstallationStateMachine.cs` | The P-046/06 s1 transition table and per-state policy predicates |

Non-goals honoured: no per-frame service lookup, no derivation algorithm, no runtime code loading, no live ECS
write. Nothing in the package references `UnityEngine`/`Unity.*`; the only ECS-shaped work (publication) is
represented as immutable proposals and ownership records.

## 2. Files created

Package (`Packages/com.gamecore.composition/`):

- `package.json`, `Runtime/GameCore.Composition.asmdef`, `Tests/GameCore.Composition.Tests.asmdef`
- `Runtime/Documents.cs`, `Runtime/Configuration.cs`, `Runtime/ConfigurationCodec.cs`
- `Runtime/Scopes/ScopeRecords.cs`
- `Runtime/Services/ServiceResolver.cs`
- `Runtime/Operations/CompositionState.cs`, `Runtime/Operations/CompositionEditPayload.cs`,
  `Runtime/Operations/CompositionEditApplier.cs`, `Runtime/Operations/OperationLedger.cs`,
  `Runtime/Operations/CompositionHost.cs`
- `Runtime/Lifecycle/ManagedResources.cs`, `Runtime/Lifecycle/CallbackGate.cs`,
  `Runtime/Lifecycle/InstallationStateMachine.cs`
- `Tests/Fixtures/CompositionFixtures.cs`, `Tests/Fixtures/ManifestBuilders.cs`
- `Tests/ScopeAndConfigurationTests.cs`, `Tests/ServiceResolutionTests.cs`, `Tests/ControlLaneTests.cs`,
  `Tests/ResourceGateTests.cs` (59 test methods)

Plain dotnet:

- `dotnet/src/GameCore.Composition/GameCore.Composition.csproj` (netstandard2.1; compiles the package Runtime
  sources; `ProjectReference` to `GameCore.ReferenceSeams` for the W1 gate to swap for production contracts)
- `dotnet/tests/GameCore.Composition.Tests/GameCore.Composition.Tests.csproj` (net8.0, NUnit 3.14,
  NUnit3TestAdapter 4.5.0, Microsoft.NET.Test.Sdk 17.11.1; compiles the package `Tests/` sources)

Evidence:

- `artifacts/gc-004/static-checks.log`

Shared files changed (minimal):

- `dotnet/GameCore.sln`: two projects added (`GameCore.Composition`, `GameCore.Composition.Tests`) with new
  solution GUIDs and the four Debug/Release configuration rows each. No existing line changed.
- `unity/GameCore.Validation/Packages/manifest.json`: one dependency line
  (`"com.gamecore.composition": "file:../../../Packages/com.gamecore.composition"`) and a `testables` array
  naming the package. No existing entry changed.
- No file under `tests/GameCore.ReferenceSeams/` was touched, so the frozen W0 surface and its API snapshot are
  unchanged. `dotnet/README.md` was intentionally not edited (it lists the W0 projects; three Wave 1 tasks add
  projects in parallel and it is GC-002-owned).

## 3. Exact commands for the Linux build host

Plain dotnet (from the repository root):

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx
```

GameCore.Composition alone:

```sh
dotnet build dotnet/src/GameCore.Composition/GameCore.Composition.csproj -c Release
dotnet test  dotnet/tests/GameCore.Composition.Tests/GameCore.Composition.Tests.csproj -c Release --logger trx
```

Unity EditMode tests of this package, on the qualification project (`UNITY` = Editor path):

```sh
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Composition.Tests \
  -testResults artifacts/gc-004/unity-editmode-results.xml -logFile artifacts/gc-004/unity-editmode.log
```

The Unity run requires the package to resolve; `unity/GameCore.Validation/Packages/packages-lock.json` is
regenerated by that Editor run and should be committed with the result.

Interpreter static checks (already run on this host; repeatable anywhere with Python 3):

The checks and their exact statements are in `artifacts/gc-004/static-checks.log`. They parse the committed API
snapshot and the package sources; they need no toolchain.

## 4. Requirement and test coverage mapping

Direct owner of (09 "GC-004 · Normative sections"):

| Requirement | Where it is implemented | Executable evidence (test file / test) |
|---|---|---|
| P-007 references and leases | `ServiceBinding` produced by `ServiceResolver` (contract, provider, activation epoch, lease id); `ResourceLedger`/`IManagedResourceLease` | `ServiceResolutionTests.ExportedProviderReachesDescendants…` (epoch-bound binding); `ResourceGateTests.RetiringAnInstanceDisposesEachLeaseOnce…`, `FailedReleaseQuarantinesTheResource…` |
| P-009 manifest | `IPluginManifestSource`, `PlanMount` (missing manifest → `MissingDependency`, plugin-type mismatch → `OwnershipConflict`, config schema defaults required) | `ScopeAndConfigurationTests.UndeclaredConfigurationFieldIsRejected`, `ControlLaneTests.RemountingAStableInstanceIdentity…` |
| P-010 scope membership | `ScopeRecord`/`ScopeRegistry`, `PlanScopeCreate/Reparent/Remove` | `ScopeTreeTests` (7 tests: root, depth, duplicate id, cycle, subtree move, nonempty removal, isolation storage) |
| P-011 service visibility | `ServiceResolver.VisibleProviders`, `SelectBindings`, `SelectWinner`, `FindSelected`, `BlockedByIsolation` | `ServiceResolutionTests` (11 tests incl. sibling invisibility, private vs exported, isolation boundary, same-scope duplicates, override, multi-binding order, explicit selection, `SelfOnly`, mode independence, mount-order independence) |
| P-012 dependency closure | `ServiceResolver.ClosureOrder` (providers before consumers, cycle → whole-proposal rejection), resolver state mapping (`WaitingForDependencies`), optional fallback rebinding | `ServiceResolutionTests.WaitingInstallationActivatesWhenItsProviderAppears…`, `RequiredDependencyCycleRejectsTheWholeProposal`, `OptionalAbsenceIsExplicitAndRebindsToItsDeclaredFallback`; `ResourceGateTests.RetirementRunsConsumersBeforeTheirProviders` |
| P-013 modes and grants | `CompositionHost` default `Automatic`; `ScopeGrants`/`PlanScopeGrants` stores and validates Conservative grant data; `PlanModeSet` (world root only, `NoChange`) | `ControlLaneTests.ModeSwitchPublishesOneSettingAndKeepsAutomaticAsTheDefault`; `ScopeTreeTests.ConservativeGrantDataIsStoredAndValidatedAgainstRealProviders`; `ServiceResolutionTests.ServiceResolutionIsIdenticalInBothPropagationModes` |
| P-020 configuration and overrides | `ConfigDocument`, `ConfigComposer` (three layers, per-field mask, explicit null, id-set union, contradiction rejection), `ValidateConfigDocument`/`ValidateConfigPatch`, `PlanReconfigure` (effective-configuration-only change, activation epoch only) | `ConfigurationTests` (6 tests incl. mask semantics, canonical hash round trip, composed effective mount, undeclared field, declared-hash mismatch, reconfigure preserves generation) |
| P-046 installation lifecycle | `InstallationStateMachine` (exact 06 §1 edges), `PlanMount/Unmount/Suspend/Resume`, `InstallEntry.State`, waiting installations are published | `ControlLaneTests.RemountingAStableInstanceIdentity…`; `ServiceResolutionTests` waiting/activation tests; `ResourceGateTests.UnmountPublishesCleanupErrorsWithRetainedReferences` |
| P-050 cancellation and idempotency | `OperationLedger` (admission kinds, issuer high-water, bounded retention, step window, tombstones), `CompositionEditApplier.InputHashOf` | `ControlLaneTests.SameOperationIdAndSameInputCoalesceToOnePublication`, `ConflictingReuseIsRejectedAndKeepsTheOriginalResult`, `ResultsExpireByCountAndAnExpiredOperationCannotReexecute`, `ResultsAlsoExpireByLogicalStepWindow`, `ReorderedIssuerSequenceIsRefused` |
| P-051 operation discipline | `CompositionHost.Submit/Read/Drain/Publish/Cancel`, `LedgerPhase` cutoff, staged plans readable while the committed revision is unchanged | `ControlLaneTests.StagedProposalIsInspectableWhileTheCommittedRevisionIsUnchanged`, `CancelBeforeTheCutoffIsCancelledAndImmediatelyAfterIsTooLate`, `CancellingARequiredStepRejectsTheDependentProposal`, `SeveralAdmittedProposalsPublishInAdmissionOrder`, `LaneCapacityRefusesFurtherAdmission` |
| P-052 diagnostics | `Diagnostic` values with stable codes (`StalePlan`, `MissingDependency`, `ServiceConflict`, `Cycle`, `OwnershipConflict`, `UnsupportedVersion`, `BudgetExceeded`, `IdempotencyConflict`, `ResultExpired`, `Cancelled`, `ResourceUnavailable`, `MigrationRequired`) | Asserted by code in every rejection test above (e.g. `Is.EqualTo(DiagnosticCode.Cycle)`, `DiagnosticCode.ServiceConflict` via `CodeText`) |
| Immutable proposals, no live writes (DoD) | `CompositionEditPlan` (Before/After/delta/closure/dispositions/hash), `CompositionState` immutability | `ControlLaneTests.StagedProposalIsInspectableWhileTheCommittedRevisionIsUnchanged` (committed snapshot unchanged while staged), `PlanHashIsCanonicalForTheSameDeclarationAndBaseRevision` |

Suites the task names (`08-validation-and-performance.md`), applicable subset here:

| Suite | GC-004 subset delivered |
|---|---|
| TEST-002 identities/epochs/stale references | conflicting reuse keeps the original row; expired results cannot re-execute; issuer sequence reordering refused; publication moves revision+epoch and never the step; unmount/remount advances the installation generation; callback gate rejects foreign world, retired activation and stale activation |
| TEST-003 manifests and service resolution | missing manifest/plugin-type mismatch/undeclared config/partial config rejection; sibling invisibility; isolation boundary (blocked and boundary-local provider); same-scope duplicate conflict; nearest-ancestor override; explicit selection inside and outside the boundary; version-range incompatibility; required-dependency cycle; optional absence explicit; injection-order independence |
| TEST-004 automatic propagation to existing/future descendants | scope tree creation/move/removal with membership in the committed snapshot; waits for GC-006 to consume the grant mode data |
| TEST-006 isolation, exclusions and mode switching | service isolation set stored per scope and enforced in resolution; contradiction (`*` plus named contracts) rejected; mode switch publishes one setting; `Auto`→`Conservative`→stored; resolution identical in both modes; sibling invisibility |
| TEST-008 incremental indexes and subtree movement | subtree reparent preserves descendant identity and recomputes depth; nonempty scope removal requires an explicit `DestroySubtree` disposition; removal retires the affected installations |
| TEST-009 prepared plans and atomic publication visibility | stage → publish separation with committed queries unchanged; stale expected revision rejected without publishing; `NoChange` increments nothing; multi-proposal drain publishes in admission order with one increment each; cancellation before/after the cutoff; counter exhaustion rejects instead of wrapping |
| TEST-015 lifecycle and managed resource teardown | inert gate before publication; gate opens only at publication; late work dropped by a closed gate; staged release on cancellation and on failed preparation; disposal at most once; reverse acquisition order; failed release quarantined and reported; consumers retire before providers; activation retired on removal |
| TEST-016 fault injection and recovery boundaries | preparation failure releases staged acquisitions and leaves the old assembly published; rejection paths keep the previous published epoch; a cleanup failure after publication reports `PublishedWithCleanupErrors` with retained references instead of a false disposal; the callback gate discards work at a closed fence |

Operation rows exercised (`08` operation table): **O-02** (EditScopes), **O-03** (Mount), **O-04**
(ActivateOrResume / waiting activation), **O-05** (ReconfigureOrReplace: generation kept, activation epoch
advanced), **O-06** (Suspend), **O-07** (Unmount), **O-08** (SetMode), **O-09** (BuildPlan semantics as the
pure applier + plan hash), **O-10** (PreparePlan: inert staged leases), **O-18** (CancelOperation), and the
publication side of **O-11** at the composition level. O-01/O-12…O-17/O-19…O-26 belong to GC-005/GC-007+.

## 5. Decisions, assumptions and doc ambiguities

Recorded because 09 asks for the simplest reading consistent with 00:

1. **Suite scope.** GC-004 delivers the *managed, engine-free* subset of TEST-002/003/004/006/008/009/015/016.
   Their Unity-world parts (real Entities worlds, jobs, native lifetime, IL2CPP) remain with GC-005/GC-008+.
2. **Admission vs publication.** `ICompositionHost.Submit` returns a handle before the result exists, so the
   lane is staged: admission → planning → `Drain()` publication. Publication is one property swap plus one
   `CompositionRevision`/`AssemblyEpoch` increment; the logical step never moves (P-006). Cancellation of a
   `Pending` row is `Cancelled` with no new epoch; any settled row is `TooLate`.
3. **Staged pipeline base.** Each admitted proposal is planned against the *tail* of the staged pipeline, while
   `ExpectedRevision` is checked against the **published** revision (P-027/P-028 literally; 00 §9 "checked
   against the last published revision"). Consequence: two proposals admitted in one window publish
   sequentially with two increments, and the second sees the first's result.
4. **Cancellation rebuilds the tail.** After cancelling a pending proposal, the remaining pending proposals are
   replanned against the committed definition; one whose base disappeared is rejected (`MissingDependency`)
   rather than published on a plan that assumed a cancelled scope existed (P-051).
5. **Capacity and retention semantics.** `ControlLaneCapacitySettings.MaxQueuedOperations` bounds the number of
   live ledger rows (the same reading the GC-002 collaborator double uses); `MaxRetainedResults` bounds settled
   rows. Capacity refusal is `BudgetExceeded`. `OperationExpirySettings.RetainedResultSteps` is a logical-step
   window applied by `AdvanceSteps`.
6. **Expired results cannot re-execute, without unbounded tombstones.** Dropping a result tombstones its
   operation id (bounded list) and the issuer's high-water sequence is retained, so a new submission with a
   sequence at or below the high-water mark is refused with `StalePlan`. This is the mechanism that makes
   P-050's sequence rule enforceable after retention has forgotten the row.
7. **`IdempotencyConflict` keeps the original.** A conflicting reuse returns the original row's handle; the
   conflict is observable through `OperationLedger.ConflictCount`/`LastConflictCode` and `EditAdmission.Kind`
   (the row itself is never overwritten, matching the frozen seam's statement and the GC-002 double).
8. **Service resolution domains.** `SelfOnly` restricts to the consumer's own scope; `AncestorsAndSelf` and
   `WorldImported` share the upward domain (own scope plus visible ancestors). A world service is one an
   ancestor installation exports, and declaring the domain is what makes the import explicit (P-011). Sibling
   scopes are never in any domain.
9. **Visibility rule.** A provider in the consumer's own scope is visible whatever its export visibility; a
   provider in a strict ancestor is visible only with `ExportToDescendants`; a service-isolation boundary on
   any scope strictly between provider and consumer blocks named contracts, and a provider installed *at* the
   boundary still works (P-016).
10. **Conflicts.** Same-scope duplicate single bindings always conflict. Across depths the nearest layer wins
    only if its export declares `OverridesAncestor`; otherwise `ServiceConflict`. A contract whose visible
    declarations mix single and multi binding kinds is `ServiceConflict` (a contract cannot be both). Multi
    binding returns every visible provider in canonical provider-identity order.
11. **Explicit selection never falls back.** An instance `ServiceSelection` overrides the dependency's selected
    provider; a selection that names no installation is `MissingDependency`, one that is not visible is
    `ServiceConflict`. For a multi-binding contract, an explicit selection narrows the binding to that provider.
12. **Waiting is a published state, not a failure.** A missing *required* provider yields
    `InstallationState.WaitingForDependencies` with no bindings and a diagnostic; optional absence yields an
    explicit diagnostic and a fallback binding when the manifest declares one (P-011/P-012).
13. **Illegal lifecycle edge code.** 00 §9's code set has no "illegal transition" literal, so a refused edge is
    reported as `OwnershipConflict` (the request claims authority the current state does not grant) and the
    mapping is documented on `LifecycleTransition`. A `TransferTo` slot without a declared transfer mapping is
    `MigrationRequired`.
14. **Default identity rejection.** A declaration naming a default (zero) identity is `MissingDependency`
    ("no such stable identity can exist", P-004); a *contradictory* declaration (`*` isolation plus named
    contracts) is `CapabilityConflict`; an undeclared configuration field or a config hash that does not
    describe its document is `UnsupportedVersion`.
15. **Installation generation on remount.** A remount of the same stable installation id (only possible after
    the previous installation was disposed) increments the installation generation, so callbacks from the
    removed activation are invalid even though the id was reused (P-005). Counter exhaustion is
    `BudgetExceeded`.
16. **Effective configuration is stored composed.** A mount stores schema-defaults-plus-local as the
    installation's effective configuration (with its hash re-derived, never trusted), so a later patch composes
    over the same provenance layers instead of layering defaults twice (P-020).
17. **Failed release is quarantined.** A disposer that throws leaves the ledger record `Quarantined` (retained,
    counted as a failed release) and the lease `Readiness = Failed`; the publication outcome becomes
    `PublishedWithCleanupErrors` and the result carries quarantine references. Nothing is reported as disposed
    while it is still retained (P-048).
18. **Teardown order is plan data.** `CompositionEditPlan.RetiredInstances` is already ordered consumers-before-
    providers, computed from the *previous* definition's closure order reversed with a canonical-identity tie
    break, so retirement order never depends on sort implementation details (P-008/P-012/P-048).
19. **Document schemas are name-derived.** `CompositionSchemas` derives its document identities from a
    reverse-domain name via SHA-256's first 16 bytes, so every host and build agrees without a generator; the
    production catalog's generated schema ids must not reuse these names.
20. **`IPluginManifestSource` is a package-local seam.** Mounts resolve `PluginManifest` and config schema
    defaults through it (implemented by the generated catalog in production, by a dictionary in tests). It is
    not part of the frozen W0 surface, so no interface gate is reopened by its introduction.
21. **`ResourcePreparationException` is offered to GC-005.** It is defined in `GameCore.Composition`
    (`GameCore.Unity.Runtime` may reference Composition per 04 §2) so the resource-preparation failure type has
    one home; the frozen seam exposes no such exception type.

## 6. Known gaps

- **Nothing has been compiled or executed.** The build host is the first compiler to see this code. Residual
  risk is concentrated in nullability annotations, overload resolution on the seam DTO constructors and any
  C# 9 subtleties; the interpreter checks in `artifacts/gc-004/static-checks.log` cover syntax balance,
  forbidden constructs, constructed-type resolution, constructor arities, member existence, interface
  completeness and unused-member risks, and they were validated against planted defects.
- The package's Unity EditMode run and the IL2CPP probe are untested here; the `testables` entry is added but
  the Editor has not resolved this package on this host.
- `dotnet/README.md` still lists only the W0 projects (intentional; see §2).
- Service resolution tests exercise the pure resolver through the host; a direct `ServiceResolver.Resolve`
  fixture (bypassing the host) is not included, since the host path is the one that must work.
- No performance or allocation measurement is claimed: the brief's non-goals exclude per-frame lookup, and
  `08` TEST-023 belongs to a later wave with an instrumented standalone player.
- Derivation does not consume the stored `ScopeGrants` yet (GC-006); this task stores and validates the data
  and proves it is inspectable (`ScopeTreeTests.ConservativeGrantDataIsStoredAndValidatedAgainstRealProviders`).

## 7. Proposed seam changes

None. No file under `tests/GameCore.ReferenceSeams/` was modified and the API snapshot is untouched, so the W0
interface gate stays closed. The two shared-file edits are additive (solution projects, Unity manifest lines)
and are listed in §2.
