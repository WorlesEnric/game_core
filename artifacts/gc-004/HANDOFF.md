# GC-004 HANDOFF — scopes, services and serialized control admission (Wave 1)

Branch: `gc-004` (worktree `/Users/yangcao/wkspace/gc-wt/gc-004`).
**Status of every executable check: NotRun (pending orchestrator build host).** This machine has no .NET SDK,
no Unity and no Mono, so nothing here has been compiled or executed. Only interpreter-level static checks ran;
they are recorded in `artifacts/gc-004/static-checks.log` and are explicitly not a build result.

**Round 2 (cancellation identity) supersedes §7 and extends §4/§6:** the seam change it requested has landed,
the cancellation contract of P-050 is now implemented, and the test additions and remaining gaps are recorded
in `§8 Round 2 — cancellation identity and test strengthening` below. Sections 1–7 are the round 1 record and
are kept as written; where they disagree with §8, §8 is current. The Linux build report
(`artifacts/gc-004/BUILD_REPORT.md`) holds the executed round 1 evidence and must be reread for this round:
**every check in §8 is again NotRun (pending the build host).**

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

## 8. Round 2 — cancellation identity and test strengthening

**Status: NotRun (pending orchestrator build host).** Nothing in this round has been compiled or executed here.
The round 1 build report remains the only executed evidence; the build host must rerun
`dotnet build dotnet/GameCore.sln -c Release` and `dotnet test dotnet/GameCore.sln -c Release` to cover it.

### 8.1 Seam change consumed

`main` added `CancelOutcome.IdempotencyConflict = 4` and `CancelOutcome.Rejected = 5` and regenerated
`tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt`. This branch merged them (`8562623`); no frozen
seam file was edited here, so the W0 interface gate is closed again and §7's request is answered.

### 8.2 Cancellation is now a ledgered operation (P-050, P-051, O-18)

`CompositionHost.Cancel` no longer discards `cancellationOperation`. The request is validated, admitted,
decided and recorded in that order:

| Case | Result | State that changes |
|---|---|---|
| Valid new request, target pending | `Cancelled` | target settled `Cancelled`; target's staged resources released; the staged tail is replanned |
| Valid new request, target applying or settled | `TooLate` | nothing; the target's terminal result stands (no rollback) |
| Valid new request, target never on this lane | `Unknown` | nothing |
| Same identity + same target (retransmission) | the **original recorded outcome** | nothing; the cutoff is not applied twice |
| Same identity + different target | `IdempotencyConflict` | nothing; the original row stands and the **second target is not cancelled** |
| Identity reused from an edit | `IdempotencyConflict` | nothing; one operation id names one operation |
| Foreign world, unknown issuer, cross-world target, self-target | `Rejected` | nothing: no ledger row, no retention entry, no issuer sequence consumed |
| Lane at capacity, or an already-used/absent issuer sequence | `Rejected` | nothing, for the same reason |

Implementation: `LedgerRowKind` (`CompositionEdit` / `Cancellation`), a shared `OperationLedger.Admit` for both
kinds, `OperationLedger.CancellationInputHash(target)` (a canonical document under its own schema, so a
cancellation hash can never alias an edit payload hash), `CancelRequest` (admission → decision → settlement) and
`SettlementCancellation`; `CancellationResult` exposes the admission kind, the recorded outcome and the
retrieval key. The publication queue (`PendingInAdmissionOrder`) is filtered to composition edits, so a
cancellation can never occupy or block the publication order. Terminal mapping: `Cancelled` → `Outcome.Cancelled`;
`TooLate`/`Unknown`/`ResultExpired`/`IdempotencyConflict` → `Outcome.Rejected` with `TooLate`/`StaleHandle`/
`ResultExpired`/`IdempotencyConflict` (00 s9: a refusal before live writes is `Rejected(code)`).

New counters for inspection: `CancelRequestCount`, `CancelRetransmissionCount`, `CancelConflictCount`,
`CancelRejectedCount` (plus the existing `CancelledCount`/`TooLateCount`). `AdmissionKind.CapacityRejected` was
renumbered from 5 to 4 to close the gap left when the obsolete sequence-violation variant was removed; no
external consumer exists yet.

