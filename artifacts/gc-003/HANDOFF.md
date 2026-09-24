# GC-003 HANDOFF — stable contracts and generated catalog validation (Wave 1)

Branch: `gc-003` (worktree `/Users/yangcao/wkspace/gc-wt/gc-003`).

**Status of every executable check in this handoff: `NotRun (pending orchestrator build host)`.** This host has
no .NET SDK, no C# compiler and no Unity (see `artifacts/gc-003/host-tools.log`). Only Python checks ran here:
the documentation validator, its self-test and the new static C# checker.

## 1. Summary

Delivered in five parts.

1. **Production contracts** `Packages/com.gamecore.contracts/` — assembly `GameCore.Contracts`,
   `noEngineReferences: true`, sources under `Runtime/**`. It starts from the frozen W0 seam sources (same
   namespace, same public surface) and implements the behaviour the seam only shaped: `ImmutableCatalog`
   (validated, canonically ordered, fingerprinted), `ManifestValidator` (P-009 rejection rules),
   `CatalogFingerprint`, `StableNameKeyDerivation`, and the generated-code runtime seam
   (`GeneratedSerializerBase`, `GeneratedEnvelopeReader`, `GeneratedFieldSlot`, `GeneratedFieldBuffer`,
   `ISchemaSerializer`, `BoundRegistration<T>`).
2. **Content compiler** `Packages/com.gamecore.content.compiler/` — Unity-free assembly
   `GameCore.Content.Compiler` (Editor-only platform) plus a thin `Editor/` Unity entry (`CatalogGeneratorMenu`).
   It reads a documented, versioned JSON description document, validates it (duplicate ids, unknown schemas,
   unsupported required features and protocol versions, bad code fragments), and emits deterministic C#:
   stable `FactoryKey` registrations sorted by derived 128-bit key, per-entry `FactoryRegistration` tables,
   generated value types and serializers over the 05 §6 envelope, explicit closed generic roots with
   `RegisterGenericJobType`, a catalog fingerprint and a file-prefix hash. Output is byte-reproducible (LF, no
   timestamps, no machine paths, declaration-order independent).
3. **Adapted pure fixtures** — `dotnet/src/GameCore.ProtocolFixtures.Production` compiles the *same* oracle and
   fixture sources against production `GameCore.Contracts`; `dotnet/tests/GameCore.ProtocolFixtures.Production.Tests`
   compiles the *same* test source. The seam-based run is untouched and writes
   `artifacts/protocol-fixtures/results.json`; the production run writes
   `artifacts/protocol-fixtures/results-production-contracts.json`.
4. **Probe consumes production contracts** — `unity/GameCore.Validation` now references `com.gamecore.contracts`
   and `com.gamecore.content.compiler`; `ProbeKey` delegates derivation to production
   `StableNameKeyDerivation`, the handler surface uses production `FactoryKey`, the generated catalog is produced
   by the production compiler from `Catalogs/ProbeCatalog.catalog.json` through a thin Editor bridge, and a new
   probe step `production-contract-catalog` validates the immutable catalog, its fingerprint, keyed lookups and
   a generated-serializer round trip (with tamper and truncation rejection). All GC-001 probes are retained,
   including the negative mode (which now also asserts the immutable catalog reports an explicit miss).
5. **Documentation and gates** — `Packages/com.gamecore.contracts/README.md` documents every shared DTO layout
   and who implements each interface; `Packages/com.gamecore.content.compiler/README.md` documents the input
   form and the invocation; `tools/check_game_core_csharp.py` is the host-side static check;
   `tools/verify_generated_catalog.py` recomputes the committed generated catalog's file hash and fingerprint;
   `tools/check_contract_surface_parity.py` checks the production contract surface against the frozen snapshot;
   and `tools/run_gc003_checks.sh` runs all three plus the documentation validator, the dotnet build/tests and
   (when `UNITY` is set) Unity codegen plus both player probes.

`main` was merged into this branch after the W0 interface gate reopened: the seam change added
`CancelOutcome.IdempotencyConflict = 4` and `CancelOutcome.Rejected = 5` with the regenerated snapshot, and the
production `CancelOutcome` now declares exactly the same six members. §4 records the coverage; §5 item 13
records the decision.

## 2. Files

### Created — contracts package

