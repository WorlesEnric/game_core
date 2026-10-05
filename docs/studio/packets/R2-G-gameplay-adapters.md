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

1. **R2-A / P1.7c kernel owner**, `Packages/com.gamecore.studio.core/Editor/Engine/LiveWorld.cs`,
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
   until the kernel can represent it. Bind the active-world implementation through
   `WorldLiveOpTranslator.Register(runtime).Bridge = currentWorldBridge;` after root
   replacement and clear it on stop. Do not disguise commands as plugin reconfigure.
2. **Metadata/integration owner**, `Packages/com.gamecore.gameplay.world/package.json`:
   add exact dependencies `"com.gamecore.studio.core": "1.0.0"` and
   `"com.unity.nuget.newtonsoft-json": "3.2.1"` (the verified Hollowmere pin), and synchronize
   that package's dependency map in `games/hollowmere/Packages/packages-lock.json`
   (and any other locking project). The typed Studio adapter's asmdef now references
   these assemblies. These manifests/locks are outside this packet's exclusive paths.
3. **R2-D**, `Packages/com.gamecore.studio.etos/Editor/EtosStudioSession.cs`: implement
   `GameCore.Gameplay.Contracts.Narrative.IMediaGenerationGatewayProvider` on the
   existing ScriptableSingleton; `public IMediaGenerationGateway? MediaGateway`
   returns the current project-scoped adapter and null on stop. The configured
   singleton must be instantiated on session startup, not by gameplay lookup.
   The adapter implements RequestVoiceLine, returns Requested with the durable
   request id, redacts every visible detail with core SecretRedactor, and
   imports/binds only through journaled candidate operations.
   Optionally implement ISoundEffectGenerationGateway; absent capability stays
   NotConfigured. Rebind on domain/project/session replacement.
4. **P3.1**, `games/hollowmere/Assets/Hollowmere/Boot/GameBoot.cs` / trusted Editor
   bootstrap: after runtime/session creation and each restored-world replacement add
   the one-line hook, using the real `UseSaves(SaveService service)` parameter,
   before its UI-rig early return (bridge from a game-owned Editor integration assembly):
   `StudioAdmissionServices.BindAdmission(StudioServices.Runtime, () => service, () => World != null && Narrative != null && ReferenceEquals(World.Root, service.ActiveRoot) && ReferenceEquals(World.Root, GameApplication.Current), verdict => StudioAdmissionServices.RunSmokeTest(StudioServices.Runtime, verdict, (type, method, steps) => RunAdmittedSmokeEntry(verdict, type, method, steps)));`
   Re-execute UseSaves on each domain/session creation; its readiness lambda follows
   World/Narrative after Reattach. The game does not yet expose a live smoke registry; RunAdmittedSmokeEntry has signature
   `bool RunAdmittedSmokeEntry(StageVerdict verdict, string type, string method, int steps)` and selects
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

6. **P1.7b test owner / integrator**,
   `games/hollowmere/Assets/Hollowmere/Tests/P1_7b/EditMode/ToolJournalTests.cs:73`:
   update `Catalog_ExportsEveryNewTool_AndTheMediaToolsAreComposeToolsThatRequireAgentMedia`.
   Retain the Compose-tier assertions; replace the mandatory authored `agent.media`
   prerequisite assertion with a check that service availability is resolved from
   registration (e.g. `Assert.That(tool.Prerequisites?.Any(p => p.Requires == "agent.media") ?? false, Is.False, id);`). The old prerequisite rejects a configured gateway before any tool
   invocation: R2-G's real ChangeSetEngine test fails against baseline for precisely
   this reason. Do not add a fictitious authored node to satisfy a service dependency.
   This test is outside the exclusive R2_G test directory; its assertion is retained
   unchanged and its failure is reported.

## Tests

Unity XML counts and retained failures are recorded below. Regression names:

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
- P1.7b has one obsolete media catalog expectation after removing the impossible
  authored service prerequisite. Its exact test update is requested above; no
  existing test outside this packet was changed or skipped.
- Package metadata cannot pass without the two manifest dependencies above. This
  packet does not edit unauthorized manifests/locks or weaken the checker.
- R2-D is not on this branch; real provider execution remains external. No paid
  operation, credential file read, installed companion/etosd change, or sibling clone
  mutation is performed.
