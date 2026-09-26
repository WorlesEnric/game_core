# GC-020 HANDOFF — the real-time action reference and optional engine stages (Wave 6)

**Status of every build/test command in this document: `NotRun (pending orchestrator build host)`.** This authoring
host has no .NET SDK, no C# compiler, no Mono and no Unity, so nothing in this change set has been compiled,
imported, executed or built here. What did run is interpreter-level only and is recorded verbatim in
`artifacts/gc-020/static-checks.log`: the host-side C# checker, the documentation validator (self-test + full), the
Unity `.meta` coverage and GUID-uniqueness scan, `bash -n` over the two new shell scripts, a JSON validation of the
inventory, an independent recomputation of the course's digest literal (with the rule cross-checked against the
committed GC-019 narrative literal), and the meta generator's idempotence. None of those is a build or a test
result.

## 1. Summary

GC-020 implements the real-time action/traversal reference composition of `07 §4` on the existing kernel, with the
optional engine stages kept behind ports whose committed-output logic is provable without a device:

* **The exact traversal reference.** `Packages/com.gamecore.gameplay.traversal` declares 07 §4.2's five-stage graph
  (`traversal.input → integrate → sense → checkpoints → output`, plus the separate declared
  `integrate → output` edge), one owner per written domain over six declared state slots, one inherited `Additive`
  `traversal.acceleration` slot, data-defined checkpoint volumes, deduplicated ordered run progress through the pure
  course rules, a bounded per-step crossing write set with one committed event per accepted crossing, a committed
  step snapshot, a declared motion-authority table, and a bounded observation trace.
* **Rule repeatability is separated from native physics.** The pure-motion fixture is exact integer arithmetic
  (millimetres and thousandths), and 07 §4.3's `1.00 → 1.04 → 1.02` m/s assertion is a *rule*
  (`TraversalMotionRules`) rather than a test constant. The trace comparison compares byte-exactly at tolerance zero
  for the pure fixture and under a declared tolerance for a recorded engine observation, so the two claims are never
  conflated (P-008, TEST-022).
* **The optional rigidbody mode is a single declared authority.** A recipe's motion authority is declared
  (ECS-owned kinematic by default); a conflicting selection is refused as `OwnershipConflict`, the integrator skips
  and counts an externally owned pose rather than writing it, and the real `UnityPhysicsSceneBackend` owns a
  dedicated local the scene 04 §7 names and calls `PhysicsScene.Simulate(fixedDelta)` from a gate that refuses a
  repeated or regressing admitted step. 30/60/144 Hz presentation therefore cannot double-advance authority.
* **Committed animation and audio output.** `CommittedAnimationStage` presents one committed pose image per strictly
  newer token and queues bounded root-motion proposals for a *later* admitted step; `CommittedAudioStage` consumes
  the world's own committed event store, deduplicates by the event identity and never unwinds simulation on a sink
  failure. Both are engine-free stages behind ports, which is why the crash-139 constraint is honoured rather than
  worked around: the headless player runs with Unity audio **disabled** and the gate drives a recording sink.
* **Cards and narrative still contain no action phase.** The gate's own observation walks both families'
  declarations and the compiled descriptors looking for any traversal stage id, system key, buffer id or the
  acceleration capability, and reports `offenders=<none>`.
* **The qualification gate.** `Gc020Family` / `Gc020TraversalHost` / `Gc020Scenario` mirror the GC-019
  one-runner/two-adapters shape (here: one genre, one catalog — see §7.1), with fourteen named observations, an
  EditMode suite that recomputes the digest from the observation table, and a `-probeTraversal` player mode.

## 2. Commits on this branch (all on `gc-020`, none pushed)

| Commit | Contents |
|---|---|
| `6b32860` | The pure traversal rules package, the three engine-stage seams and their plain-dotnet suite |
| `c30f5cc` | The traversal gameplay package, its fixture half, the Unity adapter halves and the qualification wiring |
| `513c454` | The gate (family, host, scenario), the probe mode, the two scripts; the rules-layer split |
| `a0818d1` | The defects four independent read-only audits found |
| `1033ebc` | The gate audit's defects, including a mistranscribed digest literal |
| `e613bd1` | The inventory proposals (nothing promoted) |
| `b12828a` | The second audit round's blockers, the stronger jump assertion and the static-checks log |

