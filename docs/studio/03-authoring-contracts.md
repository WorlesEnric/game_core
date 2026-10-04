# GameCore Studio: authoring, selection, edit and tool contracts

**Status:** contract for implementation (2026-10-04). These shapes are shared by the Studio packages, the companion
agent and the workers; one owner per shape (named in each section). Changing a shape means changing it here first.
JSON is the interchange form; C# shapes live in `com.gamecore.studio.core/Runtime/Model` (Unity-free,
`GameCore.Studio.Model.asmdef`, netstandard2.1, C# 9, `#nullable enable`) and are mirrored by `serde` types in
`studio/agent/src/model.rs`. The JSON Schemas are generated from the C# shapes by
`tools/studio/emit_studio_schemas.py` into `docs/studio/schemas/*.schema.json` and the companion validates against them.

## 1. Authoring identity (owner: `com.gamecore.gameplay.entities`, shape in studio.core)

```jsonc
// AuthoringRef — a pointer to an authored thing, stable across sessions
{
  "kind": "Entity | Definition | Region | SceneObject | Asset | UiElement | Scope | Location",
  "authoringId": "7f1c…",             // GUID stored on the object/asset; absent for Location and Asset
  "global": "GlobalObjectId_V1-2-…",   // Unity GlobalObjectId (asset GUID + local id + prefab instance id); absent for Location
  "assetGuid": "…", "path": "Assets/Hollowmere/Regions/Marsh.unity#/NPCs/Ferryman",
  "definition": "npc.ferryman@3",      // DefinitionRef name@revision when the thing has a definition
  "scope": "Instance | Prefab | Definition | Scope",   // what the user chose to edit
  "stamp": "sha256:…",                 // content stamp at selection time (asset hash or scene-object serialized hash)
  "location": { "region": "marsh", "position": [12.5, 0.0, -3.25], "normal": [0,1,0] } // kind=Location only
}
```

