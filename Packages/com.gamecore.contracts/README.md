# com.gamecore.contracts — production shared contracts (GC-003)

Normative sources: [`00-core-protocols.md`](../../docs/game-core/00-core-protocols.md) and
[`05-contracts-and-data-model.md`](../../docs/game-core/05-contracts-and-data-model.md). Where this README and
the protocol disagree, the protocol wins.

Assembly `GameCore.Contracts`, namespace `GameCore.Contracts`, `noEngineReferences: true`, .NET Standard 2.1 /
C# 9. It references no Unity assembly, uses no runtime reflection and is not a second ECS facade
(`01-architecture.md` §1, `04-unity-integration.md` §2, P-058).

`dotnet/src/GameCore.Contracts/GameCore.Contracts.csproj` compiles exactly these `Runtime/**` sources, so the
same files build under plain dotnet and inside Unity. `dotnet/tests/GameCore.Contracts.Tests` gates the surface
against the frozen W0 snapshot `tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt`.

## 1. Layout and ownership

| Area | Files | Owns |
|---|---|---|
| Identity and version domains | `Runtime/Identity/Id128.cs`, `GeneratedIdWrappers.cs`, `Counters.cs`, `Handles.cs`, `CanonicalHex.cs`, `StableNameKeyDerivation.cs`, `TimeDebt.cs` | 128-bit ids, their canonical compare/hex form, the version/epoch/step counters and runtime handles (P-004–P-006, P-008) |
| Manifest and declaration schema | `Runtime/Manifest/ManifestEnums.cs`, `SharedValueTypes.cs`, `Declarations.cs`, `PluginManifest.cs` | The declarative plugin manifest, installation spec, service/capability/stage/buffer/resource declarations (P-009, P-011, P-039–P-043) |
| Plans and results | `Runtime/Plans/PlanDeltas.cs`, `ChangePlan.cs`, `Runtime/Results/Diagnostics.cs`, `OperationResult.cs`, `Events.cs` | Immutable plan and delta shapes, structured diagnostics, operation results and committed events (P-027, P-045, P-052) |
| API boundary contracts | `Runtime/Contracts/HostContracts.cs`, `CompositionHost.cs`, `WorldHost.cs`, `CatalogContract.cs` | The role seams later tasks implement or consume (P-002) |
| Catalog | `Runtime/Catalog/ImmutableCatalog.cs`, `CatalogOrdering.cs`, `CatalogFingerprint.cs`, `ISchemaSerializer.cs`, `BoundRegistration.cs` | The validated immutable catalog, canonical table order, the content fingerprint, the generated-serializer seam and the generated registration record (P-009, P-028, P-053) |
| Manifest validation | `Runtime/Validation/ManifestValidator.cs` | Catalog rejection rules: duplicate ids, unknown schemas/keys, unsupported required features, stratum/termination rules, access/ordering conflicts, missing policies (P-009, P-021, P-040) |
| Serialization | `Runtime/Serialization/Envelope.cs`, `CanonicalMapOrder.cs`, `GeneratedEnvelopeReader.cs`, `GeneratedSerializerBase.cs` | The canonical length-delimited envelope, the generated field-table walk and the generated serializer base class (P-054, P-055) |

### Who implements what

| Type | Kind | Implemented by |
|---|---|---|
| `ICatalog` | contract | `GameCore.Contracts.ImmutableCatalog` (this package) and test doubles. No other production implementation is expected |
| `ICompositionHost`, `ICompositionCommands`, `ICommandIngress`, `IOperationReader`, `IOperationControl` | contract | `GameCore.Composition` (GC-004) |
| `IWorldHost`, `IExecutionDriver`, `IWorldLifecycleObserver` | contract | `GameCore.Unity.Runtime` (GC-005 and successors) |
| `IObservationReader`, `IExplanationReader`, `IStagedPlanDiagnostics`, `IRecipeApplyBridge`, `IStateMigrator`, `IAssemblyPublisher` | contract | Unity runtime/adapters and the publisher (later waves) |
| `IManagedResourceLease`, `IResourceGate` | contract | control-plane resource factories (GC-004) |
| `ISchemaSerializer`, `GeneratedSerializerBase` | contract | generated code emitted by `com.gamecore.content.compiler` |
| `ImmutableCatalog`, `ManifestValidator`, `CatalogFingerprint`, `StableNameKeyDerivation` | implementation | this package |

`GameCore.ReferenceSeams` (W0, test-only) is retired as soon as consumers move to this assembly. It stays in the
repository as the frozen snapshot source for the API-compatibility gate.

## 2. Shared DTO layouts and their invariants

