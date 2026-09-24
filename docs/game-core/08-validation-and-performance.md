# Validation and performance

This document defines executable acceptance suites for the [core protocol](00-core-protocols.md), its [Unity mapping](04-unity-integration.md), and the [implementation waves](09-implementation-guide.md). Protocol behavior is normative in the protocol document; the fixtures and provisional performance budgets below specify how to gather evidence. A test plan is not a report of a passing implementation.

The current delivery is documentation and a documentation validator. No Unity project, IL2CPP player, gameplay test, resource stress run, or performance benchmark has been executed for this design. The implementation team must attach actual results to the task and wave that claim them.

## Evidence and test layers

Each run records the source revision, content/catalog hashes, exact Editor and package lock versions, OS and CPU architecture, machine model, worker count, scripting backend, stripping level, Burst configuration, safety-check settings, seed, and input/observation trace. Keep raw failures and measurements alongside the summary. A failing required test prevents its implementation task and dependent wave from passing; documentation completeness cannot waive a runtime failure.

| Layer | Executes | Can establish | Cannot establish |
| --- | --- | --- | --- |
| Pure contract/rule tests | Engine-independent contracts, derivation, schedule validation, game rule functions | Stable policy results, identities, service resolution, graph validity, serialization migrations | Unity queries, system updates, native lifetime, PlayerLoop integration, AOT compatibility |
| Unity world tests | Actual Entities worlds, system groups, jobs and structural playback | Component authority, scheduling, mapping, world separation, safe assembly boundaries | Shipping-player reachability or target-platform performance |
| Standalone player tests | Built executable on the recorded target | IL2CPP code generation, stripping, precompiled dynamic mounting, restart and headless paths | Other targets that were not built and run |
| Stress/fault/performance tests | Instrumented generated worlds and lifecycle injection | Bounded work, failure behavior, leak trends, baseline costs | Universally safe load limits or cross-platform deterministic physics |
| Documentation checks | Local Markdown and traceability registry | Resolvable links, ID definitions, task graph and wave consistency | Protocol implementation or external-source correctness |

Use small pure-rule tests when possible, but retain Unity-world tests for every contract that depends on actual entity handles, jobs, structural changes or adapters. A custom dictionary pretending to be an ECS is not an integration fixture.

## Acceptance suites

The stable identifiers below name suites, not claims that code already exists. Within a suite, use descriptive test names and parameterized cases. The [traceability registry](traceability.json) maps normative requirements to these suites and to implementation tasks.

<a id="test-001"></a>
### TEST-001 — Toolchain, generated registration and IL2CPP