## 3. Files created

### The rules package (Unity-free; compiled by Unity **and** by `dotnet/src/GameCore.Rules.Traversal`)

| File | Contents |
|---|---|
| `Packages/com.gamecore.rules.traversal/Runtime/TraversalIdentity.cs` | The stable-name derivation wrappers (P-004, 05 §3) |
| `.../TraversalVocabulary.cs` | The course's stable names, the declared numeric representation and the reference's values (20 ms step, 1000/1040/1020 milli-units) |
| `.../TraversalMotionRules.cs` | `TraversalVector3i` and the exact integer one-step kinematics, including the declared gravity/jump/ground policy |
| `.../TraversalCourseRules.cs` | `CheckpointVerdict`, `CheckpointObservation`, `CheckpointCourse`, `RunProgress` — the dedup/order rules of 07 §4.2 and REF-A04 |
| `.../TraversalAcceleration.cs` | The acceleration slot's canonical int32 codec, the registered `Additive` reducer and the always-accepting predicate |
| `.../GameCore.Rules.Traversal.asmdef`, `Tests/GameCore.Rules.Traversal.Tests.asmdef`, `package.json` | Package/assembly declarations |
| `.../Tests/TraversalRulesTests.cs` | Three fixtures: the reference velocity sequence, the course rules, the payload/reducer |

### The gameplay package (Unity; `Runtime/` + `Fixtures/Runtime/` asmdefs)

| File | Contents |
|---|---|
| `Packages/com.gamecore.gameplay.traversal/Runtime/TraversalKeys.cs` | Every stable identity, domain, slot, layout, field, buffer, route, recipe and declared bound |
| `.../TraversalComponents.cs` | The blittable components/buffers (`TraversalPose`, `TraversalVelocity`, `TraversalJumpState`, `TraversalMovementInput`, `TraversalCheckpointVolume`, the observation/progress/crossing rows, `TraversalCourseSnapshot`) and the command/crossing codec |
| `.../TraversalDeclarations.cs` | The five `StageSpec`s, six `StateSlotSpec`s, the capability contract and rule, the declared step buffer |
| `.../TraversalSystems.cs` | `TraversalModule`, `TraversalAccess` and the five `[DisableAutoCreation]` systems |
| `.../TraversalTrace.cs` | `TraversalBodyTrace`/`TraversalCrossingTrace`/`TraversalStepTrace`, the bounded recorder with `CompareTo`, and `TraversalStepTraceRegistry` |
| `.../TraversalComposition.cs` | `TraversalDerivationValueSource` — the registered reducer/predicate bound to the derivation engine |
| `.../TraversalRegistration.cs` | `Messages()`, `Readers()`, `Systems()`, `DispatchKinds()`, `Create()` and `FixedStepRequest()` |
| `.../TraversalPayloads.cs` | The O-02/O-03/O-07/O-08 mount payloads |
| `.../Fixtures/Runtime/TraversalCatalogTable.cs` | The hand-written generated-style catalog table, with `DerivationHolds()` |
| `.../Fixtures/Runtime/TraversalCourseWorld.cs` | The composition tree, manifests, recipes, appliers, and the course target identity |

### The engine-stage seams and Unity halves

| File | Contents |
|---|---|
| `Packages/com.gamecore.unity.adapters/Runtime/Pure/Physics/PhysicsAuthority.cs` | `IPhysicsSceneBackend`, `PhysicsBodyKey`/`PhysicsPose`/`PhysicsVector3i`, `PhysicsAuthorityGate` (the once-per-admitted-step gate) and `PhysicsIntentCodec` |
| `.../Runtime/Pure/Audio/CommittedAudio.cs` | `IAudioOutputSink`, `AudioCueTable`, `CommittedAudioStage`, `RecordingAudioSink` |
| `.../Runtime/Pure/Animation/CommittedAnimation.cs` | `ICommittedPoseSource`, `IAnimationPresentationSink`, `CommittedAnimationStage`, `RecordingPoseSource`, `RecordingAnimationSink` |
| `.../Runtime/Physics/UnityPhysicsSceneBackend.cs` | The real local `PhysicsScene`: create, add/remove bodies, apply intents, one `Simulate`, and restore the process's automatic-simulation mode on dispose |
| `.../Runtime/Presentation/UnityOutputSinks.cs` | `UnityAnimationPresentationSink` and `UnityAudioOutputSink` — the engine halves an audio/graphics-enabled application installs, never the headless player |
| `dotnet/tests/GameCore.Adapters.Tests/EngineStageTests.cs` | The plain-dotnet suite for the once-per-step gate, the disabled-device audio path and the bounded proposal queue |

