# P0.4 kernel-app — packet report

Branch `worktree-agent-a0524a3d15ab6a066` (merged with `main` at P0.2+P0.3). Owner: Opus 5.5.
Code commit: `3f6e0df`. Everything below was compiled and run on `myubuntu`; nothing was built on the Mac.

## 1. Changes by SADR

### SADR-010: application root (`Packages/com.gamecore.unity.app`, new)

- Package `com.gamecore.unity.app` 1.0.0, unity 6000.0. Its dependencies are the asmdef's references exactly:
  composition, contracts, derivation, planning, unity.adapters, unity.runtime, `com.unity.collections`
  2.6.6 and `com.unity.entities` 1.4.6. Collections is required by the Entities codegen (DC0061).
- Assembly `GameCore.Unity.App` in `Runtime/GameCore.Unity.App.asmdef`. It has these files:
  - `GameApplicationDefinition` (immutable) with a `Builder`. It holds:
    - the catalog and its declared fingerprint;
    - plugin declarations, the world definition, the temporal model and fixed step;
    - the propagation mode, the root scope and scope seeds (`ScopeRecord`);
    - system registrations, dispatch kinds, slot-policy migrations and migration executors;
    - the message plane, recipes, value source, rule keys and overrides;
    - SADR-013 config bindings, the budget, the issuer and provenance bounds;
    - an ordered boot script (`GameApplicationBootStep.Seed` / `.Apply`);
    - `IGameApplicationRestoreHook`, which yields an `IRestoreTargetBuilder` for the later SADR-012 packet;
    - an optional adapter-frame factory.
  - `GameApplicationRoot` composes, in one order:
    1. `CatalogManifestSource`;
    2. the `OwnershipSchedulePipeline` descriptor and adaptation (→ registration);
    3. `TargetRegistry`, `AssemblyPublisher` (with `SpawnRecipeCatalog`, `MigrationRegistry` and `OwnershipStageDescriptor`), `LiveTargetIndex` and `LiveTargetSeeder`;
    4. the lane, which consults a `CompositionEditValidatorSet[DerivationModeSwitchValidator, DerivedAssemblyPreflightValidator]`;
    5. `DerivedAssemblyPipeline`, then the preflight `Attach`;
    6. `WorldCompositionBridge` (pipeline mode);
    7. `ProvenanceStore` and `ProvenanceExplanationReader`, and the observation hub;
    8. the adapter-frame registration of the pump counter, wrapping the game's own frame.

    The root then pauses the world at its first boundary, runs the boot script and checks P-006 equality. Any refusal disposes the half-composed world.
  - `GameApplication`, a static, MonoBehaviour-free entry: `Boot(definition[, options])` (throws), `TryBoot`, `Register(definition)` (player path), `Unregister`, `TryPrepare`, `CreateRegistration`, `Current`, `LastFailure` and the `BootFailed` event.
  - `GameApplicationBootFailed{Code, Diagnostic, Stage, Detail}` and `GameApplicationBootException`. The codes are:
    InvalidDefinition, CatalogHashMissing, CatalogFingerprintMismatch, PluginDeclarationRejected, ScheduleRejected,
    BootstrapFallback (`FallbackCount != 0`), WorldCreationFailed, ConfigBindingInvalid, TargetSeedRefused,
    BootEditRefused, PublicationSeriesSplit, AlreadyBooted, CompositionFault. Every failure is logged
    (`Debug.LogError`) and raised to the caller.
  - `GameApplicationPumpCounter` is the world's registered `IAdapterFrame`. It counts the sanctioned pumps of
    `GameCoreApplicationPump`. It flags a second pump in the same host frame (frame clock) and any host pump that
    bypassed the app pump (`host.PumpCount` delta). In Editor and development builds each violation is logged as an
    error, which is the assertion. Release players only count.
- Lifecycle: a booted root is `Ready`, with the world Paused at a committed boundary. Then come `Start`, `Pause` and
  `Resume` (O-26 `SetRunState`). `Stop` unregisters the frame first, then calls `host.Stop` (O-19).
