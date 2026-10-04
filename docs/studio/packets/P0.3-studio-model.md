# PACKET P0.3 studio-model

Owner: Opus 5.5. Contract: [docs/studio/03-authoring-contracts.md](docs/studio/03-authoring-contracts.md) s1 to s6 and s9;
02 boundaries B/C, s6 identity map, SADR-004/008/009. Branch base: `7a9c409` (the studio design set). The worktree
started at `0523bea`, so it was fast-forwarded to `7a9c409` to get `docs/studio/`.

## Built

| Path | What |
|---|---|
| `Packages/com.gamecore.studio.core/package.json` (+ `.meta`) | `com.gamecore.studio.core` 1.0.0, unity 6000.0, deps `com.gamecore.contracts` 1.0.0 and `com.unity.nuget.newtonsoft-json` 3.2.1 |
| `Packages/com.gamecore.studio.core/Runtime/Model/GameCore.Studio.Model.asmdef` | `noEngineReferences: true`, references `GameCore.Contracts`, `overrideReferences: true`, `precompiledReferences: ["Newtonsoft.Json.dll"]`, `autoReferenced: false` |
| `Packages/com.gamecore.studio.core/Runtime/Model/*.cs` (13 files, all with `.meta`) | the Unity-free model (API summary below) |
| `dotnet/src/GameCore.Studio.Model/` | netstandard2.1 project over the package sources (same pattern as `GameCore.Rules.Narrative`), NuGet Newtonsoft.Json 13.0.3, project reference to `GameCore.Contracts` |
| `dotnet/src/GameCore.Studio.Model.Schema/` | net8.0 console app: a small reflective JSON Schema (draft 2020-12) emitter, `--out <dir>` |
| `dotnet/tests/GameCore.Studio.Model.Tests/` | NUnit tests (78) + `Samples/*.json` (concrete versions of the 03 samples, plus the tool-catalog sample) |
| `dotnet/GameCore.sln` | the three projects added, with GUIDs `{5D0D1E00-0303-4A51-9C03-00000000030x}` (picked so they can't collide with other packets' GUIDs) |
| `tools/studio/emit_studio_schemas.py` | stdlib-only driver: regenerates `docs/studio/schemas/`. `--check` fails on any difference. Exits 2 when no .NET SDK is present (the Mac has none) |
| `docs/studio/schemas/*.schema.json` | authoring-ref, selection-snapshot, semantic-index, tool-catalog, change-set, diagnostic (generated on the host, committed) |

Design notes:
- Every shape is an immutable `sealed class` with a constructor, `[JsonObject(MemberSerialization.OptIn)]` and an
  explicit camelCase `[JsonProperty]` with `Required.Always` for each required member. Nulls are omitted. A plain
  `JsonConvert` call produces the same keys (tested).
- `StudioJson` holds the strict read rules: unknown members refused, trailing content refused, no date sniffing
  inside free-form `args` (date-like strings stay verbatim), and `'\n'` newlines.
- Enums are always strings. `StrictStringEnumConverter` accepts only the exact documented spelling, so it refuses
  integers, other casings and comma-joined flags. The lowercase JSON spellings in 03 (`references|contains|...`,
  `agent|manual|...`, `stamp|none`, `pending|pass|fail`) come from `[EnumMember]`.
- Optional arrays are nullable rather than defaulting to empty, so documents round-trip exactly: an omitted
  `dependsOn` stays omitted. `ChangeSet.State`/`Policy` are optional, and `EffectiveState` (Requested) and
  `EffectivePolicy` (AllOrNothing) supply the defaults.
- `ChangeSet` is immutable. Lifecycle progress uses `WithState/WithOutcomes/WithLinks/WithTimestamps/WithValidation/WithRequirements`.
- The ToolCatalog JSON shape is not written out in 03; this packet defines it (see the schema and
  `Samples/tool-catalog.json`).

## Verified (Linux host `myubuntu`, .NET SDK 8.0.425, synced copy at `~/wkspace/gc-studio/p03`)