- D3 Docker/licence qualification belongs to R2-F; this packet launches trusted
  gameplay tests through the shared host lock, not a candidate stage child, and
  issues no staging verdict. No host-confinement fallback is claimed.

## Verification evidence (Linux host)

- `dotnet test dotnet/tests/GameCore.Rules.Gameplay.Tests/GameCore.Rules.Gameplay.Tests.csproj`:
  TRX **307 passed, 0 failed/skipped**, `/tmp/r2-g-dotnet/r2-g-gameplay.trx`, SHA256
  `85be38db04a00c14b46127aff68a40580019771f21e5c6f07714ded56927f23b`.
- `dotnet test dotnet/tests/GameCore.Studio.Model.Tests/GameCore.Studio.Model.Tests.csproj`:
  TRX **104 passed, 0 failed/skipped**, `/tmp/r2-g-dotnet/r2-g-model.trx`, SHA256
  `d4cb1c44d1f42cfa092321e1131a25279a27a3e2dd286c3d5a9e1fbf7822b0d7`.
- `python3 tools/check_game_core_csharp.py`: **pass, 1,102 files**.
- `python3 tools/check_package_metadata.py`: **failed, exactly two missing world-package
  dependencies** (studio.core and Newtonsoft), requested above. No checker waiver.
- Initial Unity run using the requested `Hollowmere\.R2_G.*|GameCore\.Studio\.Core.*`
  EditMode filter: first startup attempt timed out after 600 s silence; automatic
  retry compiled successfully and produced `.unity-logs/r2-g-edit.xml`: **78 total,
  75 passed, 3 failed, 0 skipped/inconclusive**. All 67 existing core tests passed.
  The three new fixture errors were two assertions expecting Failed rather than
  the correct pre-apply Rejected state, and a missing Definition scope on the media
  test target reference. Corrected without changing production refusal behavior.
  This failed run is retained, never counted as acceptance. Total Editor wrapper
  time across attempts: 1,398 s.
- R2-41 before/after proof: temporarily restored only DialogueTools.cs and AudioTools.cs
  from baseline `de2d9593`, retaining the new regression fixture and additive seam.
  `.unity-logs/r2-g-media-before.xml`: **4 total, 1 passed, 3 failed**, no skips.
  Failures are exact: registered tool rejected for authored `agent.media` prerequisite;
  dialogue returned NotConfigured despite registration; audio constructed an
  unregistered CountingMedia fixture. Both fixed files were restored byte-for-byte
  before the final run. No real media provider exists on this branch or was invoked.
- Final broad EditMode run, after fixture corrections and restoration of fixed media
  tools: `.unity-logs/r2-g-broad.xml`: **142 total, 140 passed, 1 failed, 1 skipped,
  0 inconclusive** (294 s Editor wrapper, one attempt). Per suite:
  R2_G **11/11**; core **67/67**; P1.1 **16/16**; P1.4 **7/7**;
  P1.5 **12 passed + 1 existing headless preview skip** (GP-UI-008);
  P1.7b **27 passed + 1 obsolete agent.media catalog assertion failed**, requested
  above. The requested R2_G/core selection is therefore **78/78 passing on the final
  implementation**; the broader suite is explicitly not all green.
  The registered-tool engine test passes all three media IDs. The real Hollowmere
  Play-world refusal test and fake-bridge non-undoable journaling test both pass;
  this does not claim a production command bridge.
- XML SHA256:
  - initial `r2-g-edit.xml`: `e96c47b98d7eb53eb0063eeb9662529c746f68ccec14bbfbcf341dcac4b5b234`
  - baseline `r2-g-media-before.xml`: `b80236afdf80e5bcf6a01b8740d62ad0d40fdaad3c2c91f0a0d323b096b68ca6`
  - final broad `r2-g-broad.xml`: `27aa52d464551717f19da33d83162be9fdaf5575981ef0aaa03a0a15c7da78d3`
- The broad authoring/migration suites reserialized 28 tracked Hollowmere assets;
  all 28 were restored to their pre-test HEAD bytes after Editor exit. No content
  asset change is included. Every Unity invocation used unity-batch.sh and held at
  most one host-wide slot. Test dispositions above come from XML, not stdout.