### The qualification gate

| File | Contents |
|---|---|
| `unity/.../Runtime/Gc020Family.cs` | `Gc020Step`, `Gc020ScenarioResult`, `TraversalCourseSurface`, `Gc020StageRuntime`, `IGc020Family : IGc013Family` |
| `unity/.../Runtime/Gc020TraversalHost.cs` | `Gc020TraversalHost` and the nested `CourseFamily` |
| `unity/.../Runtime/Gc020Scenario.cs` | The 14-observation runner and its `Executor` |
| `unity/.../Runtime/ProbeTraversal.cs` | The `-probeTraversal` player mode and the digest literal |
| `unity/.../Tests/Gc020/GameCore.Gc020.Tests.asmdef` + `Gc020IntegrationTests.cs` | The EditMode suite: one `[Test]` per observation, the recomputed-table assertion, and the registered derivation seam |
| `tools/unity/run_traversal_probe.sh`, `tools/run_gc020_gate.sh` | The probe harness and the gate script |
| `artifacts/gc-020/static-checks.log` | The verbatim host-side checks (not a build, not a test) |

### Modified

| File | Change | Why it is safe |
|---|---|---|
| `dotnet/GameCore.sln` | Two additive project entries and their configuration rows | The traversal rules projects are Unity-free, like the other two rules packages; GUIDs checked unique (27 projects, 27 unique) |
| `dotnet/README.md` | Two rows plus a paragraph | The README enumerates every plain-dotnet project |
| `tools/check_game_core_csharp.py` | Additive `TARGETS` entries and an additive `engine_free` entry | The host-side checker had no coverage of the new packages |
| `unity/.../Runtime/ProbeArguments.cs` | One constant, one ctor parameter, one property, one `IsProbeInvocation` term, one `Parse` local and branch, one ctor argument | The additive set every earlier task added for its own mode |
| `unity/.../Runtime/ProbeRunner.cs` | One dispatch branch and one identity branch | Same |
| `unity/.../Runtime/GameCore.Validation.ProbeHost.asmdef` | Four additive reference lines | The gate uses the two traversal packages and their fixtures |
| `unity/GameCore.Validation/Packages/manifest.json` | Three additive dependency lines and two `testables` entries | The two new packages must resolve; the rules package's own tests must run |
| `artifacts/gates/w4-generic-profile/inventory.{md,json}` | A `gc020Revisions` / "GC-020 revision notes" section: **proposals only** | No row promoted: a row may only be promoted from an archived passing run |

## 4. Contract changes

**None.** No file under `Packages/com.gamecore.contracts/` and no plan DTO was modified. Every new public type is
additive and lives in the two new packages or in three new `Runtime/Pure` folders plus two new Unity folders of
`Packages/com.gamecore.unity.adapters`. Two *existing* files changed behaviourally only by becoming reachable from a
new mode: `ProbeArguments`/`ProbeRunner` keep every earlier mode's branch and task id unchanged.

## 5. Requirement → implementation → observation mapping

