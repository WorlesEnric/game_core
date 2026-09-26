# W6-GATE HANDOFF — Wave 6 integration gate (fixed-step action, durable delivery, unload stress, replay/cost counters)

Branch `w6-gate` (worktree `/Users/yangcao/wkspace/gc-wt/w6-gate`) = `main` + GC-020, with `origin/gc-021`,
`origin/gc-022` and `origin/gc-023` merged into it here and reconciled by hand — including GC-023's **round 2**, whose
release-clone change arrives as a second merge of the same branch (§4.7).

**Status of every build/test command in this document: `NotRun (pending orchestrator build host)`.** This host has no
Unity, no .NET SDK, no Mono and no C# compiler, so nothing in this change set has been compiled, imported, executed or
built here. What did run is interpreter-level only and is recorded verbatim in `artifacts/w6-gate/static-checks.log`:
the host-side C# checker (542 files), the two checks this reconciliation added and wired into the gate
(`tools/check_gate_sources.py`: member resolution, using coverage, ambiguity, balance and the frozen-table/digest/probe
agreement; `tools/check_release_clone.py`: the clone's own invariants, run on the clone), the frozen contract-surface
parity check, the committed-catalog verifier, the documentation validator (self-test + full), the release-fault and
release-telemetry checks in `--no-build` mode, the release-clone preparation **run for real** followed by the clone
verification and then deletion, the release-surface check against synthetic players in five directions, `bash -n` over
every shell script, `py_compile` over every Python tool, the Unity `.meta` generator (idempotent: 0 creations), and an
independent recomputation of all five digest literals from the frozen name tables (cross-checked against every
declaration site). None of those is a build or a test result.

## 1. Summary

The Wave 6 exit gate is: *"Integrate the fixed-step action reference, durable reward delivery, unload stress and
replay/cost counters. Demonstrate that optional physics/animation are absent from cards/narrative. Complete
1,000-cycle teardown and repeatability fixtures before broader qualification."* Four tasks finished the four
mechanisms (GC-020 traversal + optional engine stages, GC-021 durable outbox + idempotent delivery, GC-022 unload
stress + callback/lifecycle proof, GC-023 replay + telemetry counters); this task is the join on one revision, and it
adds no kernel behaviour of its own.

One runner drives three real genre worlds through one loop (`W6GateScenario` over `IW6Family`), and its observations
are:

1. **The fixed-step course (traversal).** The world is a fixed-step world at the reference's declared step with the
   declared catch-up bound, it commits NO step while no host time elapses, its live runners and both checkpoint volumes
   carry the genre's storage, and the physics gate's simulated-step count and the engine's own `Simulate` count equal
   the admitted steps.
2. **The cost counters (traversal, GC-023).** The world's own owners are sampled through GC-023's fixed schema and the
   driver's `StepsAdvanced` counter records the admitted steps; the observation also requires the counting to be
   COMPILED IN, which is what the qualification build (and not the release clone) is for.
3. **One physics simulation per admitted step (traversal).** A declared body per owner-runner, an intent applied
   before the simulation already reflected by the engine pose, one `Simulate` per admitted step, and a repeated step
   refused and counted — on a world of its own so the replay comparison below is between two runs of equal length.
4. **Replay of the recorded input (traversal).** A second world integrates the SAME recorded input through the SAME
   declared step: the traces agree at tolerance zero and produce the same rule digest, while a perturbed copy
   mismatches at zero and matches at the package's declared tolerance — rule repeatability is never conflated with
   native physics.
5. **The traversal side of the composition audit.** The course really carries the five declared stages, the dedicated
   local physics scene, the once-per-step gate, and the committed audio and animation stages with their recording
   sinks.
6. **The composition audit (narrative, cards).** Every declared manifest, every compiled descriptor and the loaded
   assembly graph are walked for a traversal stage, stage key, system key, buffer (the course's declared step buffer
   and its movement lane are its ports), capability contract, derivation rule, configuration schema, plugin factory
   key, descriptor stage, descriptor system key or owned slot — and for a reference to `GameCore.Unity.Adapters`, where
   the optional physics/animation/audio halves live. The walked-entry counter being zero is itself a failure, so a
   genre the audit read nothing of cannot look clean.
7. **Durable reward delivery across a receiving world's unload/reload (cards).** One committed event of a real card
   market becomes one durable obligation; the receiving world is stopped and disposed; the obligation's rows are
   reinstated into a world of a NEW session and dispatched, and the destination mutates once; the acknowledgement is
   then lost — the at-least-once boundary P-045 states — and the same rows are reinstated once more into a third
   session, where the destination is asked a second time under the SAME external key and mutates nothing.
8. **The 1,000-cycle loop (all three genres).** create/mount/step/unmount/teardown: a fresh installation identity per
   cycle, mounted through the genre's own payload and the control lane with one real staged lease, published, one
   admitted command committing exactly one logical step, then unmounted through the genre's own payload and the
   lifecycle controller until the installation reaches `Disposed` with its lease retired.
9. **Bounded registries and stable counters (all three genres).** The lane owns no live lease, no retained row and no
   quarantine; its retired history is bounded by the acquisitions the loop really made (never by elapsed time); neither
   job fence has an outstanding handle; the world's ledger balance, retained count and outstanding-job count are back
   at the world's own baselines. The numbers are reported as the high-water marks they are.
10. **Repeatability (all three genres).** One digest over the whole observation table, per genre and per catalog, which
    the EditMode suite recomputes from the frozen name table alone and the player probe compares against five frozen
    literals.

## 2. Commits on this branch

| Commit | Contents |
|---|---|
| `Merge GC-021 into W6 gate` | GC-021, with the probe host's conflicted regions resolved before the commit and the release-clone preparation taught to strip the new mode (§4). |
| `Merge GC-022 into W6 gate` | GC-022, same shape: every probe mode kept, the stress fixture stripped from the release clone. |
| `Merge GC-023 into W6 gate` | GC-023, the solution file unioned with unique GUIDs, and the inventory gaining its third Wave 6 revision section. |
| `W6-GATE: fix the probe modes whose report was never finalized` | Three dispatch branches (`-probeTraversal`, `-probeGc021`, `-probeLifecycleStress`) had lost their `report.CompletePositive()` call in the merges. Recorded separately because it is a behaviour fix on merged code rather than part of this gate's own files (§4.1). |
| `W6-GATE: the gate scenario, its family contract and the composition audit` | `W6GateFamily.cs`, `W6GateScenario.cs`, `W6CompositionAudit.cs`, the three family adapters, `ProbeW6Gate.cs`. |
| `W6-GATE: the EditMode suite, the player harness and the gate script` | `Tests/W6Gate/**`, `tools/unity/run_w6_gate_probe.sh`, `tools/run_w6_gate.sh`, `tools/check_release_gate_free.py`. |
| `W6-GATE: extend the reload matrix to the traversal genre` | `LifecyclePlayModeMatrix` drives `Gc020TraversalHost.RunReloadRoute` once per session, the Editor assembly references the probe host, and the cycle JSONL records the route. |
| `Merge GC-023 round 2 into W6 gate` | GC-023's round-2 fix set (the replay qualification fixture leaves the marker-free clone: sources, mode wiring, manifest dependency and the probe host's `GameCore.Replay` reference) merged into this revision, with the one conflicted file resolved as a union and two clone-breaking defects fixed (§4.7). |
| `W6-GATE: verify the release clone and this gate's own sources` | `tools/check_release_clone.py`, `tools/check_gate_sources.py`, both wired into `tools/run_w6_gate.sh`; the release-probe list and the release-surface checker follow round 2; the static checks re-captured. |
| `W6-GATE: the handoff, the static checks and the inventory proposals` | `artifacts/w6-gate/**`, `artifacts/gates/w4-generic-profile/inventory.{md,json}`. |