```
$ dotnet build dotnet/tests/GameCore.Studio.Model.Tests/GameCore.Studio.Model.Tests.csproj
Build succeeded.            (TreatWarningsAsErrors from Directory.Build.props; 0 warnings)

$ python3 tools/studio/emit_studio_schemas.py && python3 tools/studio/emit_studio_schemas.py --check
wrote 6 schema(s) to docs/studio/schemas
studio schemas are up to date (6 file(s))

$ dotnet test dotnet/tests/GameCore.Studio.Model.Tests/GameCore.Studio.Model.Tests.csproj
Passed!  - Failed: 0, Passed: 78, Skipped: 0, Total: 78, Duration: 428 ms - GameCore.Studio.Model.Tests.dll (net8.0)

$ dotnet sln dotnet/GameCore.sln list   -> lists the three Studio projects
```

What the 78 tests cover:
- **Round trips** (9 samples). For each one: parse into its type, re-serialize, check it is equal modulo key order
  and int/float spelling, check a second round trip is byte-identical, and check both the sample and the
  re-serialized form against the emitted schema. The schema check uses a minimal draft-2020-12 checker in the test
  (`$ref`, `oneOf`, `type`, `enum`, `const`, `pattern`, `minLength`, `properties/required/additionalProperties`,
  `items/minItems/maxItems`). Negative tests show that checker rejects bad instances.
- **Strict reads**: missing required member, unknown member, integer/miscased/combined-flag enum, trailing content,
  and constructor null guards (reported as `JsonSerializationException`).
- **Schemas**: `CommittedSchemasAreCurrent` (committed files byte-equal a fresh emission; this is the in-test form of
  `--check`), deterministic emission with sorted keys, and contract facts (const schema id, id pattern, enum
  spellings, `where` as a `oneOf`).
- **Identity**: `TargetIdFor(id)` equals `StableNameKeyDerivation.Derive("auth." + id)` (asserted directly against
  the kernel helper), and non-canonical ids are refused. ULID encoding matches the ULID spec vector
  (`01ARYZ6S41`), and `cs_01K6Q0ACC07E5S3HTP4YZEASPW` was cross-checked with an independent Python encoder. Content
  stamps are checked against the known SHA-256 vectors for "abc" and the empty input.
- **ToolCatalogBuilder** over annotated sample types (`AnnotatedSamples.cs`) produces exactly
  `Samples/tool-catalog.json`. Also covered: order independence, value-type inference, float-literal normalisation
  (`0.1f` gives 0.1), the max-of runtime-apply rule, declaration errors, and `Merge` with conflict refusal.
- **ChangeSetValidator**: one test per rule. Each mutates the valid 03 sample and asserts the code and the `where`.

Checkers on the Mac:
- `python3 tools/check_game_core_csharp.py` passes repo-wide (627 files). It does not reach the new files yet,
  because P0.2 owns the TARGETS list.
- I ran the same checker over only this packet's paths by overriding `TARGETS` (22 files): ok. An engine-type /
  `System.Text.Json` scan of `Packages/com.gamecore.studio.core` is also clean.
- `python3 tools/check_package_metadata.py` fails with two expected findings that P0.2 and integration own:
  1. `com.unity.nuget.newtonsoft-json` is declared, but the checker sees no asmdef reference to it. It is a
     precompiled reference, which the checker does not yet understand (P0.2 is adding that).
  2. `packages-lock.json: com.gamecore.studio.core is not locked`. The lock is outside this packet's paths.
- Meta GUIDs were made with `tools/make_unity_metas.py Packages/com.gamecore.studio.core`. There are no duplicate
  GUIDs in `Packages/` or `unity/`.