| Requirement / test | Where implemented | Where observed (this change set) |
|---|---|---|
| P-001 genre independence | the traversal package declares no kernel schema: its stages, owners and capability are package-owned | `gc020-cards-and-narrative-declare-no-action-phase` walks both other families' declarations and compiled descriptors for any traversal stage/system/buffer/capability |
| P-002 participants and authority | one world host per course; the movement command port; the compiled five-stage graph; the physics adapter as `EngineAdapter` | every observation; `gc020-one-simulation-per-admitted-step` |
| P-034 one owner per domain; engine-owned state as stamped observation | `TraversalModule.TrySelectMotionAuthority`, `MotionDecisionOf`, the integrator's skip-and-count, `PhysicsAuthorityGate` | `gc020-externally-owned-pose-is-not-integrated`, `gc020-one-simulation-per-admitted-step` |
| P-035 world lifecycle | the course runs on `UnityWorldHost` and stops through `Stop`/`Dispose` | `gc020-teardown-settles-and-disposes` |
| P-036 temporal models, bounded catch-up, retained debt | `TraversalRegistration.FixedStepRequest`, `TraversalKeys.StepDurationTicks`/`MaxStepsPerPump` | `gc020-fixed-step-world-and-runners` (idle = zero steps), `gc020-presentation-rate-does-not-double-advance` |
| P-038 clocks | the integration reads the world's own step clock; the trace stamps the logical step | `gc020-one-admitted-step-integrates-once` |
| P-039 stage registration | `TraversalDeclarations.Stages()`: five `StageSpec`s, system keys, access sets, affinity | `gc020-fixed-step-world-and-runners` (declared vs registered stage/system counts) |
| P-040 execution plan | both 07 §4.2 edges declared `requiredAfter`; `TraversalRegistration.DispatchKinds()` | `gc020-fixed-step-world-and-runners` |
| P-041 concurrency and structural work | direct owned component writes; the declared `sense → checkpoints` step buffer | `gc020-one-admitted-step-integrates-once` |
| P-044 commit and bounded write sets | the crossing capacity is a refusal *before* any write; per-crossing events staged for the step's commit | `gc020-reparent-changes-the-contribution-and-keeps-state`, `gc020-committed-animation-and-audio-output` |
| P-045 immutable observation, external output | `CommittedAudioStage` (event-identity dedup), `CommittedAnimationStage` (strictly newer token) | `gc020-committed-animation-and-audio-output` |
| P-056 extension points | a registered reducer/predicate pair, an external-authority descriptor, an audio/animation sink port | the EditMode suite's `TheRegisteredDerivationSeamResolvesReducesAndRejects`, plus the audio/cue-table assertions |
| P-058 V1 profile | the new packages compile under the pinned Editor/package set; no pin changed | `tools/unity/build_probe.sh` in the gate |
| P-059 genre validation before freeze | the third family exists with its own domain policies; the no-action-phase audit | `gc020-cards-and-narrative-declare-no-action-phase`, the mode-switch and future-descendant observations |
| TEST-011 temporal models and idle worlds | the fixed-step course plus the idle-step assertion | `gc020-fixed-step-world-and-runners` |
| TEST-012 execution graphs and job synchronisation | the declared DAG and the drain check at commit | `gc020-fixed-step-world-and-runners`, `gc020-one-admitted-step-integrates-once` |
| TEST-013 authority, direct writes, requests, buffers | three owners over disjoint domains; the bounded movement lane | every observation; `gc020-one-simulation-per-admitted-step` |
| TEST-018 Unity worlds, bootstrap and Play Mode | the gate runs in a real world and a real EditMode assembly, and the mode runs in the player | `-probeTraversal`, `GameCore.Gc020.Tests` |
| TEST-019 engine adapters and single state authority | the external-authority declaration, the intent path, the refusal of a gameplay write to an owned pose | `gc020-externally-owned-pose-is-not-integrated` |
| TEST-021 genre neutrality and cross-template conformance | the card/narrative clause; the cross-family composition is GC-024's | `gc020-cards-and-narrative-declare-no-action-phase` |
| REF-A01/A02 | `gc020-one-admitted-step-integrates-once`, `gc020-reparent-changes-the-contribution-and-keeps-state` (1040 → 1020, publication moves nothing) |
| REF-A04 | `gc020-committed-animation-and-audio-output`, `gc020-replay-separates-pure-motion-from-engine-observation` (duplicate/stale diagnostics) |
| REF-A05 | `gc020-externally-owned-pose-is-not-integrated` (refusal, no double simulation, no write-order workaround) |
| REF-A06 | `gc020-presentation-rate-does-not-double-advance`, `gc020-one-simulation-per-admitted-step` |