## 3. Files created

### The gate (every `.cs` file carries a Unity `.meta` with a unique GUID)

| File | Contents |
|---|---|
| `unity/.../Runtime/W6GateFamily.cs` | `W6GateStep`, `W6GateScenarioResult` (digest over `name=pass` lines via `NarrativeDigest.OfLines`), `W6StageRuntime` (the union of the two stage-runtime shapes) and `IW6Family : IGc013Family` — what a genre must answer for the three-family loop, on top of everything `IGc013Family` already declares. |
| `unity/.../Runtime/W6GateScenario.cs` | The runner: the three observation tables, `ObservationNames(label)`, `QualifiedNames(label)`, `Run(IW6Family, bool)`, `RunReloadRoute(IW6Family)`, the private `Fixture` (one real genre world), the recording delivery destination, the first-committed-event obligation source, and the `Executor` with the twenty-two observations. |
| `unity/.../Runtime/W6CompositionAudit.cs` | `W6FamilyAudit`, `W6AdapterAssemblyFacts` and the static audit: the declaration/descriptor walk and the loaded-assembly check, plus the explicit load of each genre assembly before its reference graph is read. |
| `unity/.../Runtime/W6FamilyNarrativeHost.cs` | The narrative slice's `IW6Family` part and its two catalog entry points + the two frozen digest literals. |
| `unity/.../Runtime/W6FamilyCardsHost.cs` | The card market's equivalent. |
| `unity/.../Runtime/W6FamilyTraversalHost.cs` | The traversal course's equivalent, its four cycle manifests (acceleration modifiers of the course's own declared shape) and `RunReloadRoute()`. |
| `unity/.../Runtime/ProbeW6Gate.cs` | The `-probeW6Gate` player mode: three process steps (resolved cycle count, native leak-detection mode, telemetry build shape), every observation of all three genres, and one digest step per genre. |
| `unity/.../Tests/W6Gate/GameCore.W6Gate.Tests.asmdef` | Editor-only EditMode assembly (`UNITY_INCLUDE_TESTS`). |
| `unity/.../Tests/W6Gate/W6GateIntegrationTests.cs` | One `[Test]` per named observation per genre, the digest/table agreement test (every literal recomputed from `QualifiedNames(label)`), and a `[TearDown]` that asserts the world registry is back at zero. |
| `tools/unity/run_w6_gate_probe.sh` | The player harness: `PROBE_RUNS` runs, strict JSON, all 41 required steps by name, the claim clauses, the five digest literals, the resolved cycle count, and native leak attribution over every retained run log. |
| `tools/check_release_clone.py` | The disposable clone's own invariants, asserted on the clone rather than trusted to the script that made it: no reference to any qualification-only type or to a member declared only in a removed file (qualified accesses included), every asmdef reference resolvable, the stripped `ProbeArguments` constructor call agreeing with its own parameter list, every C# file balanced, the kept modes still wired and the removed ones gone, and the manifest/lock/`Tests/` residue. Wired into the gate right after the clone is prepared. |
| `tools/check_gate_sources.py` | The source-level invariants of this gate's change set, in under a second and with no toolchain: brace/parenthesis balance with comments and literals removed, every `Type.Member` access resolved against the declaring sources, every used type reachable through the file's own `using` directives (with a documented rule for a simple name that binds to a member rather than a type), no name declared in two imported namespaces at once, and the frozen observation tables agreeing with all five digest literals and with the probe harness's 41 required steps. Wired into the gate's static-check step. |
| `tools/run_w6_gate.sh` | The gate script (§6). |
| `tools/check_release_gate_free.py` | The union-of-markers release-surface inspection (§5). |
| `artifacts/w6-gate/static-checks.log`, `artifacts/w6-gate/HANDOFF.md` | The verbatim host-side checks and this document. |

