# GameCore Studio: authoring, selection, edit and tool contracts

**Status:** final contract reference as of P4.2d (2026-10-06). These shapes are shared by the Studio packages, the companion
agent and the workers; one owner per shape (named in each section). Generated schemas and source define exact members; update prose with their owning implementation.
JSON is the interchange form; C# shapes live in `com.gamecore.studio.core/Runtime/Model` (Unity-free,
`GameCore.Studio.Model.asmdef`, netstandard2.1, C# 9, `#nullable enable`) and are mirrored by `serde` types in
`studio/agent/src/model.rs`. The JSON Schemas are generated from the C# shapes by
`tools/studio/emit_studio_schemas.py` into `docs/studio/schemas/*.schema.json` and the companion validates against them.

## 1. Authoring identity (owner: `com.gamecore.gameplay.entities`, shape in studio.core)

| JSON member | Presence |
|---|---|
| `assetGuid` | optional |
| `authoringId` | optional |
| `definition` | optional |
| `global` | optional |
| `kind` | required |
| `location` | optional |
| `path` | optional |
| `scope` | optional |
| `stamp` | optional |

Exact nested types/constraints: [authoring-ref.schema.json](schemas/authoring-ref.schema.json).

Rules:

- `authoringId` is minted once by the authored-object pipeline and never regenerated on copy
  unless the copy is a *new* object. Prefab assets carry no entity id; placed instances receive one.
- `TargetId = StableNameKeyDerivation.Derive("auth." + authoringId)` (the kernel helper refuses `:`); placed instances derive from their own authoring id.
- `stamp` is the precondition value; the edit engine refuses an op whose target stamp changed (`Conflict`) unless
  the op declares `preconditions: "none"`.
- Unity instance IDs, `Entity` indices and `TargetHandle`s never appear in a change set.

The serialized narrative `contentStamp` is bake-derived metadata, distinct from the Studio `AuthoringRef.stamp` conflict precondition: a successful production bake writes the lowercase SHA-256 of `DefinitionCanonicalizer`'s ordinal canonical authored fields and stable references, never a clock or session value. History restores authored fields without fabricating a bake result; if an intervening Play/bake stamped edited content, undo leaves that cached stamp stale until the next successful bake. A saved byte-consistency comparison therefore starts from a baked baseline and runs the production bake after the final history transition, then saves and compares the complete asset bytes (including `contentStamp`); it must neither normalize/drop fields nor restore fixture bytes to manufacture equality. Bake failure fails consistency rather than bypassing validation.

## 2. Selection (owner: `com.gamecore.studio.core` Picking + `com.gamecore.studio.ui`)

| JSON member | Presence |
|---|---|
| `frame` | optional |
| `id` | required |
| `indexRevision` | required |
| `mode` | required |
| `parts` | optional |
| `regionRect` | optional |
| `targets` | required |
| `worldSession` | optional |

Exact nested types/constraints: [selection-snapshot.schema.json](schemas/selection-snapshot.schema.json).

Picking contract ([IPickingService](../../Packages/com.gamecore.studio.core/Runtime/Authoring/Picking/IPickingService.cs), Unity side):
- `Pick(Vector2 screenPoint) → PickResult` ordered by depth: physics hits (colliders), renderer-bounds hits (no collider),
  UI Toolkit `panel.Pick`, then ground plane. Each candidate: `AuthoringRef`, `distance`, `occluded: bool`,
  `part`. Overlapping candidates (within 0.5 % of depth or occluded chains) are shown as a list; the first is default.
- `Marquee(Rect screenRect, bool requireFullContainment = false) → PickResult` by frustum test on renderer bounds, optionally requiring full containment.
- `PointAt(Vector2 screenPoint) → LocationPick` samples the ground (NavMesh if present) and the owning region.
- Stale check: `Validate(SelectionSnapshot) → StaleReport` recomputes stamps, detects destroyed objects, unloaded
  regions (`RegionResidency.Unloaded`) and changed assets before any apply.

