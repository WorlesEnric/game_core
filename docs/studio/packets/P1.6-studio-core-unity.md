# PACKET P1.6 studio-core-unity

Owner: Opus 5.5. Contract: [docs/studio/03-authoring-contracts.md](docs/studio/03-authoring-contracts.md) (all
sections, Unity side), 02 s1, s2 B/C, s5 to s8, SADR-007/008/009/011; packets P0.3 (model), P0.4 s2 (kernel app
bridge), P0.2 (host tooling). Branch `worktree-agent-a004eae06ca97de37`, merged with `main` at `444e266` (P0.3
review fixes and P1.1).

## Built

| Path | What |
|---|---|
| `Packages/com.gamecore.studio.core/Runtime/Authoring/**` | asmdef `GameCore.Studio.Authoring` (**Editor-only**, see decisions): authoring identity (duck-typed), `IAuthoringSource`, `IAuthoringRefResolver` + `ResolveResult`, stale reports, residency (`IResidencyQuery`, default all resident) and region locator, `IStudioLog` with secret redaction, service contracts (`IAgentGateway`, `IExplainSource`, `IBuildLane`), picking (`IPickingService`, `PickingService`) |
| `Packages/com.gamecore.studio.core/Editor/Core/**` | `StudioPaths`, `StudioDiagnostics`, `ValueCodec`, `AuthoringRefResolver`, `AuthorableTypeRegistry`, `DefaultAuthoringSource` |
| `Editor/Index/SemanticIndexService.cs` | incremental semantic index, cache, slices, references, impact |
| `Editor/Tools/**` | tool contracts (`IStudioTool`, `EditContext`, `OperationResult`, `IReplannableTool`, `IDirectTool`, `ILiveOpTranslator`), `ToolRegistry`, reflected `[AuthorOperation]` tools, `FieldValueChecker`, `ToolSupport`, the 29 built-ins (`BuiltIn/*`) |
| `Editor/Engine/**` | `ChangeSetEngine`, `ApplyQueue`, `PreviewStaging`, `LiveWorld` (`ILiveWorldGateway`, `GameApplicationLiveGateway`), engine types (`StageOptions`, `StagedChangeSet`, `ApplyReport`, `UndoPayload`, fault injection) |
| `Editor/Journal/**` | `Journal`, `ArtifactStore`, `HistoryService` (undo/redo/recovery) |
| `Editor/Services/**` | `StudioRuntime` (+ options), `StudioServiceRegistry`, `StudioServices` (ScriptableSingleton), `StudioIndexTriggers` + asset postprocessor |
| `Editor/Inspector/**` | `AuthoringInspectorBuilder` (UI Toolkit), `ManualEditCommitter`, `MoveChangeSets`, `GizmoMoveController`, fallback editors, `StudioMoveTool` (EditorTool) |
| `Tests/Fixtures/**` | asmdef `GameCore.Studio.Core.Tests.Fixtures` (runtime, no references, `UNITY_INCLUDE_TESTS`): a mirror attribute set like gameplay.contracts', fixture item/NPC definitions, entity, duck-typed entity, region, mood enum, fixture tools `fixture.setGreeting` and `fixture.fail` |
| `Tests/Editor/**` | asmdef `GameCore.Studio.Core.Editor.Tests`: EditMode tests (list below) |
| `package.json` | deps now `com.gamecore.composition`, `com.gamecore.contracts`, `com.gamecore.unity.app`, `com.gamecore.unity.runtime`, `com.unity.nuget.newtonsoft-json` (exact set of the asmdef references) |
| `games/hollowmere/Packages/manifest.json` + lock | `"testables": ["com.gamecore.studio.core"]`; lock dependency map of studio.core updated |
| `games/hollowmere/Assets/Hollowmere/Tests/P1_6/EditMode/**` | asmdef `GameCore.Studio.Hollowmere.Tests` (refs only Studio assemblies): `HollowmereDiscoveryTests` |
| `unity/GameCore.Validation/Packages/packages-lock.json` | outside the exclusive paths, mechanical: the studio.core dependency map follows the new `package.json` (the metadata checker failed otherwise after the merge) |

Runtime/Model was not touched (no missing shape).

## Verified (host myubuntu, Unity 6000.0.75f1, one instance)