## 6. Exact commands for the Linux build host

Run from the repository root. Nothing below has been run.

### 6.1 The whole gate (one command)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet \
  PROBE_RUNS=5 tools/run_gc020_gate.sh
```

In order: the host-side C# checker; `dotnet build` + `dotnet test dotnet/GameCore.sln -c Release` (trx into
`artifacts/gc-020/trx`); the Unity resolve; EditMode (every testable package plus `GameCore.Gc020.Tests`); PlayMode;
`tools/unity/build_probe.sh` (StandaloneLinux64 IL2CPP, High stripping) with the three committed catalogs'
byte-identity check; `-probeTraversal` and then the card/narrative probes **and** every remaining mode, each
`PROBE_RUNS` times; the release-surface check; the documentation validator. Every Unity invocation is wrapped in
`timeout` with one logged retry on a timeout.

### 6.2 The traversal half alone

```sh
DOTNET=$HOME/.dotnet/dotnet
"$DOTNET" build dotnet/src/GameCore.Rules.Traversal/GameCore.Rules.Traversal.csproj -c Release
"$DOTNET" test  dotnet/tests/GameCore.Rules.Traversal.Tests/GameCore.Rules.Traversal.Tests.csproj -c Release \
  --logger trx --results-directory artifacts/gc-020/trx
"$DOTNET" test  dotnet/tests/GameCore.Adapters.Tests/GameCore.Adapters.Tests.csproj -c Release \
  --filter "FullyQualifiedName~EngineStageTests" --logger trx --results-directory artifacts/gc-020/trx
```

The three new fixtures are `TraversalMotionRulesTests`, `TraversalCourseRulesTests`, `TraversalAccelerationTests`
(the first project) and `PhysicsAuthorityGateTests`, `CommittedAudioStageTests`, `CommittedAnimationStageTests`
(second project).

### 6.3 The Unity halves

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics \
  -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testFilter GameCore.Rules.Traversal.Tests \
  -testResults "$PWD/artifacts/gc-020/unity/rules-traversal-editmode.xml" \
  -logFile artifacts/gc-020/unity/rules-traversal-editmode.log

timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics \
  -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testFilter GameCore.Gc020.Tests \
  -testResults "$PWD/artifacts/gc-020/unity/gc020-editmode.xml" \
  -logFile artifacts/gc-020/unity/gc020-editmode.log
```

Do not add `-quit` to a `-runTests` command (04 §10). `-testResults` with a relative path resolves against the Unity
**project** path, unlike `-logFile`, so pass an absolute path or copy the file out afterwards.

### 6.4 The player probe (the headless half)

```sh
UNITY="$UNITY" UNITY_PROJECT=unity/GameCore.Validation ARTIFACTS=artifacts/gc-020/toolchain \
  tools/unity/build_probe.sh
PROBE_RUNS=5 UNITY_PROJECT=unity/GameCore.Validation ARTIFACTS=artifacts/gc-020/toolchain \
  tools/unity/run_traversal_probe.sh
```

The player is built and launched headless with Unity audio **disabled** (crash-139). The probe's audio observation
uses the engine-free recording sink; the physics observation uses the real local `PhysicsScene`, which needs neither
a renderer nor an audio device.

### 6.5 The host-side checks (what already ran here)

```sh
python3 tools/check_game_core_csharp.py
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
python3 tools/make_unity_metas.py     # idempotent: reports 0 creations on a complete tree
bash -n tools/run_gc020_gate.sh tools/unity/run_traversal_probe.sh
```

## 7. Known gaps, assumptions and doc ambiguities

1. **Nothing has been compiled or executed here.** The build host is the first real gate. The highest-risk items, in
   the order a compiler would find them: (a) `Gc020Scenario.cs` is ~2 800 lines of hand-written scenario code and
   `TraversalSystems.cs` ~1 300, both audited in full by five independent read-only passes (§9) but only a compiler
   settles them; (b) `UnityPhysicsSceneBackend` and `UnityOutputSinks` are the only files that touch
   `UnityEngine` and have never been imported by the Editor; (c) two of the audits' findings were mistakes in *my*
   transcription (a 63-character digest literal) rather than in the design, which is a reminder that these passes are
   not a compiler.