| DTO | Layout | Invariant |
|---|---|---|
| `Id128` | `ulong High`, `ulong Low` | Canonical order and bytes are big-endian high then low; never platform `Guid` memory order. The all-zero value is not a catalog identity (P-004) |
| `WorldId` | `Id128` session id | Fresh per session, never reused, including on checkpoint restore (P-004) |
| `TargetHandle` | `WorldId`, `uint Slot`, `ulong Generation` | Not persisted; generation 0 is reserved so `default` is never live; every dereference validates world, generation, liveness, category (P-005) |
| `ScopeHandle` / `PluginHandle` | Target-handle shape plus a scope or installation generation | Installation generation changes on unmount/remount only, not on reconfigure (P-005) |
| `SnapshotToken` | `WorldId`, `AssemblyEpoch`, `LogicalStepId` | Immutable observation identity; a composition publication may keep the same step (P-006) |
| `AsyncWorkToken` | `OperationId`, `PluginInstanceId`, `InstallationGeneration`, `ActivationEpoch`, `uint WorkOrdinal` | Validated at ingress and at completion; a stale completion releases its own resources and publishes nothing (P-007, P-047) |
| `DefinitionRef` | `DefinitionId`, `SchemaId`, `uint SchemaVersion`, `DefinitionRevision` | Immutable revision lookup; holds no asset lease (05 §2) |
| `ContributionKey` | `ProviderInstallationId`, `RuleId`, `TargetId`, `CapabilityId`, `uint OutputSlot` | Identity survives payload reconfiguration; provenance holds activation/revision separately (P-017) |
| `StateSlotKey` | `TargetId`, `OwnerId`, `SlotId` | Mutable-state lifetime, distinct from the contribution key (P-032) |
| `FactoryKey` | `Id128 RegistrationKey`, `uint KeyVersion` | Generated registration identity; a miss is reported, never substituted by reflection (P-009) |
| `PluginManifest` | Plugin type id, package version + content hash, protocol range, required features, config schema, factory key, then every declaration array | Every category is explicit; an empty array means "none", never "discover later" (P-009) |
| `InstallationSpec` | `PluginInstanceId`, `ScopeId`, `DefinitionRevision`, config blob, priority, service selections | Identity is stored assembly data, never derived from creation order (P-004) |
| `ChangePlan` | Operation id, input hash, base revision, base epoch, catalog hash, plan hash, composition/derivation/runtime deltas, staged resources, validity and cost | Immutable; holds no writable world access, no stale component pointer and no plugin closure delegate (P-027) |
| `OperationResult` | Operation id, `Outcome`, `DiagnosticCode`, published snapshot, old/new revision and epoch, diagnostics, cleanup and quarantine references | `PublishedWithCleanupErrors` is already authoritative, `Rejected` made no live write, `Faulted` left storage unsafe, `NoChange` incremented no revision (05 §4) |
| `CommittedEvent` | Event cursor, schema, epoch, step, causal request, payload | Exposed only with its publication image; carries stable ids, not engine handles (P-045) |
| `ImmutableCatalog` | Canonically sorted factory/schema/serializer/feature tables plus a `ContentHash` fingerprint | Accepts only a consistent description; lookups report `MissingDependency`/`UnsupportedVersion`; fingerprint is declaration-order independent (P-009, P-028) |

Field-level rules that apply to every DTO: no `Entity`, `JobHandle`, native container or `UnityEngine.Object`
appears anywhere in this assembly; a runtime handle is never serialized as a stable id; durable references use
stable id plus version (05 §7).

## 3. Additions beyond the frozen W0 surface (GC-003)

Everything the W0 seam froze is present unchanged. This release adds, and
`artifacts/gc-003/HANDOFF.md` lists the review:

1. `FactoryKind.Handler` — the W0 enum had no category for a generated closed generic handler registration.
   `CancelOutcome.IdempotencyConflict` and `CancelOutcome.Rejected` are **not** an addition of this task: they
   arrived from the reopened W0 gate on `main` and are declared here with the same values (P-050).
2. `EnvelopeError.MissingRequiredField`, `EnvelopeError.DuplicateField` — the generated field-table walk needs
   to report a missing required field and an ambiguous repeated field.
3. `EnvelopeReader.TrySeekTo` — a generated deserializer re-reads a recorded field record at its absolute
   offset instead of scanning the document a second time.
4. `GeneratedFieldSlot`, `GeneratedFieldBuffer`, `GeneratedEnvelopeReader`, `GeneratedSerializerBase`,
   `ISchemaSerializer`, `BoundRegistration<T>` — the runtime half of generated catalogs and serializers. Without
   them a generated serializer would have to reimplement header, required-feature, required-field and checksum
   rules per schema, which is exactly the duplication the compiler exists to avoid (05 §6, 04 §8).
5. `ImmutableCatalog`, `CatalogBuildResult`, `CatalogOrdering`, `CatalogFingerprint`, `ManifestValidator` and
   `StableNameKeyDerivation` — real behaviour behind `ICatalog`: validation, canonical ordering, fingerprints and
   the documented stable-name derivation.

## 4. Deliberate non-goals

No ECS world, no planner, no service resolver, no gameplay schema, no runtime reflection-based construction and
no managed `Task` in any job path. Those belong to GC-004, GC-005 and later waves (`09-implementation-guide.md`
GC-003 non-goals).