### Modified (shared surfaces)

| File | Change | Why it is safe |
|---|---|---|
| `unity/.../Runtime/Gc020TraversalHost.cs` | `CourseFamily` became `partial`. | One word; it lets the Wave 6 adapter declare the gate's contract on the same class so the gate runs GC-020's real course family rather than a second implementation. |
| `unity/.../Runtime/ProbeArguments.cs` | One constant, one ctor parameter, one assignment, one property, one `IsProbeInvocation` term, one local, one parse branch and one ctor argument. | The additive block every earlier task added for its own mode; every earlier mode's branch is untouched. |
| `unity/.../Runtime/ProbeRunner.cs` | One dispatch arm and one report-identity arm for `-probeW6Gate`, plus the §4.1 fix. | Additive for the new mode; the fix restores the documented contract of three existing modes. |
| `unity/.../Editor/GameCore.Validation.Editor.asmdef` | One reference: `GameCore.Validation.ProbeHost`. | The reload matrix now drives the traversal route, which lives in the qualification probe host. |
| `unity/.../Editor/LifecyclePlayModeMatrix.cs` | Every session also runs the traversal reload route and asserts the registry returns to its baseline; the route is recorded in the entry log and in the per-cycle JSONL. | Additive: the route creates, steps, stops and disposes its own world inside the session, so every existing assertion (one registry world, fresh session, zero residue at exit) still holds and is now also checked across the third genre. |
| `tools/unity/prepare_gc017_release_project.py` | The Wave 6 gate's seven files join the removal list; GC-022's four lifecycle-stress runtime files join it too; the W6 gate's mode wiring joins the stripping blocks. | The clone is disposable and gitignored; every `replace_once` needle was verified by running the script and inspecting the result (§7). |
| `artifacts/gates/w4-generic-profile/inventory.{md,json}` | A `w6GateRevisions` / "W6-GATE revision notes" section: **proposals only**, no row promoted. | A row may only be promoted from an archived passing run. |

## 4. Merge reconciliations (each recorded)

1. **The probe host.** Both conflict sites in `ProbeArguments.cs` and `ProbeRunner.cs` were resolved by keeping **every**
   probe mode from all four tasks: one flag, one property, one `Parse` branch, one `IsProbeInvocation` term, one
   dispatch arm, one report-identity arm and one ctor argument per mode. The merged dispatch now has thirteen
   single-branch arms plus the multi-probe fallback.

   **4.1 A real defect the merge introduced, found and fixed here.** In the GC-023 merge, the traversal, GC-021 and
   lifecycle-stress dispatch arms ended up sharing a single trailing `report.CompletePositive()` call, so three of the
   thirteen probe modes would have run their scenario, written their steps, and then never finalized the report —
   `Result` would have stayed `Fail` with exit code 1, and `-probeTraversal`, `-probeGc021` and
   `-probeLifecycleStress` would all have failed their harnesses on this revision. Each arm now calls
   `CompletePositive()` itself, which is the shape every other arm has. This is a behaviour fix on merged code, not a
   change to any gate's semantics, and it is the reason the merge commit list has its own entry for it.

2. **`dotnet/GameCore.sln`.** Merged by keeping both sides' project entries with their own GUIDs and a complete set of
   four configuration rows each: 30 `Project(` lines, 30 `EndProject`, 31 distinct GUIDs (one of which is the solution
   header), 120 configuration rows. The GC-023 side's repair of `GameCore.Rules.Narrative.Tests`' missing `EndProject`
   is preserved.

3. **`unity/GameCore.Validation/Packages/manifest.json` and `packages-lock.json`.** Merged cleanly; the union holds
   every package all four tasks added (gameplay.integration, gameplay.traversal, rules.traversal, gameplay/traversal
   fixtures, the replay package and the telemetry qualification marker), the `testables` list covers every testable
   package, and the lock was taken from either side (the build host regenerates it on resolve).

4. **`inventory.{md,json}`.** Merged by keeping every task's own revision-notes section (GC-020 **and** GC-021 **and**
   GC-023) and appending this gate's section, which promotes nothing. The JSON section was appended textually rather
   than by re-serializing the document, so the diff is 146 added lines and no reformatting.

5. **Shared kernel files.** The `ResourceLedger`/`JobFenceRegistry` bounds GC-022 added (the bounded retired history),
   the outbox/committed-event store GC-021 added, GC-023's replay/telemetry counters (compiled out of a release build
   by the marker package) and GC-017's fault latches all merged with their own changes; the only hand-reconciliation
   inside a kernel file was `ManagedResources.cs`, where GC-022's `RetiredHistoryCapacity` constant and GC-023's
   explicit `ITelemetryOwner.TelemetryOwner` member had to coexist — both are kept, and the class implements the
   interface while keeping the bound.