`studio/tools/unity-compile.sh p1.6 games/hollowmere --tests EditMode --filter 'GameCore\.Studio\..*'` at the
P1.6 head (merged with `main` `444e266`, P1.1 included): compile clean, **37/37 passed**, 0 failed/skipped; test run
7.2 s (Unity process 56 s with a warm Library; the first run on a fresh copy took 981 s on the loaded host).

| Fixture | Tests | Duration |
|---|---|---|
| `GameCore.Studio.Edit.Tests.AuthoringRefResolverTests` | 4 | 0.78 s |
| `ChangeSetEngineTests` (W-EDIT-02 AllOrNothing/BestEffort, rollback, rebase, journal path, queue) | 6 | 1.62 s |
| `HistoryTests` (undo/redo with retained artifacts, undo conflict, reload, crash rollback/resume) | 6 | 1.05 s |
| `LiveEditTests` (OperationId, StalePlan as Conflict) | 2 | 0.32 s |
| `ManualEditTests` (W-EDIT-05, inspector rows, inline validation) | 4 | 0.65 s |
| `PickingServiceTests` (depth/occlusion/ground, overlap groups, UI first, marquee, point-at, validate) | 6 | 1.27 s |
| `SemanticIndexTests` | 4 | 1.15 s |
| `ToolRegistryTests` | 3 | 0.29 s |
| `GameCore.Studio.Hollowmere.Tests.HollowmereDiscoveryTests` (P1.1 `entity.place` in the catalog; every minted Hollowmere `EntityDefinition` is an `entity.definition` node) | 2 | 0.04 s |

Timing logs (`[P1.6] ...`, single samples, not benchmarks): index rebuild 6 objects 2.9 ms, incremental flush
0.2 ms, slice 0.4 ms; Hollowmere index rebuild (17 nodes) 19 ms; catalog build with TypeCache (38 tools) 10 ms;
W-EDIT-02 apply 5 ops 2.9 ms (AllOrNothing) / 7.4 ms (BestEffort); journal undo 16.1 ms, redo 16.6 ms; pick 1.8 ms,
overlap pick 3.1 ms, UI pick 4.6 ms, marquee 3.1 ms, point-at 0.5 ms.

Static: `tools/check_game_core_csharp.py` ok; `tools/check_package_metadata.py` agrees.

## API (namespace `GameCore.Studio.Edit` unless noted)

- `StudioRuntime.Create(StudioRuntimeOptions?)`: `Paths, Log, Identity, Types, Source, Resolver, Index, Artifacts,
  Journal, Services, Staging, Live, Registry, Engine, Queue, History`; `Single(intent, origin, op)`;
  `InvalidateCode()`. The project's runtime is `StudioServices.Runtime`.
- `AuthoringRefResolver` (`IAuthoringRefResolver`): `BuildRef(obj, scope?, includeStamp)`, `Resolve(ref)` returns
  `ResolveResult{Object, Stale, CurrentStamp, Report}`, `TryResolve`, `Find`, `ComputeStamp`, `Residency`
  (`IResidencyQuery`).
- `SemanticIndexService`: `Rebuild, Flush, MarkAssetsChanged/MarkSceneChanged/MarkSceneClosed/
  MarkPrefabStageChanged/MarkObjectChanged, Revision, Changed, Snapshot, FindNode, ReferencesTo, ImpactOf,
  Slice(selection, depth, byteCap, includeTypes) -> IndexSlice{Index, Truncated, Bytes, OmittedNodes}, Export,
  SaveCache, LoadCache, Contributors (IIndexContributor)`.
- `ToolRegistry`: `Catalog` (revision minted), `BuildCatalog()`, `Export(path?)` (default
  `Library/GameCoreStudio/tool-catalog.json`), `Register(tool)`, `Find(id)`, `Invoke(op, EditContext)`,
  `Invoke(toolId, target, args)` (read-only and direct tools only), `Problems`.
- `ChangeSetEngine`: `Stage(cs, StageOptions{Mode, ToolCatalogRevision, IndexIsSlice, Previews})`,
  `Apply(staged|cs)`, `ApplyAsync(cs)`, `Rebase(staged, opIds?)`, `Skip(staged, opIds)`, `Discard(staged, reject)`,
  `Previews`, `Applied` event, `RunOutsideAssetEditing(action)`, `IsPlayMode`, `EngineOptions{FaultHook,
  PlayModeProbe, Replan}`.