- `Packages/com.gamecore.unity.adapters/Runtime/PlayerLoop/GameCoreApplicationBootstrap.cs` changes:
  - New `GameCoreBootstrapFailurePolicy {InfrastructureFallback (default), HardFailure}`.
  - New `CatalogHash`, used in `TryCreateWorld`. It replaces the `ContentHash.Empty` literal; the default is still
    Empty. `Propagation` is also added.
  - New `WorldCreated` hook, `BootFailed` event, `HardFailureCount` and `LastBootFailed`.
  - Under HardFailure, any of these is a logged, named failure with `DefaultGameObjectInjectionWorld = null`, and no
    fallback world is created: a missing root, an empty hash, a failed creation, or a throwing hook. It still returns
    `true`, so Unity's default world is suppressed too.
- **The switch (legacy vs. root):** the validation project never calls `GameApplication.Register`, so its bootstrap
  keeps `InfrastructureFallback`, its probe `RootFactory` and the empty hash, unchanged. A game calls
  `GameApplication.Register(definition)` from its `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` method.
  That sets `RootFactory`, `FailurePolicy = HardFailure`, `CatalogHash`, `Propagation` and `WorldCreated`;
  `Unregister()` reverts all of them. This is documented in `docs/game-core/04-unity-integration.md` §9 (additive
  paragraph).

### SADR-011: bridge (`WorldCompositionBridge.cs`, new `Integration/CompositionEditValidation.cs`)

- `SubmitAndExecute(payload, operationId, CompositionRevision expectedRevision)` is new. The two-argument overload
  delegates with `Composition.Committed.Revision`.