### 8.3 The failing probe is now permanent conformance tests

`CancellationIdentityTests` (11 tests) is the probe's permanent form, including
`TheConformanceProbeForCancellationIdentityNowResolvesCorrectly`, which reproduces the recorded sequence
(two pending edits, one cancellation identity, then the same identity aimed at the second target) and asserts
`IdempotencyConflict`, a pending second target, a retrievable cancellation result and a successful publication
of the second edit. The other cases cover retransmission coalescing (including after the target published),
conflicting reuse, edit-identity reuse, every refusal class, capacity refusal, consumed issuer sequence,
self-target, unknown target, the `Applying` latch, and cancellation-hash domain separation.

Two existing tests had to change for the new contract, and the reason is itself the defect the report found:
`CancelBeforeTheCutoffIsCancelledAndImmediatelyAfterIsTooLate` reused one cancellation identity against
different targets, which is now a conflict rather than a second decision; it allocates a distinct, strictly
increasing request identity per decision and asserts the coalesced retransmission separately.

### 8.4 "Asserts too little" items from the round 1 test review

Addressed in this round (each now asserts the behaviour its name claims):

| Review item | What the test now does |
|---|---|
| P-010 root test did not try a second root or a root removal/move | second root → `OwnershipConflict`; root removal → `OwnershipConflict`; root moved under its own descendant → refusal; revision unchanged |
| P-010 membership test asserted depth, not membership | mounts at a leaf and asserts exactly one scope lists it, no ancestor does, and the installation records that one owner scope |
| P-010 removal test had no installation in the subtree | installs inside the subtree, asserts the refusal, the refusal publishes nothing, and `DestroySubtree` reports the installation in `RetiredInstances` and removes it |
| P-010 "no ReparentTo removal path" | adds the lawful two-step path: reparent the member out, then remove the now-empty scope, asserting the moved member's depth and survival |
| P-010 contradictory isolation published-nothing gap | asserts rejection outcome, unchanged revision, previous root isolation intact, empty drain |
| P-011 override test used a shallow chain | three-level chain where the winner is in a strict ancestor, plus a plain-provider control that conflicts |
| P-011 mixed single/multi declarations untested | new test: a mixed declaration of one contract is `ServiceConflict` and publishes nothing |
| P-011 multi-binding across isolation, optional availability | not addressed here (multi-binding isolation variants remain unexercised; see §8.5) |
| P-011 selection did not test an unknown selected id | new case → `MissingDependency`, waiting, empty bindings, never degrading to another provider |
| P-011 optional fallback was never reached from a real provider loss | new test: real binding → provider unmount → binding is the declared fallback with the absence still reported |
| P-011 version test asserted state only | asserts empty bindings and the `MissingDependency` diagnostic; new range test drives both inclusive endpoints and a version just above the window |
| P-012 "optional rebinds" was not proven | same new fallback test |
| P-013 mode switching one-way only | not addressed in this round (both-direction switching and grant survival remain unexercised; §8.5) |
| P-046 lifecycle table never exhaustively exercised | `InstallationLifecycleTests` asserts all 9x9 state pairs against the 06 §1 edge set, no self-edge, `Disposed` terminal, the authority/contribution/definition predicates, and host-level refusal of an illegal edge (resume on Active, repeat suspend) plus the waiting→retiring→disposed path |
| P-046 remount token not dispatched | covered by the existing stale-activation assertions on the suspend/resume and reconfiguration paths; remount generation is asserted |
| P-046 the machine allowed three edges the diagram does not (`Active->Failed`, `Active->Retiring`, `Preparing->Retiring`) and one extra edge (`Preparing->WaitingForDependencies`) | corrected: the table is exactly the diagram's 17 edges, an active installation teardown walks `Active -> Quiescing -> Retiring`, and an active activation no longer becomes `Failed` (P-046 reserves that for a failing candidate). The exhaustive test compares the machine against the 17 edges extracted from the diagram, so the two cannot drift |
| P-046 speculative predicates nothing consulted (`Contributes`, `RetainsDefinition`, `HasExecutionAuthority`, `IsTerminal`, `ChangesActivationEpoch`) | removed; `CanResolveActivation` replaces the resolver's own private state list so the "which states expose bindings" rule has one home, and `Contributes(Quiescing)` no longer contradicted the resolver |
| P-050 capacity had no recovery case | asserts retention frees capacity: refuse while full, expire the settled row, admit and publish the next request |
| P-050 cancellation identity | §8.2/§8.3 |
| P-050 `Applying` latch never tested | `CancellationDecidedAtTheApplyingLatchIsTooLateAndRecordsWhy` holds the target at `Applying` through the lane API and asserts `TooLate` with `Rejected(TooLate)` recorded |
| P-050 payload round trip asserted counts | asserts every nested value (isolation members, exclusion kind/target/scope/subtree, import pair, selection pair, config field kind and value), full byte-for-byte re-encoding, and that a one-field semantic difference changes the input hash |
| P-052 no structured-diagnostic evidence | diagnostics now carry the contract id and the visible provider set as involved ids, the visible-provider count and a retry classification, and the plan's diagnostics are stamped with the owning operation; `DiagnosticContractTests` drives seven real rejections (StalePlan, MissingDependency, OwnershipConflict, CapabilityConflict, Cycle, UnsupportedVersion, MigrationRequired) and asserts code, code text, phase, operation identity, involved ids, count, retry classification, summary and the ledger's recorded code |
| P-020 union size not members; cleared-field provenance by count | asserts the exact union members in canonical order, per-field provenance origin/source for both fields, explicit-null recording, and that an omitted field keeps its inherited value |
| P-020 canonical hash used one text field | new test round-trips all ten value kinds with exact payloads, ordered-vs-set semantics, declaration-order independence, ascending key order, a truncated document and a tampered checksum |
| P-020 mount compared a hash from the same helper | additionally asserts the stored effective value, revision and empty selections from the committed state |