2. **One catalog, not two.** No committed generated traversal catalog exists on this revision — its emission is
   GC-025's catalog-coverage work — so `Gc020TraversalHost.GeneratedCatalogPresent` is `false` and `RunBoth`
   answers both out-parameters with the fixture catalog's single run. The file header, the `RunBoth` doc comment,
   the EditMode suite and `run_traversal_probe.sh` all state that rather than implying a second catalog ran. When
   GC-025 adds the generated catalog, `RunGeneratedCatalog` and the two-catalog digest step are the additions.
3. **The rigidbody mode is proved with an Editor-qualification world, not a rigidbody game.** The declared
   authority, the intent path, the refusal of a gameplay write, one `Simulate` per admitted step and the
   presentation-rate invariance are all asserted; what is *not* claimed is a production rigidbody fixture with real
   colliders and contacts.
4. **The audio stage is engine-free by design.** The headless player cannot re-enable audio (crash-139), so what an
   audio-enabled player needs is documented in `UnityOutputSinks.cs`'s header and the sink is never constructed by
   the headless gate. The committed-output logic — committed-events-only reads, event-identity dedup, retryability
   after a device refusal — is proved with the recording sink in both the plain-dotnet suite and observation 12.
5. **`TraversalModule` is bound from the gate, not by the kernel.** The module classifies each live target by its
   registered recipe (course target → `BindCourse`, runner recipes → `BindRunner`, the checkpoint recipe →
   `BindVolume`). A production hook ("bind every live target") would remove that classification from the gate; no
   such kernel API exists today. Recorded as a gap, not a defect.
6. **Instance identity is keyed by the low half of the session id.** `TraversalStepTraceRegistry` and
   `TraversalModule`'s per-target lookups key on `Id128.Low`. Two worlds whose session ids shared a low half would
   collide; the deterministic session sequences make that unreachable in the fixtures, and a full-`Id128` key would
   be the fix if a future scenario minted ids differently.
7. **Physics observations are compared under a declared tolerance, never bitwise.** `TraversalTraceRecorder`
   compares exactly at tolerance zero for the pure integer fixture and with `TraversalVocabulary.
   VelocityToleranceMilli` for a recorded engine observation. The gate asserts both halves (one must mismatch at
   zero and match at the declared tolerance), which is what "separates rule repeatability from native physics"
   means here.
8. **The physics gate is a caller obligation, not a kernel hook.** `PhysicsAuthorityGate.TrySimulateExactlyOnce`
   must be called once per admitted step *at the plugin's declared stage* (04 §7). No kernel file was touched for
   this: the gate scenario discharges the obligation explicitly, and the same gate refuses a duplicate or regressing
   step from any caller. A production game would call it from its own declared physics stage.
9. **Doc ambiguity: `IGc013Family.SetupEdits` is consulted by `Gc013Scenario` but never by `Gc019Scenario`.** For a
   genre whose root *is* the world definition's declared scope tree the two readings conflict. Resolved by carrying
   the course tree in `CompositionLaneSeed.InitialAssembly.WithScopes(...)` and returning `SetupEdits` for interface
   parity only; both the file header and the property's doc comment say so. A reviewer can overrule this cheaply.
10. **Doc ambiguity: the fixture declares the course *target* but no recipe for it.** `TraversalCourseWorld.cs`
    declares `TraversalCourseTargets.Course` and `TraversalAccess.InstallCourseStorage` exists to install it, but the
    fixture half declares no `SpawnRecipe` for it, so the gate supplies that recipe in `Gc020Family.cs`. If the
    fixture should own it, moving one method closes the gap.
11. **`P-059` stays `Partial`.** This is the row GC-020 is named against, and the reason it is not promoted is
    simply that nothing has run: the action family's IL2CPP run and the cross-family composition are GC-024's.