- `HistoryService`: `Undo(id?, force)`, `Redo(id?)`, `NextUndo`, `NextRedo`, `RedoStack`, `Interrupted()`,
  `RollbackInterrupted(id)`, `ResumeInterrupted(id)`.
- `Journal`: `PathOf(id)`, `TimeOf(id)`, `Write`, `Read`, `List(state?)`, `Rescan`. `ArtifactStore`:
  `Put(bytes, ArtifactRef?)`, `Read(digest)` (verified), `Has`, `Missing(cs)`, `Describe`, `Entries`.
- `StudioServiceRegistry`: `AgentGateway`, `BuildLane`, `RegisterExplainSource`, `RegisterLiveTranslator`.
- `GameCore.Studio.Authoring.IPickingService` / `PickingService(camera, viewportRect, resolver, identity?,
  regions?, options?)`: `Pick(point)`, `Marquee(rect, full)`, `PointAt(point)`, `Validate(snapshot)`, timings on
  every result. Points are viewport GUI coordinates (top-left origin).
- Manual edits: `ManualEditCommitter` (`IManualEditSink`), `MoveChangeSets.Build(runtime, go, pos, rot?, scale?)`,
  `GizmoMoveController(runtime)` (`Begin/DragTo/End/Cancel`), `AuthoringInspectorBuilder(identity, codec, sink)`.

## Decisions

- **Authoring asmdef is Editor-only.** The Model asmdef became Editor-only (P0.3 review), and identity, refs, stale
  reports, picking results and the service contracts all carry Model types. Splitting a Model-free player subset
  would duplicate the contract, so `GameCore.Studio.Authoring` is `includePlatforms: ["Editor"]`. Runtime-side
  picking for a shipped game is out of scope for Studio (the game uses its own input).
- Duck-typed identity (coordinator note): an interface named `IAuthoredObject` from any assembly, else a public
  `AuthoringId` property, else a serialized `authoringId` field. `IResidencyQuery` defaults to "all resident";
  `NamedResidencyQuery.TryWrap(streamer)` binds P1.1's `RegionStreamer` by interface name (`ResidencyOf(string)` by
  reflection, enum by member name; `RegionOf` maps a target to its region id, default: region refs and locations).
  No dependency on the P1.1 packages.
- **Mirror attributes by name.** `AuthoringMetadata` (Authoring) is the single reader: it returns the Model
  attribute, else converts an attribute of the same type name from any other assembly (gameplay.contracts) into a
  Model attribute instance, reading same-named properties and converting enums by member name.
  `AuthoringIdentity` (index, inspector), `ReflectedTool`, `AuthorableTypeRegistry` and the tool catalog
  pass all go through it; `AuthoringTypeCache` finds mirror-attributed types and methods with TypeCache. Mirror tool
  entries are built by `AuthoringMetadata.BuildToolEntry` with `ToolCatalogBuilder`'s rules (Model-attributed
  methods still use `ToolCatalogBuilder` itself).
- Engine-built change sets (manual edits, tools; any mode but `Candidate`) get `requirements` derived from their
  tools when they declare none; agent candidates must declare their own (03 s6).
- Stamp changes are `Conflict` with `data {expected, actual}`; `StaleTarget` only for destroyed or unloaded targets.
  A moved target (renamed, reparented) is reported but does not block.
- The engine validates with the model's `ChangeSetValidator` (stamp checks off) and does live stamp/existence checks
  itself with the resolver, so a lagging index never causes a false conflict. A target that does not resolve yet
  but whose op `dependsOn` others is resolved at apply time (it may be created by a dependency).
- Candidates (`StageOptions.Mode = Candidate`) must state `ToolCatalogRevision`; a missing or different revision is
  `StaleContext` with `data {expected, actual}`. A valid candidate is journaled as `Candidate` (02 s5).
- Apply: journal `Interrupted` first, then one Undo group and one `StartAssetEditing` block, a journal checkpoint
  after each op, then commit or rollback. AllOrNothing: a refused op before any write gives `Rejected` (others
  `Skipped`); a failure during apply rolls back with `Undo.RevertAllDownToGroup` and then the asset-level inverses
  in reverse order, state `Failed`, the applied ops reported `Skipped` ("Rolled back"). BestEffort records partial
  outcomes; state `Applied` when anything applied. A tool exception is `Failed` with code `Refused`.