### 8.5 Review items deliberately not addressed in this round

These are outside GC-004's owned surface or belong to a later task's gate; they are recorded rather than
silently dropped. No test was deleted, skipped, disabled or weakened.

| Remaining item | Why it is not GC-004's |
|---|---|
| Target-level eligibility, propagation to existing/future targets, Conservative grant gating, capability exclusion in both modes (TEST-004/TEST-006 remainder) | Derivation is GC-006's owned algorithm; GC-004 stores and validates the grant data only |
| Both-direction mode switching, conflicts retaining the prior mode, stored grants surviving both switches | Needs derivation to produce a mode-gated conflict; the switch itself is already exercised one way |
| Service replacement during use, fencing consumers before releasing old leases, full manifest/version matrix (TEST-003 remainder) | O-05 replacement *policy* for consumers is GC-006/GC-008 (planner + publisher); GC-004 resolves and records bindings |
| Native handle reuse, world restart, 100 late completions (TEST-002/TEST-015 remainder) | Requires Unity worlds and adapters (GC-005/GC-008/GC-019) |
| 1,000-cycle churn, unfinished-job quarantine, complete TEST-016 fault matrix | Unity worlds, jobs and player harnesses |
| Full diagnostic payload contract for every emitted code (involved keys, plans, count/budget on every path) | GC-004 populates what it knows (contract, providers, count, retry); plan-hash and budget fields are filled at publication by GC-007/GC-008 |
| Multi-binding across isolation boundaries, optional multi-binding availability changes | In scope but not yet covered; listed as a known gap rather than claimed |

### 8.6 Files changed in this round

- `Runtime/Lifecycle/InstallationStateMachine.cs`: the edge table is now exactly the 06 s1 diagram's 17
  transitions (an active activation no longer jumps straight to `Retiring` or to `Failed`), plus
  `TryTeardownPath` and `CanResolveActivation`, which the applier and the resolver now consult.
