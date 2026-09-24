# GameCore.ReferenceSeams — test-only reference seam (GC-002)

**This assembly is test-only.** It is not production code, is not shipped with any plugin, and is
deliberately not a second supported ECS. GC-003 replaces it with production `GameCore.Contracts` without a
surface change; until then it exists so Wave 1 peers can compile against the frozen shared surface in
parallel. The marker is machine-readable: `TestOnlyMarker.cs` applies
`[assembly: GameCore.TestOnlyReferenceSeam("GC-002", ...)]` plus `AssemblyMetadata` entries.

- Assembly name: `GameCore.ReferenceSeams` (project `dotnet/src/GameCore.ReferenceSeams`).
- Types are in namespace `GameCore.Contracts` with the exact names production will use.
- Deterministic collaborator doubles live in namespace `GameCore.TestFixtures` (`Stubs/`) so they are not
  part of the frozen `GameCore.Contracts` surface or of the committed API snapshot.
- Pure data, interfaces, canonical comparison and canonical byte writers only. **No** ECS/world runtime,
  **no** `UnityEngine`/`Unity.*` reference, **no** derivation, ordering, ownership or validation policy.

## Contents

| Area | Files |
|---|---|
| Identity and version domains | `Identity/Id128.cs`, `Identity/GeneratedIdWrappers.cs`, `Identity/Counters.cs`, `Identity/Handles.cs` |
| Manifest and declaration schema | `Manifest/ManifestEnums.cs`, `Manifest/SharedValueTypes.cs`, `Manifest/Declarations.cs`, `Manifest/PluginManifest.cs` |
| Plans and results | `Plans/PlanDeltas.cs`, `Plans/ChangePlan.cs`, `Results/Diagnostics.cs`, `Results/OperationResult.cs`, `Results/Events.cs` |
| API boundary contracts | `Contracts/HostContracts.cs` |
| Control-lane seam (GC-004) | `Contracts/CompositionHost.cs` |
| World/execution seam (GC-005) | `Contracts/WorldHost.cs` |
| Catalog lookup seam | `Contracts/CatalogContract.cs` |
| Serialization envelope | `Serialization/Envelope.cs`, `Serialization/CanonicalMapOrder.cs` |
| Deterministic test stubs | `Stubs/DeterministicIds.cs`, `Stubs/InMemoryHost.cs`, `Stubs/StubCatalog.cs`, `Stubs/CompositionStubs.cs`, `Stubs/WorldStubs.cs`, `Stubs/ObservationStubs.cs`, `Stubs/ExecutionStubs.cs` |

## Wave 1 seams frozen here

| Consumer | Seam | Notes |
|---|---|---|
| GC-003 | all `GameCore.Contracts` data types, `ICatalog` | `ICatalog` reports a miss as `CatalogLookup` with `MissingDependency`/`UnsupportedVersion`; no reflection and no default substitution (P-009) |
| GC-004 | `ICompositionHost`, `ScopeSnapshot`, `InstallRecord`/`InstallSnapshot`, `ServiceBinding`, `OperationStatusHandle`/`OperationLedgerEntry`/`OperationReadResult`, `ControlLaneCapacitySettings`/`OperationExpirySettings`, `ICompositionCommands`, `ICommandIngress`/`CommandAdmissionReceipt` | Admission returns a handle before the result exists; a status read distinguishes Unknown from Expired (P-050, P-051) |
| GC-005 | `IWorldHost`, `IExecutionDriver`, `WorldCreateRequest`/`WorldCreateResult`, `FixedStepSettings`, `TimeDebt`, `SystemDispatchEntry`/`OrderedDispatchTable`, `StepCommitEvent`/`IWorldLifecycleObserver`, `WorldResourceLedgerSnapshot` with `WorldResourceRecord`/`JobLedgerRecord` | Keys and descriptors only: no `Entity`, `JobHandle`, native container or other Unity type appears in the seam (04 s4, P-031, P-035, P-041, P-048) |
| GC-004/005 and later | `IObservationReader` returning `SnapshotAcquireResult`, `IExplanationReader` with paging, `IStagedPlanDiagnostics`, `IManagedResourceLease` with readiness/disposer/gate, `IResourceGate` | Expiry and backpressure are returned as values, never thrown (P-007, P-045); staged-plan diagnostics are labelled distinctly from published observation (05 s5) |

## Encoding decisions recorded for GC-003

