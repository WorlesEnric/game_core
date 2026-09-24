# GC-002 HANDOFF — protocol identity and conformance fixtures executable (Wave 0)

Branch: `gc-002` (worktree `/Users/yangcao/wkspace/gc-wt/gc-002`).
Status of every executable check in this handoff: **NotRun (pending orchestrator build host)**. This machine
has no .NET SDK, no Unity and no Mono, so nothing here has been compiled or executed. Only the Python
documentation validator was run (it is the one check this host can execute).

## 1. Summary

Delivered the W0 contract-risk package in four parts:

1. **Plain-dotnet infrastructure** (owned by GC-002 for later tasks to extend): `dotnet/Directory.Build.props`
   (C# 9, nullable enable, warnings-as-errors, deterministic, implicit usings off, analyzers off),
   `dotnet/GameCore.sln` with five projects, root `.gitignore` entries for `dotnet/**/bin`,
   `dotnet/**/obj`, `dotnet/**/TestResults` and `artifacts/**/raw/`, and `dotnet/README.md` with the exact
   commands.
2. **Test-only compiled reference seam** `GameCore.ReferenceSeams` (assembly name fixed; all contract types in
   namespace `GameCore.Contracts`) covering the full shared 05 surface: identity types and every generated
   category wrapper, version/epoch/revision counters, runtime handles, manifest schema DTOs, plans/results,
   owner/slot/access descriptors, stage and buffer descriptors, catalog/factory keys, host dispatch and
   observer/reader/callback-gate contracts, and the serialization envelope — plus deterministic collaborator
   stubs in `GameCore.TestFixtures` for W1 peers.
3. **API snapshot freeze**: `GameCore.ApiSnapshot` console tool (canonical sorted public-API listing,
   namespace-filtered, assembly name excluded), committed snapshot at
   `tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt` carrying the placeholder header
   `# snapshot pending generation on build host`, and an NUnit test that regenerates the listing in-process,
   prints a line-oriented diff on mismatch and fails with a clear message while the placeholder is present.
4. **Independent pure oracle and fixtures**: `GameCore.ProtocolFixtures` (canonical byte order, handle and
   world-incarnation validation, counter/epoch/step advancement and overflow, P-055 version interpretation,
   assembly-independence gate), 40 canonical JSON cases under `Data/cases/` (valid and invalid, each with its
   expected outcome), `Data/result-schema.json` (case schema, result-document shape, per-kind parameter
   tables, outcome vocabulary, source-of-truth rules), and the NUnit suite that executes every case and writes
   `artifacts/protocol-fixtures/results.json`.
5. **Documentation gate**: `tools/run_w0_checks.sh` runs the validator self-test, the validator, `dotnet
   build` and `dotnet test`, teeing raw logs into `artifacts/raw/w0/`; the validator self-test and full
   validator both pass on this host (the only check that could be executed).

## 2. Files created

Documentation and scripts

- `dotnet/README.md`, `dotnet/Directory.Build.props`, `dotnet/GameCore.sln`, `.gitignore`, `tools/run_w0_checks.sh`
- `tests/GameCore.ReferenceSeams/README.md`, `tests/GameCore.ProtocolFixtures/README.md`
- `tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt` (pending header only)
- `artifacts/protocol-fixtures/README.md`, `artifacts/gc-002/HANDOFF.md`

dotnet projects

- `dotnet/src/GameCore.ReferenceSeams/GameCore.ReferenceSeams.csproj`
- `dotnet/src/GameCore.ProtocolFixtures/GameCore.ProtocolFixtures.csproj`
- `dotnet/tools/GameCore.ApiSnapshot/GameCore.ApiSnapshot.csproj`, `Program.cs`, `ApiSnapshotGenerator.cs`, `ApiSnapshotDiff.cs`
- `dotnet/tests/GameCore.ReferenceSeams.Tests/GameCore.ReferenceSeams.Tests.csproj`, `ApiSnapshotTests.cs`
- `dotnet/tests/GameCore.ProtocolFixtures.Tests/GameCore.ProtocolFixtures.Tests.csproj`, `ProtocolFixtureTests.cs`

Reference seam sources (`tests/GameCore.ReferenceSeams/`)

- `TestOnlyMarker.cs` (assembly attribute + `AssemblyMetadata` marker)
- `Identity/Id128.cs`, `Identity/GeneratedIdWrappers.cs`, `Identity/Counters.cs`, `Identity/Handles.cs`
- `Manifest/ManifestEnums.cs`, `Manifest/SharedValueTypes.cs`, `Manifest/Declarations.cs`, `Manifest/PluginManifest.cs`
- `Plans/PlanDeltas.cs`, `Plans/ChangePlan.cs`
- `Results/Diagnostics.cs`, `Results/OperationResult.cs`, `Results/Events.cs`
- `Contracts/HostContracts.cs`, `Serialization/Envelope.cs`
- `Stubs/DeterministicIds.cs`, `Stubs/InMemoryHost.cs`, `Stubs/StubCatalog.cs`, `Stubs/ObservationStubs.cs`, `Stubs/ExecutionStubs.cs`

Oracle and fixtures (`tests/GameCore.ProtocolFixtures/`)

- `RepoLayout.cs`
- `Oracle/CanonicalOrder.cs`, `Oracle/IdentityOracle.cs`, `Oracle/CounterOracle.cs`, `Oracle/VersionOracle.cs`
- `Fixtures/FixtureModel.cs`, `Fixtures/FixtureLoader.cs`, `Fixtures/FixtureRunner.cs`, `Fixtures/ResultDocument.cs`
- `Data/result-schema.json`, `Data/cases/{identity-canonical-bytes,identity-order,identity-handles,version-counters,version-support,assembly-independence}.json`

Shared file changed (minimal, explained): `docs/game-core/README.md` line 37. The validator failed on
`docs/game-core/README.md:37: missing local target '../Game_Core_Implementation_Design_v1.md'` because that file
is not in this repository. The sentence now names the file without a Markdown link, so the W0 documentation
gate can pass; no meaning changed.

## 3. Exact commands for the Linux build host

Documentation gate (works on any host with Python 3):

```sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

Full W0 gate in one step (validator + self-test + dotnet build + dotnet test, raw logs in `artifacts/raw/w0/`):

```sh
DOTNET=/usr/bin/dotnet PYTHON=/usr/bin/python3 tools/run_w0_checks.sh
```

Pure dotnet steps directly:

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx
```

Generate the real API snapshot, then rerun the tests so the comparison and the committed file agree:

```sh
dotnet run --project dotnet/tools/GameCore.ApiSnapshot -c Release -- \
  --assembly dotnet/src/GameCore.ReferenceSeams/bin/Release/netstandard2.1/GameCore.ReferenceSeams.dll \
  --output tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt \
  --namespace GameCore.Contracts
dotnet test dotnet/GameCore.sln -c Release --logger trx
```

No Unity command is needed for GC-002: the seam and the oracle are Unity-free, and the Unity/IL2CPP probe is
GC-001's. `System.Text.Json` 8.0.5 is the only NuGet dependency and must be restorable on the build host
(network or a warm package cache).

## 4. Requirement and test coverage mapping

Requirement sections GC-002 is responsible for (`docs/game-core/09-implementation-guide.md`):

| Requirement | Executable evidence in this package |
|---|---|
| P-001 genre independence | `assembly-independence.json` cases assert the oracle and the seam reference no engine and no gameplay assembly and expose no `UnityEngine`/`Unity.*`/`GameCore.Gameplay.*` types |
| P-004 stable identities | `identity-canonical-bytes.json` (canonical bytes, hex round trip, word-swap detection), `identity-handles.json` (`WrongWorld`, collision/incarnation separation), seam `Id128` + generated wrappers + `Id128Codec` |
| P-005 runtime handles | `identity-handles.json` (stale generation, retired slot, unknown slot, wrong category, stale activation epoch), `version-counters.json` (generation/epoch overflow refusal without wraparound) |
| P-006 version domains | `version-counters.json` (`firstPublication` publishes revision/epoch 1 with step 0, publication moves revision+epoch only, `noChange` moves nothing, committed step moves only the step, exhausted revision refuses) |
| P-008 stable ordering | `identity-order.json` (canonical high-then-low order, every insertion order, duplicate identities, wrong expectation detected) plus `ProtocolFixtureTests.ShuffledInsertionOrdersProduceIdenticalCanonicalOutput` (rotations and idempotence) |
| P-055 protocol evolution | `version-support.json` (major mismatch, minor below/above window, unknown required feature, known feature accepted, inverted window), all rejected without fallback |
| P-057 conformance | same assembly-independence cases; plus the harness distinction between fixture expectation and observed verdict, and the explicit statement that this model is not runtime conformance |
| P-060 evidence discipline | `artifacts/protocol-fixtures/README.md` records the pending status and exact commands; the suite writes its result document with the executed status; nothing in this package claims a passing run |

Suites (`docs/game-core/08-validation-and-performance.md`):

| Suite | W0-applicable executable subset delivered here |
|---|---|
| TEST-002 | canonical identity bytes, world/generation collision, stale-handle detection, counter/epoch/revision overflow, seam/oracle codec cross-check, `CounterOracleRefusesOverflowForEveryCounter` |
| TEST-021 | kernel imports no example gameplay assembly (assembly-independence cases); no gameplay/engine type appears in the frozen surface |
| TEST-022 | canonical ordering is independent of insertion and rotation order, sorting is idempotent, and repeated generation of the same listing is byte-identical |
| TEST-024 | P-055 version interpretation cases plus the documentation gate (`tools/run_w0_checks.sh`: validator self-test, validator, build, test) and the result-document format assertions |

Test IDs per case are carried in the fixture data and surface in `artifacts/protocol-fixtures/results.json`.

## 5. Decisions, assumptions and doc ambiguities

Recorded in full in `tests/GameCore.ReferenceSeams/README.md` (§Encoding decisions) and summarised here.

1. **Typed category wrappers over the skeleton's raw `Id128`.** 05 §1 states production generates a strongly
   typed wrapper for every identity category and that the compilable skeleton uses `Id128` only for
   compactness. The seam therefore types `SchemaRef.Id` as `SchemaId`, `DefinitionRef.Id` as `DefinitionId`,
   `ContributionKey` fields as `ProviderInstallationId`/`RuleId`/`TargetId`/`CapabilityId`,
   `AsyncWorkToken.PluginInstanceId` as `PluginInstanceId` and `StateSlotKey` as `TargetId`/`OwnerId`/`SlotId`,
   while keeping every skeleton field *name*. If GC-003 instead follows the skeleton literally for those
   fields, the API snapshot must be regenerated in the same change — this is the one place where a surface
   mismatch is possible.
2. **`SlotId` treated as a 128-bit stable id** because 05 names `SlotId` in `StateSlotKey` and `StateSlotSpec`;
   the alternative reading (small integer) would change `StateSlotKey`.
3. **`OperationResult` extended, not replaced.** The skeleton's four-argument constructor is preserved; a
   second constructor adds the old/new revision-epoch, structured diagnostics, cleanup and quarantine
   references that 05 §4 requires.
4. **Structs vs classes.** Identity/handle/reference/counter types are readonly structs with value equality;
   DTO aggregates with declaration arrays are sealed classes that freeze input via
   `ContractCollections.Freeze`. No `record`, `init`, `required` or file-scoped namespace anywhere.
5. **Handles are not order keys.** Only stable ids and counters implement `IComparable<T>` (P-004/P-008 defines
   that order); handles implement equality only, because no normative text orders handles.
6. **Semantics stay out of the seam.** The seam holds data, interfaces, comparison and byte writers. Handle
   validation, counter policy, propagation/derivation, catalog validation and envelope limits policy live in
   the oracle or later tasks. Counter structs expose the pure arithmetic predicate (`TryIncrement`) so the
   overflow *policy* remains a caller decision tested by the fixtures.
7. **Opaque keys where the docs are vague.** Stage `ActivationMemberships` is modelled as a list of
   membership keys rather than inventing an enum with unspecified semantics; `ServiceResolutionDomain`,
   `FailureClassification` and `BufferCancellationPolicy` follow the closest literal wording in 05/P-011/P-043.
8. **Version/requirement IDs in fixture data are validated** (`^P-\d{3}$`, `^TEST-\d{3}$`) so fixture rows
   cannot drift away from the registry.
9. **`artifacts/protocol-fixtures/results.json` is intentionally not committed.** Committing it before a run
   would record outcomes nobody observed. The directory README states the pending status and the commands; the
   build host creates the file.
10. **Analyzers disabled, warnings-as-errors on.** The host build must be reproducible without the analyzer
    package surface; every compiler warning remains a build failure.

## 6. Known gaps

- Nothing has been compiled: the seam, oracle, tool and both test projects are unverified by a compiler.
  The most likely first-failure candidates are (a) the `System.Text.Json` package restore on the host,
  (b) `NullabilityInfoContext` usage in the snapshot generator against a `netstandard2.1` assembly, and
  (c) minor nullability annotations. All are contained to tooling/tests and none affect runtime semantics.
- The committed API snapshot is a placeholder; the freeze is only effective after the generation command
  above runs on the host.
- `GameCore.ProtocolFixtures` and `GameCore.ReferenceSeams` are compiled by dotnet only. They are deliberately
  not referenced by any Unity package; if a later task wants to run the fixtures inside Unity, the
  `System.Text.Json` dependency must be resolved first (Unity has no such BCL package).
- The oracle covers the W0 subset named by the task (identity, stale references, overflow, ordering,
  version interpretation, kernel-only boundary). Propagation, derivation, scheduling, persistence and the
  26 operations are out of scope and belong to GC-003 and later tasks.
- The fixture suite asserts that a delivered run contains no `NotRun` and no `Blocked` rows, so a malformed
  case fails the suite instead of being skipped silently.
