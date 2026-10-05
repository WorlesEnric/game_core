# R2-G gameplay Editor adapters

Branch `codex/r2-g`, Linux build host. First command after inspecting the branch was
`git merge origin/main`: already up to date (`de2d9593`). R2-A's root note was moved to
`docs/studio/packets/R2-A-core-edit-recovery.md`; that is the handoff read here.

## Implemented

- R2-41: additive Unity-free `IMediaGenerationGatewayProvider` and
  `MediaGenerationLookup.Resolve(IEnumerable<object>)` in gameplay contracts. The
  dialogue/audio tools resolve existing Editor session registrations. No provider
  constructor discovery, cached gateway, or static mutable state. Removed the
  impossible authored-index prerequisite `agent.media`; service availability is
  determined by the registered gateway. Two distinct
  registrations fail closed; null unregisters. Optional
  `ISoundEffectGenerationGateway.RequestSoundEffect(string clipId, string description, int durationMs)`
  supports audio.generateSfx without breaking voice-only providers.
- R2-06 / D4: `WorldLiveOpTranslator` recognizes `RuntimeOnly && RuntimeApply.Live`
  catalog entries, including mechanism routes. Requires explicit `expectedRevision`,
  refuses stale revisions/unavailable worlds/unconfigured bridge. Standard runtime
  tool declarations are registered for world.travel, dialogue.start, entity.spawn,
  entity.despawn and npc.goTo. The existing engine owns non-undoable journaling and
  atomic-batch refusal. First-party `[InitializeOnLoad]` in gameplay.world Editor
  re-registers idempotently after runtime creation/reload; this is not candidate code.
- Admission: exact `StudioAdmissionServices.BindAdmission(StudioRuntime,
  Func<SaveService?>, Func<bool>, Func<StageVerdict,bool>)` binding, with dynamic
  service/readiness delegates and companion-verified instance identity before smoke.
  `RunSmokeTest` reads the digest-bound retained proposal and dispatches its smoke
  descriptor only through a trusted game callback. It never reflects a candidate
  type or boots a second global root itself.
- R2-34: P1.7b already supplies AuthorArgs on all secondary parameters and region
  selection for AddPortalEnd. No residual signature change is necessary here.

## Requests to other packets