6. **Release-clone preparation.** The stripped-marker union is now: the two qualification marker packages and the two
   Unity test packages (with `testables` emptied), the `Tests/` tree, and — as one consistent set — the fault scenario
   and probe, the Wave 5 gate's five files, GC-021's three files, GC-022's five files, GC-023's three replay fixture
   files and the Wave 6 gate's seven files, plus `Editor/LifecyclePlayModeMatrix.cs` (§4.7.1). GC-022's four
   `LifecycleStress*` runtime files were missing from the merged list while the *mode* was already stripped; they are
   removed now, which is what makes the clone one consistent set rather than dead code that happens to compile.
7. **GC-023's round 2 (the merge the orchestrator started).** Round 2 removes GC-023's replay *qualification fixture*
   from the marker-free clone: `ReplayParallelJobs`, `ReplayScenario` and `ProbeReplay`, the `-probeReplay` dispatch and
   argument plumbing, the `com.gamecore.replay` manifest dependency, and the `GameCore.Replay` reference in
   `GameCore.Validation.ProbeHost.asmdef`. The one conflicted file (`tools/unity/prepare_gc017_release_project.py`, eight
   hunks) was resolved as a **union**: every removal this gate already had, plus round 2's replay removal and its asmdef
   edit. Two of the hunks are the constructor-argument and qualification-mode-expression replacements, and both were
   **re-derived from the merged sources** rather than taken from either side, because either side's text no longer
   matches:

   * the qualification-mode expression becomes `|| Gc013 || W4Gate || Gc018 || Gc019 || Traversal`, so the mode the
     clone keeps (`-probeTraversal`) still counts as a probe (round 2's version has no `Traversal` term and my previous
     one had dropped it);
   * the constructor call becomes `w4Gate, gc018, gc019, traversal, resultPath`, which is exactly the parameter list
     that survives.

   The per-member needled list for `ProbeArguments.cs` (36 needles: const, parameter, assignment, local, property with
   its doc comment, and parse branch for each of the six removed modes) is **generated from the merged file**, and every
   needle must match exactly once or the script raises.

   **4.7.1 Two defects the union exposed, both fatal to the clone.**

   * **The previous clone's `ProbeArguments` call passed 14 arguments to a 15-parameter constructor.** My earlier
     revision kept `bool traversal,` in the signature while dropping `traversal` from the call, and the arity check that
     would have caught it did not exist yet. This is the defect the orchestrator's warning about the constructor
     replacement pointed at, and it is exactly the class of failure a copy-and-delete script cannot see.
   * **A surviving clone file called a removed member.** `Editor/LifecyclePlayModeMatrix.cs` (added by this gate) calls
     `Gc020TraversalHost.RunReloadRoute`, which round 2 does not remove — but the method lives in
     `W6FamilyTraversalHost.cs`, one of the Wave 6 gate files the clone *does* remove, so the clone's Editor assembly
     would not compile. The matrix is editor-only qualification tooling, so the clone now removes it for the same
     reason it removes the `Tests/` tree. The reload-matrix runs themselves are unaffected: they happen in the
     qualification project, which still has both files.

   **4.7.2 What follows for this gate's own scripts.**

   * `tools/run_w6_gate.sh`'s release-probe list drops `run_replay_probe.sh`, because the mode no longer exists in the
     clone. **Nothing was added in its place**: the five remaining harnesses are exactly the modes the pre-Wave-6 gates
     drove against a release player, so this gate still introduces no release-player run that has never executed
     anywhere. The clone does keep `-probeTraversal`; driving it in a release player would be new evidence never run on
     any host, which is a decision for the orchestrator rather than a merge reconciliation, and the release-surface
     scan already anchors on that flag (§5).
   * `tools/check_release_gate_free.py` follows round 2: its marker groups now include a `gc023-replay-fixture` group
     (type names plus `-probeReplay`) whose qualification-side marker must be present, the telemetry group is gone from
     the marker scan (a `[Conditional]` switch leaves no string to find; `check_release_telemetry_free.py` settles it),
     and the kept-mode anchor moves from `-probeReplay` to `-probeTraversal`.
   * The clone's Editor assembly still references `GameCore.Validation.ProbeHost` although, with the matrix gone, no
     surviving editor file uses it. That reference is left alone deliberately: it *resolves* (the assembly is in the
     clone), so it cannot break the build, whereas removing it would break the clone's Editor assembly if any other
     editor file ever needs the probe host. The dangling-reference failure mode is the one round 2 had to fix in the
     asmdef, and `tools/check_release_clone.py` now checks for it on every gate run.

## 5. The release surface, and why there is one new script