- `Packages/com.gamecore.contracts/package.json`, `README.md`
- `Packages/com.gamecore.contracts/Runtime/GameCore.Contracts.asmdef` (`noEngineReferences: true`), `Runtime/AssemblyInfo.cs`
- `Runtime/Identity/`: `Id128.cs`, `GeneratedIdWrappers.cs`, `Counters.cs`, `Handles.cs`, `CanonicalHex.cs`,
  `TimeDebt.cs` (copied from the seam) and `StableNameKeyDerivation.cs` (new)
- `Runtime/Manifest/`: `ManifestEnums.cs`, `SharedValueTypes.cs`, `Declarations.cs`, `PluginManifest.cs` (copied)
- `Runtime/Plans/`: `PlanDeltas.cs`, `ChangePlan.cs` (copied)
- `Runtime/Results/`: `Diagnostics.cs`, `OperationResult.cs`, `Events.cs` (copied)
- `Runtime/Contracts/`: `HostContracts.cs`, `CompositionHost.cs`, `WorldHost.cs`, `CatalogContract.cs` (copied)
- `Runtime/Serialization/`: `Envelope.cs`, `CanonicalMapOrder.cs` (copied), `GeneratedEnvelopeReader.cs`,
  `GeneratedSerializerBase.cs` (new)
- `Runtime/Catalog/`: `ImmutableCatalog.cs`, `CatalogOrdering.cs`, `CatalogFingerprint.cs`, `ISchemaSerializer.cs`,
  `BoundRegistration.cs` (new)
- `Runtime/Validation/ManifestValidator.cs` (new)

### Created — content compiler package

- `Packages/com.gamecore.content.compiler/package.json`, `README.md`
- `Runtime/GameCore.Content.Compiler.asmdef`, `Runtime/Json/JsonReader.cs`
- `Runtime/Description/`: `CatalogDescription.cs`, `CatalogWireTypes.cs`, `CatalogDiagnostic.cs`,
  `CatalogDescriptionReader.cs`, `CatalogFactoryKinds.cs`, `CatalogEmitter.cs`, `CatalogGenerator.cs`,
  `CatalogGeneratorArguments.cs`
- `Editor/GameCore.Content.Compiler.Editor.asmdef`, `Editor/CatalogGeneratorMenu.cs`

### Created — dotnet

- `dotnet/src/GameCore.Contracts/GameCore.Contracts.csproj`
- `dotnet/src/GameCore.Content.Compiler/GameCore.Content.Compiler.csproj`
- `dotnet/src/GameCore.ProtocolFixtures.Production/GameCore.ProtocolFixtures.Production.csproj`
- `dotnet/tests/GameCore.Contracts.Tests/`: `GameCore.Contracts.Tests.csproj`, `ContractTests.cs`,
  `ManifestValidationTests.cs`
- `dotnet/tests/GameCore.Content.Compiler.Tests/`: `GameCore.Content.Compiler.Tests.csproj`, `CompilerTests.cs`
- `dotnet/tests/GameCore.ProtocolFixtures.Production.Tests/`:
  `GameCore.ProtocolFixtures.Production.Tests.csproj`, `ProductionRunResultPath.cs`
- `dotnet/tools/GameCore.ApiSnapshot/ApiSurfaceComparer.cs`

### Created — probe input, tools, evidence

- `unity/GameCore.Validation/Catalogs/ProbeCatalog.catalog.json` (the committed description document; inside the
  project but outside `Assets/`, so Unity does not import it and no `.meta` file is needed)
- `tools/check_game_core_csharp.py`, `tools/verify_generated_catalog.py`,
  `tools/check_contract_surface_parity.py`, `tools/run_gc003_checks.sh`
- `artifacts/gc-003/`: `HANDOFF.md`, `README.md`, `artifact-hashes.json`, `api-surface-diff.log`,
  `static-checks.log`, `verify-generated-catalog.log`, `check-contract-surface-parity.log`, `validator.log`,
  `validator-self-test.log`, `host-tools.log`, `review-round-1.md`

### Modified (minimal, explained)