1. **Generated category wrappers.** `docs/game-core/05-contracts-and-data-model.md` s1 states that production
   generates a strongly typed wrapper for every identity category, and that the compilable skeleton uses
   `Id128` only as a compactness placeholder. This seam therefore uses the wrappers
   (`SchemaId`, `DefinitionId`, `TargetId`, `OwnerId`, `SlotId`, `PluginInstanceId`, `RuleId`,
   `ProviderInstallationId`, `CapabilityId`, `StageId`, `BufferId`, `ResourceKey`, `RouteId`, `ScopeId`,
   `PluginTypeId`, `WorldDefinitionId`) as field types wherever 05 s2 names that category. Field *names*
   follow the skeleton (`DefinitionRef.Id`, `ContributionKey.Provider`, ...).
2. **Structs versus classes.** Identity, handle, reference and counter types are readonly structs with value
   equality; DTO aggregates that own declaration arrays are sealed classes that defensively freeze input
   with `ContractCollections.Freeze`. No `record`, `init` or `required` is used (C# 9 / netstandard2.1).
3. **Handles are not order keys.** `Id128` and the category wrappers implement `IComparable<T>` because
   P-004/P-008 define canonical big-endian identity order. Runtime handles (`TargetHandle`, `ScopeHandle`,
   `PluginHandle`, `AsyncWorkToken`, ...) implement equality only, because no protocol text defines an order
   over handles.
4. **`OperationResult` extension.** The skeleton's four-argument arity is preserved, and the old/new
   revision-epoch, structured `Diagnostics`, cleanup and quarantine references required by 05 s4 are added as
   a second constructor. The code is the `DiagnosticCode` enum (with `CodeText` for the normative literal)
   rather than a bare string, so a typo cannot become an unrecognized code.
5. **Counter overflow.** Counter structs expose the pure arithmetic successor (`TryIncrement`) and the
   bounded constants; the *policy* (reject further allocation and require world recreation, P-005) belongs
   to the caller and is proven by `tests/GameCore.ProtocolFixtures`, not by the seam.
6. **Typed version domains.** `SnapshotToken` carries `AssemblyEpoch`/`LogicalStepId`, `AsyncWorkToken` carries
   `InstallationGeneration`/`ActivationEpoch`, `DefinitionRef.Revision` is a `DefinitionRevision`, and
   `EventCursor.Sequence` is an `EventSequence`. Counter arithmetic stays pure (`TryIncrement`); the overflow
   *policy* is the caller's, proven by the fixtures.
7. **Handles reserve generation 0.** `TargetHandle`, `ScopeHandle` and `PluginHandle` reject generation 0 in
   their constructors and expose `IsAllocated`, so `default(Handle)` is never live (P-005). `Slot` is `uint`, so
   a negative slot cannot be expressed at all.
8. **Envelope header carries schema plus required features.** The fixed header is magic (4), major (1), minor (1),
   schema id (16), schema version (4), required-feature count (4), then that many 16-byte feature ids; a reader
   asked to validate features refuses an unknown one before the body is used (P-055). The trailing checksum
   record uses reserved field id 0 and covers every preceding byte. Signed integers are two's complement
   big-endian; floats are IEEE-754 bits with any NaN canonicalized to one quiet NaN on write and compared by bits.
9. **Canonical map keys.** `CanonicalMapOrder` orders id keys by canonical bytes, unsigned keys numerically and
   string keys by code point (identical to canonical UTF-8 byte order), and reports duplicate keys so a caller
   rejects a malformed map instead of silently choosing an entry.
10. **Not included on purpose.** ECS/world runtime, `AssemblyPlanner`/publisher logic, derivation and
   propagation rules, catalog validation, migration graphs, Unity apply implementations, and CodeCLI or
   player-loop integration. GC-003/GC-004/GC-005 and later tasks own those.

## API snapshot

`api/GameCore.Contracts.api.txt` is the frozen public surface of namespace `GameCore.Contracts`
(types sorted by full name, members grouped and sorted, assembly name excluded from the listing body).
The committed file was generated from the compiled reference seam on the Linux build host with
`dotnet/tools/GameCore.ApiSnapshot`. `dotnet/tests/GameCore.ReferenceSeams.Tests` fails if the
placeholder header is present or the listing differs.
See `dotnet/README.md` for the exact command. Any seam change reopens the W0 interface gate before dependent
modules compile (`docs/game-core/09-implementation-guide.md`, GC-002).