- `undo.inverse` shape: `{operations: [Operation], assetLevel?: true, after?: [{ref, stamp}], replay?: {...}}`.
  `after` drives the undo conflict check (an object changed since is `Conflict`, unless forced); `replay` lets redo
  recreate the same identity (minted authoring ids, chosen asset paths).
- Undo marks the entry `Undone` (no separate undo entry); redo re-runs the original operations with replay hints
  and retained artifacts (preconditions none) and marks it `Applied` with fresh outcomes. Requests
  (`asset.generate`, `mechanism.propose`, `project.build/launch`) are never repeated by redo. The redo stack is a
  file (`Library/GameCoreStudio/redo.json`), cleared by every new apply.
- Recovery: `RollbackInterrupted` runs the checkpointed inverses (best effort; a crash already lost unsaved scene
  edits) and marks the entry `Failed`; `ResumeInterrupted` applies the ops without an outcome and finalizes.
- Journal path `Studio/History/YYYY/MM/<id>.json` with YYYY/MM from the ULID time of the id (stable across state
  changes). A history index cache lives in `Library/GameCoreStudio/history-index.json`.
- Artifacts: `Studio/Artifacts/sha256/aa/<hash>` + `manifest.json`; every write and read is re-hashed; files over
  8 MiB are listed in `Studio/Artifacts/.gitignore` (the manifest stays tracked).
- Direct tools (`IDirectTool`): `history.*`, `preview.*`, `project.*` run through `ToolRegistry.Invoke`, never in a
  change set (undo operates on the journal itself). Read-only tools may also run inside a change set.
- `set` takes `{field, value}` or `{fields}`; the catalog types `value` as `fieldValue` and the engine checks it
  against the field's spec with the model validator's own rules (`FieldValueChecker`).
- Scene-object delete/replace keep a prefab blob of the hierarchy as an artifact; the inverse restores it (the
  authoring id is kept; references from other objects come back through Unity Undo within the session and by
  authoring id otherwise).
- Live (Play) ops: a Live tool with a registered `ILiveOpTranslator` is submitted through
  `GameApplication.Current.Submit(payload, expectedRevision)`; the expected revision starts at the revision seen at
  stage time and follows each executed edit. `StalePlan` becomes `Conflict` (`revision:<n>` witnesses). The
  OperationId is recorded as `w:<session>/i:<issuer>/s:<seq>` in the outcome and in `links.gameCoreOps`. After an
  executed world edit the tool also applies the authored change.
- Picking order: UI Toolkit hits first (by sorting order), then world hits by distance (physics, plus renderer
  bounds for objects without a solid collider) with occlusion and overlap groups, then a ground candidate.
- The index node type is the `[Authorable]` type id; capabilities come from a public `Capabilities` property;
  component stamps include placement.
- Manual edits use the agent tools: inspector commits are `set`/`assign` change sets (origin manual) with inline
  validation; a gizmo drag moves the object live without Undo and on mouse-up restores the start pose and applies one
  `move` change set, identical to typing the final position (W-EDIT-05).
- Tests use explicit type and tool sources (the fixtures), a temporary asset folder `Assets/GameCoreStudioTests/*`,
  a saved temporary scene and a temporary state root; nothing is written to the project's `Studio/` folder.

## Left open

- The package tests build their fixtures programmatically in a temporary folder (no committed YAML fixtures);
  `Tests/P1_6` holds only the Hollowmere discovery tests, which read the shipped assets.
- `BuildToolEntry` duplicates `ToolCatalogBuilder`'s method rules for mirror methods; if P0.3's builder learns to
  read by name, the copy can go.
- Duplicated prefab instances share an authoring id until a Studio duplicate mints new ones (Unity's own
  Ctrl+D copies the serialized id). A validator that reports duplicate ids is a follow-up.
- The fixture tools are discovered by `TypeCache` in hollowmere because studio.core is a testable there; the
  exported project catalog therefore lists `fixture.*` while tests are included.
- No Studio UI (P2.x), etos client, graph views or staging lane.