| Path | Change and reason |
|---|---|
| `dotnet/GameCore.sln` | six new projects added; existing entries and GUIDs untouched |
| `dotnet/README.md` | project table extended and a GC-003 command section added; GC-002 text untouched |
| `tests/GameCore.ProtocolFixtures/RepoLayout.cs` | `ResultDocumentPath` reads an optional result-file-name override so two suites can both run the oracle without overwriting evidence |
| `tests/GameCore.ProtocolFixtures/Fixtures/FixtureRunner.cs` | the assembly-independence case substitutes the run tokens `{oracleAssembly}`/`{kernelAssembly}` with the assemblies actually loaded in the test process |
| `tests/GameCore.ProtocolFixtures/Data/cases/assembly-independence.json` | same two cases, now tokenized (the second case id became `kernel-contract-assembly-references-no-engine-or-gameplay-assembly`) |
| `tests/GameCore.ProtocolFixtures/Data/result-schema.json` | documents the two tokens |
| `tests/GameCore.ProtocolFixtures/README.md` | states that both builds exist, and how the two result documents are named |
| `dotnet/tests/GameCore.ProtocolFixtures.Tests/ProtocolFixtureTests.cs` | the result-path assertion now checks the documented directory and a `.json` name instead of one hard-coded file name |
| `unity/GameCore.Validation/Packages/manifest.json` | two `file:../../../Packages/...` lines added; every GC-001 pin untouched |
| `unity/GameCore.Validation/Assets/GameCore.Validation/**` | probe sources, asmdef references and the generated catalog; see §1 item 4 |

Merged in: `tests/GameCore.ReferenceSeams/Manifest/ManifestEnums.cs` and the regenerated
`tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt` arrived from `origin/main` (the W0 gate reopened
for `CancelOutcome`); that produced one corresponding production change, recorded in §5 item 13. Nothing else in
the frozen surface changed.

Not touched: `tests/GameCore.ReferenceSeams/**` beyond that merge,
`unity/GameCore.Validation/ProjectSettings/**`, `Packages/packages-lock.json` (the host resolves it),
`docs/**`, `tools/validate_game_core_docs.py`.

## 3. Exact commands for the Linux build host

Everything below runs from the repository root.

### 3.1 One command (static checks, docs validator, build, tests, then Unity and the probe)

```sh
DOTNET=/usr/bin/dotnet PYTHON=/usr/bin/python3 tools/run_gc003_checks.sh
# with the pinned Editor, to also run codegen, the IL2CPP build and both player probes:
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
DOTNET=/usr/bin/dotnet PYTHON=/usr/bin/python3 tools/run_gc003_checks.sh
```

### 3.2 The pieces, if you want them separately

```sh
python3 tools/check_game_core_csharp.py
python3 tools/verify_generated_catalog.py
python3 tools/check_contract_surface_parity.py
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-003/trx

# Unity: regenerate the probe catalog with the production compiler, then build and run the player
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity tools/unity/build_probe.sh
tools/unity/run_probe.sh both
```

`build_probe.sh` step 1 calls `GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog`, which now
delegates to `GameCore.Content.Compiler`; step 2 builds the StandaloneLinux64 IL2CPP player with High stripping.
`run_probe.sh` runs the positive probe (exit 0, `"result": "Pass"`) and the negative probe (exit 3,
`"result": "ExpectedNegative"`). The scripts take `UNITY`, `UNITY_PROJECT`, `ARTIFACTS`, `PROBE_PLAYER` and
`DOTNET` from the environment.

Expected new evidence files after a full run: `artifacts/gc-003/{static-checks,validator,validator-self-test,
dotnet-build,dotnet-test}.log`, `artifacts/gc-003/trx/*.trx`, `artifacts/gc-003/toolchain/*`
(codegen/build/player logs, probe JSON), `artifacts/protocol-fixtures/results.json` and
`artifacts/protocol-fixtures/results-production-contracts.json`.

### 3.3 Regenerating the committed probe catalog

`unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs` is owned by the compiler.
`CommittedProbeCatalogTests.CommittedGeneratedCatalogMatchesAFreshGeneration` fails when the committed file
differs from a fresh generation of `Catalogs/ProbeCatalog.catalog.json`. If that happens, the fix is mechanical:

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity tools/unity/build_probe.sh   # step 1 regenerates the file
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs
git add unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs && git commit
```

The committed file was produced on the authoring host by a faithful mirror of `CatalogEmitter` (this host has no
C# compiler). §6 records that as the single highest-risk item in this delivery: if the mirror drifted, only the
text of that file is affected, and the command above replaces it with the compiler's own output.

## 4. Requirement and test coverage mapping

Requirement sections named by `09-implementation-guide.md` GC-003:

| Requirement | Implementation | Executable evidence |
|---|---|---|
| P-004 stable identities | `Id128`, generated wrappers, `StableNameKeyDerivation`, `CanonicalHex` | `IdentityDerivationTests` (derivation equals the committed probe literals, non-canonical names reject); fixture cases `identity-canonical-bytes`, `identity-order`, `identity-hex-parsing` (both runs) |
| P-005 runtime handles | `TargetHandle`/`ScopeHandle`/`PluginHandle` (generation 0 reserved), counters with `TryIncrement` | fixture cases `identity-handles`, `version-counters` (both runs); `CatalogBuildTests` covers key/version mismatch reporting |
| P-006 version domains | `CompositionRevision`, `AssemblyEpoch`, `ActivationEpoch`, `LogicalStepId`, `SnapshotToken` | fixture cases `version-counters`, `assembly-independence` (both runs) |
| P-009 manifest/catalog rejection | `ManifestValidator`, `ImmutableCatalog.Build`, `CatalogDescriptionReader` | `ManifestValidationTests` (20 cases: duplicate ids, unknown schema/version, unknown key, unsupported protocol/feature, missing policies, stratum rules, access conflicts, stage/resource cycles, buffer bounds, partitioned writers accepted); `CatalogBuildTests`; `CatalogDescriptionRejectionTests` |
| P-027 plans | `ChangePlan`, `PlanDeltas`, `CompositionDelta`/`DerivationDelta`/`RuntimeDelta` | contract shape asserted by the API-compatibility test and by the fixture run returning the same `ChangePlan`/delta types |
| P-039 stage registration | `StageSpec`, `SystemSpec`, `AccessSet`, `OrderedDispatchTable` | `ManifestValidationTests` (stage/system access, system cycles, ambiguous order, stage cycles) |
| P-042 commands/requests | `CommandEnvelope`, `RequestResult`, `CommandAdmissionReceipt`, `RouteId` | API-compatibility test; fixture run `assembly-independence` (no gameplay/engine types in the surface) |
| P-043 buffers | `BufferSpec`, `BufferPort`, `BufferLifetime`/overflow/cancellation enums | `ManifestValidationTests` (capacity, unknown stage endpoints) |
| P-054 serialization | `Envelope` (copied), `GeneratedSerializerBase`, `GeneratedEnvelopeReader`, generated serializers | `GeneratedSerializerContractTests` (valid/tampered/missing-required/duplicate/unknown-optional/foreign-schema/unknown-feature/truncation/trailing bytes); fixture case `envelope-codec` (both runs); probe step `production-contract-catalog` |
| P-055 protocol evolution | `SupportedProtocolRange`, `EnvelopeHeader` feature gate, catalog supported features | `ManifestValidationTests` (protocol range, unknown required feature); `CatalogDescriptionRejectionTests` (major/minor); fixture case `version-support` (both runs); generated catalog feature gate in the probe |
| P-058 V1 profile | package/asmdef boundary, generated registrations, closed generic roots, `RegisterGenericJobType`, fingerprint | probe: `generated-aot-roots`, `production-contract-catalog`; `CompilerTests` asserts emitted assembly attributes, root statements and their terminators; `noEngineReferences: true` on both packages |
| P-050/P-051 cancellation ledger | `CancelOutcome`, `OperationId`, `CancelOutcome` reporting on the operation seam | `CancelOutcomeContractTests` pins all six values, including the two the reopened W0 gate added; `tools/check_contract_surface_parity.py` checks them against the regenerated snapshot |

Suites named by GC-003:

| Suite | Implemented subset |
|---|---|
| TEST-001 | generated registration + closed generic handler + query + Burst job + serialization round trip all execute in the player; generated output reproducible; omitted registration fails explicitly |
| TEST-002 | identity/derivation fixtures, stale-handle and world-incarnation cases, counter/epoch overflow, catalog key-version mismatch reporting |
| TEST-003 | manifest validation: unknown schema, unsupported version, duplicate exports, missing providers modelled as explicit codes; catalog lookup miss is never substituted |
| TEST-009 | plan/DTO shapes and the `ImmutableCatalog`/`ChangePlan` boundary; no live writes exist in this task |
| TEST-012 | stage/system declarations, access sets, ambiguous-order and cycle rejection at validation time |
| TEST-013 | access declarations and partition disjointness (validated writers, ambiguous writers) |
| TEST-017 | schema/version registrations, serializer binding, migration and policy keys required by `ManifestValidator`; envelope corruption/tamper detection |
| TEST-020 | generated catalog roots constructors/handlers directly, unknown keys resolve to an explicit miss, generated output is reproducible from one catalog description |

Also: TEST-021/TEST-024's mechanical parts continue to run through the seam-based fixture suite and
`tools/run_gc003_checks.sh`.

Host-side gates that run without a C# compiler (they complement, never replace, the build-host gates):

| Gate | What it proves |
|---|---|
| `tools/check_game_core_csharp.py` | brace balance, forbidden-language-construct scan, engine-reference scan, and a non-void method with no return (the CS0161 class of defect) |
| `tools/verify_generated_catalog.py` | the committed generated catalog's `CatalogFileHash` covers its own prefix and its `CatalogFingerprint` equals an independent recomputation over the tables its `BuildCatalog()` builds |
| `tools/check_contract_surface_parity.py` | every enum and enum value in the frozen snapshot exists in the production sources with the same numeric value (additions are reported, not failed) |

## 5. Decisions, assumptions and documentation ambiguities

Recorded here because 00 wins over 05, which wins over 09; each decision is the simplest reading consistent with
00.

1. **Typed category wrappers, not raw `Id128`.** 05 §1 says production generates a wrapper per identity
   category and that the compilable skeleton uses `Id128` only for compactness. The production package therefore
   keeps the W0 wrappers (`SchemaId`, `DefinitionId`, `TargetId`, ...) with the skeleton's field *names*.
2. **`FactoryKind.Handler = 10` added.** The W0 enum had no category for a generated closed generic handler
   registration (04 §8 item 3), and reusing `SystemFactory` would misreport the kind in the catalog fingerprint.
   This is a surface *addition*; `api-surface-diff.log` shows every difference from the frozen sources is an
   addition.
3. **`EnvelopeError.MissingRequiredField` (16) and `DuplicateField` (17) added.** The generated field-table walk
   must distinguish "required field absent" and "field id repeated" from a generic length mismatch (05 §6).
4. **`EnvelopeReader.TrySeekTo(int)` added.** A generated deserializer re-reads each recorded declared field at
   its absolute offset instead of scanning the document twice. Without it, every generated serializer would
   rescan the whole document per field.
5. **Generated-code runtime seam lives in `GameCore.Contracts` (`ISchemaSerializer`, `GeneratedSerializerBase`,
   `GeneratedEnvelopeReader`, `GeneratedFieldSlot`, `GeneratedFieldBuffer`, `BoundRegistration<T>`).** Generated
   source is compiled into a project assembly that must work in the player, and the contract assembly is the
   only engine-free assembly generated code may depend on. The envelope codec already lives here, so the walk
   that validates a document against a generated field table belongs beside it. Keeping it here means one
   implementation of the header/feature/required-field/checksum rules instead of one per generated schema.
6. **Value types are immutable; the deserializer builds them through the constructor.** Generated value types
   use `readonly` fields, so the generated reader collects per-field locals and constructs once. (An earlier
   draft assigned fields directly, which C# rejects for a `readonly` field outside a constructor.)
7. **Compiler input form is a strict JSON description document** rather than C# declaration classes, because
   the source of truth must be readable data with no compiler in the loop; the format id and every member are
   documented in the compiler README and validated member-by-member (`UnknownMember`/`MissingMember`/
   `InvalidValue` with a precise document path).
8. **Identity hex must be lowercase**, matching the canonical form (`Id128Codec.TryParseHex`) rather than being
   case-folded (P-004 through 05 §6's "canonical byte order" rule).
9. **Two reachable-rejection notes.** A duplicate *derived key* cannot be constructed without a SHA-256
   collision, so the reachable duplicate is a repeated stable name; the generator reports
   `DuplicateStableName` and the `DuplicateKey` diagnostic was removed rather than shipped as dead code. A
   description-level double registration of the same feature id is caught by the catalog check
   (`InvalidCatalog`, "duplicate supported feature id").
10. **The probe's generated catalog is compiler output, not hand-written**, so the artifact has one owner. The
    GC-001 `ProbeCatalogGenerator` became a thin bridge over `GameCore.Content.Compiler`.
11. **A `link.xml`-preserved description document outside `Assets/`.** The description lives in
    `unity/GameCore.Validation/Catalogs/` so Unity does not import it and no generated `.meta` GUID is invented;
    the generated file stays inside `Assets/` where the existing `.meta` already exists.
12. **`Store`, `HostContracts` and the GC-004/GC-005 seams are untouched.** They are other waves' surfaces; this
    task only added the catalog/validation/result behaviour they consume.
13. **The merged `CancelOutcome` addition is mirrored verbatim.** `origin/main` reopened the W0 interface gate to
    add `CancelOutcome.IdempotencyConflict = 4` and `CancelOutcome.Rejected = 5` for P-050, with a regenerated
    snapshot (`# member count: 1591`). Production now declares all six members with the same numeric values and
    the same documentation, and `CancelOutcomeContractTests` pins the numbers so a silent renumber fails. The
    reason these two belong in production rather than only in the seam: a caller switches on the value, and the
    ledger row conflict the code names is a production behaviour, not a test fixture concern.