12. **The two audits' non-blocking notes were addressed, not merely recorded:** the jump test now asserts
    `Grounded == 0` on the jump's own step, the registered derivation seam regained its miss/malformed coverage in
    the EditMode suite (it cannot live in the rules package, whose layer may not reference `GameCore.Derivation`),
    and the stale headers were corrected.

## 8. Inventory: proposals only

`artifacts/gates/w4-generic-profile/inventory.{md,json}` gains a `gc020Revisions` / "GC-020 revision notes" section
that **promotes nothing** — no row's status changes, because nothing in this change set has executed. It proposes a
status for each row GC-020 touches (`P-001`, `P-002`, `P-034`, `P-035`, `P-036`, `P-038`, `P-039`, `P-040`,
`P-041`, `P-044`, `P-045`, `P-056`, `P-058`, `P-059`) with the observation that would carry it and the artifacts the
build host must first produce, and records the carried gaps and `contractChanges: none`.

## 9. Read-only audits and the defects they found

Five independent read-only passes audited the change set (two on the rules package and its project files, one on the
gameplay runtime and fixtures, one on the engine stages and the fixture catalog, one on the gate and the shell
scripts, and a final fresh-eyes pass over the fix delta). Findings, all fixed before the final commit:

1. **Four compile blockers in the gameplay package:** an undeclared `TraversalKeys.HostIngressProducer`; the type
   name `TraversalStepTrace` declared twice in one namespace (the per-step record and the registry); the checkpoint
   stage reading a `Progress` member the ECS row never had; and `TraversalPayloads` using `TraversalVocabulary`
   without importing its namespace.
2. **Two compile blockers found by the delta pass:** `TraversalCourseRules` missing `using GameCore.Contracts` for
   `TargetId`, and `UnityPhysicsSceneBackend` missing `using GameCore.Unity.Adapters.Authority` for
   `AuthorityIntentKind`.
3. **A behavioural defect in the rules package:** the unconditional ground clamp erased the impulse of a jump the
   same step had just accepted, making the jump impulse, the `Jumped` flag and `Airborne` unreachable and failing the
   package's own assertions.
4. **A step that never committed:** the input stage drained the movement lane without releasing it, so any step that
   carried a movement command would have failed commit validation with `MissingDependency` and faulted the world
   (P-031). Fixed by releasing the lane at the end of its single consuming stage.
5. **A wrong digest literal:** the committed course digest was 63 characters — one nibble lost in transcription — so
   the frozen-table assertion and the probe's `expectedDigest` check could never hold. Recomputed and cross-checked
   against the committed GC-019 narrative literal.
6. **A build-blocking probe wiring defect:** `ProbeArguments.Parse` lost its `resultPath` local when the traversal
   flag was added.
7. **Three missing usings in the gate** (`GameCore.Execution.Time` for the clock types the scenario names,
   `GameCore.Unity.Adapters.Input` for the typed ingress, `GameCore.Validation.ProbeHost` in the EditMode suite) and
   one redundant one removed.
8. **Two engine-half defects:** `UnityAudioOutputSink` declared `IsAvailable` twice, and the local physics scene's
   bare `Physics.simulationMode` bound to the file's own namespace segment rather than `UnityEngine.Physics`
   (fixed with `global::UnityEngine.Physics`, the same workaround this repository already uses for
   `global::Unity.Jobs`).
9. **Four probe-harness clause fragments named strings the scenario cannot emit** (`extra144HzFrames` is the rate,
   `activeRowAfterUnmount` is `False` on success, `atTolerance0` is `False` where a mismatch is required, and the
   conflict is recorded inside `reselectRefused`, not as its own key).
10. **An architecture defect the audits surfaced:** the rules package referenced `GameCore.Derivation` for
    `IDerivationValueSource`, which 04 §2's rules layer may not reference. The source moved to the gameplay package,
    where the card package keeps its own.

The audits also confirmed: every literal `Id128` in the fixture catalog table reproduces the documented derivation
(verified independently in Python, with all eight committed card literals reproduced first); the digest rule
reproduces the committed GC-019 narrative literal exactly; both scripts use `probe_run_n`/`probe_require_steps` with
the right arity; and the `dotnet/GameCore.sln` entries carry unique GUIDs and complete configuration rows.
