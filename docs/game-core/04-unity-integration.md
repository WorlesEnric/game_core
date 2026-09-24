# Unity integration

This document maps the engine-independent requirements in [Core Protocols](00-core-protocols.md) to the only V1 backend: Unity Entities. Protocol definitions, operation ordering, state ownership, and failure semantics belong there. The [runtime design](03-runtime-and-execution.md) explains the execution model; this document owns the concrete Unity integration choices. No Unity project, Unity compilation, player build, or performance result is delivered by this documentation change. A host-only contract skeleton build is separately recorded in the validation report.

The governing requirements are [identity and references](00-core-protocols.md#p-004) (P-004–P-008), [assembly publication](00-core-protocols.md#p-027) (P-027–P-034), [execution](00-core-protocols.md#p-035) (P-035–P-045), [lifecycle](00-core-protocols.md#p-046) (P-046–P-054), and the [V1 backend profile](00-core-protocols.md#p-058) (P-058–P-060). The sections below implement these requirements rather than introduce a second protocol.

## 1. Pinned baseline and qualification gate

The V1 qualification baseline is the following exact combination. These are deliberate pins, not aliases for the newest release.

| Item | V1 pin or setting | Evidence and purpose |
| --- | --- | --- |
| Unity Editor | **6000.0.75f1**, Linux x86_64 installation with the **Linux IL2CPP** build-support module | Official released Editor; [release record](https://unity.com/releases/editor/whats-new/6000.0.75f1) |
| Entities | **1.4.6** | Registry requires at least Unity 2022.3.20f1; its dependency declarations select the packages below |
| Collections | **2.6.6** | Exact Entities dependency; also requires at least Unity 2022.3.20f1 |
| Burst | **1.8.28** | Exact dependency shared by Entities and Collections |
| Mathematics | **1.3.2** | Exact dependency shared by Entities and Collections |
| Unity Test Framework | **1.4.6** | Exact Collections dependency; used for EditMode, PlayMode, and player qualification |
| Performance Testing package | **3.0.3** | Exact Entities/Collections dependency; benchmarks remain separately gated |
| Managed toolchain | Editor-bundled Roslyn, **C# 9**, **.NET Standard 2.1** API compatibility | No independent modern .NET runtime assumption; [compiler](https://docs.unity3d.com/6000.0/Documentation/Manual/csharp-compiler.html) and [API profile](https://docs.unity3d.com/6000.0/Documentation/Manual/dotnet-profile-support.html) |
| Qualification host | **Linux x86_64 (Ubuntu 24.04)**, the Editor's Linux IL2CPP toolchain, its **sysroot** and C++ toolchain from `com.unity.toolchain.linux-x86_64` **2.0.11** | The selected build host is Linux, so a Linux-hosted Linux player is the direct route; a concrete build-test host, not a restriction on the game's commercial platforms; [toolchain package](https://docs.unity3d.com/6000.0/Documentation/Manual/com.unity.toolchain.linux-x86_64.html) |
| First standalone target | **StandaloneLinux64, x86_64**, **IL2CPP**, direct player build, Burst enabled, run headless with `-batchmode -nographics` | Build and run the player on the qualification host; no Xcode or Apple SDK is involved in this profile |
| Unqualified targets | **macOS ARM64 and every other platform**, including the previously documented macOS 15.5 ARM64/Xcode 16.4 profile | Selecting a Linux build host replaced the macOS qualification profile; the macOS target stays unqualified until it has equivalent player evidence from its own build script |

The Editor is hosted on Linux x86_64 and builds a Linux IL2CPP player with the toolchain and sysroot supplied by `com.unity.toolchain.linux-x86_64`; no Apple SDK or Xcode is installed or required on this host, so the earlier macOS-ARM64-with-Xcode profile is documented as replaced (see the [evidence ledger](references/unity-evidence.md)) rather than assumed. The package graph declares an Editor floor below the pinned Editor, and [Unity's system requirements](https://docs.unity3d.com/6000.0/Documentation/Manual/system-requirements.html) cover this host architecture. These sources establish a documented compatibility basis, **not evidence that this exact project has passed a build**. Record the Editor revision, host OS build, native compiler and linker versions, sysroot, resolved package lock, and player exit codes in the first qualification artifact. [IL2CPP platform constraints](https://docs.unity3d.com/6000.0/Documentation/Manual/il2cpp-introduction.html) and [Burst platform/build constraints](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/building-projects.html) apply unchanged; only the native toolchain differs from the macOS profile.

The [evidence ledger](references/unity-evidence.md) records the exact official registry dependencies and archive hash inspected. Entities 1.4.6 includes an ECB exception-path corruption fix, which is one reason to choose it over earlier 1.4 releases; this does not make ECB playback transactional. The ledger distinguishes package evidence, release-line documentation, and unexecuted qualification.

The project created in the first implementation wave contains these explicit direct pins in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.unity.entities": "1.4.6",
    "com.unity.collections": "2.6.6",
    "com.unity.burst": "1.8.28",
    "com.unity.mathematics": "1.3.2",
    "com.unity.test-framework": "1.4.6",
    "com.unity.test-framework.performance": "3.0.3",
    "com.unity.toolchain.linux-x86_64": "2.0.11"
  }
}
```

This is the package-specific fragment, not a complete generated project manifest. `com.unity.toolchain.linux-x86_64` is a build-host pin: it supplies the Linux IL2CPP toolchain, compiler and sysroot for the qualification host and contributes nothing to the shipped runtime. Unity-provided module entries and the Editor-resolved transitive graph must be committed with it. Do not synthesize `packages-lock.json` from this table. Open a clean project in the pinned Editor, resolve, inspect every transitive override, compile Entities source generation, then repeat resolution on a clean machine and build the player. `unity/GameCore.Validation` is that qualification project for GC-001; it pins these versions and adds only the probe assemblies described in [validation and performance](08-validation-and-performance.md). Any pin change is a reviewed baseline revision with the same gate. Rendering, Netcode, DOTS Physics, Input System, and Addressables packages are not kernel dependencies; add exact pins and qualification tests only when their optional adapter is implemented.

## 2. Assemblies and dependency direction

| Assembly | Allowed references | Contents and execution boundary |
| --- | --- | --- |
| `GameCore.Contracts` | Supported BCL subset only; `noEngineReferences: true` | IDs, manifest DTOs, protocol results, schema/version descriptors; no `Entity`, `GameObject`, `JobHandle`, or native allocation ownership |
| `GameCore.Composition` | Contracts, supported BCL | Managed scopes, services, configuration, lifecycle/resource ledger and immutable composition snapshots |
| `GameCore.Derivation` | Contracts, supported BCL | Pure finite derivation, contribution composition, provenance and change-driven indexes |
| `GameCore.Planning` | Contracts, Composition, Derivation | Immutable plans, state-disposition validation and semantic schedule compilation; no Entity/query facade |
| `GameCore.Rules.<Name>` | Contracts and small value types only | Game-specific value schemas and reusable pure rules; the Burst-callable subset stays allocation-free and unmanaged. Assembly placement alone does not make a method Burst-compatible |
| `GameCore.Unity.Runtime` | Contracts, Composition, Planning, Unity Entities/Collections/Jobs/Burst/Mathematics | World host, bindings, stage dispatch, structural application, snapshot extraction |
| `GameCore.Unity.Adapters` | Runtime, UnityEngine | Bootstrap/PlayerLoop, input, GameObject, audio, animation, assets, and optional physical-simulation adapters |
| `GameCore.Content.Compiler` | Contracts, Planning, Unity Runtime, Unity Editor APIs; Editor-only | Authoring adapters, bakers, build-time recipe/registry generation and validation |
| `GameCore.Gameplay.<Name>` | Corresponding Rules assembly, Unity Runtime and explicitly required adapters | Concrete unmanaged components, declarations and precompiled `ISystem`/job implementations |
| `GameCore.Generated` | Known plugin and adapter assemblies | Closed registry, serializers, recipe factories, system factories, and AOT roots |

`.asmdef` references must remain acyclic. Generated registration is the application composition root; Contracts and Composition cannot reference it. Editor code cannot leak into runtime assemblies. The architecture supports extraction of pure rules, definitions, and protocol tests into another host later; Unity queries, jobs, baking, assets, native containers, and simulation adapters would require a new implementation.

Managed service resolution, `Task`, cancellation sources, delegates holding plugin objects, reflection, managed collections, and exceptions as normal control flow stay outside Burst jobs. Job inputs are validated unmanaged values, blob references, and native collections with explicit owner/dependency lifetimes. Burst jobs return values/status codes and buffered domain results; they do not call a managed scope or publish an event. Burst's documented type subset is narrower than C# or the managed API profile. [Burst type support](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-type-support.html).

## 3. World ownership and one update path

Each protocol world has one owning `UnityWorldHost` and one `Unity.Entities.World`. Multiple protocol worlds require separate hosts and worlds, not global singleton state. Baking and Editor inspection worlds are outside this gameplay world registry. The host owns native allocations, protocol identity, composition snapshot, schedule, job fences, and output snapshots. The plugin tree never becomes Unity `Parent`, `LocalTransform`, or a GameObject hierarchy automatically.

Use one application `ICustomBootstrap` implementation. It creates the designated gameplay world, assigns `World.DefaultGameObjectInjectionWorld`, and returns `true` to suppress default gameplay initialization. Install an application-owned PlayerLoop node once. Do **not** also call `ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop`, call `World.Update()`, or attach MonoBehaviour `Update`/`FixedUpdate` methods which drive the same world. Unity explicitly supports custom bootstraps and manual world updates. [Custom bootstrap documentation](https://docs.unity3d.com/Packages/com.unity.entities@1.4/manual/systems-icustombootstrap.html).

The implementation follows this algorithm; it is **pseudocode using intended project types**, not a compilable bootstrap listing:

```text
ICustomBootstrap.Initialize:
    reject a second live host for the same WorldId
    world := new Unity.Entities.World(name)
    host := UnityWorldHost.Create(world, generated registry)
    create infrastructure groups and only explicitly approved systems
    World.DefaultGameObjectInjectionWorld := world
    remove stale GameCore loop nodes from PlayerLoop.GetCurrentPlayerLoop()
    insert one GameCore pump before Update.ScriptRunBehaviourUpdate
    PlayerLoop.SetPlayerLoop(modified current loop)
    return true

GameCore pump:
    collect adapter input and completed host callbacks
    allow one ready assembly publication at a protocol boundary
    obtain the number of logical steps from the world's temporal driver
    for each admitted step:
        set World.Time from the logical clock
        update GameCoreStepGroup exactly once
        finish required dependencies and structural playback
        extract and publish a complete SnapshotToken(WorldId, epoch, step)
    update presentation from the last published snapshot
```

The real installer edits the **current** PlayerLoop recursively and preserves unrelated nodes. It identifies its own node by a dedicated marker type and host generation; it never overwrites the entire loop with an old cached default. A single application pump may enumerate registered hosts, but each host has a reentrancy guard and step counter. Stop removes the node/host route before completing jobs and disposing the world. Never retain a delegate to a disposed world.

The bootstrap's allowlist is generated. Do not discover and create every system in loaded assemblies. Add a third-party system only through an adapter whose required companion systems, update positions, lifecycle, and authority are known. Gameplay systems use `[DisableAutoCreation]` and are explicitly created through generated factories; this prevents accidental creation in another default/Editor world. A designated display/default world may populate Unity's injection property; other hosts use explicit references.

`GameCoreIngressGroup`, `GameCoreStepGroup`, and `GameCoreOutputGroup` are adapter infrastructure. They are not genre-specific protocol stages. They are explicitly updated by the host and are not children of Unity's automatically driven default groups. Presentation runs on host frames even when a command-driven world is idle; an idle world does not synthesize simulation steps or increment `LogicalStepId`. A composition-only publication changes `AssemblyEpoch` and publishes a new consistent view without advancing the logical clock.

For fixed steps, the host owns the accumulator and maximum catch-up policy described in the runtime design. Do not simultaneously enable `FixedStepSimulationSystemGroup`'s own rate manager for the same simulation. A card game can admit one logical step per accepted command batch; a narrative world can wait indefinitely for input. Use `World.SetTime(...)` to supply the chosen simulation clock. CommandDriven does not derive elapsed simulation seconds from rendered frames or step count; its Unity delta is zero unless an explicit domain-time command supplies a clock advance. Host wall time is input to the temporal driver, not authoritative game time.

## 4. Runtime stage plans, system instances, and Jobs

The protocol stage compiler resolves names, ports, ownership, and dependencies before Unity system creation/publication. It produces a stable topological order and dataflow dependency table. The adapter flattens this order into `GameCoreStepGroup`; its subclass sets the protected `EnableSystemSorting = false` property and installs an explicit ordered dispatch table at the assembly fence. This avoids pretending that static `UpdateBefore` attributes can express a newly mounted runtime stage graph. Unity ordering attributes only order siblings in an applicable group; the adapter verifies or rejects any imported constraints before flattening. [System ordering](https://docs.unity3d.com/Packages/com.unity.entities@1.4/manual/systems-update-order.html) and [group API](https://docs.unity3d.com/Packages/com.unity.entities@1.4/api/Unity.Entities.ComponentSystemGroup.html).

**Failure handling cannot use the stock group's update loop.** The exact 1.4.6 `ComponentSystemGroup.UpdateAllSystems` implementation catches system exceptions, logs them, and continues. `GameCoreStepGroup.OnUpdate` therefore overrides dispatch and does not call `base.OnUpdate()` for gameplay stages. Its generated entries invoke managed `SystemBase.Update()` or unmanaged `SystemHandle.Update(World.Unmanaged)` directly within a guarded host dispatcher, in the compiled order. In this pinned package, the latter is the public entry point; the underlying `UpdateSystem` implementation is internal. A caught exception sets the host fault latch and stops further dispatch; the host drains already scheduled work and refuses step publication. No imported nested group may hide exceptions behind its own continue-on-error loop. This exact-package source finding is recorded in the [evidence ledger](references/unity-evidence.md).

Burst jobs use validated inputs and explicit status output for expected failures; exception recovery inside Burst is not a supported gameplay control path. The host checks registered job status at the required consumption/commit boundary. Already submitted independent work may finish after a failure, but its uncommitted results cannot publish. Native crashes are outside recoverable in-process faults. An early Unity integration probe must throw from a managed stage after a write and prove that the next stage does not execute and no successful step/snapshot appears.

The host's outer guarded step boundary also covers dependency completion, ECB playback, drain validation, and snapshot construction, so a failure outside a system's `OnUpdate` cannot accidentally produce a success result. Notification delivery occurs only after the publication pointer has switched; a subscriber failure is a delivery/cleanup diagnostic and cannot roll back an already committed step.

One precompiled system type has one live scheduling instance per world in V1. A plugin can have many mounted scope instances; its system batches over ECS binding rows keyed by plugin-instance/scope identity and validated `PartitionId`. Protocol per-partition multiplicity maps to these logical binding/partition rows, not duplicate type-singleton lookups. A concrete system type cannot occupy two incompatible positions in one execution plan. A package needing different entry points provides distinct concrete system types. The generated registry maps stage implementation IDs to those types/factories; no runtime type emission is needed. A system stays allocated while another contribution references it; removal of the final binding removes it from the schedule and disposes it only after the fence. `OnCreate`/`OnDestroy` manage world-local technical resources, not per-scope gameplay semantics.

Every scheduled job returns a `JobHandle`. Each adapter system combines its incoming stage edges with `SystemState.Dependency` or `SystemBase.Dependency`, schedules, writes its resulting dependency back, and records its output handle in the stage table. Entities tracks component hazards for correctly declared accesses; external native queues, blob replacement, service-owned buffers, and manually scheduled jobs require explicit dependency registration. A required gameplay order is not inferred solely from Unity's race detector. [Unity job dependencies](https://docs.unity3d.com/6000.0/Documentation/Manual/job-system-job-dependencies.html).

Use generated stage indices and a host-owned native fence table accessible to the main-thread scheduling wrappers; Burst `ISystem.OnUpdate` must not resolve a managed dictionary or composition service to obtain its input fence. The table is reset at step admission and changed only by the serialized scheduler, not captured by running worker jobs. A skipped/disabled/query-empty system forwards the combined incoming fence and emits no new work. A scheduled system replaces its output with the combined incoming and produced handles. This prevents stale prior-step handles or an early-return path from dropping a producer dependency. Buffer owners separately retain their users' handles until disposal is safe.

Protocol partitions establish logical non-overlap, but Unity's component-type dependency tracking can still serialize jobs that touch the same component type in disjoint partitions. V1 accepts that conservative scheduling; it does not bypass container safety attributes merely to promise partition parallelism. Demonstrated overlap is required only for independently declared, Unity-safe data/resource access paths.

Disjoint authorized component writers may schedule in parallel. A graph edge normally creates a dependency, not a main-thread `Complete()` call. Main-thread engine calls complete only their required prerequisites. The output publication boundary completes the jobs necessary to expose the world's consistent committed view; the composition fence drains all world work that can touch replaced bindings, structures, systems, or allocations. V1 deliberately uses one whole-world fence for assembly publication, with no stop-the-world barrier between every pair of ordinary stages.

Use direct `ref` component writes for the registered owner updating an existing component within its stage. Use typed requests when another owner decides the change. Use `EntityCommandBuffer` for deferred structural changes, with one producer buffer or disjoint producer scheme and declared playback points. Producer completion precedes playback and disposal. Domain ordering keys come from the protocol/domain command order, not query chunk order or worker completion timing. ECB temporary entities cannot escape their originating buffer; stable object IDs are resolved to real entities after playback. [ECB semantics](https://docs.unity3d.com/Packages/com.unity.entities@1.4/manual/systems-entity-command-buffers.html).

An ECB is a mechanism for deferred writes. A domain transfer involving several entities still needs that domain's validation/resolver and committed-event policy. The adapter does not provide a universal speculative transaction or undo log.

## 5. Assembly publication and authoritative storage

The bridge maps a validated protocol change plan to component/buffer diffs, binding activation, resource leases, registered system membership, and an execution plan. It does not rebuild the world or traverse the full scope tree each frame.

Creating a Unity system in the live world may invoke `OnCreate` and mutate ECS storage. Consequently runtime additions create real systems only at the apply fence, after fallible pure validation/migration; their exceptions follow the postwrite fault rule. Preparation may allocate inert managed factory data, but may not call arbitrary live-world `CreateSystem` under the label “staging.” Initial world creation can build its systems before the world is exposed.

Preparation reads immutable composition/schema metadata and can proceed while the currently published simulation runs. The apply path checks the plan's expected `CompositionRevision`, fences jobs, revalidates handles and referenced assets, and computes state migrations from the now-stable current ECS state into staging. Staging is unobservable. Structural writes, binding changes, and schedule replacement happen with simulation and external observation blocked. The host then swaps its published assembly and snapshot together and advances the assembly epoch according to the protocol. Preparation never captures a mutable gameplay value and assumes it remains current until apply.

Use one preconstructed immutable `PublishedWorldView` reference holding revision/epoch, snapshot, binding/schedule references, and the active ingress-gate table. Validate/install gates closed and allocate the result record before the commit; a serialized nonthrowing `Volatile.Write` of this reference exposes the new view. Readers/gates capture one reference for their check. Do not swap several independently observable fields or invoke plugin callbacks between the epoch and gate changes. Subscriber notification delivery follows the commit and cannot roll it back.

Before the first live write, failure disposes staged resources and leaves the active assembly untouched. After the first live write, an unexpected exception faults the world, stops new simulation, and retains the previous immutable published snapshot for observation; a write being theoretically reversible does not relax this cutoff. The partial live world is not advertised as healthy and is not “rolled back by ECB.” Recovery follows [Lifecycle and recovery](06-lifecycle-and-recovery.md). The 1.4.6 ECB fix does not change this protocol rule.

Runtime state and derived data occupy distinct ECS components/buffers. Generated binding components identify the contribution, its provider, schema version, and active/dormant status. Removing one provider removes only its owned derived rows/components or recomputes the effective binding from remaining contributions. It does not destroy an entity simply because that entity benefited from the plugin. Retained state stays in ECS as dormant state excluded from active binding queries; it is not copied into a managed “model” that also becomes writable.

The stable-ID index stores only `TargetId`-to-`Entity` lookup information and `TargetHandle` generation metadata. Raw `Entity.Index`/`Entity.Version`, `SystemHandle`, `UnityEngine.Object` instance IDs, and native pointers are world/session-local handles; do not serialize them as domain IDs. Resolution validates world identity/generation, entity existence, and the protocol reference constraints. Do not cache an `Entity` through structural replacement without validation. A copied immutable observation snapshot is explicitly non-authoritative and cannot be a write path back into gameplay state.

## 6. Authoring, baking, and precompiled spawn recipes

Authoring definitions describe intent: stable definition/schema IDs, compatibility traits, tags, configuration defaults, and recipe references. `ScriptableObject` and authoring `MonoBehaviour` assets adapt this data for Unity. They are not the live state of a running match or chapter. Asset GUIDs identify authored assets; runtime instances receive distinct stable domain IDs.

Editor bakers convert referenced assets/prefabs into entity prefabs, immutable blobs, and typed recipe metadata. Record baker dependencies so changed assets rebake correctly. Baking is an Editor workflow; the player instantiates baked entities or invokes precompiled factories rather than invoking bakers at runtime. [Baking overview](https://docs.unity3d.com/Packages/com.unity.entities@1.4/manual/baking-overview.html).

A generated `SpawnRecipeId` entry contains its compatible schema/trait set, known archetype/prefab references, initializer, required build assets, and supported derived-binding installers. The automatic propagation resolver uses this reusable schema to determine eligibility for **all** instances; an individual child does not import each inherited capability. On spawn, create a pending entity, assign stable IDs and membership, resolve current inherited contributions, apply the selected recipe/bindings, then publish it as active. A pending entity cannot be observed or simulated in an incomplete assembly. A stale prepared spawn is revalidated against the current composition revision before activation.

Moving a plugin scope subtree updates composition membership and derived bindings; it does not reparent `LocalTransform` or GameObjects. An explicit presentation adapter may separately request a visual reparent. Runtime recipes cover object creation, destruction, binding installation, and component initialization for the closed set of built types. Unknown schemas/recipe IDs fail before activation with a diagnostic naming the missing catalog entry. Assets delivered later may contain new data for known schemas; new executable C# types require a new player build.

## 7. Presentation and engine adapters

| Adapter | Input to authoritative simulation | Output and authority |
| --- | --- | --- |
| Input | Host samples UI/device input; validates and stamps typed commands with source sequence/world identity | A sampled key press is not itself a committed game event. First slice uses test commands and ordinary Unity callbacks, so no Input System package pin is implied |
| GameObject/Transform | Explicit adapter commands only | GameObjects read committed ECS snapshots; a visual interpolation cache is disposable. Transform parentage and scope membership are independent |
| Animation | Optional marker/root-motion proposals copied into a typed input buffer | Animator state is presentation authority unless a gameplay plugin explicitly declares an external authority contract. Root-motion intent is consumed by the movement owner |
| Audio | None by default | Committed events trigger playback with stable event IDs for duplicate suppression; an audio failure does not undo simulation |
| Assets | Completed load results carrying WorldId, installation generation, ActivationEpoch, and operation/work identity | Leases keep assets alive through their last consumer job/frame; late results are discarded/released after teardown or reconfiguration. Asset loading does not activate a half-built recipe |
| Physics | See the single-owner rule below | Physics is an optional plugin stage/adapter, never a kernel-mandated phase |

The primary [action fixture](07-reference-compositions.md) owns kinematic pose/velocity in ECS and uses data-defined sensors; it needs no rigidbody simulation. For the separate optional rigidbody mode, select Unity built-in 3D physics as the physical solver. The physics adapter owns a dedicated local `PhysicsScene`. The host calls that scene's `PhysicsScene.Simulate(fixedDelta)` exactly once per admitted action step at the plugin's declared stage. It does not also auto-simulate those bodies in the default scene. It applies validated intents before simulation, then copies pose/velocity/contact results into externally owned ECS observation components before downstream gameplay runs. Unity's explicit simulation API makes this scheduling possible. [PhysicsScene simulation API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PhysicsScene.Simulate.html).

For those bodies, Unity Rigidbody/physics scene state is authoritative for physical pose/velocity. ECS owns gameplay state and records the last synchronized physical observation; gameplay systems may not independently integrate or overwrite the same pose. Teleports/impulses are commands to the physics adapter. The domain schema declares that external-authority exception and synchronization step. For ECS-owned kinematic objects, ECS owns the pose and the adapter uses kinematic presentation/physics bodies instead. Authority mode is fixed per body recipe and changes only through a fenced migration. This prevents two writable copies disguised as “synchronization.” Cross-platform deterministic physics is not promised.

GameObject callbacks may enqueue work for the next admitted step, not mutate ECS in the middle of one. Presentation work never causes a second authoritative simulation update. Headless compositions omit presentation services and use fake asset/input adapters; gameplay cannot depend on an audio device or a renderer to progress.

## 8. Dynamic mounting in IL2CPP players

“Dynamic plugin” means mounting, configuring, suspending, and unmounting **precompiled** plugin implementations already included in the player. It does not mean loading arbitrary new managed assemblies, JIT compilation, reflection-based dependency injection, or compiling types from downloaded manifests. IL2CPP's AOT restrictions require the set of executable types and supported generic instantiations to be known at build time. [Scripting restrictions](https://docs.unity3d.com/6000.0/Documentation/Manual/scripting-restrictions.html).

The Editor generator validates manifests and emits ordinary C# source before player compilation:

1. A registry mapping stable plugin/service/stage/schema/recipe IDs to direct constructors and typed installers.
2. Direct references for all runtime component/buffer/system types, serializers, migrations, and plugin factories.
3. Closed generic job/serializer/service usages required by every build-included plugin, including plugins initially unmounted in the startup scene.
4. Preservation metadata (`link.xml` or applicable `[Preserve]`) for entry points reached by Unity callbacks or native code, and a registry/catalog hash included in build diagnostics.
5. Build failures for missing stage types, schema installers, assets, duplicate IDs, unsupported generic closure, or unresolved dependencies.

Prefer non-generic concrete job wrappers for plugin entry points. If a supported generic job is necessary, emit its closed instantiation and required `RegisterGenericJobType` registration; never assume a generic helper called only through reflection will make it into Burst AOT output. Linker preservation keeps code from being stripped but does not create every required native generic specialization. [Burst generic jobs](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/compilation-generic-jobs.html) and [Unity linker preservation](https://docs.unity3d.com/6000.0/Documentation/Manual/managed-code-stripping-preserving.html).

The release-player probe starts with a plugin absent from the initial composition, mounts it by ID, runs a generated component/job/serializer path, reconfigures, unmounts, then mounts a second instance. This catches a registry that works in the Editor but loses rarely referenced code in IL2CPP. The generator is an ordinary build step producing inspectable sources; V1 does not need a runtime compiler or a custom universal ECS interface.

## 9. Domain reload, Play Mode, and disposal

Treat every Play Mode session as a new host/world generation. Domain reload disabled does not reset static fields or event subscriptions automatically. Install an idempotent `RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)` reset path and Editor-only reload/Play Mode hooks. Reset the host registry, remove stale application loop nodes, dispose surviving hosts if any, clear static delegates, and invalidate cached runtime handles. Do not reset immutable generated catalogs by mutating their content. [Unity domain reload behavior](https://docs.unity3d.com/6000.0/Documentation/Manual/domain-reloading.html).

Normal disposal stops routing new callbacks/commands, cancels managed operations, removes update routes, drains every registered world job, then retires systems and leases in reverse dependency/acquisition order. A system's `OnDestroy` runs while the dependencies that its registered destructor consumes are still alive; resource-ledger edges govern that ordering rather than a blanket “free every lease first” pass. Dispose the world after its registered consumers retire, then release remaining host-owned allocations and identity indexes. A Unity-native allocation cannot be freed merely because a cancellation token was set. Late callbacks compare WorldId, installation generation, ActivationEpoch and operation/work identity, and release their result without entering a retired activation. Shutdown hooks can arrive more than once; the protocol disposal operation and the adapter implementation remain idempotent.

Editor compilation/reload terminates the running session in V1. It does not transparently migrate live arbitrary managed stack frames. A deliberate saved checkpoint can start a new session via schema migration. Run enter/exit Play Mode loops with domain reload both enabled and disabled, and with scene reload disabled, checking one live host, one update route, and zero retained native allocations/subscriptions after exit.

## 10. Qualification tests and required artifacts

The full test catalog and task ownership are in [Validation and performance](08-validation-and-performance.md) and the [Implementation guide](09-implementation-guide.md). This adapter requires these observable checks:

| Layer | Required proof |
| --- | --- |
| Pure rules/composition | EditMode tests without `World`, GameObjects, or PlayerLoop validate resolution, automatic descendants, precedence, mode switching, and schedule compilation |
| Isolated Unity worlds | Tests create/drive/dispose a world explicitly; test Jobs dependencies, structural playback, stable-ID rebinding, dormant state, and output consistency |
| PlayMode | Count requested steps versus actual system executions; idle command world executes zero steps; fixed world executes exactly admitted steps; inspect loop for duplicate routes |
| Lifecycle faults | A job remains in flight while unmount is requested; resources live until completion. A thrown apply operation faults the world and preserves only the last published snapshot |
| Editor lifecycle | Repeated enter/exit with domain and scene reload combinations produces no duplicate hosts, subscriptions, or stale callbacks |
| Standalone IL2CPP | The x86_64 Linux executable launches headless and completes precompiled late-mount, generic-job, serialized-save, reconfiguration, reparent, unload, and disposal probes with High stripping |
| Burst | Build logs/artifacts show the intended jobs compiled natively for x86_64; a passing player build with Burst silently disabled does not pass this gate |
| Physics/presentation | Dedicated physics scene advances once per declared step; ECS physical observation agrees at its synchronization boundary; headless card/narrative fixtures do not require it |

Use Unity Test Framework's batch CLI for EditMode/PlayMode tests with `-batchmode -nographics -runTests -testPlatform ... -testResults ... -logFile ...`. Do not add `-quit` to a test-run command that relies on the runner to finish asynchronously. Build the standalone probe through a checked-in `-executeMethod` build entry point and launch the resulting application with the probe arguments; “Editor tests pass” is not a player result. A graphical PlayMode test that needs rendering belongs in a separate graphics-enabled job. [Test runner CLI](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html).

Archive the exact Editor revision, host/compiler versions, manifest, resolved lock, generated catalog hash, source revision, test XML, Editor/build logs, player probe result, Burst compilation evidence, and leak counters. The first wave is blocked from claiming backend viability until the minimal late-mount IL2CPP probe passes; broader platform qualification follows the same procedure when product platforms are selected.