Not run: the Unity batchmode compile of the package (no Unity project lists it yet; P0.2 and P1.6), and the
solution-wide `dotnet test` (that is the integration owner's gate).

## Contract findings for Fable (decided 2026-10-04)

1. **TargetId formula (resolved).** 02 s6 / 03 s1 originally said `StableNameKeyDerivation("auth:" + authoringId)`,
   and `Derive` refuses `:`. Fable decided to use the kernel helper with a dot prefix:
   `IdDerivation.TargetIdFor(id) = StableNameKeyDerivation.Derive("auth." + id)`. That is implemented, the earlier
   parallel SHA rule (`DeriveKey`) is removed, and Fable is updating 02 s6 and 03 s1. P0.5's Rust mirror must use
   `"auth." + id`.
2. **03 s3 sample `type` (accepted; docs being updated).** The sample index node uses `"type": "npc.NpcDefinition"`. The validator compares
   `IndexNode.Type` with the tool's `targetType`, which is the `[Authorable]` typeId (e.g. `npc.definition`). So
   the index builder (P1.6) must project the typeId. The samples here use typeIds.
3. **Diagnostic codes for structural rules (accepted).** 03 s9 lists no code for dependency cycles, duplicate op ids, unused
   or missing artifacts, or inconsistent requirements. All of them use `CandidateInvalid`, and the message says
   which rule fired. `Diagnostic` keeps the 03 shape exactly (`code, message, hint?, where?`), with no `severity`
   field.
4. **Stamp checks in the validator (accepted).** It compares op target stamps with the index (`StaleTarget`) and base-version
   stamps with the index (`Conflict`, per 03 s7). It also refuses a stamp-precondition op whose target has no stamp
   (`CandidateInvalid`). Both checks can be switched off with `ChangeSetValidationOptions`. The live Unity precheck
   stays with P1.6.
5. **Additions beyond the 03 text, needed to make s4 exportable (accepted).** `AuthorValidatorAttribute(id){Codes}` (the
   "validators and their diagnostic codes" part), `AuthorOperation.Requires/RequiresOnTarget/Scope/TargetKinds`,
   `AuthorArg/AuthorField.Type` (value-type override, e.g. `ref` for a DefinitionRef passed as text, or
   `artifact`), and `AuthorArg.Name/Category/Required`. `AuthorableAttribute`'s id property is called
   `ObjectTypeId`, because `Attribute.TypeId` already exists on the base class (the constructor parameter is still
   `typeId`).

## Left open

- `tools/check_game_core_csharp.py` TARGETS/`engine_free` entries for `Packages/com.gamecore.studio.core/Runtime/Model`
  and the three dotnet projects. P0.2 owns that file.
- `check_package_metadata.py` support for the precompiled Newtonsoft reference, and the lock entry (P0.2 / integration).
- The Unity compile of the asmdef (needs a project manifest that includes the package and
  `com.unity.nuget.newtonsoft-json`). IL2CPP/AOT preservation of the Newtonsoft constructor-based types (a
  `link.xml`) has not been looked at. The model is meant for Editor and companion use.
- `Packages/com.gamecore.studio.core/Runtime.meta` was created here because Unity needs it. P1.6 should keep it
  rather than mint another one.
- No built-in tool *entries*. `BuiltInToolIds` lists the ids from 03 s5, and the edit engine (P1.6) registers
  their `ToolEntry`s and merges them with `ToolCatalog.Merge`.
- The validator has no value checks for generic `set`/`assign` tools whose argument names an object field
  dynamically. That would need a field-addressed argument convention from P1.6.

## API summary (namespace `GameCore.Studio.Model`, assembly `GameCore.Studio.Model`)

For P0.5 (Rust `serde` mirror): JSON names are camelCase exactly as in the schemas. Enums are the strings listed.
Optional members are omitted, never `null`. Required members are the schema `required` arrays.

| Type | JSON shape / role |
|---|---|
| `AuthoringRef` | `{kind, authoringId?, global?, assetGuid?, path?, definition?, scope?, stamp?, location?}`. Also `IdentityKey`, `SameTarget`, `ShapeProblems()`, `WithStamp/WithScope`, value equality |
| `LocationRef` | `{region, position[3], normal[3]?}` |
| `AuthoringKind` | `Entity, Definition, Region, SceneObject, Asset, UiElement, Scope, Location` |
| `AuthorScope` (flags) | `Instance, Prefab, Definition, Scope`. A single value in a ref; arrays in catalogs |
| `SelectionSnapshot` | `{id ("sel_"+ULID), mode, targets[], parts[]?, regionRect?, frame?, worldSession?, indexRevision}` |
| `SelectionMode` | `Edit, Play` |
| `PartRef` / `RegionRect` / `FrameContext` / `CameraPose` | `{owner, part}` / `{screen[4]}` / `{camera, viewport[2] int, image? stamp}` / `{position[3], rotation[3..4], fov, aspect}` |
| `SemanticIndex` | `{revision, project, nodes[], edges[]?, scopes[]?}`. Also `FindNode`, `FindByDefinition` |
| `IndexNode` | `{ref, type, name?, fields{name: IndexField}?, refs[]?, capabilities[]?, provenance?}`. Also `Provides(typeOrCapability)` |
| `IndexField` / `IndexRef` / `IndexEdge` / `ScopeEntry` / `Provenance` | `{type, value? any, unit?, range[2]?}` / `{field, to}` / `{from, to, kind}` / `{scope, region?, installs[]?}` / `{asset?, line?}` |
| `EdgeKind` | `references, contains, spawns, bindsUi, triggers` |
| `ToolCatalog` | `{schema:"gamecore.studio.toolcatalog/1", plugin?, objectTypes[], tools[]}`. Also `FindTool`, `FindObjectType`, `Merge` |
| `ObjectTypeEntry` | `{typeId, displayName?, doc?, scopes[]?, runtimeApply, fields[]}` |
| `ValueSpec` (abstract) / `FieldSpec` / `ArgSpec` | `{name, type, required, unit?, min?, max?, step?, category?, doc?, enumValues[]?}` |
| `ValueTypes` | `bool int float string enum vector2 vector3 vector4 quaternion color ref artifact object`, plus the `[]` suffix |
| `ToolEntry` | `{id, tier, doc?, targetType?, targetKinds[]?, targetRequired, scopes[]?, args[], prerequisites[]?, runtimeApply, validators[]?}` |
| `ToolTier` / `RuntimeApply` | `Configure, Compose, Mechanism` / `Live, Rebuild, Compile, Build` (ordered) |
| `Prerequisite` / `PrerequisiteSubject` / `ValidatorRef` | `{requires, on, doc?}` / `Project, Target` / `{id, codes[]}` |
| `BuiltInToolIds` | the 03 s5 built-in tool ids |
| `ChangeSet` | `{id ("cs_"+ULID), schema:"gamecore.studio.changeset/1", intent, selection?, baseVersions[]?, operations[], artifacts[]?, validation[]?, requirements?, links?, state?, outcomes[]?, policy?, timestamps?}` |
| `Intent` / `IntentOrigin` | `{text, voiceTranscriptId?, origin}` / `agent, manual, voice, replay` |
| `BaseVersion` | `{ref, stamp}` |
| `Operation` / `Preconditions` | `{opId, tool, target?, args? object, dependsOn[]?, preconditions?, applyRequirement?}` / `stamp, none` |
| `ArtifactRef` / `ArtifactProducer` / `ArtifactImport` | `{sha256 (bare hex), name?, mediaType, bytes, producer?, role?, import?}` / `{etosTask?, op?, provider?, model?}` / `{type, settings? object}`. Args point at an artifact as `{"artifact":"sha256:<hex>"}` |
| `ValidationScenario` / `ScenarioStatus` | `{scenario, status, detail?}` / `pending, pass, fail` |
| `Requirements` | `{max, worldRebuild, compile, build}`. Also `FromOperations` |
| `Links` | `{etosTasks[]?, parent?, gameCoreOps[]?}` |
| `ChangeSetState` | `Requested, Running, Candidate, Staged, Applied, Rejected, Failed, Undone, Interrupted` |
| `OperationOutcome` / `OutcomeStatus` / `OperationUndo` | `{opId, status, code?, detail?, gameCoreOps[]?, undo?}` / `Applied, Skipped, Refused, Failed` / `{inverse? object}` |
| `ApplyPolicy` / `Timestamps` | `AllOrNothing, BestEffort` / `{requested?, candidate?, applied?}` (ISO-8601 text) |
| `Diagnostic` / `DiagnosticWhere` | `{code, message, hint?, where?}`, where `where` is an opId string or an AuthoringRef object |
| `DiagnosticCodes` | `StaleTarget, Conflict, UnknownTool, InvalidArgs, MissingPrerequisite, ScopeNotAllowed, ValidationFailed, Refused, CandidateInvalid, StaleContext, StageFailed, LedgerConflict, NotConfigured, OutcomeUnknown, Blocked`. Also `All`, `IsRegistered` |
| `IdDerivation` | `TargetIdFor(authoringId) -> GameCore.Contracts.TargetId` = `StableNameKeyDerivation.Derive("auth." + authoringId)` (`AuthoringNamePrefix = "auth."`), `NewChangeSetId(ms, IIdEntropy)` / `NewChangeSetId()`, `NewSelectionId`, `FormatUlid`, `IsChangeSetId`, `IsSelectionId`. Also `IIdEntropy`, `CryptoIdEntropy` |
| `ContentStamp` | `Of(bytes)`, `OfUtf8`, `Sha256Hex`, `IsValid`, `IsValidHex`, `DigestOf` |
| `StudioJson` | `CreateSettings`, `Serialize`, `Deserialize<T>`, `ToToken`, `ParseToken`. Also `StrictStringEnumConverter`, `SchemaHintAttribute`, `StudioPatterns` |
| Attributes | `AuthorableAttribute(typeId){DisplayName, Scope, RuntimeApplicability, Doc}` (property `ObjectTypeId`), `AuthorFieldAttribute{Type, Unit, Min, Max, Step, Doc, Required}`, `AuthorRefAttribute{Category, Required=true, Doc}`, `AuthorOperationAttribute(toolId){Doc, Validator, Tier, RuntimeApplicability, Scope, Requires, RequiresOnTarget, TargetKinds}`, `AuthorArgAttribute{Name, Type, Unit, Min, Max, Step, Doc, Category, Required=true}`, `AuthorValidatorAttribute(id){Codes}` |
| `ToolCatalogBuilder` | `AddAssembly/AddType/AddMethod`, `Build(plugin?)`, `MapValueType`, `ExpandScopes`, `Number` |
| `ChangeSetValidator` / `ChangeSetValidationOptions` | `new ChangeSetValidator(catalog, index?, options?).Validate(changeSet) -> IReadOnlyList<Diagnostic>` / `{CheckStamps=true, RequireTargetsInIndex=true}` |

Validator rules, in output order:
1. Envelope (`CandidateInvalid`): schema id, change-set id, `links.parent`, empty operations.
2. Op ids and dependencies (`CandidateInvalid`): duplicate opIds, unknown `dependsOn`, cycles. Each cycle is
   reported once, at its first member.
3. Per operation:
   - `UnknownTool`.
   - Target: missing target, kind not in `targetKinds`, or node type not equal to `targetType` → `InvalidArgs`;
     malformed ref → `CandidateInvalid` at the ref; not in the index → `StaleTarget` at the ref.
   - Scope: not allowed by the tool or by the object type → `ScopeNotAllowed`.
   - Stamps: stamp-precondition op with no stamp → `CandidateInvalid`; stamp differs from the index →
     `StaleTarget`.
   - Prerequisites on the Project or the Target → `MissingPrerequisite`.
   - Arguments → `InvalidArgs`: unknown, missing required, wrong type, out of min/max, enum value, vector arity,
     `ref` category (checked when the referenced node is inside the index slice), artifact shape.
   - `applyRequirement` weaker than the tool's → `CandidateInvalid`.
4. Artifacts (`CandidateInvalid`): bad digest, duplicate, referenced but not carried, carried but used by no op.
5. Requirements (`CandidateInvalid`): `max` or a flag under-declared relative to the ops, internal inconsistency,
   requirements missing when the ops need more than Live.
6. Base versions: bad stamp → `CandidateInvalid`; stamp differs from the index → `Conflict`.

Regenerate the schemas after any model change: `python3 tools/studio/emit_studio_schemas.py` on the host. The test
`SchemaTests.CommittedSchemasAreCurrent` fails until you do.