- PlayMode regression run: `.unity-logs/r2-g-play.xml`, **5/5 passed**, zero failures,
  skips or inconclusives; 212 s Editor wrapper, one attempt. P1.1 ThreeRegionLoop,
  P1.3 PlayerWalkAndInteract, P1.4 DrownedBellHeadless, P1.5 BootWiring and
  UiFlowHeadless all pass. SHA256
  `84f892cdae30abf57eef5e7a86e36eefe7bb25399432d175236f73e8f8abf731`.
- Final metadata rerun retains exactly the two documented missing dependencies;
  41 packages / 89 package assemblies inspected. `git diff --check` passes.
- No runtime kernel, GameBoot, Hollowmere content asset, installed service or
  sibling-clone change is included. `.codex/` was already untracked and is untouched.
- Final source policy rerun after the fixture corrections: **pass, 1,102 C# files**,
  `/tmp/r2-g-csharp-final.txt`. No Unity/Rust test or external acceptance failure is
  hidden by the successful focused selection.

## R2-G2 — tri-state gameplay admission (PACKET.md)

Branch `codex/r2-g2`, based on `46357460`, Linux build host `myubuntu`.
This appendix is the packet report: a root PACKET.md is outside the exclusive paths.
Earlier R2-G verification and open items above are historical; the dispositions below
supersede requests #2, #4–#6 only to the extent explicitly stated.

### R2 fixes

- R2-G request #2: world `package.json` declares its asmdef-derived dependencies
  `com.gamecore.studio.core: 1.0.0` and `com.unity.nuget.newtonsoft-json: 3.2.1`.
  The checker reports **no layering violation**. Its two remaining errors are
  stale project lock dependency maps, outside R2-G2's exclusive paths.

### Requests to other packets

- **Metadata/integration owner:** in
  `games/hollowmere/Packages/packages-lock.json` and
  `games/cleanproof/Packages/packages-lock.json`, synchronize
  `dependencies["com.gamecore.gameplay.world"].dependencies` with the package manifest:
  add `"com.gamecore.studio.core": "1.0.0"` and
  `"com.unity.nuget.newtonsoft-json": "3.2.1"`, preserving existing pins.
  Exact checker messages are
  `games/hollowmere/Packages/packages-lock.json: com.gamecore.gameplay.world dependency map disagrees with its manifest`
  and the identical message with `games/cleanproof/Packages/packages-lock.json`.
  The checker compares the local `com.gamecore.*` map; Unity's resolved lock should
  also include the declared Newtonsoft dependency. No checker exemption is needed.

### Left open

- The repository-wide metadata gate cannot pass on this packet alone: both locking
  projects are outside the authorized file set. The manifest itself now matches its
  assembly references; the exact two lock changes are requested above.
- No APP-1 note or restore-adoption note is present in the local `origin/main`
  documentation tree at this packet's baseline. No restore-adoption behaviour is
  inferred, and no game/kernel source is edited by this packet.

### R2-G2 adapter contract and regression mapping

- **R2-14 / R2-G requests #4–#5:** additive
  `BindAdmission(StudioRuntime, Func<SaveService?>, Func<bool>, Func<StageVerdict, AdmissionSmokeStatus>)`
  binds `PollSmokeTest` and clears the synchronous assertion. The original bool
  overload stays synchronous and clears an obsolete poll binding. Replacing a
  pending poll with a bool binding installs the failed-closed recovery poller;
  it cannot turn incomplete frame assertions into an immediate success.
- Readiness is checked before and after the game callback. An unverified verdict
  object, lost readiness, exception or unknown status returns Failed. The tri-state
  `RunSmokeTest` overload resolves only the retained digest-bound proposal and
  preserves Pending through the trusted game dispatcher. It does not reflect
  candidate method names, boot a root, or drive the world.
- `RecoverPendingSmoke(runtime)` reads durable `smoke-pending` admissions and
  re-registers their existing lifecycle through `StageAdmission.Resume`. Calling
  Bind again uses this same path; the first-party `WorldStudioRegistration` calls
  it after runtime creation when a poll adapter is absent. Missing registrations
  return Failed, including when `SmokeTest` is a passing bool. Core still requires
  fresh authenticated verdict transport after reload before invoking any poll.
  No second journal or frame counter is introduced; the original persisted
  frame/time budgets remain authoritative (default 120 polls / 60 seconds).