## 3. Semantic index (owner: `com.gamecore.studio.core` Authoring)

The index is a projection, rebuilt incrementally from `AssetPostprocessor`/scene events, cached under
`Library/GameCoreStudio` and exported in slices.

| JSON member | Presence |
|---|---|
| `edges` | optional |
| `nodes` | required |
| `project` | required |
| `revision` | required |
| `scopes` | optional |

Exact nested types/constraints: [semantic-index.schema.json](schemas/semantic-index.schema.json).

Slices for agents are bounded: the selection closure (depth 2), plus definitions by type on request, plus the tool
catalog; the companion enforces a 2 MiB cap and reports truncation explicitly.

## 4. Authoring metadata (owner: studio.core; used by every plugin)

Use gameplay mirror attributes for runtime types and Studio metadata for Editor-only types. Exact declarations are in [AuthoringMetadata.cs](../../Packages/com.gamecore.gameplay.contracts/Runtime/AuthoringMetadata.cs); a compiling tool signature is in [09 §Metadata and identity](09-plugin-developer-guide.md#metadata-and-identity). `ReadOnly`, `RuntimeOnly` and field `Structural` are explicit flags, default false. ([P1.7b §1](packets/P1.7b-gameplay-hardening-metadata.md#1-what-was-built))

Exported per plugin as `ToolCatalog` entries (schema `tool-catalog.schema.json`): supported objects and operations,
field types/units/constraints/reference categories, prerequisites (`Requires = "world.region"`), allowed scope,
runtime applicability, validators and their diagnostic codes. The catalog is the agents' only view of what the
project can do.

## 5. Tools (owner: studio.core Edit; registry populated by plugins)

Three tiers, visible in every tool entry:
1. **Configure**: set/clear fields, assign references and assets on existing objects (`set`, `assign`, `bind`).
2. **Compose**: create, duplicate, delete, replace, reparent, place, layout, add/remove components or definitions,
   add dialogue nodes, quest stages, rules, UI elements (`create`, `duplicate`, `delete`, `replace`, `move`,
   `place`, `layoutRing`, `layoutLine`, `addNode`, …).
3. **Mechanism**: `mechanism.propose` produces a staged package (never applied directly); only the staging lane
   can turn it into an `admit` operation.

Built-in tools (always present): `inspect.describe`, `inspect.explain` (why a rule/condition did or did not fire,
from traces), `query.references`, `query.impact`, `preview.stage`, `preview.compare`, `history.undo`,
`history.redo`, `project.save`, `project.reload`, `project.build`, `project.launch`, `asset.import` (verified),
`asset.generate` (opens an etos task through D, returns an artifact ref).

`assign` with `append: true` uses reference-set semantics: an already-listed Unity object is a recorded Applied no-op (`alreadyListed: true`, existing `index`) with no inverse mutation; a distinct reference appends and `index` still replaces.

## 6. Change set (owner: studio.core Edit)

| JSON member | Presence |
|---|---|
| `artifacts` | optional |
| `baseVersions` | optional |
| `id` | required |
| `intent` | required |
| `links` | optional |
| `operations` | required |
| `outcomes` | optional |
| `policy` | optional |
| `requirements` | optional |
| `schema` | required |
| `selection` | optional |
| `state` | optional |
| `timestamps` | optional |
| `validation` | optional |

Exact nested types/constraints: [change-set.schema.json](schemas/change-set.schema.json).

Engine pipeline per change set: `Resolve → Precheck (stamps, existence, residency, scope) → Stage (temp objects,
dry-run validators, preview) → Validate (tool validators + scenarios) → Apply (single-writer queue, one
AssetDatabase edit block, one Undo group, GameCore ops when in Play) → Journal → Notify`. Partial failure produces
per-op outcomes; `AllOrNothing` rolls back via the Undo group and inverse ops.

Journal location: `<project>/Studio/History/YYYY/MM/<id>.json` (tracked) and `Studio/Artifacts/sha256/<aa>/<hash>`
(tracked via LFS-free small assets, or ignored with a manifest when > 8 MiB; the manifest is tracked).

## 7. Concurrency and conflicts

- One apply queue per project; applies are serialized and short. Long tasks hold no lock.
- Every op carries the stamp it was planned against; conflicts are per-op (`Conflict{expected, actual}`), and the
  UI offers *rebase* (re-plan against the current stamp through the same tool) or *skip*.
- Agent candidates arriving after the user edited the same object are shown with a conflict badge before Apply.
- In Play, world edits use SADR-011's explicit expected revision; a `StalePlan` refusal maps to `Conflict`.

## 8. Staging and admission of code (owner: `studio/stage` + studio.core)

`mechanism.propose` carries exact package/proposal artifacts. The trusted companion creates a minimal Docker slot, runs scan, checkers, dotnet with mandatory Roslyn analysis, Unity EditMode, PlayMode smoke, determinism and budget. Warm budget is 360 s with recorded once-only cold grace. Unavailable confinement issues no verdict. The semantic scan refuses `InitializeOnLoad`/`InitializeOnLoadMethod`, `AssetPostprocessor`, `AssetModificationProcessor`, `[MenuItem]` side-effect entry points, `DidReloadScripts`, reflection emit, `Process`, networking and file IO outside the package's own `Assets/<pkg>` or `Application.persistentDataPath`. Candidate Editor extensions are restricted to attribute-declared `[AuthorOperation]`/`[AuthorValidator]` methods and catalog contributors; string prefixes and candidate `allowUnsafe` reasons do not grant authority. ([Stage lane §Lane contract and Semantic analyzer](../../studio/stage/README.md), [R2-B](packets/R2-B-admission.md))

The companion signs job/app/project/source/catalog/package/proposal/steps and confinement. Unity obtains the signed record from authenticated `GET /v1/stage/{job}/verdict` and asks `POST /v1/stage/{job}/verify`; `VerdictCheck` enforces every mandatory successful step. A file import or CAS digest alone is not authority. `mechanism.admit` and `mechanism.remove` are internal, absent from worker discovery. Only explicit creator Admit starts durable capture/stop/copy/compile/reload/restore/smoke. Failed or pending transitions keep recovery evidence; admission history uses its registered handler. ([R2-B §Contracts and R5](packets/R2-B-admission.md), [04 §6](04-etos-integration.md#6-staging-code-admission))

**Source-world freshness:** a tracked baked description alone is not staging authority. Explicit Studio Stage saves dirty authored inputs through the existing `project.save` tool, then exports the same non-mutating production bake computation used by admission verification; it does not rewrite generated code, manifests or definition stamps. The trusted Editor export binds the description to the source revision and complete imported-source inventory, which the companion independently rehashes before copying the immutable snapshot and signing its world fingerprint. Candidate artifacts cannot supply or replace this export. A clean Edit-to-Play transition may prepare the same read-only export without saving; Stage in Play only consumes a still-current saved export and never saves runtime mutations. Missing, changed or incoherent source/generated-runtime state refuses early with `bake_stale`, naming the source or outputs requiring regeneration. Normal authored edit → Apply → journal Undo → Stage recomputes the restored state without a manual rebake. Admission still recomputes the current authored world, verifies the generated mechanism catalog and requires equality with the authenticated signed prediction; no fingerprint substitution or budget relaxation is permitted.

As of P4.2d, isolated stages pass but real live admission rolls back with `catalog_mismatch` after a compile stall. No successful live restoration or 90 s acceptance is claimed. ([P4.2d §Stage and admission](packets/P4.2d-live-rerun.md#stage-and-admission))

## 9. Diagnostics

Every refusal and validation failure is `{code, message, hint?, where?: AuthoringRef|opId, data?: object}` with the
same code whether raised by an inspector, a validator, the kernel bridge or an agent candidate. `where` is absent
for change-set-wide findings (envelope, artifacts, requirements). `data` carries a structured witness when one
exists: `Conflict` always has `data: {expected, actual}` (stamps or revisions); `StaleTarget` means the target no
longer exists or is unloaded, `Conflict` means it exists but changed since it was read.

**Null policy (all shapes):** optional members are omitted when absent and are never written as `null`; readers
refuse `null`. **Candidate mode:** a change set arriving from a worker may carry only `state: "Candidate"` (or no
state), no `outcomes`, no `timestamps.applied`, no `links.gameCoreOps`; the companion and the engine refuse
anything else. **Catalog revision:** `ToolCatalog.revision` is the sha256 of the catalog's canonical JSON without
the `revision` member, minted by the tool registry; requests carry it as `toolCatalogRevision` and a candidate
built against another revision is `StaleContext`. Codes are registered in
`GameCore.Studio.Model.DiagnosticCodes` and listed in [09-plugin-developer-guide.md](09-plugin-developer-guide.md).

## 10. Exact schema and execution amendments

The ETOS baseline is **etos main ≥ e4067fd (contains 278ef9c)**, merged and pushed 2026-10-06 as supplied by the owner; retained binaries/vendor hashes still identify the original build. ([P4.3-final §Baseline](packets/P4.3-final-docs.md#baseline))

| Shape | Generated contract |
|---|---|
| authoring-ref | [schema](schemas/authoring-ref.schema.json): `assetGuid`, `authoringId`, `definition`, `global`, `kind`, `location`, `path`, `scope`, `stamp` |
| selection-snapshot | [schema](schemas/selection-snapshot.schema.json): `frame`, `id`, `indexRevision`, `mode`, `parts`, `regionRect`, `targets`, `worldSession` |
| semantic-index | [schema](schemas/semantic-index.schema.json): `edges`, `nodes`, `project`, `revision`, `scopes` |
| tool-catalog | [schema](schemas/tool-catalog.schema.json): `objectTypes`, `plugin`, `revision`, `schema`, `tools` |
| change-set | [schema](schemas/change-set.schema.json): `artifacts`, `baseVersions`, `id`, `intent`, `links`, `operations`, `outcomes`, `policy`, `requirements`, `schema`, `selection`, `state`, `timestamps`, `validation` |
| diagnostic | [schema](schemas/diagnostic.schema.json): `code`, `data`, `hint`, `message`, `where` |

Member tables above come from generated schemas; nested fields and required/type constraints remain defined there. Never hand-edit the schemas. Run `python3 tools/studio/emit_studio_schemas.py --check` on Linux. ([P0.3 §Built](packets/P0.3-studio-model.md#built))

Apply rechecks every base-version dependency and catalog revision. A missing scope is inferred only from a singleton intersection and the inference is retained visibly. Prepared inverses precede side effects; failed recovery stays Interrupted. RuntimeOnly Live actions do not mutate authored data, cannot undo, and cannot claim multi-op AllOrNothing without an atomic world gateway. ([R2-A §R2 fixes and R3](packets/R2-A-core-edit-recovery.md))

`asset.import` and `bind` accept delivered artifact handles only. The built-in importer accepts PNG/JPEG, WAV/OGG/MP3, JSON/TXT/CSV and FBX; WebP/GLB/glTF currently refuse. Settings use enum literals such as `textureType: Sprite`, `spriteImportMode: Single`, with finite `spritePixelsPerUnit` in `(0,16384]`. Raw prefab/controller/material bytes, executable inputs, Editor/Plugins paths and custom importer hooks refuse with `MediaTypeForbidden`, `MediaPathForbidden` or `MediaImporterInvalid`; host source paths refuse `ArtifactSourceForbidden`. ([R2-A §R2 fixes and R3](packets/R2-A-core-edit-recovery.md), [P4.2c §Guide outcome](packets/P4.2c-live-rows.md#guide-outcome))