- `Runtime/Documents.cs`: `CompositionSchemas.CancellationRequest`.
- `Runtime/Operations/OperationLedger.cs`: `LedgerRowKind`, shared admission, cancellation hash, `CancelRequest`,
  `SettleCancellation`, `CancellationResult`, `CurrentStep`, cancellation counters, `NoteCancellationRefused`,
  publication-queue filter, `CapacityRejected` renumbered to 4.
- `Runtime/Operations/CompositionHost.cs`: request validation, admission-first `Cancel`, refusal counter.
- `Runtime/Services/ServiceResolver.cs`: dependency diagnostics carry the contract, the visible provider set,
  the provider count and a retry classification.
- `Runtime/Operations/CompositionEditApplier.cs`: diagnostics carry a retry classification; resolver
  diagnostics are stamped with the owning operation.
- Tests: new `CancellationIdentityTests`, `InstallationLifecycleTests`, `DiagnosticContractTests`; strengthened
  `ScopeAndConfigurationTests`, `ServiceResolutionTests`, `ControlLaneTests`.

### 8.7 Round 2 static check (not a build)

`artifacts/gc-004/static-checks.log` was refreshed: 23 files / 11 080 lines, 92 test methods, balanced delimiters,
no forbidden C# 10+ construct or Unity reference, every constructed type resolves against the merged API
snapshot (including the new `CancelOutcome` values) or this package, constructor arities and member accesses all
resolve, interfaces are complete, no duplicate declarations, no unused private members, no
read-only-property assignment, every `[Test]` attribute bound to a method, and the lifecycle edge set asserted
in `InstallationLifecycleTests` equal to the 17 edges extracted from the 06 s1 diagram. Negative controls still
confirm the checker rejects planted defects. Every suite remains NotRun.

### 8.8 Round 2 coverage mapping

The table below lists only what this round changed or added; §4's mapping still holds for everything else.

| Requirement | Implemented by | Executable evidence |
|---|---|---|
| P-050 cancellation identity | `CancellationInputHash`, `Admit(kind)`, `CancelRequest` | `CancellationIdentityTests` (11 tests) |
| P-051 cutoff and refusal channel | `CancelRequest` decision order, `SettledCodeOf`, host validation | `TheConformanceProbeForCancellationIdentityNowResolvesCorrectly`, `CancellationDecidedAtTheApplyingLatchIsTooLateAndRecordsWhy`, `UnadmittedCancellationRequestIsRejectedAndChangesNothing`, `CancelBeforeTheCutoffIsCancelledAndImmediatelyAfterIsTooLate` |
| P-046 lifecycle table | `InstallationStateMachine` (`IsAllowed`, `TryTeardownPath`, `CanResolveActivation`) | `InstallationLifecycleTests.EveryStatePairMatchesTheLifecycleDiagramExactly`, `NoStateIsItsOwnSuccessorAndDisposedIsTerminal`, `RefusalIsAValueAndContributionAuthorityFollowsTheState`, `HostRefusesAnIllegalLifecycleRequestAndKeepsTheInstallation`, `UnmountOfANeverActivatedInstallationIsAllowedAndRetiresIt` |
| P-052 diagnostics | resolver and applier diagnostic construction, `Stamp` | `DiagnosticContractTests` (4 tests, seven real rejections) |
| P-012 optional rebinding | resolver fallback path | `AnOptionalSelectedDependencyRebindsToItsDeclaredFallbackWhenTheSelectedProviderLeaves` |
| P-011 visibility/conflict rules | resolver | `NearestAncestorProviderWinsWhenItDeclaresTheOverride`, `AMixedSingleAndMultiDeclarationOfOneContractIsAConflict`, selection and version tests |
| P-010 scope rules | `ScopeRegistry`, applier | `ScopeTreeTests` (9 tests, incl. new root, membership, disposition, reparent-out cases) |
| P-020 configuration | `ConfigDocument`, `ConfigComposer` | `ConfigurationTests` (8 tests, incl. all value kinds and value-level mount assertions) |
| TEST-002/003/006/008/009/015/016 subsets | as in §4 | as in §4, plus the round 2 additions above |