- **R2-41 / R2-G request #6:** only the assigned P1.7b test method changes. Its
  Compose-tier assertions remain, while `agent.media` is asserted absent from
  authored prerequisites. Existing R2-G actual-tool tests prove registration,
  replacement and unregister behaviour through the gateway and ChangeSetEngine.

New regression fixture: `Hollowmere.R2_G.EditMode.Tests.AdmissionAdapterTests`.
Each UnityTest enters **real Hollowmere Play Mode**, loads `Boot.unity`, then exits
via UnityTearDown. The runtime test fixture advances its smoke assertions only in
`LateUpdate` against `GameApplication.Current`; repeated polls consume no world
frames. Installation/compiler/catalog/companion are deterministic doubles and
package bytes are written only to a temporary directory outside the live project.
The two reload cases reconstruct StageAdmission against the same durable record.

| Finding | Regression test |
|---|---|
| R2-14 | `R2_14_AdapterPendingPassesOnlyAfterNWorldFrames` |
| R2-14 | `R2_14_AdapterPendingFailureRollsBack` |
| R2-14 | `R2_14_AdapterSessionStopsBeingReadyFailsClosed` |
| R2-14 | `R2_14_AdapterReloadDuringPendingFailsClosedDespitePassingBool` |
| R2-14 | `R2_14_AdapterReloadRebindRetainsBudgetAndRequiresFreshTrust` |
| R2-14 | `R2_14_AdapterExceptionInvalidStatusAndReadinessLossFailClosed` |
| R2-14 compatibility | `R2_14_AdapterImmediateBoolRemainsSynchronous` |
| R2-41 | `Catalog_ExportsEveryNewTool_AndTheMediaToolsAreComposeToolsThatRequireAgentMedia` plus existing `R2_41_RegisteredToolIdsReachGatewayThroughChangeSetEngine` |

### Requests to other packets — P3.1 exact binding

In P3.1's `games/hollowmere/Assets/Hollowmere/Authoring/Editor/HollowmereStudioAdmission.cs`,
after session creation and each restored-root replacement, with `boot` the active
`GameBoot` and `service` its active `SaveService`, add this line:

```csharp
StudioAdmissionServices.BindAdmission(StudioServices.Runtime, () => service, () => boot.World != null && boot.Narrative != null && ReferenceEquals(boot.World.Root, service.ActiveRoot) && ReferenceEquals(boot.World.Root, GameApplication.Current), verdict => StudioAdmissionServices.RunSmokeTest(StudioServices.Runtime, verdict, (type, method, steps) => RunAdmittedSmokeEntry(verdict, type, method, steps)));
```

The game-owned entry must have this signature (superseding request #4's bool):
`AdmissionSmokeStatus RunAdmittedSmokeEntry(StageVerdict verdict, string type, string method, int steps)`.
It registers a trusted entry once per verdict digest/active root, returns Pending
until all `steps` normal game frames and assertions complete, Passed only then,
and Failed on failure/missing registration/root loss. Advance its Step only from
P3.1's registered per-frame callback; the poll must never call PumpFrame or run a
synchronous loop. Rebind and re-register after reload from the verified retained
proposal; if its progress cannot safely be reconstructed, return Failed. Do not
call the sandbox Begin harness that owns a separate root. Choose any larger
trusted poll budget **before** admission begins if game/editor frame pacing requires
it; rebind cannot reset the persisted budget.

### Left open — game integration and qualification

- The P3.1 Editor admission file and active-world smoke registry are absent from
  this baseline. They are explicitly outside R2-G2 ownership. The exact line and
  signature above are the required game side; this packet does not claim that
  real admitted candidate smoke is integrated in GameBoot.
- Real process-kill recovery, a domain reload while smoke is Pending, checkpoint
  adoption/restore continuity and the 90-second admission budget remain integration
  qualification. Tests exercise actual Play frames and durable service reconstruction,
  not a killed Editor or live companion. They issue no production signed verdict.
- No paid ETOS operation, credential-file read, installed companion/etosd restart,
  or sibling-clone mutation is performed. Rust is unchanged and not rebuilt.