1. **R2-A / kernel owner**, `Packages/com.gamecore.studio.core/Editor/Engine/LiveWorld.cs`,
   `Editor/Tools/ToolContracts.cs`, `Packages/com.gamecore.composition/Runtime/Operations/CompositionEditPayload.cs`:
   the existing `ILiveOpTranslator.Translate(EditContext,out Diagnostic?)` returns a
   sealed composition-only payload (scope/install/mode; subjects 0..11), whereas
   GameplayCommands.Travel/Spawn/Despawn and narrative/NPC/mechanism commands take
   `RouteId, TargetId, SchemaRef, FrozenPayload`. There is no command subject or
   validated command publication gateway. Publish a revision-checked live action
   union/gateway (including command results after validation, not merely queue
   admission), or extend the composition codec and gateway to carry that command.
   R2-G's seam is `IWorldLiveActionBridge.Translate(EditContext context,
   ulong expectedRevision, out Diagnostic? problem) -> CompositionEditPayload?`.
   It must validate catalog route, target, schema and arguments without side effects;
   no command submission during Stage/Translate. Catalog-declared mechanism methods
   must include a required integer `expectedRevision` AuthorArg and
   `RuntimeOnly=true, RuntimeApplicability=RuntimeApply.Live`. The bridge is intentionally absent
   until the kernel can represent it. Do not disguise commands as plugin reconfigure.
2. **Metadata/integration owner**, `Packages/com.gamecore.gameplay.world/package.json`:
   add exact dependencies `"com.gamecore.studio.core": "1.0.0"` and
   `"com.unity.nuget.newtonsoft-json": "3.2.1"` (confirm the project pin), and synchronize
   that package's dependency map in `games/hollowmere/Packages/packages-lock.json`
   (and any other locking project). The typed Studio adapter's asmdef now references
   these assemblies. These manifests/locks are outside this packet's exclusive paths.
3. **R2-D**, `Packages/com.gamecore.studio.etos/Editor/EtosStudioSession.cs`: implement
   `GameCore.Gameplay.Contracts.Narrative.IMediaGenerationGatewayProvider` on the
   existing ScriptableSingleton; `public IMediaGenerationGateway? MediaGateway`
   returns the current project-scoped adapter and null on stop. The configured
   singleton must be instantiated on session startup, not by gameplay lookup.
   The adapter implements RequestVoiceLine, returns Requested with the durable
   request id, and imports/binds only through journaled candidate operations.
   Optionally implement ISoundEffectGenerationGateway; absent capability stays
   NotConfigured. Rebind on domain/project/session replacement.
4. **P3.1**, `games/hollowmere/Assets/Hollowmere/Boot/GameBoot.cs` / trusted Editor
   bootstrap: after runtime/session creation and each restored-world replacement add
   the one-line hook (under the game's Editor integration assembly):
   `StudioAdmissionServices.BindAdmission(StudioServices.Runtime, () => activeSaveService, () => restoredWorldReady, verdict => StudioAdmissionServices.RunSmokeTest(StudioServices.Runtime, verdict, RunAdmittedSmokeEntry));`
   Supply these game-owned accessors; RunAdmittedSmokeEntry has signature
   `bool RunAdmittedSmokeEntry(string type, string method, int steps)` and selects
   only trusted, admitted entry registrations. It boots/asserts in the active world,
   returns true only after all required assertions pass, and is idempotent across
   admission crash recovery. Do not call the sandbox Begin() harness in the live
   Editor: it owns a separate root and would displace the restored game. GameBoot
   must not acquire an unconditional reference to an Editor assembly in player builds.

5. **R2-B**, `Packages/com.gamecore.studio.core/Editor/Stage/StageAdmission.cs`
   and `AdmissionLifecycle.cs`: add `Func<StageVerdict, AdmissionSmokeStatus>? PollSmokeTest`
   with `AdmissionSmokeStatus.Pending|Passed|Failed`, persisting the smoke transition
   while Pending and polling across Editor frames. Current `SmokeTest` is synchronous
   and false immediately starts rollback; it cannot run the stage harness's 120-frame
   Begin/Step/SlotHash protocol against the resumed world. Retain the bool callback
   for synchronous game assertions. The P3.1 callback above must not report Pending
   as false or pump the world in a synchronous loop.

## Tests

Pending Unity XML counts; final evidence is appended below. Regression names:

- R2-41: `R2_41_ToolsUseRegisteredGatewayAndObserveReplacementAndUnregister`,
  `R2_41_AmbiguousRegistrationsFailClosed`,
  `R2_41_UnregisteredConcreteTypesAreNotConstructed`,
  `R2_41_RegisteredToolIdsReachGatewayThroughChangeSetEngine`.
- R2-34: `R2_34_WorldToolReferencesAreBindableArguments` (already passing after P1.7b).
- Admission: `R2_B_BindingReplacesServicesAndRejectsUnverifiedSmoke`.
- R2-06: `R2_06_RuntimeMetadataAndReloadRegistration`,
  `R2_06_CatalogMechanismIsNonUndoableAndNeverCallsAuthoredTool`,
  `R2_06_MissingBridgeAndStaleRevisionRefuseWithoutSubmit`,
  `R2_06_MixedAtomicBatchRefusesBeforeEffects`,
  `R2_06_HollowmerePlayWorldRefusesUnavailableCommandBridgeAndMixedAtomicBatch`.

## Left open

- Successful production runtime travel/dialogue/spawn/NPC/mechanism execution is
  blocked by the command-versus-composition mismatch above. Test bridges prove
  adapter/engine contracts only; they are not production gameplay execution evidence.
- End-to-end admitted-mechanism smoke and restored-game continuity require P3.1's
  active-world smoke dispatcher and hook. Binding callbacks alone does not qualify
  the 90-second admission budget.
- Package metadata cannot pass without the two manifest dependencies above. This
  packet does not edit unauthorized manifests/locks or weaken the checker.
- R2-D is not on this branch; real provider execution remains external. No paid
  operation, credential file read, installed companion/etosd change, or sibling clone
  mutation is performed.
- D3 Docker/licence qualification belongs to R2-F; this packet launches trusted
  gameplay tests through the shared host lock, not a candidate stage child, and
  issues no staging verdict. No host-confinement fallback is claimed.
