# com.gamecore.content.compiler — build-time content compiler (GC-003)

Normative sources: [`05-contracts-and-data-model.md`](../../docs/game-core/05-contracts-and-data-model.md) §3
and §6, [`04-unity-integration.md`](../../docs/game-core/04-unity-integration.md) §8,
[`00-core-protocols.md`](../../docs/game-core/00-core-protocols.md) P-009, P-028, P-054, P-055.

Two assemblies:

| Assembly | Sources | Platform | Engine |
|---|---|---|---|
| `GameCore.Content.Compiler` | `Runtime/**` | Editor only (`includePlatforms: ["Editor"]`) | none (`noEngineReferences: true`) |
| `GameCore.Content.Compiler.Editor` | `Editor/**` | Editor only | `UnityEditor`/`UnityEngine` for paths and logging only |

The generator core is Unity-free so `dotnet/src/GameCore.Content.Compiler` and
`dotnet/tests/GameCore.Content.Compiler.Tests` exercise exactly the code Unity runs. It emits ordinary C# source;
there is no runtime compiler, no reflection-based construction and no runtime assembly loading (04 §8).

## Input form

The compiler consumes one **catalog description document**: strict JSON (no comments, no trailing commas, no
duplicate member names, no leading zeros), format id `gamecore.catalog-description/1`. Every member is required
unless marked optional; an unknown member rejects with `UnknownMember` so a typo cannot silently drop a
registration.

```json
{
  "descriptionFormat": "gamecore.catalog-description/1",
  "protocolVersion": "1.0",
  "namespace": "My.Project.Generated",
  "className": "GameCoreCatalog",
  "fileName": "GameCoreCatalog.g.cs",
  "supportedFeatureIds": ["<32 lowercase hex>", "..."],
  "schemas": [
    {
      "stableName": "my.project.schema.record",
      "valueTypeName": "RecordValue",
      "serializerTypeName": "RecordSerializer",
      "serializerKeyName": "RecordSerializerKey",
      "schemaId": "<32 lowercase hex>",
      "schemaVersion": 1,
      "serializerKeyVersion": 1,
      "ownerPackageId": "<32 lowercase hex, all zero = kernel>",
      "required": true,
      "fields": [
        { "id": 1, "name": "High", "wireType": "UInt64", "required": true },
        { "id": 2, "name": "Label", "wireType": "Utf8", "required": false }
      ]
    }
  ],
  "groups": [
    {
      "tableName": "PluginRegistrations",
      "keysName": "PluginKeys",
      "lookupMethodName": "TryGetPluginFactory",
      "interfaceType": "My.Project.IPluginFactory",
      "kind": "PluginFactory",
      "entries": [
        {
          "stableName": "my.project.plugin.alpha",
          "keyName": "AlphaKey",
          "keyVersion": 1,
          "ownerPackageId": "<32 lowercase hex>",
          "implementationId": "<32 lowercase hex, non-zero>",
          "implementationExpression": "new My.Project.AlphaFactory()"
        }
      ]
    }
  ],
  "code": {
    "usingDirectives": ["My.Project"],
    "assemblyAttributes": ["RegisterGenericJobType(typeof(My.Job<My.Value>))"],
    "closedGenericRootStatements": ["My.Roots.Track(default(My.Job<My.Value>))"]
  }
}
```

Rules:

- `supportedFeatureIds`, `schemas` and `groups` are optional and default to empty; `code` is optional.
- Identity fields are exactly 32 **lowercase** hexadecimal characters, the canonical form of
  `Id128Codec`/`StableNameKeyDerivation` (P-004). Uppercase is rejected rather than normalized.
- `wireType` is one of `Bool`, `Bytes`, `Float32`, `Float64`, `Id128`, `Int32`, `Int64`, `UInt32`, `UInt64`,
  `Utf8`. A schema field id is positive; id 0 is reserved for the envelope checksum.
- `kind` is one of `PluginFactory`, `SystemFactory`, `Reducer`, `StaticPredicate`, `Serializer`, `Migration`,
  `ResourceFactory`, `LayoutApply`, `SchemaFactory`, `StatePolicy`, `Handler` — the members of
  `GameCore.Contracts.FactoryKind`.
- `code` fragments are single-line, comment-free, brace-free and semicolon-free; the emitter supplies the
  statement terminator. They exist because direct constructors, closed generic instantiations and assembly
  attributes cannot be synthesized from data (04 §8). A fragment that does not compile fails the consuming
  project's build, which is the intended type check.
- `implementationExpression` may reference the entry's own generated key constant by name, for example
  `new My.Handler(AlphaKey)`.

## Output

Deterministic C# with LF line endings, no BOM, no timestamps and no machine paths. Regenerating from the same
validated description reproduces the file byte for byte, and so does a description whose declarations are
permuted: group tables are emitted in ordinal table-name order, schema registrations in schema-identity order,
entries in derived-key order, fields in field-id order and feature ids in canonical identity order. No
declaration order in the document reaches the output, and the test suite asserts each of those permutations.
The generated class contains:

- `GeneratedFileName`, `DescriptionFormat`, `ProtocolVersion`, `HashAlgorithm`, `CatalogFileHash`,
  `CatalogFileHashScope`, `CatalogFingerprint`, `CatalogFingerprintScope`, `SupportedFeatureIds`;
- one `FactoryKey` constant and one `BoundRegistration<T>` entry per group entry, sorted by derived key;
- one `FactoryRegistration[]` table per group, carrying owner package and implementation identity;
- `SchemaRegistrations`, one generated value type per schema, one generated serializer per schema, and the
  `Serializers` array — so no serializer is discovered at runtime;
- `BuildCatalog()` (validates the tables under the production catalog rules), `BuildVerifiedCatalog(out ...)`,
  `FingerprintMatchesGeneratedCatalog(out ...)`;
- `RootClosedGenericInstantiations()` and `HasClosedGenericRoots` when root statements are declared.

`CatalogFingerprint` is computed by `GameCore.Contracts.CatalogFingerprint` over the same canonical tables the
runtime catalog builds, so a stale generated file cannot describe a catalog whose declarations changed (P-028).

## Invocation

```sh
# from the project root
<UNITY> -batchmode -nographics -quit -projectPath unity/GameCore.Validation \
  -executeMethod GameCore.Content.Compiler.Editor.CatalogGeneratorMenu.GenerateFromCommandLine \
  -catalogDescription <path to the description document> \
  -catalogOutput <path of the generated C# file> \
  -logFile artifacts/gc-003/codegen.log

# verification only: fails when a committed generated file is stale
<UNITY> -batchmode -nographics -quit -projectPath unity/GameCore.Validation \
  -executeMethod GameCore.Content.Compiler.Editor.CatalogGeneratorMenu.VerifyFromCommandLine \
  -catalogOutput <path of the generated C# file>
```

Unity exits `0` only after the file was written and re-read with a matching file hash and fingerprint;
otherwise it exits nonzero (`CatalogGeneratorMenu.GenerationFailureExitCode`) with the diagnostics in the log.

The Editor menu `GameCore/Content/Generate Catalog` generates the project defaults
(`Assets/GameCore/Catalogs/GameCoreCatalog.catalog.json` →
`Assets/GameCore/Generated/GameCoreCatalog.g.cs`).

The GC-001 qualification project keeps its own explicit bridge,
`GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog`, which reads
`unity/GameCore.Validation/Catalogs/ProbeCatalog.catalog.json` and writes
`unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs` through this compiler. That
description is the committed input for the probe's generated catalog, so the file has exactly one owner.