Rules:
- `authoringId` is minted once by `AuthoredEntity`/`AuthoredDefinition` importers and never regenerated on copy
  unless the copy is a *new* object (prefab instances keep the prefab's id plus an instance id).
- `TargetId = StableNameKeyDerivation.Derive("auth." + authoringId)` (the kernel helper refuses `:`); prefab-variant instances derive from the instance id.
- `stamp` is the precondition value; the edit engine refuses an op whose target stamp changed (`StaleTarget`) unless
  the op declares `preconditions: "none"`.
- Unity instance IDs, `Entity` indices and `TargetHandle`s never appear in a change set.

## 2. Selection (owner: `com.gamecore.studio.core` Picking + `com.gamecore.studio.ui`)

```jsonc
// SelectionSnapshot — captured when a prompt is sent
{
  "id": "sel_…",
  "mode": "Edit | Play",
  "targets": [ AuthoringRef, … ],       // logical objects (subparts resolved to their logical owner unless the user picked "this part")
  "parts":   [ { "owner": AuthoringRef, "part": "Mesh:Lantern_Glass" } ],
  "regionRect": { "screen": [x0,y0,x1,y1] },           // for box selections
  "frame": { "camera": { "position": [...], "rotation": [...], "fov": 60, "aspect": 1.78 },
             "viewport": [w,h], "image": "sha256:…" },  // image stored in Studio/Artifacts, never inline
  "worldSession": "WorldId | null",     // Play only
  "indexRevision": 1234                 // semantic index revision the snapshot was taken against
}
```

Picking contract (`IPickingService`, Unity side):
- `Pick(screenPoint) → Candidate[]` ordered by depth: physics hits (colliders), renderer-bounds hits (no collider),
  UI Toolkit `panel.Pick`, then ground plane. Each candidate: `AuthoringRef`, `distance`, `occluded: bool`,
  `part`. Overlapping candidates (within 0.5 % of depth or occluded chains) are shown as a list; the first is default.
- `Marquee(rect) → Candidate[]` by frustum test on renderer bounds, optionally requiring full containment.
- `PointAt(screenPoint) → Location` samples the ground (NavMesh if present) and the owning region.
- Stale check: `Validate(SelectionSnapshot) → StaleReport` recomputes stamps, detects destroyed objects, unloaded
  regions (`RegionResidency.Unloaded`) and changed assets before any apply.

## 3. Semantic index (owner: `com.gamecore.studio.core` Authoring)

The index is a projection, rebuilt incrementally from `AssetPostprocessor`/scene events, serialized to
`Library/GameCoreStudio/index.json` (cache) and exported in slices.

```jsonc
{ "revision": 1234, "project": "hollowmere",
  "nodes": [ { "ref": AuthoringRef, "type": "npc.definition", "name": "Ferryman",   // the [Authorable] type id
               "fields": { "speed": { "value": 1.8, "unit": "m/s", "range": [0.5, 6], "type": "float" } },
               "refs": [ { "field": "dialogue", "to": AuthoringRef } ],
               "capabilities": ["dialogue.speaker", "quest.giver"],
               "provenance": { "asset": "Assets/…/Ferryman.asset", "line": 0 } } ],
  "edges": [ { "from": AuthoringRef, "to": AuthoringRef, "kind": "references | contains | spawns | bindsUi | triggers" } ],
  "scopes": [ { "scope": "world/marsh", "region": AuthoringRef, "installs": ["npc.behaviour@1", …] } ] }
```

Slices for agents are bounded: the selection closure (depth 2), plus definitions by type on request, plus the tool
catalog; the companion enforces a 2 MiB cap and reports truncation explicitly.

## 4. Authoring metadata (owner: studio.core; used by every plugin)

```csharp
[Authorable("npc.definition", DisplayName = "NPC", Scope = AuthorScope.Definition | AuthorScope.Instance,
            RuntimeApplicability = RuntimeApply.Live)]            // Live | Rebuild | Compile
public sealed class NpcDefinition : AuthoredDefinition {
  [AuthorField(Unit = "m/s", Min = 0.5f, Max = 6f, Doc = "Walking speed")] public float speed = 1.8f;
  [AuthorRef(Category = "dialogue.graph", Required = false)]   public DialogueGraph dialogue;
  [AuthorRef(Category = "prefab.character")]                   public GameObject prefab;
}
[AuthorOperation("npc.setPatrol", Doc = "Replace the patrol route", Validator = typeof(PatrolValidator))]
public static OperationResult SetPatrol(EditContext ctx, NpcDefinition npc, [AuthorArg] Vector3[] points) { … }
```

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

## 6. Change set (owner: studio.core Edit)

```jsonc
{
  "id": "cs_01J…", "schema": "gamecore.studio.changeset/1",
  "intent": { "text": "Give the ferryman a lantern and make him mention it", "voiceTranscriptId": "tr_…|null",
              "origin": "agent | manual | voice | replay" },
  "selection": SelectionSnapshot,
  "baseVersions": [ { "ref": AuthoringRef, "stamp": "sha256:…" } ],   // read dependencies
  "operations": [
    { "opId": "op1", "tool": "inventory.grantStarting", "target": AuthoringRef,
      "args": { "item": "item.lantern@2", "count": 1 }, "dependsOn": [], "preconditions": "stamp",
      "applyRequirement": "Live | Rebuild | Compile | Build" },
    { "opId": "op2", "tool": "dialogue.addNode", "target": AuthoringRef, "args": { … "voice": { "artifact": "sha256:…" } },
      "dependsOn": ["op1"] }
  ],
  "artifacts": [ { "sha256": "…", "name": "ferryman_line_07.wav", "mediaType": "audio/wav", "bytes": 48213,
                   "producer": { "etosTask": "t_…", "op": "tts", "provider": "dashscope", "model": "qwen3-tts-flash" },
                   "role": "voiceLine", "import": { "type": "AudioClip", "settings": {} } } ],
  "validation": [ { "scenario": "dialogue.reachable", "status": "pending | pass | fail", "detail": "" } ],
  "requirements": { "max": "Live", "worldRebuild": false, "compile": false, "build": false },
  "links": { "etosTasks": ["t_…"], "parent": "cs_…|null", "gameCoreOps": [] },   // filled as they happen
  "state": "Requested | Running | Candidate | Staged | Applied | Rejected | Failed | Undone | Interrupted",
  "outcomes": [ { "opId": "op1", "status": "Applied | Skipped | Refused | Failed", "code": "", "detail": "",
                  "gameCoreOps": ["w:…/i:…/s:42"], "undo": { "inverse": { … } } } ],
  "policy": "AllOrNothing | BestEffort",
  "timestamps": { "requested": "…", "candidate": "…", "applied": "…" }
}
```

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

- `mechanism.propose` → worker output `package/` (UPM package with `Rules/` noEngineReferences asmdef, `Runtime/`
  asmdef, `Editor/` optional, `Tests/`), plus `proposal.json` (what it adds to the tool catalog).
- Companion `stage(jobId)`: export to slot, `dotnet test` on Rules, batchmode Unity compile + EditMode tests, a
  10-minute watchdog, a verdict `{ok, compile, tests, forbidden: [...]}`. Forbidden: Editor code in `Runtime/`,
  native plugins, `allowUnsafeCode`, reflection emit, network, file IO outside `Application.persistentDataPath`.
- `admit(changeSetId)`: checkpoint the live world (if Play), copy the package, one `AssetDatabase.Refresh`, reload,
  re-enter Play and restore (SADR-012), run the proposal's smoke test, record `Admitted` or revert.

## 9. Diagnostics

Every refusal and validation failure is `{code, message, hint, where: AuthoringRef|opId}` with the same code
whether raised by an inspector, a validator, the kernel bridge or an agent candidate. Codes are registered in
`GameCore.Studio.Model.DiagnosticCodes` and listed in [09-plugin-developer-guide.md](09-plugin-developer-guide.md).