14. **Declared feature ids are canonicalised at read time.** The emitter previously emitted
    `SupportedFeatureIds` in document order while the fingerprint sorted them, so two descriptions with the same
    feature set listed in opposite order produced different bytes and the same fingerprint. `ReadFeatureIds` now
    sorts by canonical identity order, and `FeatureDeclarationOrderDoesNotChangeGeneratedBytes` covers it.
15. **Emitted member order is canonical, not declarative.** Group tables are sorted by ordinal table name and
    schemas by schema identity, so `DeclarationOrderDoesNotChangeGeneratedBytes` (groups, entries and fields all
    reversed) now holds; previously only the members *inside* a table were canonicalised.

## 6. Known gaps

1. **Nothing here has been compiled or executed.** The dotnet projects, both Unity packages, the generated
   probe catalog and every test are `NotRun`. The most likely first-compile candidates are the two new test
   projects (NUnit API usage and member arity) and the generated probe catalog. Two read-only review rounds ran
   on this host and found ten defects in the pre-review revision; all are fixed and recorded in
   `artifacts/gc-003/review-round-1.md`, and the fixed areas were re-reviewed (byte-for-byte reproduction of the
   committed catalog confirmed against the emitter's own Append sequence).
2. **The committed probe catalog was produced by a local mirror of the emitter**, because this host has no C#
   compiler. Its two recorded 64-hex values are independently recomputed from the file's own tables by
   `tools/verify_generated_catalog.py` (the same computation the player performs), and a reviewer transcribed the
   emitter's Append sequence by hand and reproduced the file byte for byte (22579 bytes). Only
   `CommittedProbeCatalogTests` can prove it against the real compiler; remediation is one command (§3.3) and
   touches only that file. The mirror itself is a scratch tool and is not committed — the emitter is the owner.
3. **`Packages/packages-lock.json` for the Unity project is not updated here.** The two new `file:` package
   entries are resolved on the first Editor run; commit the resolved lock as GC-001 already requires.
4. **Unity `.meta` files for the new package folders are not committed.** Unity creates them on first import;
   the same rule GC-001 recorded applies (commit them together with the resolved lock).
5. **No Unity EditMode/PlayMode test assemblies were added**, so the contract tests run under plain dotnet only.
   GC-003's tests are engine-free by design; the Unity-side evidence is the IL2CPP probe.
6. **`ManifestValidator` is a batch validator, not the planner.** It validates declarations and cross-manifest
   references; it does not compute derivation, eligibility or schedules (GC-006/GC-009 own those), and it
   deliberately does not mutate anything.
7. **No perf claim.** Nothing in this task measures runtime cost; the fingerprint and canonical ordering are
   correctness properties, not performance evidence.

## 7. Proposed seam changes

**None proposed by this task.** `tests/GameCore.ReferenceSeams/**` is byte-identical to `origin/main`; the only
seam change on this branch arrived through the merge (§5 item 13). All additions to the production surface are
listed in §5 items 2–5 and in `Packages/com.gamecore.contracts/README.md` §3, and
`dotnet/tests/GameCore.Contracts.Tests` enforces "strict superset, no removals" against the frozen snapshot as a
committed test, with `tools/check_contract_surface_parity.py` backing the same rule on a compiler-less host.