`tools/check_release_gate_free.py` is new because none of the three existing surface tools covers the union this gate's
sentence names. It takes a built release player **and** the qualification player, scans the managed assemblies and the
IL2CPP generated C++ of both for the union of qualification-only markers (GC-017's latches, GC-021's seat, GC-022's
stress, GC-023's replay fixture, the Wave 5 gate, the Wave 6 gate), and requires:

* the release player to carry **none** of them (GC-023's replay fixture included: round 2 removes it, so the release
  player must not show `ReplayParallelJobs`, `ReplayScenario`, `ProbeReplay` or `-probeReplay`);
* the qualification player to show one marker per group, so the scan is proved to be able to see them at all — a scan
  that finds nothing because it is looking in the wrong place is a FAILED check, not a clean release;
* the one mode the clone deliberately keeps (`-probeTraversal`: the GC-020 course, whose local physics scene and
  committed animation/audio output are the optional engine surface this gate is about) to be present in BOTH players,
  which is the second half of the falsifiability argument: this inspection really does read mode flags out of a built
  player. That anchor moved from `-probeReplay` to `-probeTraversal` when round 2 removed the replay mode from the
  clone;
* the production seams the gate must not have removed (`DurableOutbox`/`DeliveryKey`, `TraversalKeys`/
  `TraversalRegistration`, the three optional engine-stage types) to be **reported**, not asserted, because a stripped
  player may legitimately drop a type no surviving reference needs, and this tool's verdict is about the markers.

The telemetry switch is deliberately NOT in that list: `GAMECORE_TELEMETRY` gates `[Conditional]` call sites, which
leave no distinctive string behind, so the telemetry claim is settled where it is observable — GC-023's
`check_release_telemetry_free.py` compiles the real sources both ways and scans both assemblies — rather than by a
player-surface scan that could only look convincing. Both tools run in the gate script.

`tools/check_release_clone.py` closes the other half of the release claim, and it exists because the defect §4.7.1
describes was invisible until it was checked: **preparing** a clone is textual, so the clone's own invariants are
asserted on the clone. It fails on a surviving reference to any removed type or member (qualified accesses included), on
any asmdef reference whose assembly is no longer available, on a stripped `ProbeArguments` whose constructor call does
not match its own parameter list, on an unbalanced C# file, on a kept mode that lost its wiring, and on manifest/lock/
`Tests/` residue. It runs in the gate between the clone preparation and the release player build, so a clone that cannot
compile fails in seconds instead of after an IL2CPP build.

Both tools were exercised here in every direction they claim to detect, against synthetic operands and against the real
clone: a clean release player passes; a release player containing `ProbeW6Gate` or GC-023's replay fixture fails; a
qualification player that does not show a group's marker fails; a qualification player that does not show the kept-mode
anchor fails; and a clone with a planted stale reference fails while the restored clone passes. All five, plus the
clone's full report, are in `artifacts/w6-gate/static-checks.log`.

## 6. Requirement → implementation → observation mapping

| Requirement / test | Where implemented | Where observed (this change set) |
|---|---|---|
| P-001 genre independence | `W6CompositionAudit.WalkFamily` + `AdapterAssemblies`, `IW6Family` | `<genre>/w6-declares-no-action-physics-or-audio-surface`, `<genre>/w6-adapter-assembly-is-absent`, `traversal/w6-carries-the-optional-engine-surface` |
| P-002 participants and authority | one real world per observation group, built through the family's own request/registration/lane/publisher/pipeline | every observation |
| P-004 / P-005 identities and handles | a fresh installation identity per cycle; three delivery sessions with distinct `WorldId`s; old handles not reused | `w6-thousand-cycle-teardown-is-bounded`, `w6-reward-delivery-is-exactly-once-across-a-reload` |
| P-007 references and leases | the loop's staged lease per cycle; retired history bounded by the acquisitions the loop made | `w6-ledger-and-fence-high-water-marks-are-bounded` |
| P-008 stable ordering and determinism | the recorded-input replay, the frozen digest literals, the table check | `w6-replay-reproduces-the-rule-digest`, `w6-repeatable-digest` |
| P-034 one owner per physical domain | the local `PhysicsScene` as the course's own authority, one simulation per admitted step | `w6-one-physics-simulation-per-admitted-step` |
| P-035 world lifecycle | stop/dispose of every world the gate builds, including the delivery sessions | `w6-counters-return-to-baseline`, the suite's registry check |
| P-036 temporal models | no step without elapsed host time; one declared step per pump; one committed step per admitted command | `w6-fixed-step-course-runs`, `w6-thousand-cycle-teardown-is-bounded` |
| P-038 clocks | the integration reads the world's own step clock; the counters sample the world's own epoch/step | `w6-telemetry-counters-record-admitted-steps` |
| P-043 bounded work | a bounded cycle count, bounded delivery passes, a bounded retired history | `w6-thousand-cycle-teardown-is-bounded`, `w6-ledger-and-fence-high-water-marks-are-bounded` |
| P-045 external idempotency and durable delivery | `DurableOutbox`/`DurableDeliveryAdapter`/`WorldDeliveryOwner` over a real receiving world | `w6-reward-delivery-is-exactly-once-across-a-reload` |
| P-046 installation lifecycle | 1,000 mount/unmount cycles with a step inside each, all three genres, one loop | `w6-thousand-cycle-teardown-is-bounded` |
| P-047 in-flight lifetime | the loop's teardown settles before its world stops; no outstanding fence handle afterwards | `w6-ledger-and-fence-high-water-marks-are-bounded` |
| P-048 teardown and quarantine | `Disposed` per cycle, lease retired, no quarantine, bounded quarantine entries | `w6-thousand-cycle-teardown-is-bounded`, `w6-ledger-and-fence-high-water-marks-are-bounded` |
| P-049 recovery limits | the obligation survives its source world's unload and is reinstated explicitly; no hidden replay | `w6-reward-delivery-is-exactly-once-across-a-reload` |
| P-050 cancellation and idempotency | one operation identity per step of the loop; one attempt reused across a redelivery | `w6-thousand-cycle-teardown-is-bounded`, `w6-reward-delivery-is-exactly-once-across-a-reload` |
| P-053 checkpoint, outbox rows | the rows reinstated into a new session are the ones the world's own owner exported | `w6-reward-delivery-is-exactly-once-across-a-reload` |
| P-054 serialization discipline | the rows are read back through the same owner seam; no new format is introduced | `w6-reward-delivery-is-exactly-once-across-a-reload` |
| P-059 genre validation before freeze | the third genre runs in the same stripped player, and the audit is made from both directions | every traversal observation, `<genre>/w6-declares-...` |
| P-060 evidence and release status | this gate's evidence set and the release-surface inspection | `artifacts/w6-gate/**`, `release-gate-surface.json` |
| TEST-011 temporal models and idle worlds | the idle-pump assertion on the fixed-step course | `w6-fixed-step-course-runs` |
| TEST-015 lifecycle and managed teardown | the three-family loop and its bounded registries | `w6-thousand-cycle-teardown-is-bounded`, `w6-ledger-and-fence-high-water-marks-are-bounded`, `w6-counters-return-to-baseline` |
| TEST-016 / TEST-017 fault points and checkpoint/outbox consistency | the reinstatement after an unload, and the redelivery after acknowledgement loss | `w6-reward-delivery-is-exactly-once-across-a-reload` |
| TEST-018 Unity worlds, bootstrap and Play Mode | the EditMode suite, the player mode, and the reload matrix's traversal route | `GameCore.W6Gate.Tests`, `-probeW6Gate`, `MatrixCombinations.TraversalRoute` |
| TEST-019 engine adapters and single state authority | the physics gate as the course's authority; the adapters reachable only from the traversal genre | `w6-one-physics-simulation-per-admitted-step`, `w6-carries-the-optional-engine-surface`, `<genre>/w6-adapter-assembly-is-absent` |
| TEST-022 ordering, replay and determinism | the recorded-input replay and the five frozen digests | `w6-replay-reproduces-the-rule-digest`, `w6-repeatable-digest` |
| TEST-023 performance, bounded memory, architecture regressions | the high-water marks, the bounded retired history, the zero-control-work sampling | `w6-telemetry-counters-record-admitted-steps`, `w6-ledger-and-fence-high-water-marks-are-bounded` |

## 7. Exact commands for the Linux build host

Everything runs from the repository root. Nothing below has been run.

### 7.1 The whole gate (one command)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet \
  PROBE_RUNS=5 GC_W6_GATE_CYCLES=1000 tools/run_w6_gate.sh
```

In order: `dotnet build` + `dotnet test dotnet/GameCore.sln -c Release` (trx into `artifacts/w6-gate/trx`); the
host-side static checks (`check_game_core_csharp.py`, `check_gate_sources.py`, the contract-surface parity check, the
committed-catalog verifier and `bash -n`); the Unity resolve; EditMode (every testable package plus `GameCore.W6Gate.Tests`); PlayMode;
the card- and checkpoint-catalog generation and `tools/unity/build_probe.sh` (which regenerates the probe catalog and
builds the StandaloneLinux64 IL2CPP qualification player with High stripping); the byte-identity check of all three
committed catalogs; every player probe `PROBE_RUNS` times (including `-probeW6Gate`, `-probeReplay` and `-probeLifecycleStress`
on the qualification player); the release-surface halves; a real marker-free release player (clone, **clone verification**,
build, latch inspection, union-of-markers inspection, telemetry-player inspection, and the five family probes the clone
still carries — `run_replay_probe.sh` is no longer among them, because round 2 removed that mode from the clone); and the
documentation validator. Every Unity invocation is wrapped in `timeout` with one logged retry on a timeout.

### 7.2 This task's suites alone

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics \
  -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testFilter GameCore.W6Gate.Tests \
  -testResults "$PWD/artifacts/w6-gate/unity/w6gate-editmode.xml" \
  -logFile artifacts/w6-gate/unity/w6gate-editmode.log
```

Do not add `-quit` to a `-runTests` command (04 §10). `-testResults` with a relative path resolves against the Unity
**project** path, not the shell cwd, so pass an absolute path or copy the file out afterwards.

```sh
PROBE_RUNS=5 GC_W6_GATE_CYCLES=1000 UNITY_PROJECT=unity/GameCore.Validation \
  ARTIFACTS=artifacts/w6-gate/toolchain tools/unity/run_w6_gate_probe.sh
```

### 7.3 The reload matrix with the traversal route

```sh
UNITY="$UNITY" MATRIX_COMBINATIONS="reload-on-scene-on" MATRIX_CYCLES=2 \
  tools/unity/run_lifecycle_playmode_matrix.sh    # quick smoke: one Editor process
UNITY="$UNITY" tools/unity/run_lifecycle_playmode_matrix.sh
```

Every combination now also runs `Gc020TraversalHost.RunReloadRoute` per session; its detail (session, committed steps,
module stepped steps, engine simulations, lane publications, stop outcome, registry before/after) is recorded in the
cycle JSONL as `traversalRoute` and checked to begin with `pass`.

### 7.4 Cost of the counted cycles, and how to dial it

`GC_W6_GATE_CYCLES` (default 1000) decides how many real mount/unmount/step cycles each genre and catalog performs. If
1,000 real publications per genre prove too slow, run `GC_W6_GATE_CYCLES=100 tools/run_w6_gate.sh` and record the
reduced count in the BUILD_REPORT — the probe reports the count it actually used as its own step
(`w6-gate-cycle-count`, `resolvedCycleCount=`), and the harness refuses a run whose resolved count is not the requested
one. **Do not report a gate that ran 100 cycles as the 1,000-cycle gate.**

## 8. The reload matrix, extended

`LifecyclePlayModeMatrix.EnterPlayMode` now drives one traversal reload route per session, after its existing
per-session assertions and before it records the session: one real course world created, one movement sample admitted,
one committed step, then the world stopped and disposed — with the process-wide registry asserted back at the baseline
it had (the application host is still registered throughout). The route is `Gc020TraversalHost.RunReloadRoute()`, a
`W6GateScenario.RunReloadRoute(IW6Family)` call, so it builds the same world the gate's own observations build rather
than a model of it. Every `{domain, scene}` combination therefore covers the third genre, and the route's detail is in
both the entry log line and the per-cycle JSONL.

## 9. Inventory: proposals only

`artifacts/gates/w4-generic-profile/inventory.{md,json}` gains a `w6GateRevisions` / "W6-GATE revision notes" section
that **promotes nothing** — no row's status changes, because nothing in this change set has executed. It proposes a
status for each row this gate would evidence (`P-045`, `P-049`, `P-007`, `P-001`, `P-034`, `P-036`, `P-046`, `P-048`,
`P-008`, `P-059`, `P-060`) with the observation that would carry it and the artifacts the build host must first
produce, and records `contractChanges: none`, the five reconciliations and the explicit non-proposals.

## 10. Known gaps, assumptions and doc ambiguities

1. **Nothing has been compiled or executed here.** The highest-risk items, in the order a compiler would find them:
   (a) `W6GateScenario.cs` is ~2,300 lines of hand-written scenario code whose every external call was checked
   mechanically against its declaration (a script that resolves every `Type.Member` and every `family.*`/`fixture.*`
   member against the declaring sources found no unresolved member), but only a compiler settles it; (b) the delivery
   observation depends on GC-021's owner reporting a committed event through the world's own reader at the boundary it
   is polled at; (c) the traversal loop mounts four acceleration modifiers of the course's own declaration shape
   against a lane whose compiled schedule was built from the catalog declarations only, which is the same shape
   GC-022's stress uses for the other two genres.
2. **The traversal course has one catalog, not two.** No committed generated traversal catalog exists on this revision
   (its emission is GC-025's catalog-coverage work), so the gate runs the fixture catalog once, `RunBothW6Gate` answers
   both out-parameters with the same run, and the probe reports `catalogs=1`. The suite asserts that `ReferenceEquals`
   rather than implying a second catalog ran.
3. **The delivery destination is the seam's recording destination, not the card family's `Transfer`.** GC-021's own
   gate exercises the real card destination; this gate owns the unload/reload half, and its destination's mutation
   table is deliberately owned by the gate rather than by a world, which is exactly what makes "asked twice, mutated
   once" observable across three world incarnations. A single world mounting both families and delivering one narrative
   receipt into its own card table remains GC-024's cross-template composition.
4. **Native leaks are attributed out of process.** The in-process observation asserts that managed residue and the
   registry return to baseline and reports the process's own leak-detection mode; the 1,000-cycle native claim is
   evidenced by `tools/attribute_native_leaks.py` over the retained player logs against GC-022's resource policy, which
   the harness runs. GC-022's 57 inherited Editor allocations are attributed against the same policy by that task's own
   evidence, not by this gate.
5. **The rigidbody authority is still proved with an Editor-qualification world**, not a production rigidbody game
   (GC-020's recorded gap, unchanged here).
6. **Audio stays disabled in the headless player** (crash-139). The gate's audio observation is engine-free by design:
   the committed-audio stage drives a recording sink, and the live-device path is documented rather than run.
7. **`O-22 RecoverWorld` is still not composed** — GC-027 owns it over the restore path GC-018, the Wave 5 gate and
   this gate's reinstatement step all prove. `P-049`'s host-configured bounded retries remain unproven too.
8. **`ARGS`-free decision: the loop's step for a fixed-step genre.** The traversal course commits a step from elapsed
   host time, but its input stage must still consume one admitted movement sample or the step commit faults (GC-020's
   own note), so the gate's cycle always submits one sample and then pumps exactly the declared step. For the
   command-driven genres the cycle submits the genre's own command and pumps the million-tick frame the earlier gates
   use. `IW6Family.CyclePumpTicks` is the one declared difference between the two temporal models.
9. **`P-007`'s long-run retention budget (TEST-023) and `P-045`'s cursor/dedup scale claims remain open**: this gate
   exercises them at one-world/one-loop scale, not under memory pressure; GC-026 owns those rows.
10. **The release player no longer carries the replay mode.** Round 2 removed GC-023's replay fixture from the clone, so
    the release-surface evidence for GC-023's mechanism is now the *qualification* player's (`-probeReplay` in the
    gate's probe loop) plus `check_release_telemetry_free.py`'s both-configurations assembly scan. The release player
    still carries the traversal course, whose flag is the release-surface scan's kept-mode anchor.
11. **Driving `-probeTraversal` in a release player is a deliberate non-change.** The clone keeps the mode, and its
    harness exists, but no host has ever run that mode against a release-shaped player; adding it to the release probe
    list would make the wave gate depend on a run nobody has done and which is not part of the exit sentence ("optional
    physics/animation are absent from cards/narrative"). Recorded as an available strengthening, not taken here.
12. **The build host's first run will re-record nothing.** Every literal this gate compares is a digest over the
    observation table (not over a recorded run), so there is no "first run must write a literal back" step. GC-023's
    recorded replay digest is round 2's, already pinned in `tests/GameCore.Replay/Data/replay-record.json` and
    asserted by that task's own harness; this gate only consumes the package's `TelemetryCollector`, which needs no
    re-recording.

## 11. What the build host should look at first

1. `dotnet build dotnet/GameCore.sln` — the merge touched `dotnet/GameCore.sln`, the delivery core, the traversal
   packages and the telemetry counters; a project-level failure here is the cheapest thing to find.
2. Unity import errors in the six new runtime files and the three adapters. `W6GateScenario.cs` is the largest; the
   three adapter files are the smallest and the most mechanical.
3. `GameCore.W6Gate.Tests` in the EditMode XML: a failing observation carries its own detail string (the walk counts,
   the cycle index and code, the destination's own counters), so no re-run is needed to diagnose it.
4. The `-probeW6Gate` result's `w6-gate-cycle-count` and `w6-gate-native-leak-detection` steps first, then the
   `w6-*-digest` steps: the first two say whether the run measured what it claims, the last three say whether the
   observation table is the frozen one.
5. `artifacts/w6-gate/release-gate-surface.json`: if the qualification player does not show a group's marker, the scan
   is the problem, not the release player — that is what the second operand is for. `artifacts/w6-gate/clone-surface.json`
   is the same idea for the clone: it is produced before the release player is built, so a clone that cannot compile
   fails there rather than after an IL2CPP build.

## 12. Defects found and fixed while preparing this gate

All of these were found by mechanical audits and a read-through, not by running anything, and each was fixed before
the last commit on this branch.

1. **Three probe modes never finalized their report (§4.1).** The merges left `-probeTraversal`, `-probeGc021` and
   `-probeLifecycleStress` without their `report.CompletePositive()` call, so those three modes would have reported
   `Fail` with exit code 1 in the player and their harnesses would have failed. Fixed: each arm calls it itself.
2. **Missing `using` directives in six of the eight new C# files.** A script that resolves every type name in a new file
   against its declaring namespace and the file's own `using` list found: `GameCore.Composition` missing from the three
   genre adapters and the family contract (the cycle payload type), `GameCore.Gameplay.Cards` missing from the card
   adapter (`CardTableKeys`), `GameCore.Planning` and `GameCore.Unity.Runtime.Integration` missing from the composition
   audit (`OwnershipStageDescriptor`/`DescriptorStage` and `CatalogPluginDeclaration`/`PipelineDescriptorReport`), and
   `GameCore.Unity.Adapters.Authority` and `GameCore.Unity.Fixtures` missing from the scenario and the narrative
   adapter. Every one of them is a `CS0246` the build host would have hit first.
3. **The physics observation never asked the engine to step.** The first rewrite of `w6-one-physics-simulation-per-
   admitted-step` admitted five steps and asserted five simulations without ever calling
   `PhysicsAuthorityGate.TrySimulateExactlyOnce`, so the assertion could only have failed. Fixed: the observation now
   drives one admitted step and one simulation at a time, which is the shape GC-020's own physics observation has.
4. **The reload route asserted a simulation it did not drive.** `RunReloadRoute` asserted `engineSimulations == 1` while
   never stepping the physics gate. Fixed the same way, and the assertion now distinguishes a genre that owns an engine
   authority from one that does not.
5. **The delivery observation over-specified the world's event count.** It required the receiving world to have
   committed exactly one event; a world that committed more would have failed even though the *obligation* count — the
   claim this gate makes — was still one. Fixed: the source claims exactly one event and refuses the rest, the
   assertion is about the obligation, and the observed/unclaimed counts are reported beside it.
7. **The release clone's `ProbeArguments` call did not match its constructor (§4.7.1).** The previous revision left
   `bool traversal,` in the signature while dropping `traversal` from the call, so the clone passed 14 arguments to a
   15-parameter constructor. Found by re-deriving both replaced strings from the merged sources during the round-2
   reconciliation; `tools/check_release_clone.py` now checks the agreement on every gate run.
8. **A surviving clone file called a member the clone removes (§4.7.1).** The reload matrix's traversal route lives in
   a Wave 6 gate file, so the clone's Editor assembly could not compile. The clone now removes the matrix; the
   qualified reload-matrix runs are unaffected.
9. **The source audit had a false negative of its own.** Its first version built the comment-stripped body by handing
   a joined multi-line string to a helper written for one line, which truncated everything after the first `//` and
   made the using-coverage and ambiguity passes check almost nothing while still printing `none`. Caught by planting a
   broken copy of a real file and requiring the check to fail on it - which is why `tools/check_gate_sources.py` ships
   with that falsifiability experiment written down in the static-checks log, and why the member-shadowing rule for a
   simple name like `CommandRoute` is stated where a reviewer can overrule it.
10. **The harness pinned a hard-coded 1,000.** Its `cycles=`/`completed=` clauses now derive from
   `GC_W6_GATE_CYCLES`, so a deliberately reduced run reports and is checked against the count it really used instead of
   failing a clause nobody can satisfy honestly.