- **No `NotifyCommandAdmitted(1U)` for composition edits, in any mode.** The report's `CommandSubmitted` is always
  false, and `DemandAfter` and the new `StepAfter` show the unchanged demand and step. This follows 04 §3 ("A
  composition-only publication changes AssemblyEpoch … without advancing the logical clock"). The bridge never
  performs spawns and despawns; they keep their semantics (`Pipeline.PublishSpawn`).
- Validate-before-commit works as follows:
  - New `DerivedAssemblyPipeline.Preflight(CompositionState proposed, OperationId)` runs the real target view,
    derivation input (with bindings), incremental derivation against the accepted base, proposal translation and
    `AssemblyPlanner.Build` against the published bindings. It then releases the plan's leases and scratch, and
    publishes nothing. `PreviousDerivation` and the publish counters do not move.
  - `DerivedAssemblyPreflightValidator` is an `ICompositionEditValidator` that calls it, and is late-bound to the
    pipeline by `Attach`. It refuses (MissingDependency) while unattached.
  - `CompositionEditValidatorSet` is an ordered set where the first refusal wins, and it keeps per-validator
    `EditValidationReason`s. `DerivationModeSwitchValidator` is its first member, with unchanged behaviour.
  - New bridge constructor `(world, lane, publisher, pipeline)`. It requires the lane to consult a preflight attached
    to that pipeline. After `Drain` it publishes the world's half itself: `PublishDerived`, plus
    `PublishUnchangedAssembly` on `NoTargetChange`. It also refuses if other callers' proposals are pending.
- `BridgeRefusal{Code, Phase, Witness}`, where the phase is one of WorldState, Admission, CompositionPlan,
  WorldPreflight, Publication or WorldPublication. A world-side refusal now arrives as a lane rejection in phase
  `WorldPreflight`, which closes the divergence formerly at `WorldCompositionBridge.cs:369-390`. The only post-commit
  refusal left is `WorldPublication` (`KeptOneSeries == false`). It is reachable only through what no dry run sees
  (P-030/P-031).
- **Test changed by design:** `Packages/com.gamecore.unity.runtime/Fixtures/Runtime/W1GateScenario.cs`
  (`gate-one-operation-admitted`, `gate-throwing-stage…`).
  - The gate used to rely on the bridge charging one step per edit. It now asserts that the edit charged *no* step
    (`!CommandSubmitted && DemandAfter == 0 && StepAfter == before`), then admits one explicit command
    (`NotifyCommandAdmitted(1U)`) for the guarded step that follows.
  - Every downstream step, demand, stage, fault and publication assertion is unchanged. The code comment explains
    the change.
  - This fixture is outside the listed paths; changing it was unavoidable once the semantics changed.

### SADR-013: install config reaches systems, option (a), derivation payload binding

- Derivation (`com.gamecore.derivation`):
  - `BoundRulePayload(RuleId, FrozenPayload)` is new. A new constructor
    `DerivationInstall(record, state, manifest, boundPayloads)` validates the bindings: each must be a declared rule,
    bound at most once.
  - `PayloadOf(rule)` and `BoundPayloads` are added.
  - The full engine, the incremental engine and the oracle all contribute `PayloadOf(rule)`.
  - `DerivationChangeSet.InstallsEqual` compares bound payloads.
  - The snapshot canonical text and the `DerivedRecipeCache` fingerprint append bound payloads only when present, so
    existing hashes are byte-identical.
- Runtime: new `Integration/InstallConfigBinding.cs` with `RuleConfigBinding(RuleId, Id128 field)`,
  `InstallConfigBinding.TryBind/TryEncode/TryValidate`.
  - Int32, UInt32 (≤ int.MaxValue) and Bool encode to the canonical big-endian int32; Bytes pass through raw.
  - Missing or Null keeps the manifest payload.
  - Other kinds refuse with UnsupportedVersion.
  - `CompositionDerivationInput.Build(…, configBindings)` (new overload), `DerivedAssemblyPipeline(…, configBindings)`
    and `DerivationModeSwitchValidator(…, configBindings)` are added.
- **Planner fix needed for SADR-013** (`Packages/com.gamecore.planning/Runtime/Plans/AssemblyPlanner.cs`, outside
  the listed paths; justified here):
  - In step 4b, a provider's *own* published row was ranked as a second candidate against that provider's
    re-declaration. A reconfigured value therefore conflicted with itself on an Exclusive slot (the first Unity run
    proved it: `2 eligible candidates … provider=X value=2500, provider=X value=1000`). On a Replace tie it could keep
    the stale value.
  - The row is now skipped when the same provider re-declares the identity. Rows of other providers stay candidates,
    so P-018 is unchanged.
  - All 163 planning dotnet tests and the full Unity suites pass with the change.
- Recorded as row "SADR-013 (studio)" in `docs/game-core/10-decisions-and-open-questions.md` §1, with a link to
  `docs/studio/02-architecture.md` §8. The choice is (a) over (b): the binding-row path already reaches every system,
  and it needs no new ECS storage, fence or checkpoint row. `00-core-protocols.md` is untouched.

### Other paths touched

- `unity/GameCore.Validation/Packages/manifest.json` and `packages-lock.json`: added `com.gamecore.unity.app`, which
  the validation project needs to compile the root's tests. This is a path exception. The games/hollowmere manifest
  is left to the integrator, as instructed.
- `dotnet/tests/GameCore.Derivation.Tests/GameCore.Derivation.Tests.csproj`: `Compile Include="InstallConfig/**/*.cs"`.
  The new test lives in `dotnet/tests/**` because the package's own Tests folder is out of scope.

## 2. Public API of `com.gamecore.unity.app` (for P1.1 / P1.2 / P1.6)

```
namespace GameCore.Unity.App
  static class GameApplication
    GameApplicationRoot Boot(GameApplicationDefinition d[, GameApplicationBootOptions o])   // throws GameApplicationBootException
    bool TryBoot(d, o, out GameApplicationRoot? root, out GameApplicationBootFailed? failure)
    void Register(d[, o]) / void Unregister()                       // player path via the single ICustomBootstrap
    bool TryPrepare(d, out CatalogManifestSource?, out PipelineDescriptorReport?, out GameApplicationBootFailed?)
    UnityWorldRegistration CreateRegistration(d, PipelineDescriptorReport)
    GameApplicationRoot? Current; GameApplicationBootFailed? LastFailure; int BootCount, FailureCount
    event Action<GameApplicationBootFailed> BootFailed
  sealed class GameApplicationBootOptions { InstallPlayerLoop=true; AssignDefaultWorld=true; StartImmediately;
                                            bool? PumpAssertions; Func<long>? FrameClock }
  sealed class GameApplicationDefinition (+ nested Builder: WithCatalog, AddPlugin, WithWorld, WithPropagation,
    WithRootScope, AddScopeSeed, AddSystem, WithDispatchKinds, WithSlotPolicyMigrations, AddMigration, WithMessages,
    WithSeedWorldState, WithRecipes, WithValues, AddRuleKeys, AddOverride, BindRuleToConfig, WithBudget,
    WithTargetCapacity, WithIssuer, AddBootStep, WithProvenance, WithRestoreHook, WithAdapterFrame, Build)
  sealed class GameApplicationBootStep { static Seed(name, target, scope, recipe); static Apply(name, edit) }
  interface IGameApplicationRestoreHook { IRestoreTargetBuilder CreateRestoreTargetBuilder(GameApplicationRoot) }
  struct GameApplicationProvenanceSettings
  sealed class GameApplicationRoot : IDisposable
    Definition, Host, World, CatalogHash, Manifests, Schedule, Descriptor, Registry, Publisher, Recipes, Migrations,
    Targets, Seeder, Lane, Validators, ModeSwitch, Preflight, Pipeline, Bridge, Provenance, Explanations,
    Observations, Observation, PumpCounter, State, LastProvenance, ProvenanceMisses
    OperationId NextOperation()
    WorldAdmissionReport Submit(payload) / Submit(payload, CompositionRevision expected)   // SADR-011 path
    OperationResult Start() / Pause() / Resume() / Stop(reason); Dispose()
    bool TryGetLatestBoundary(out SnapshotToken)
    CaptureContext CreateCaptureContext(); UnityCommittedBoundaryReader CreateBoundaryReader()   // P1.2
    bool TryCreateRestoreTargetBuilder(out IRestoreTargetBuilder?)
  enum GameApplicationState { Ready, Running, Paused, Stopped }
  enum GameApplicationBootCode { … see §1 }
  sealed class GameApplicationBootFailed { Code, Diagnostic, Stage, Detail }; GameApplicationBootException
  sealed class GameApplicationPumpCounter : IAdapterFrame { SanctionedPumps, DuplicateFramePumps, BypassPumps,
    Violations, LastViolation, AssertionsEnabled, Inner, SetInner, UseFrameClock, ObserveHost }
```

Runtime additions other packets use:

- `WorldCompositionBridge`: `SubmitAndExecute(p, op, expected)`, `Pipeline`, `Preflight`, `LastRefusal`,
  `StaleExpectationCount`, `PreflightRefusalCount`.
- `WorldAdmissionReport`: `Refusal`, `StepAfter`, `Assembly`, `UnchangedAssembly`.
- `BridgeRefusal` and `BridgeRefusalPhase`.
- `DerivedAssemblyPipeline`: `Preflight`, `World`, `Lane`, `Publisher`, `ConfigBindings`, `PreflightCount`,
  `PreflightRefusedCount`.
- `CompositionEditValidatorSet`, `DerivedAssemblyPreflightValidator`, `EditValidationReason`.
- `RuleConfigBinding` and `InstallConfigBinding`.
- `GameCoreApplicationBootstrap`: `FailurePolicy`, `CatalogHash`, `Propagation`, `WorldCreated`, `BootFailed`.

## 3. Test evidence (host `myubuntu`, Unity 6000.0.75f1, dotnet 8.0.425)

Results were read from the XML/TRX files with a parser (`p04-runs/nunit_summary.py`, `trx_summary.py`), not from
summarised console output. All paths are under `~/wkspace/gc-studio/p04-runs/`.

| Suite | Command | Result | Duration | Files |
|---|---|---|---|---|
| dotnet (final, after merging main) | `~/.dotnet/dotnet test dotnet/GameCore.sln --logger trx --results-directory p04-runs/dotnet-2` | **1390/1390 passed**, 0 failed, 0 not executed | 253 s wall | `dotnet-2/*.trx`, `dotnet-2.log` |
| dotnet (before merging main) | same, `dotnet-1` | 1312/1312 passed (Derivation 118, incl. 4 new) | 3 m 45 s wall | `dotnet-1/*.trx`, `dotnet-1.log` |
| Unity EditMode | `timeout 1800 Unity -batchmode -nographics -projectPath …/p04/unity/GameCore.Validation -runTests -testPlatform EditMode -testResults editmode-6.xml -logFile editmode-6.log` | **1295/1295 passed** (10 new `GameCore.App.Tests`) | test run 344.7 s; 1535 s wall incl. import | `editmode-6.xml`, `editmode-6.log` |
| Unity PlayMode | same, `-testPlatform PlayMode -testResults playmode-1.xml` (from the git clone at `3f6e0df`) | **84/84 passed** | test run 187.5 s; 452 s wall | `playmode-1.xml`, `playmode-1.log` |

The NuGet audit was reachable, so no `GAMECORE_OFFLINE`.

Earlier EditMode attempts, reported faithfully:

- `editmode-1`, `-2`, `-3`: compile errors in the new code, since fixed:
  - `Unity.Entities` resolved inside the `GameCore.Unity` namespace;
  - the missing Unity.Collections reference;
  - a test helper type.
- `editmode-4`: aborted at startup ("Package Manager could not establish a connection", 43 s). This was the known
  pre-dispatch class; it was retried once.
- `editmode-5`: ran 1295 tests with 1292 passed and 3 failed, all new SADR-013/011 tests. The cause was the planner
  self-conflict above, which is now fixed.

The brief's baseline was 1270 EditMode tests. This branch runs 1285 pre-existing EditMode tests plus 10 new ones. I
did not verify the source of the 15-test difference (likely tests added after the brief's count). `tools/reproduce.sh`
was not run, as instructed.

New tests:

- dotnet `BoundRulePayloadTests` (4):
  - bound payload agreement across full, incremental and oracle;
  - a reconfigure is a *changed* contribution with the same key, and the change set marks the install;
  - no snapshot-hash drift without bindings;
  - an invented or duplicate binding is refused.
- Unity `GameCore.App.Tests.GameApplicationRootTests` (10):
  - boot smoke (real hash, FallbackCount 0, pump counter, Start/Pause/Resume/Stop);
  - a bypass and duplicate pump are counted;
  - a corrupted catalog gives `CatalogFingerprintMismatch` and no world;
  - a missing hash gives `CatalogHashMissing`;
  - a refused boot edit disposes the world;
  - the bootstrap HardFailure policy creates no fallback;
  - a stale revision gives `StalePlan` with no publication;
  - a refused world plan leaves the lane unchanged, and the world stays editable and the boundary reader reads it;
  - an edit does not advance `CurrentStep`;
  - a numeric reconfigure is seen by a system in the next step (1000 → 2500; the manifest value 7 is never seen).

Checkers, run through `rtk proxy` to unfiltered files:

- `python3 tools/check_game_core_csharp.py`: ok, 649 files, including `com.gamecore.unity.app`, which is now in P0.2's
  targets.
- `python3 tools/validate_game_core_docs.py`: passed.
- `python3 tools/check_package_metadata.py`: **fails, on one problem I did not introduce.** `com.gamecore.studio.core`
  (merged from P0.3) is not locked in `unity/GameCore.Validation/Packages/packages-lock.json` or in
  `games/hollowmere/Packages/packages-lock.json`. `main` has the same gap. Before the merge, this branch passed the
  check, `com.gamecore.unity.app` included.

## 4. Leftovers

1. The P0.4 row's acceptance asks for a hollowmere smoke scene booting through the root with FallbackCount == 0. This
   was not done: hollowmere belongs to P0.2, and the integrator adds `com.gamecore.unity.app` to its manifest. The
   equivalent headless proof is `Boot_ComposesTheRootWithTheRealCatalogHash_AndOnePumpPath`.
2. `check_package_metadata.py` fails on `main` because `com.gamecore.studio.core` is unlocked (see §3). This is for the
   integrator or P0.3.
3. The root does not create a `WorldTimeDriver` and does not call `AdoptResourceTable(adaptation.NativeTable)`. A game
   whose stages publish native producer fences needs that wiring (P1.1).
4. `CreateCaptureContext()` declares no plugin clocks, RNG streams, next-step buffers, command payloads or outbox
   rows. P1.2 extends it, together with the SADR-012 restore builder behind `IGameApplicationRestoreHook`.
5. Provenance after an edit is published at the newest committed image token
   (`Observation.TryGetLatestBoundary`). That is not a token of the new assembly epoch, so misses are counted rather
   than failed. P1.6 may want an epoch-anchored token.
6. SADR-013 binds only int32-encodable kinds and raw Bytes. Multi-value slots stay refused by
   `DerivedCompositionProposal` (unchanged), and option (b), a per-install buffer, was not built.
7. The pump assertion is a logged error in Editor and development builds, not a thrown exception. A throw from an
   adapter frame would be swallowed as `Faulted` by `AdapterFrameRegistry`.
8. The EditMode bootstrap test calls `GameCoreApplicationBootstrap.Initialize` directly. That increments
   `BootstrapCount` and installs, then removes, the loop node in the EditMode process. PlayMode's TEST-018 counts
   are unaffected; it runs in its own process and passed.
