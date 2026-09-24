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
| Serialization envelope | `Serialization/Envelope.cs` |
| Deterministic test stubs | `Stubs/DeterministicIds.cs`, `Stubs/InMemoryHost.cs`, `Stubs/StubCatalog.cs`, `Stubs/ObservationStubs.cs`, `Stubs/ExecutionStubs.cs` |

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
4. **`OperationResult` extension.** The skeleton's four-argument constructor is preserved, and the old/new
   revision-epoch, structured `Diagnostics`, cleanup and quarantine references required by 05 s4 are added as
   a second constructor.
5. **Counter overflow.** Counter structs expose the pure arithmetic successor (`TryIncrement`) and the
   bounded constants; the *policy* (reject further allocation and require world recreation, P-005) belongs
   to the caller and is proven by `tests/GameCore.ProtocolFixtures`, not by the seam.
6. **Not included on purpose.** ECS/world runtime, `AssemblyPlanner`/publisher logic, derivation and
   propagation rules, catalog validation, migration graphs, Unity apply implementations, and CodeCLI or
   player-loop integration. GC-003/GC-004/GC-005 and later tasks own those.

## API snapshot

`api/GameCore.Contracts.api.txt` is the frozen public surface of namespace `GameCore.Contracts`
(types sorted by full name, members grouped and sorted, assembly name excluded). The committed file ships
with the placeholder header `# snapshot pending generation on build host`; the orchestrator's build step
regenerates it with `dotnet/tools/GameCore.ApiSnapshot`, and
`dotnet/tests/GameCore.ReferenceSeams.Tests` fails while the placeholder is present or the listing differs.
See `dotnet/README.md` for the exact command. Any seam change reopens the W0 interface gate before dependent
modules compile (`docs/game-core/09-implementation-guide.md`, GC-002).