Protocol coverage: [P-009](00-core-protocols.md#p-009), [P-054](00-core-protocols.md#p-054), [P-055](00-core-protocols.md#p-055), [P-058](00-core-protocols.md#p-058), [P-060](00-core-protocols.md#p-060).

Create the exact project and package lock specified by the [baseline](04-unity-integration.md). Build and run a minimal standalone IL2CPP executable before broad framework implementation. Execute a generated component registration, a closed generic command handler, an entity query, a Burst job and one serialization round trip; capture the process result and structured assertion output. Mount a plugin that is linked into the executable but not activated by the startup scene, then invoke its generated handlers. Repeat with the intended release stripping configuration and Burst enabled.

Acceptance: every expected generated catalog entry and generic instantiation executes successfully in the player; the package resolver produces the committed lock; no required type is rescued only by an Editor reference or an accidental scene dependency. A compile-only build does not pass. Run the baseline desktop target early; record untested product platforms explicitly and gate their support on equivalent player evidence. Run a deliberately omitted registration fixture that fails validation with the missing stable ID rather than silently selecting a fallback. Source generation output must be reproducible from the same input catalog.

<a id="test-002"></a>
### TEST-002 — Identities, epochs and stale references

Protocol coverage: [P-004](00-core-protocols.md#p-004), [P-005](00-core-protocols.md#p-005), [P-006](00-core-protocols.md#p-006), [P-007](00-core-protocols.md#p-007), [P-050](00-core-protocols.md#p-050).

Create two worlds with colliding local entity indices, retire/recreate scopes and plugin instances, and reuse native entity storage. Exercise every public handle after its target is retired and after a world restart. Attempt duplicate operation IDs with the same payload and with a different payload. Deliberately supply old expected epochs, binding epochs and stale asynchronous completions.

Acceptance: identity checks distinguish world incarnation, target generation, content revision and assembly epoch; stale work never acquires authority in a new incarnation. Idempotent retry follows the original observable outcome without duplicating effects; conflicting reuse is rejected. Counter exhaustion is tested by a small-width test implementation or boundary fixture and fails before an identifier wraps. Persistence contains stable identifiers rather than native ECS handles.

<a id="test-003"></a>
### TEST-003 — Manifests and service resolution

Protocol coverage: [P-009](00-core-protocols.md#p-009), [P-011](00-core-protocols.md#p-011), [P-012](00-core-protocols.md#p-012), [P-055](00-core-protocols.md#p-055).

Use required, optional and multi-binding contracts across a root, two sibling scopes and one isolated descendant. Vary installation order while keeping stable IDs constant. Include missing providers, incompatible contract versions, duplicate single-binding providers, explicit provider selection, dependency cycles, invalid configuration and a required provider replacement during use.

Acceptance: invalid manifests have structured diagnostics before publication; a valid mount whose required provider is absent publishes a waiting instance with no active contribution. Visibility and provider choice follow the [service contract](00-core-protocols.md), independent of registration timing. Optional absence is explicit. Rebinding invalidates old bindings and fences consumers before releasing old leases; a capability match alone does not grant service visibility. Service resolution is exercised separately from target capability derivation.

<a id="test-004"></a>
### TEST-004 — Automatic propagation to existing and future descendants

Protocol coverage: [P-001](00-core-protocols.md#p-001), [P-010](00-core-protocols.md#p-010), [P-013](00-core-protocols.md#p-013), [P-015](00-core-protocols.md#p-015), [P-024](00-core-protocols.md#p-024), [P-026](00-core-protocols.md#p-026).

Mount one narrative capability at a chapter with compatible characters, gates and encounter targets declared by reusable schemas/recipes. Include incompatible targets and targets with multiple entities. Create additional eligible targets after the mount and verify the same derived capability set without per-instance capability imports. Repeat with a card-rule provider and with a non-spatial target lacking any GameObject.

Acceptance: every eligible existing and future target receives the expected derived assembly; incompatible targets do not. Empty or absent Transform hierarchies do not affect selection. Explain records identify the provider, rule, target schema, scope path, selected policy and publication epoch. Replaying the mount operation does not create duplicate systems, components or subscriptions.

<a id="test-005"></a>
### TEST-005 — Contribution composition and precedence

Protocol coverage: [P-017](00-core-protocols.md#p-017), [P-018](00-core-protocols.md#p-018), [P-019](00-core-protocols.md#p-019), [P-020](00-core-protocols.md#p-020), [P-026](00-core-protocols.md#p-026), [P-033](00-core-protocols.md#p-033).

For additive, replacing, ordered, exclusive and incompatible contributions, enumerate all insertion permutations of four providers from different ancestor depths. Repeat with shuffled worker completion and removal order. Exercise an explicit override, an equal-priority tie, repeated provenance, and removal of only one of two contributions supporting the same derived structure.

Acceptance: canonical contribution order and output match the declared protocol policy, using stable IDs and priorities rather than execution timing. Exclusive/incompatible combinations report the conflicting provenance and leave the old assembly visible. Retraction preserves unrelated state and the surviving support. An implementation must not silently implement last-writer-wins or delete an entire shared component because one contributor disappeared.

<a id="test-006"></a>
### TEST-006 — Isolation, exclusions and mode switching

Protocol coverage: [P-013](00-core-protocols.md#p-013), [P-014](00-core-protocols.md#p-014), [P-016](00-core-protocols.md#p-016), [P-020](00-core-protocols.md#p-020).

Construct a tree with an ordinary branch, an isolation boundary, a target exclusion, inherited defaults and a local override. Run the same composition in Automatic and Conservative modes; explicitly opt in one target for the Conservative fixture. Include a target in the provider's own scope selected by a `Descendants` rule with `SelfAndDescendants`, plus a separate `LocalOnly` rule. Switch in both directions with existing targets and subsequently create new descendants. Exclude one capability while leaving unrelated capabilities and services available.

Acceptance: only eligibility/derivation rules identified by the [mode contract](00-core-protocols.md) change. Conservative targets without the required explicit declaration lose only contributions that no longer qualify, including same-scope targets selected by the `Descendants` rule. `LocalOnly` remains eligible at its installation scope in both modes and never reaches descendants. Automatic mode requires no new per-target acknowledgment. A mode switch publishes as a normal validated assembly change and obeys the same state-retention and failure rules. Isolation and exclusions remain effective in both modes.

<a id="test-007"></a>
### TEST-007 — Derivation termination and bounded expansion

Protocol coverage: [P-015](00-core-protocols.md#p-015), [P-019](00-core-protocols.md#p-019), [P-021](00-core-protocols.md#p-021), [P-022](00-core-protocols.md#p-022), [P-028](00-core-protocols.md#p-028).

Exercise a finite capability chain, a diamond that discovers the same contribution twice, mutually dependent capability rules, a self-producing rule, a missing prerequisite and a provider that would exceed each configured derivation quota. Randomize traversal order. Use the declared protocol rule restrictions and bounds; do not invent a second evaluation language for the fixture.

Acceptance: accepted plans have one canonical finite closure and duplicate suppression. Invalid cycles or non-convergent rules are rejected with the smallest available witness. Budget exhaustion produces the declared rejection result with counts and responsible provenance; it never publishes a truncated successful closure. The old assembly remains usable after pre-publication rejection. Increasing a budget is an explicit configuration change, not an unbounded retry loop.

<a id="test-008"></a>
### TEST-008 — Incremental indexes and subtree movement

Protocol coverage: [P-010](00-core-protocols.md#p-010), [P-023](00-core-protocols.md#p-023), [P-025](00-core-protocols.md#p-025), [P-026](00-core-protocols.md#p-026).

Implement a slow full-recompute reference evaluator for tests only. Generate 50 fixed seeds, each with 500 operations chosen from mount, unmount, reconfigure, spawn, retire, reparent, exclusion change, provider replacement and mode switch. After each accepted publication, compare canonical effective capabilities and provenance against the reference evaluator. Keep failed seeds and the reduced counterexample.

Acceptance: moving a subtree retracts old inherited support and derives new support for existing and future descendants. It preserves stable target identity and unrelated runtime state. Scope cycles and illegal cross-world moves are rejected. Index counters show that local changes visit the invalidated membership/provider/schema sets; untouched sibling scopes are not rescanned. Global mode switches may legitimately invalidate the whole world and must report that cost explicitly.

<a id="test-009"></a>
### TEST-009 — Prepared plans and atomic publication visibility

Protocol coverage: [P-002](00-core-protocols.md#p-002), [P-006](00-core-protocols.md#p-006), [P-027](00-core-protocols.md#p-027), [P-028](00-core-protocols.md#p-028), [P-029](00-core-protocols.md#p-029), [P-030](00-core-protocols.md#p-030), [P-031](00-core-protocols.md#p-031), [P-050](00-core-protocols.md#p-050), [P-051](00-core-protocols.md#p-051).

Prepare two plans from the same epoch, publish one and then attempt the other. Change a required asset or configuration revision during preparation. Hold an affected job in flight, cancel before application, and observe snapshots on both sides of the safe boundary. Exercise no-op plans and a plan spanning multiple targets.

Acceptance: stale plans are rejected or rebuilt according to the operation contract; the caller sees a distinct result. Preparation cannot mutate active authority. Observers see the complete old or complete new published assembly, never mixed bindings, schedule and epoch. A canceled uncommitted plan releases its staged resources. Once application has crossed the cancellation boundary, the outcome follows the documented publication/failure protocol instead of pretending cancellation undid writes. Structural playback and gameplay commitment remain distinct concepts.

<a id="test-010"></a>
### TEST-010 — State preservation, migration and reset

Protocol coverage: [P-020](00-core-protocols.md#p-020), [P-025](00-core-protocols.md#p-025), [P-032](00-core-protocols.md#p-032), [P-033](00-core-protocols.md#p-033).

Change an inherited tuning value, replace a provider with a compatible one, remove/re-add the last supporting contribution and change a state schema. Supply one valid migration, one invalid migration and one explicitly permitted reset. Start with non-default runtime state so accidental reconstruction is visible. Include a state record whose lifetime is target-owned and another explicitly provider-owned.

Acceptance: changes follow declared retention/migration policy; unrelated runtime values and independent contribution ownership survive. Missing required migration rejects preparation. Reset is observable and occurs only for a schema/policy that explicitly permits it. Test the resulting game behavior as well as component presence. No generic unload operation rewinds already committed rewards, damage, quest completion or transfers.

<a id="test-011"></a>
### TEST-011 — Temporal models and idle worlds

Protocol coverage: [P-035](00-core-protocols.md#p-035), [P-036](00-core-protocols.md#p-036), [P-037](00-core-protocols.md#p-037), [P-038](00-core-protocols.md#p-038).

Drive identical integer-rule fixtures through command-driven steps and fixed ticks. Hold a command-driven world idle for 10 seconds of host time while rendering continues. Pause a second world; vary render rates at 30, 60 and 144 Hz for the fixed-tick fixture. Exercise step backlog limits, a plugin-local timer, simultaneous host input and reconfiguration, and an explicit registered wake without a player command.

Pause while a tracked job is executing; queue bounded commands while paused, publish a composition change, then resume after a long host-time interval. Assert the pause waits for the running step boundary, preserves existing simulation debt, adds no debt for the paused duration, and does not increment the snapshot epoch or logical step solely for its lifecycle status change. Resume resets the host sample origin and processes retained debt/commands under ordinary limits. Exercise repeated pause/resume, invalid transitions and cancellation before/after the state commit together with TEST-009.

Acceptance: idle command-driven worlds execute no simulation steps; control-plane completion and publication can still progress at a safe control boundary. A paused world does not pause unrelated worlds. Logical-step ordering and clock fields obey the selected temporal contract; render updates cannot advance simulation twice. Timer semantics belong to their declared clock domain. No test requires a universal 60 Hz loop, physics phase or animation phase.

<a id="test-012"></a>
### TEST-012 — Execution graphs and job synchronization

Protocol coverage: [P-039](00-core-protocols.md#p-039), [P-040](00-core-protocols.md#p-040), [P-041](00-core-protocols.md#p-041), [P-043](00-core-protocols.md#p-043).

Compose stages from three independently authored fixture plugins. Test named dependencies, missing required stages, duplicate stage identities, a cycle, incompatible state access, partitioned writers and producer/consumer buffer edges. Coalesce two compatible declarations into one shared stage and validate their internal system DAG: unordered overlapping access must reject, explicit system-key dependencies must order it, and valid disjoint systems may overlap. Test missing required internal systems and internal cycles as well as stage cycles. Test a stage registered while affected jobs are still running. Run disjoint jobs concurrently and verify a dependent consumer cannot read an unfinished producer.

Acceptance: schedule compilation either produces a stable valid dependency graph or a diagnostic witness before publication. The Unity mapping actually combines/forwards the corresponding job dependencies. Structural changes wait for relevant access and play back at the declared boundary. Unrelated stages do not acquire a global fence solely because they are plugins. Logs expose waits and their dependency causes, making accidental serialization testable.

<a id="test-013"></a>
### TEST-013 — Authority, direct writes, requests and buffers

Protocol coverage: [P-034](00-core-protocols.md#p-034), [P-037](00-core-protocols.md#p-037), [P-041](00-core-protocols.md#p-041), [P-042](00-core-protocols.md#p-042), [P-043](00-core-protocols.md#p-043), [P-044](00-core-protocols.md#p-044).

Register two undeclared writers to the same authoritative state, two valid disjoint partitions, one owner with multiple ordered systems and a non-owner request producer. Test direct owner updates, bounded request buffers, duplicate requests, stale targets, cancellation, consumer absence and overflow. Use both a card resource transfer and a narrative flag transition to avoid assuming combat arbitration.

Run a bounded owner-local iterative rule inside one stage and separately attempt an inter-stage reaction cycle. The first remains legal within its declared work bound; the second needs an explicit bounded next-step route or rejects. No test silently forbids ordinary finite rule loops.

Acceptance: illegal ownership conflicts fail assembly validation. Legal owner writes use direct components; a global command queue is not required. Requests distinguish `Accepted`, `Rejected`, `Cancelled` and `Committed`; admission acceptance alone is not gameplay success. A contested card transfer is fully committed or rejected by that domain coordinator with conservation checks; the test never infers a transaction from ECB playback. Authoritative requests are never silently dropped because a buffer is full.

<a id="test-014"></a>
### TEST-014 — Committed events and consistent observation

Protocol coverage: [P-007](00-core-protocols.md#p-007), [P-044](00-core-protocols.md#p-044), [P-045](00-core-protocols.md#p-045), [P-052](00-core-protocols.md#p-052).

Subscribe before and after a publication boundary. Delay one consumer until its retention window expires. Observe a step with a rejected command, an accepted command and an adapter failure. Retry delivery using the same event identity and attempt to mutate state through inspection APIs.

Acceptance: committed events describe committed results only, with stable ordering and the required identity/step/epoch context. Snapshots are read-only and self-consistent; readers do not see partially written jobs. Retention expiry is reported as an explicit gap or resnapshot requirement. Consumer delivery does not mutate authority or automatically reapply gameplay. External side-effect consumers use their own idempotency boundary; Core event delivery does not imply exactly-once external execution.

<a id="test-015"></a>
### TEST-015 — Lifecycle and managed resource teardown

Protocol coverage: [P-003](00-core-protocols.md#p-003), [P-012](00-core-protocols.md#p-012), [P-046](00-core-protocols.md#p-046), [P-047](00-core-protocols.md#p-047), [P-048](00-core-protocols.md#p-048).

Exercise every documented lifecycle edge, invalid edge and repeated lifecycle request. Mount/unmount a dependency chain 1,000 times. Each plugin acquires a service lease, system registration, asset lease, callback and subscription. Submit at least 100 delayed callbacks after unload or world recreation. Suspend and resume with both retained and replaced providers.

Acceptance: affected consumers stop before their providers/resources are released; stale callbacks cannot reactivate retired instances and release only their own acquisitions. Registries and live lease counts return to baseline after documented retention windows. Shared resources remain available to surviving consumers. Teardown reports unreleased/quarantined resources accurately and does not report successful disposal while jobs still own native buffers.

<a id="test-016"></a>
### TEST-016 — Fault injection and recovery boundaries

Protocol coverage: [P-029](00-core-protocols.md#p-029), [P-030](00-core-protocols.md#p-030), [P-031](00-core-protocols.md#p-031), [P-047](00-core-protocols.md#p-047), [P-048](00-core-protocols.md#p-048), [P-049](00-core-protocols.md#p-049), [P-050](00-core-protocols.md#p-050), [P-051](00-core-protocols.md#p-051), [P-052](00-core-protocols.md#p-052).

Inject a named failure at every boundary in the matrix below. Run each case with a single plugin and a dependent consumer, and with a pending read-only observer. Use latches to place faults deterministically; random fault probability alone cannot prove boundary coverage.

| Injection point | Required observation |
| --- | --- |
| Manifest, dependency, capability closure or schedule validation | Explicit rejection; prior published epoch, state and registrations remain active |
| Resource acquisition or plan preparation | Staged acquisitions are released; old assembly remains active; failure includes operation ID and provenance |
| After preparation but before apply, including cancellation | No partial publication; staged resources are reclaimed; stale expected epoch has a separate outcome |
| Affected job held in flight while unload begins | No access-after-release; the documented draining/quarantine/fault behavior remains observable until ownership is resolved |
| Migration/application after the first authoritative mutation | World enters `Faulted`, keeps its last good observation image, and never resumes the partially changed storage; no ECB rollback |
| Authoritative system throws after a partial update | Advancement and ordinary committed output stop; corruption is not concealed by continuing subsequent systems |
| Adapter output fails after a successful simulation commit | Simulation history is not rewound; adapter degradation/retry follows its contract |
| One disposer throws | Other safe cleanup proceeds; failed release is recorded; ownership that cannot be proven safe stays retained/quarantined |
| Old callback arrives after restart | New world is unchanged; stale result is rejected and its own resources are released |
| Checkpoint restore fails during reference repair | Incomplete destination never becomes the running published world |

Acceptance includes a restart from a known checkpoint and verified input suffix, where supported by the template. Recovery must disclose lost/unreplayed inputs and external effects. A timeout does not grant permission to free memory still reachable by a job.

<a id="test-017"></a>
### TEST-017 — Checkpoints and schema evolution

Protocol coverage: [P-004](00-core-protocols.md#p-004), [P-005](00-core-protocols.md#p-005), [P-032](00-core-protocols.md#p-032), [P-049](00-core-protocols.md#p-049), [P-053](00-core-protocols.md#p-053), [P-054](00-core-protocols.md#p-054), [P-055](00-core-protocols.md#p-055).

Save a world containing definitions, stable target references, effective composition inputs, plugin state, game state, clocks and random-stream state. Recreate it with different native entity indices and compare canonical authoritative values after restore. Test supported schema migration, unknown required schema, missing content revision, corrupt data, unresolved reference and intentionally disposable presentation state.

Acceptance: incompatible required data rejects restore before the destination is published; explicit migration is deterministic for the recorded version. Save data excludes pointers, Unity object instance IDs, native entity handles and live service objects. Reconstructed recipes and bindings agree with the restored composition. Checkpoint compatibility is versioned separately from arbitrary event-log replay; Unity physics continuation is not claimed to be bit-identical.

<a id="test-018"></a>
### TEST-018 — Unity worlds, bootstrap and Play Mode

Protocol coverage: [P-002](00-core-protocols.md#p-002), [P-005](00-core-protocols.md#p-005), [P-035](00-core-protocols.md#p-035), [P-041](00-core-protocols.md#p-041), [P-047](00-core-protocols.md#p-047), [P-048](00-core-protocols.md#p-048), [P-058](00-core-protocols.md#p-058).

Start two owned worlds, deliberately leave a default-world creation path enabled in a negative fixture, and count system executions per logical step. Inject a managed system that writes live state and then throws; assert that the next registered stage never executes, the world is `Faulted`, and neither step ID nor snapshot publishes. This exercises the guarded dispatch required by [the Unity integration](04-unity-integration.md), because stock group exception logging is not a Core failure signal. Enter/exit Play Mode 10 times with domain reload enabled and 10 times disabled. Dispose a world while callbacks and jobs are pending, then restart. Exercise a headless player with presentation disabled.

Acceptance: one declared owner creates, drives and disposes each runtime world; custom loop integration and default initialization never double-update a system. Static registries, event handlers and loop nodes do not accumulate across Play Mode sessions. A headless result is produced by actual Unity-world execution; pure-rule tests are reported separately. Logs identify the world incarnation on every lifecycle and step assertion.

<a id="test-019"></a>
### TEST-019 — Engine adapters and single state authority

Protocol coverage: [P-002](00-core-protocols.md#p-002), [P-034](00-core-protocols.md#p-034), [P-038](00-core-protocols.md#p-038), [P-041](00-core-protocols.md#p-041), [P-045](00-core-protocols.md#p-045), [P-047](00-core-protocols.md#p-047).

Run input, asset, Transform/view, physics, animation and audio adapters independently when installed by their template. For motion, test the selected ECS-owned kinematic mode and the explicitly Unity-owned physics mode as separate fixtures. Deliberately attempt bidirectional writable pose/health/quest state mirrors. Delay input and asset callbacks; remove a view before a committed event is presented.

Acceptance: each quantity has one declared authority; observed engine state is labeled with its sampling step and cannot masquerade as an independently writable authoritative copy. GameObject/Transform hierarchy changes do not silently reparent composition scopes. Rendering interpolation and animation presentation cannot deduct resources or advance authoritative steps. Missing optional audio/view output may degrade as declared; required input/physics/asset data follows an explicit reject/wait/fault policy.

<a id="test-020"></a>
### TEST-020 — Baking, runtime recipes and precompiled plugins

Protocol coverage: [P-009](00-core-protocols.md#p-009), [P-015](00-core-protocols.md#p-015), [P-024](00-core-protocols.md#p-024), [P-027](00-core-protocols.md#p-027), [P-028](00-core-protocols.md#p-028), [P-054](00-core-protocols.md#p-054), [P-058](00-core-protocols.md#p-058).

Create equivalent targets through editor baking and through the generated runtime recipe catalog. Mount a precompiled plugin in an IL2CPP player, spawn a descendant after publication, reparent it, and unload the provider. Compare normalized schemas, definitions, provenance and behavior rather than chunk layout or native entity indices. Attempt to load an unknown executable plugin and an incompatible recipe revision.

Acceptance: runtime assembly needs no editor-only baker or runtime C# compilation. Generated catalogs root required constructors/handlers/generic instantiations. Spawn eligibility derives from current published composition; a stale recipe binding is revalidated according to the protocol. Unsupported executable content is rejected explicitly. A linked inactive plugin is mountable without rebuilding the running player.

<a id="test-021"></a>
### TEST-021 — Genre neutrality and cross-template conformance

Protocol coverage: [P-001](00-core-protocols.md#p-001), [P-003](00-core-protocols.md#p-003), [P-013](00-core-protocols.md#p-013), [P-014](00-core-protocols.md#p-014), [P-016](00-core-protocols.md#p-016), [P-025](00-core-protocols.md#p-025), [P-034](00-core-protocols.md#p-034), [P-036](00-core-protocols.md#p-036), [P-044](00-core-protocols.md#p-044), [P-056](00-core-protocols.md#p-056), [P-057](00-core-protocols.md#p-057), [P-059](00-core-protocols.md#p-059).

Run all three [reference compositions](07-reference-compositions.md): a command-driven card game, an event/command-driven narrative game, and a fixed-tick action game. For each, assert the documented before/after tables for mount, unmount, subtree movement and both mode-switch directions. Run the cross-template combination using the same kernel binaries and composition runtime.

Acceptance: the card and narrative templates register no compulsory combat, actor, action, vitality, physics or animation state/stage. The action template supplies its own domain state and arbitration. The kernel tests import no example gameplay assembly. All templates use the same identities, capability derivation and publication contracts. Any generic API needing an actor, hit, turn, quest or Transform for an unrelated template blocks protocol stabilization and must be corrected in the normative source and traceability.

<a id="test-022"></a>
### TEST-022 — Ordering, replay and determinism limits

Protocol coverage: [P-008](00-core-protocols.md#p-008), [P-018](00-core-protocols.md#p-018), [P-037](00-core-protocols.md#p-037), [P-039](00-core-protocols.md#p-039), [P-040](00-core-protocols.md#p-040), [P-041](00-core-protocols.md#p-041), [P-044](00-core-protocols.md#p-044), [P-045](00-core-protocols.md#p-045), [P-053](00-core-protocols.md#p-053).

Record at least 10,000 logical steps for a pure integer-rule fixture, with fixed catalog/configuration versions, seeds, host-assigned admission sequences, sealed batch boundaries and composition-operation order. Replay the same admitted order/batches with shuffled internal producer/completion order and supported worker counts of 1, 2 and 4. Compare canonical state, decisions, committed event identities and capability provenance after every step. Vary serialization map iteration order to expose accidental dictionary dependence. A test that changes host admission or batch boundaries is a different input trace and does not require identical gameplay results.

Acceptance: policy evaluation of the same recorded admitted input does not depend on pointer values, native entity indices or worker scheduling. Hash canonical stable identities and schema fields, excluding timestamps, padding, diagnostic counters and native layout. Map fresh runtime `WorldId` values to a fixture world ordinal only in comparison output, including embedded event/request identities; separately assert that actual recreated worlds have different session IDs and reject stale handles. The normalization never changes runtime deduplication or handle validation. For Unity-physics-dependent rules, record and replay the engine observations to test downstream rule repeatability separately. Cross-platform floating-point/physics lockstep is outside the claim; report tolerances and mismatches for any exploratory comparison instead of converting them into unsupported guarantees.

<a id="test-023"></a>
### TEST-023 — Performance, bounded memory and architecture regressions

Protocol coverage: [P-007](00-core-protocols.md#p-007), [P-022](00-core-protocols.md#p-022), [P-023](00-core-protocols.md#p-023), [P-026](00-core-protocols.md#p-026), [P-034](00-core-protocols.md#p-034), [P-043](00-core-protocols.md#p-043), [P-048](00-core-protocols.md#p-048), [P-052](00-core-protocols.md#p-052), [P-060](00-core-protocols.md#p-060).

Run the workloads and counters below in a standalone player. Add a regression test that leaves the composition unchanged for 10,000 simulation steps, and another that leaves a command-driven world idle. Compare 1,000 and 10,000 inactive eligible targets while executing the same small active workload. Run lifecycle churn until bounded registries/caches reach a plateau.

Acceptance: stable execution makes zero control-tree traversals and zero string-based service resolutions; idle command worlds advance zero simulation steps. Inactive scope count does not introduce a hidden per-step propagation scan. Direct owner-state queries do not read a parallel managed authority dictionary. Detect that regression through ownership declarations, architecture dependency checks and a mutation fixture that would diverge if an adapter/service mirror were authoritative. Resource counts, retained event bytes, native allocations and quarantine are reported separately; aggregate process memory alone is insufficient to attribute a leak.

<a id="test-024"></a>
### TEST-024 — Documentation, traceability and protocol conformance

Protocol coverage: [P-001](00-core-protocols.md#p-001), [P-009](00-core-protocols.md#p-009), [P-051](00-core-protocols.md#p-051), [P-055](00-core-protocols.md#p-055), [P-056](00-core-protocols.md#p-056), [P-057](00-core-protocols.md#p-057), [P-059](00-core-protocols.md#p-059), [P-060](00-core-protocols.md#p-060).

Run the repository validator from the repository root:

```sh
rtk proxy python3 tools/validate_game_core_docs.py --self-test
rtk proxy python3 tools/validate_game_core_docs.py
```

The first command executes temporary positive/negative fixtures for the validator itself. The second checks required documents; local file/anchor links; unique, registered requirement/task/test definitions; requirement-to-task/test coverage; dependency resolution; task dependency cycles; wave ordering; and task metadata agreement with [traceability.json](traceability.json). Each task has a literal `Wave: N; Depends: ...` line; the human-readable guide and machine-readable graph must agree.

Acceptance also requires a human consistency review for terminology, state authority, service/capability separation, lifecycle transitions, mode behavior, failure boundaries, API names and the three genre fixtures. Mechanical checks cannot detect a semantically incorrect protocol. Source/version links are reviewed separately; this offline checker does not claim network-link validation. Runtime conformance is established by the other suites, not by this documentation command.

## Operation-to-suite coverage

The [normative operation catalogue](00-core-protocols.md#10-operation-catalogue) defines each procedure and its observable probe. This table locates its executable suite; the suite must exercise the catalogue's legal ordering, cancellation and failure cases as well as the successful path. A suite name is not proof that all cases have run.

| Operation | Required suites | Distinguishing probe |
| --- | --- | --- |
| O-01 CreateWorld | [TEST-002](#test-002), [TEST-018](#test-018) | Unique world incarnation; no execution before initial publication; idempotent creation |
| O-02 EditScopes | [TEST-006](#test-006), [TEST-008](#test-008) | Acyclic same-world membership, isolated branches and atomic inherited-state move |
| O-03 Mount | [TEST-003](#test-003), [TEST-004](#test-004), [TEST-015](#test-015) | Waiting dependencies or complete automatic existing/future target assembly |
| O-04 ActivateOrResume | [TEST-003](#test-003), [TEST-015](#test-015) | Provider return reactivates eligible waiters; explicit suspension still needs resume |
| O-05 ReconfigureOrReplace | [TEST-005](#test-005), [TEST-010](#test-010), [TEST-015](#test-015) | Stable contribution identity, retained mutable state and stale activation rejection |
| O-06 Suspend | [TEST-010](#test-010), [TEST-015](#test-015) | Contributions retract, dormant state remains, suspended executor cannot run |
| O-07 Unmount | [TEST-005](#test-005), [TEST-015](#test-015), [TEST-016](#test-016) | Exact support retraction, reverse dependency teardown and no gameplay undo |
| O-08 SetMode | [TEST-006](#test-006), [TEST-009](#test-009) | One setting/assembly publication, including failure retaining the prior mode |
| O-09 BuildPlan | [TEST-005](#test-005), [TEST-007](#test-007), [TEST-008](#test-008), [TEST-009](#test-009) | Finite canonical closure, oracle parity and complete validation without live writes |
| O-10 PreparePlan | [TEST-009](#test-009), [TEST-016](#test-016) | Inert staged resources; cancellation and acquisition failure reclaim or track them |
| O-11 PublishPlan | [TEST-009](#test-009), [TEST-016](#test-016) | Old/new consistent visibility; any precommit postwrite failure stops the world |
| O-12 SpawnOrDespawn | [TEST-004](#test-004), [TEST-020](#test-020) | Current complete derived recipe on first visibility; stale recipes reject/rederive |
| O-13 SubmitCommand | [TEST-002](#test-002), [TEST-013](#test-013) | Capacity, route and sequence validation; duplicate execution is impossible |
| O-14 Advance | [TEST-011](#test-011), [TEST-022](#test-022) | Idle zero steps, retained fixed debt and replayed admission/batch order |
| O-15 ExecuteStage | [TEST-012](#test-012), [TEST-013](#test-013), [TEST-018](#test-018) | Stage/system DAG, access/lifetime dependencies and unswallowed managed failure |
| O-16 CommitStep | [TEST-013](#test-013), [TEST-014](#test-014) | Required drains, no success event for rejected writes and one consistent output |
| O-17 Observe | [TEST-014](#test-014) | Immutable retained token, explicit cursor expiry/backpressure and lease release |
| O-18 CancelOperation | [TEST-002](#test-002), [TEST-009](#test-009), [TEST-016](#test-016) | Serialized cutoff permits exactly Cancelled or TooLate, never implied rollback |
| O-19 StopWorld | [TEST-015](#test-015), [TEST-016](#test-016), [TEST-018](#test-018) | Ingress closes; unfinished native users keep their storage pinned |
| O-20 CaptureCheckpoint | [TEST-017](#test-017) | Explicit queued-command disposition, stable copy and atomic checkpoint file publication |
| O-21 RestoreCheckpoint | [TEST-002](#test-002), [TEST-017](#test-017) | New unexposed world rejects bad schema/reference and old-session callbacks |
| O-22 RecoverWorld | [TEST-016](#test-016), [TEST-017](#test-017) | Faulted storage never resumes; new session restores an explicit source |
| O-23 BindServiceOrAcquireLease | [TEST-003](#test-003), [TEST-015](#test-015) | Visibility and activation checks; owned lease disposal at most once |
| O-24 CompleteAsyncWork | [TEST-002](#test-002), [TEST-015](#test-015) | 100 late completions produce zero stale writes or resurrection |
| O-25 ExplainOrInspectOperation | [TEST-004](#test-004), [TEST-005](#test-005), [TEST-014](#test-014) | Complete retained provenance; staged status remains distinct from published state |
| O-26 SetWorldRunState | [TEST-009](#test-009), [TEST-011](#test-011), [TEST-018](#test-018) | Pause waits for the boundary, preserves debt, changes no step/epoch and resumes bounded inputs |

## Instrumentation and reproducible performance method

Expose inexpensive numeric counters at stable boundaries; format descriptions on demand. At minimum record `ControlNodesVisited`, `CandidatesMatched`, `ContributionsAdded`, `ContributionsRetracted`, `StrataEvaluated`, `PlanPreparedBytes`, `ApplyDuration`, `AssemblyEpoch`, `StepsAdvanced`, `ServiceStringLookups`, per-stage duration, job wait duration, structural operations, request high-water/overflow counts, stale-result count, live leases, outstanding callbacks, retained event bytes and quarantine bytes. Separate preparation CPU work, wall-clock latency, safety-boundary wait, and apply pause.

Use a deterministic fixture generator whose seed and shape are recorded. The initial fixture has 1,000 scopes and 10,000 targets, with three schema families, isolated and excluded branches, and both shared and unique contribution sets. These are diagnostic loads, not assumptions about the eventual game. Include four update sizes: 1 target, 100 targets, 10,000 targets and a whole-world mode switch. Add 1,000-target spawns under an already-active provider and reparent a 100-target subtree between two providers. Track allocation/provenance growth for identical versus exceptional configurations.

Warm up for 30 seconds or until initialization/compilation/load work is complete, whichever is later. Measure five independent 120-second runs for steady workloads and at least 1,000 repetitions for small composition changes. Collect per-sample p50/p95/p99/max and total counts, not only averages or FPS. For idle command-driven cases report zero steps and control-plane wake counts rather than dividing by a fabricated tick count. Time end-to-end changes separately from apply time so off-thread preparation cannot hide an excessive user-visible delay.

Record the instrumented diagnostic build and release-like measurement build separately. Managed-allocation assertions use a counter that covers the relevant threads; a main-thread-only reading cannot prove worker allocation is zero. Memory reports distinguish managed heap, native containers, retained catalogs/caches, asset leases, events and quarantined work. Avoid inferring a leak solely from an engine cache that has not yet reached its documented bound.

## Provisional budgets and failure decisions

The following numbers are initial engineering targets, not measured results or universal shipping requirements. Apply them on the recorded baseline machine; retain the measurements if later product evidence justifies revising a target. A quota in the protocol is a correctness bound and remains mandatory even when a performance target changes.

| Workload | Initial target | Included / separately reported |
| --- | --- | --- |
| 10,000 integer-rule targets, 1,000 active commands per fixed step | Core execution p95 at most 4 ms | Scheduling, queries, request arbitration, commit; report native physics/render/audio separately |
| Stable execution after warmup | 0 managed bytes per logical step in the kernel hot path | Include worker work; report explicitly enabled diagnostics or plugin allocations separately |
| No composition changes over 10,000 steps | 0 control-tree visits and 0 string service resolutions | Counters count control work even if a traversal is cached or returns no matches |
| Valid plan affecting 100 existing targets | Apply pause p95 at most 2 ms | Include fencing time in a separate mandatory wait metric; report preparation and end-to-end latency |
| Whole-world derivation for 10,000 targets | Preparation p95 at most 100 ms | Full closure/index work; no partial publication to meet the number |
| 1,000-target spawn under active capabilities | Report prepare/apply p95, native/managed bytes and recipe reuse; establish a baseline | No throughput claim until the actual archetype/state footprint is measured |
| 1,000 lifecycle cycles with fixed retained data | Active counts return to baseline; bounded cache/native/managed growth plateaus | Report asset policy, retained events and quarantine separately |
| Idle command-driven world | 0 simulation steps and 0 simulation-stage updates | Host presentation and pending control-plane operations may still run |

For a budget miss, retain a trace and identify candidate enumeration, contribution sorting, provenance allocation, job fencing, structural playback or adapter work as the measured cause. First reduce avoidable work while preserving the protocol. If its preparation estimate exceeds the configured apply budget, reject or defer the proposal before live mutation; do not invisibly publish target batches with different effective assemblies. An actual overrun after writes begin is recorded and application must finish or fault, as specified by P-022 and P-031. Do not declare dynamic composition complete while correctness or lifecycle failures remain, even when throughput targets pass.

## Exit evidence and reporting

Each suite result is `NotRun`, `Pass`, `Fail` or `Blocked`, with the exact fixture/build and artifact link. `Blocked` records a missing target, toolchain or capability and cannot satisfy a dependent wave gate. Store player logs, failing seed/trace, normalized hash outputs, memory/counter data and package locks as implementation artifacts; do not replace them with a screenshot of a green Editor console.

The first implementation wave executes toolchain and generated-registration risk tests plus this documentation validator. Later waves broaden the same executable fixtures into automatic propagation, two distinct game families, full lifecycle faults and all three reference compositions. [The implementation guide](09-implementation-guide.md) determines the dependency order and the precise gate at which each suite is required.
