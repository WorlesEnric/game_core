# W7-GATE HANDOFF — Wave 7 integration gate (merged-revision join of GC-024..GC-027)

Branch `w7-gate` = `main` + GC-025, with `origin/gc-027`, `origin/gc-026` and finally `origin/gc-024` merged into it
here and reconciled by hand. All four Wave 7 tasks are now on this one revision, so the gate's own exit sentence is
fully wired: the reference-conformance tables and the combined cross-template world (GC-024), complete
IL2CPP/headless catalog coverage (GC-025), recorded benchmark data and budget decisions (GC-026) and faulted
checkpoint/outbox recovery (GC-027) are each re-run on the merged kernel.

**Status of every build/test/player command in this document: `NotRun (pending orchestrator build host)`.** This host
has no Unity, no .NET SDK, no Mono and no C# compiler, so nothing in this change set has been compiled, imported,
executed or built here. What did run is interpreter-level only and is recorded verbatim in
`artifacts/w7-gate/static-checks.log`: the host-side C# checker (623 files), the gate-source invariant checker for the
whole repository and for this gate's own change set (including a new call-site-local check, §4.9), the merged solution's
GUID/configuration audit, the frozen-table/digest/probe-step cross-checks for the Wave 6 literals and this gate's own W7
table, the contract-surface parity check, the four committed catalogs and the reachability/bake mirrors, the
budget-record checker, the link.xml checker, the release-clone preparation **run for real** followed by the clone
checker **in both a pre-import and a simulated post-import state** and the checker's own two-direction self-test, four
planted-defect directions on the real clone, `bash -n` over every shell script this gate owns, `py_compile` over every
Python tool it touches, the `.meta` generator (idempotent: 0 creations) and the documentation validator (self-test +
full). None of those is a build or a test result.

## 1. Summary

The Wave 7 exit gate, verbatim from `docs/game-core/09-implementation-guide.md`:

> "All reference transition tables and cross-template flow pass; complete IL2CPP/headless catalog coverage runs;
> faulted checkpoint/outbox recovery passes; benchmark data and budget decisions are recorded. Production fixes
> require affected gates rerun on the new revision."

Wave 7 is an **integration** gate: it owns no kernel behaviour and adds no mechanism. Its subject is the JOIN of the
Wave 7 tasks on one merged revision — which is also why the last sentence matters, because each task's evidence was
taken on an earlier revision and merging them changed shared kernel files (GC-026 changed the derivation indexes;
GC-027 changed the checkpoint/restore publication path; GC-024 changed the traversal recipe set and added a mounted
reward installation). So every observation this gate records is a **re-run**, through the owning task's own runner,
of that task's own acceptance sequence.

One runner (`W7GateScenario`) records **sixteen** observations and one digest step; one player mode (`-probeW7Gate`)
reports them; one EditMode assembly (`GameCore.W7Gate.Tests`) asserts them; one script (`tools/run_w7_gate.sh`) drives
the whole thing. The five groups are:

1. **GC-025's own coverage sequence**, re-run over the merged kernel (`CatalogCoverageScenario.Run()`, the same
   sequence `-probeCatalogCoverage` drives), in the qualification player and again in the marker-free release player.
2. **GC-012/TEST-008's incremental-vs-clean equivalence**, re-established at the declared 10,000-target scale over the
   recorded seed series, for the four edit kinds GC-026's index change could have broken (install-only mount —
   the path `BuildIncremental` was added for — subtree reparent, 1,000-target spawn, retraction of those targets).
   Each edited snapshot is derived BOTH incrementally from the previous accepted publication and cleanly from
   scratch, and the two must agree on the canonical result hash and the effective assembly count.
3. **Two merge invariants**, because they are the failure modes a five-way merge really has: every probe mode must
   still parse out of the merged `ProbeArguments` exactly one at a time (a dropped mode removes a gate from the
   release process silently), and the ten recorded budget rows must still be the declared ten.
4. **GC-027's recovery sequence**, re-run for all three genres through GC-027's own runner, with the two fault points
   the gate sentence names recorded as their own observations: **postwrite-apply** and **restart**.
5. **GC-024's conformance group** (NEW in this revision): the three transcribed 07 tables re-run through the genre
   hosts' own `RunConformanceCards/Narrative/Traversal` entry points, the combined narrative+cards reward flow through
   `ConformanceCrossWorld.Run()`, and GC-024's assembly/genre audit through
   `ConformanceCrossWorld.AuditCombinedComposition()`. The first two are the gate sentence's "all reference transition
   tables pass" and "cross-template flow"; the third is GC-024's own third acceptance clause.
6. **A release-kept mode, `-probeRecoverySmoke`**, which drives the PRODUCTION `WorldRecovery.Recover`/`Restart`
   with a real `FileCheckpointStore` and **no fault latches** in the marker-free release player. This is the backlog
   item the task asked for, and it is the first exercise of O-22 by a shipping-shaped build in this repository.

## 2. Commits on this branch

| Commit | Contents |
|---|---|
| `Merge GC-027 into W7 gate` | GC-027, with the probe host's conflicted regions, the release-clone strip lists and the constructor needle reconciled by hand (§4.1, §4.2). |
| `Merge GC-026 into W7 gate` | GC-026, same shape: every mode kept, the solution file unioned with unique GUIDs, the manifest's testables unioned, the clone strip lists unioned (§4.1–§4.4). |
| `W7-GATE: the gate scenario, its probe and the recovery smoke` | `W7GateScenario.cs`, `ProbeW7Gate.cs`, `ProbeRecoverySmoke.cs`, the two new modes' wiring, and the `PostwriteApplyObservation`/`RestartObservation` constants GC-027's table is now built from. |
| `W7-GATE: the EditMode suite, the two harnesses and the gate script` | `Tests/W7Gate/**`, `tools/unity/run_w7_gate_probe.sh`, `tools/unity/run_recovery_smoke_probe.sh`, `tools/run_w7_gate.sh`, `tools/check_budget_record.py`. |
| `W7-GATE: reconcile the release tooling with the merged revision` | `tools/unity/prepare_gc017_release_project.py`, `tools/check_release_clone.py`, `tools/check_release_gate_free.py`, `tools/check_link_xml.py`, `tools/check_gate_sources.py`, and the W6 wrapper's leak-log path fix. |
| `W7-GATE: the handoff, the static checks and the inventory proposals` | `artifacts/w7-gate/**`, `artifacts/gates/w4-generic-profile/inventory.{md,json}`. |
| `Merge GC-024 into W7 gate` | GC-024, with the sln union, the manifest/lock union, the probe-mode union, the recipe-source union and the `engine_free` restoration reconciled by hand (§4.6–§4.8). |
| `W7-GATE: the conformance tables and the combined world in the gate` | The conformance group in `W7GateScenario`, the recomputed digest literal in `ProbeW7Gate`, the three new EditMode tests, the harness's new steps and clauses, and the conformance probe in `tools/run_w7_gate.sh`. |
| `W7-GATE: the release-clone checker's scope and the Parse call-site check` | `tools/check_release_clone.py` (source-scoped walk, lock rules, `--self-test`), `tools/check_gate_sources.py` (the `Parse` call-site locals check), and the `resultPath` local this gate had dropped. |
| `W7-GATE: the handoff, the static checks and the inventory for the merged revision` | `artifacts/w7-gate/**`, `artifacts/gates/w4-generic-profile/inventory.{md,json}`. |

## 3. Files created

### The gate (every `.cs` file carries a Unity `.meta` with a unique GUID, generated by `tools/make_unity_metas.py`)

| File | Contents |
|---|---|
| `unity/.../Runtime/W7GateScenario.cs` | `W7GateStep`, `W7GateScenarioResult`, the frozen table (`ProcessObservationNames` + `FamilyObservationNames` × three families + `ConformanceObservationNames`), `ObservationNames()`, `ExpectedDigest()`, `Run()`, the four process observations, the per-family recovery group, GC-024's three conformance observations, and the falsifiable digest step. |
| `unity/.../Runtime/ProbeW7Gate.cs` | The `-probeW7Gate` player mode: the quoted digest literal `f7853e42…`, `ExpectedObservations`, one `ProbeOutcome` per recorded step, and the `w7-frozen-digest-agrees-with-the-table` step that compares the build's own table digest against the quoted literal. |
| `unity/.../Runtime/ProbeRecoverySmoke.cs` | The `-probeRecoverySmoke` release mode: eight observations per family (real-file capture and publication, envelope reload, the two refusals, recover, restart, the recovered world's own state, teardown) plus one digest step. Needs `-probeResult` (it is the directory the checkpoint files are published into). |
| `unity/.../Tests/W7Gate/GameCore.W7Gate.Tests.asmdef` | Editor-only EditMode assembly (byte-identical to the W6Gate asmdef apart from the name/rootNamespace). |
| `unity/.../Tests/W7Gate/W7GateIntegrationTests.cs` | One `[Test]` per frozen observation — sixteen of them, each reading its name out of the table at a fixed position and never writing it — plus the digest test that recomputes the literal from the table alone and compares it with the probe's constant, the "recorded run is the table plus its digest step" test, the probe/suite table-agreement test, and a `[TearDown]` asserting the world registry is back at zero. |
| `tools/unity/run_w7_gate_probe.sh` | The `-probeW7Gate` harness: `PROBE_RUNS` runs, strict JSON, all eighteen steps by name, the claim clauses (including the conformance group's `declaredTables=4`, `ran=3/3`, `crossTemplateFlow=pass`, `cross-composition-audit: clean`), the frozen literal, and native leak attribution over every retained run log. |
| `tools/unity/run_recovery_smoke_probe.sh` | The `-probeRecoverySmoke` harness: seventeen steps by name, the frozen smoke literal, the claim clauses its own details must carry, and the assertion that the two real `.checkpoint` files exist on disk. |
| `tools/run_w7_gate.sh` | The gate (§6). |
| `tools/check_budget_record.py` | The document half of "benchmark data and budget decisions are recorded": parses the ten row ids out of `PerformanceBudgets.cs`, requires one `## budget.<id>` section per id with a Decision and Evidence line, requires every Evidence path to exist, requires the project-owner deferral sentence, and refuses a section that claims a full-duration measurement while deferred. |
| `artifacts/w7-gate/static-checks.log`, `artifacts/w7-gate/HANDOFF.md` | The verbatim host-side checks and this document. |

### Modified (shared surfaces)

| File | Change | Why it is safe |
|---|---|---|
| `unity/.../Runtime/ProbeArguments.cs` | **Three** modes' worth of additive blocks — this gate's two (`w7Gate`, `recoverySmoke`) and GC-024's one (`conformance`) — plus two repairs (§4.9). | The additive block every earlier task added for its own mode; every earlier mode's branch is untouched. |
| `unity/.../Runtime/ProbeRunner.cs` | Three dispatch arms, three report-identity arms, and `ProbeRecoverySmoke.Run(report, arguments.ResultPath)`. | Additive. |
| `unity/.../Runtime/W7GateScenario.cs` | The conformance group (names + three step methods), the `-probeConformance` term in the merge-invariant mode list, and a safe `default: return false` in `IsModeSet` (an index the switch does not know is now a *reported* missing mode rather than silently another mode's value). | The table's digest literal and the probe's quoted copy were updated together; `check_gate_sources.py` recomputes both from the table. |
| `unity/.../Runtime/Gc027Scenario.cs` | The two fault-point observation names GC-027's own definition names are now `const`s (`PostwriteApplyObservation`, `RestartObservation`) and `ObservationNames` is built from them. | The resulting table is byte-identical, so GC-027's three frozen digest literals are unchanged (§4.5). |
| `tools/unity/prepare_gc017_release_project.py` | GC-024's conformance fixture files, package, mode and asmdef reference join the strip lists; the W7 gate's two files and mode are already there. | Verified by running the script for real (§8). |
| `tools/check_release_clone.py` | Source-scoped walk (§4.10), lock rules that are correct before AND after a Unity import, GC-024's removed types, a boundary-aware removed-mode test, a `wired` test that requires the assignment, and a new `--self-test`. | Verified in both directions plus five self-test cases (§8). |
| `tools/check_release_gate_free.py` | A `w7-gate` marker group, and the single kept-mode anchor becomes a tuple of three. | The release-surface check must now prove three kept modes, which is stronger. |
| `tools/check_link_xml.py` | Permits exactly one kernel-assembly type-level preserve (GC-027's `WorldRecovery`) and requires the permitted fixture preserves to carry the attribute. | A GC-025 tool that failed on the merged revision; the exception is one assembly and one exact type list (§4.11). |
| `tools/check_gate_sources.py` | Recomputes this gate's frozen table (scenario, probe, suite, harness) in addition to the Wave 6 literals, and adds the `ProbeArguments.Parse` call-site-local check that would have caught the defect §4.9 describes. | Additive; the Wave 6 block is untouched, and the new check runs only when `ProbeArguments.cs` is in the change set. |
| `tools/check_game_core_csharp.py` | The union of all four tasks' `TARGETS` and `engine_free` entries, with `tests/GameCore.Replay` **restored** (§4.7). | A checker that had silently lost a coverage entry on GC-024's branch. |
| `tools/unity/run_w6_gate_probe.sh` | Absolutizes `ARTIFACTS` before deriving the player log path. | The fix GC-026 asked for (§5). |
| `artifacts/gates/w4-generic-profile/inventory.{md,json}` | A `GC-024` section and a `W7-GATE` section: **proposals only**, no row promoted. | A row may only be promoted from an archived passing run. |

## 4. Merge reconciliations (each recorded)

1. **The probe host.** Every conflict in `ProbeArguments.cs` and `ProbeRunner.cs` was resolved by keeping **every**
   mode from all five tasks: one flag, one property, one `Parse` branch, one `IsProbeInvocation` term, one dispatch
   arm, one report-identity arm and one constructor argument per mode. The merged host carries **twenty-five** modes,
   which the gate's own mode-parse invariant then re-checks by parsing each flag.
2. **The release-clone preparation.** All conflicts were resolved as a union, and the replacement strings were
   **re-derived from the merged sources** rather than taken from either side: the qualification-mode expression becomes
   `|| Gc013 || W4Gate || Gc018 || Gc019 || Traversal`, and the constructor needle becomes the merged 16-argument call
   replaced by `w4Gate, gc018, gc019, traversal, catalogCoverage, recoverySmoke, resultPath)`. **Two modes are
   deliberately KEPT** and stay in both the signature and the call: `-probeCatalogCoverage` (GC-025's release coverage
   run) and `-probeRecoverySmoke`. Everything else in the list removes only qualification-only modes, now including
   `-probeConformance`.
3. **`dotnet/GameCore.sln`.** Unioned by hand: 36 project entries, 36 `EndProject`s, no duplicate GUID and no
   configuration row without a project (144 rows, verified programmatically). GC-026's Benchmark pair kept
   `…0035`/`…0036`; GC-024's ReferenceConformance pair, which reused the same `…0033`/`…0034` IDs, moved to
   `…0037`/`…0038`.
4. **`manifest.json` / `packages-lock.json`.** The dependency union holds every package all five tasks added — including
   `com.gamecore.gameplay.rewards` (GC-024's production reward installation) and
   `com.gamecore.reference-conformance` — and `testables` now names fifteen packages. The lock is a superset of the
   manifest's dependencies with every `com.gamecore.*` entry present.
5. **GC-027's observation table.** Turning the two fault-point names into constants changes no recorded name and no
   order: the table is the same twenty-one strings in the same sequence, so GC-027's three frozen digest literals are
   unchanged — which the `-probeRecovery` harness would fail on if it were not.
6. **GC-024's `Gc020TraversalHost` recipe source.** GC-024 hardcoded `TraversalCourseRecipes.Catalog(...)` where GC-025
   had introduced the `recipeSource.Catalog(...)` indirection; the union keeps **both intents** — the source is the one
   the caller materialized (GC-025) and the three recipes only this gate declares (the course entity, the opted-in
   runner and GC-024's descriptor-excluded P-016 variant) are still appended explicitly. Verified that
   `RuntimeCatalogCoverageRecipeSource.Catalog` delegates to `TraversalCourseRecipes.Catalog`, so the indirection is a
   strict superset and no recipe is declared twice.
7. **GC-024's regression in the host-side C# checker.** GC-024's branch had **dropped**
   `ROOT / "tests/GameCore.Replay"` from `check_game_core_csharp.py`'s `engine_free` tuple (its commit `20b4648`
   replaced the closing lines of that tuple while inserting its own comment). The union restores it and adds
   `tests/GameCore.Benchmarks` and `tests/GameCore.ReferenceConformance`, so the merged revision checks one more
   engine-free assembly than either side did. This is a coverage increase on merged code, not a task change.
8. **A malformed doc comment two merges produced.** The GC-026 conflict resolution had dropped the `/// <summary>`
   opener above the `Benchmark` property, and the GC-024 merge produced the same shape once more above
   `CatalogCoverage`; both are repaired here. The class of defect — a doc comment ending in `</summary>` that never
   began — is exactly what a compiler warns about and what a text-level merge introduces, and
   `tools/check_gate_sources.py`'s balance check cannot see it.
9. **A REAL DEFECT this gate had shipped, found and fixed here.** The `ProbeArguments.Parse` local
   `string? resultPath = null;` was missing from the pre-GC-024 revision: an edit of mine had replaced a range that
   included it without restating it, leaving `Parse` referencing an undeclared identifier — a C# compile error
   (CS0103) that the balance check, the member-resolution check (the name exists as a *field*, not a local) and the
   release clone's constructor-arity check (the arity still matched) all could not see. It is fixed, and
   `tools/check_gate_sources.py` now **checks the class**: every identifier the constructor call passes must be a local
   the same method declares. Verified by removing the local again and watching the check fail (§8.3).
10. **The release-clone checker's false positives after a Unity import** (reported by both GC-024 and GC-026: ~27
    problems in third-party cached source and the regenerated lock). Fixed on three axes: (a) the walk is explicitly
    **the clone's own sources** and prunes `Library/`, `Temp/`, `Logs/`, `Builds/`, `UserSettings/`, `obj`, `bin` at
    every level and inside the `asmdef` scan too; (b) the lock rules are correct in **both** states — the preparer
    deletes the lock and Unity regenerates it, so a present lock is not a problem by itself, while a
    qualification-only package name in it, or a `com.gamecore.*` entry the manifest does not declare, are defects
    either way (a regenerated lock is a resolution *of the manifest*, so an undeclared gamecore entry means the
    manifest edit was lost); (c) the checker gained `--self-test`, which builds a synthetic post-import clone and
    requires a generated-tree-only defect set to PASS and a planted stale reference in the clone's own sources to
    FAIL. The gate runs the checker **before** it builds the clone (the strictest moment) and runs the self-test as a
    host-side step. §8.4 records what I could and could not reproduce on this host: the pre-fix checker is clean on my
    synthetic noise (so I could not reproduce their exact 27 here), which is precisely why the fix is expressed as an
    explicit scope plus a falsifiable self-test rather than as a number I claim to have matched.
11. **`link.xml` and its checker.** GC-027's build report records that High managed stripping dropped `WorldRecovery`
    once its only caller (the qualification probe) left the clone, and that `Assets/link.xml` now preserves the
    production type. The checker GC-025 owns and runs — `tools/check_link_xml.py` — was not updated, so on the merged
    revision it reported three problems and **GC-025's own gate could not pass**. The reconciliation is deliberately
    narrow: one assembly, one exact type list, and the checker now also *requires* the permitted preserves to be
    present and to carry the attribute. All six negative directions were exercised (§8.4).

## 5. `-probeRecoverySmoke`, and why it is shipping-shaped

`-probeRecoverySmoke` exists because the gate's backlog item asks for a recovery smoke that survives in a marker-free
build. It references **no** `Gc027*`, `W5*`, `W6*`, `W7*`, `Conformance*`, benchmark, replay, fault-latch or
`GameCore.Unity.Runtime.Faults` type and carries no `#if GAMECORE_FAULT_INJECTION` region — verified by a scan of the
file and by the release-clone checker, whose removed-type list the conformance group joined in this revision. It drives
only production seams: `CheckpointPublication.CaptureAndPublish`, `WorldRecovery.Recover`, `WorldRecovery.Restart`,
`FileCheckpointStore`, `UnityCommittedBoundaryReader` and GC-018's own restore builder.

**One documented deviation, and it is a real constraint of the production API.** The mode's "recover" step cannot use
a `TryCreate`-fresh world as its source and report `Created->Disposed`, because `UnityWorldHost.CreateCore` sets the
lifecycle to `Running` before `TryCreate`/`TryCreateUnexposed` returns, and `WorldRecovery.Recover` refuses any source
that is `Running`/`Paused` with `TooLate`. There is no API that returns a registered host to `Created`. So the second
source is created normally and then retired into the terminal non-live state a recovery actually reads through the
production driver seam (`createdHost.Driver.LatchFault(...)`, the same path `WorldHost` itself uses for a post-write
failure), and the step's detail reports `sourceLifecycle=Faulted->Disposed` plus a `sourceNote` explaining it. Every
other claim of that step is what the task asked for and is asserted verbatim.

## 6. Requirement → implementation → observation mapping

| Requirement / test | Where implemented | Where observed (this change set) |
|---|---|---|
| P-001 genre independence | GC-027's runner re-run for three genres from each genre's own host; the budget/merge checks are genre-free; GC-024's audit walks the combined composition for cross-family edges | `narrative\|cards\|traversal/w7-recovery-*`, `w7/w7-probe-mode-dispatch-keeps-every-mode`, `w7/w7-conformance-genre-audit-is-clean` |
| P-002 participants and authority | every world built through the genre's own request/registration/lane/publisher; the smoke drives the production composition; the conformance tables drive real worlds per stage | every observation; `release/probe-recovery-smoke.json`, `w7/w7-conformance-tables-repass` |
| P-004 / P-005 identities and handles | a source session distinct from every recovered incarnation; the smoke requires the disposed source to be unresolvable | `…/w7-recovery-restart-without-in-process-state`, `recovery-smoke-recovered-world-is-authoritative` (`oldSessionRefused=true`) |
| P-008 deterministic evidence | one digest over the frozen table, recomputed from the table alone by the suite and compared with the probe's quoted literal; the conformance runs carry normalized trace digests | `w7/w7-gate-digest`, `w7-frozen-digest-agrees-with-the-table`, `GameCore.W7Gate.Tests` |
| P-009 catalog roots and reachability | the coverage sequence re-run over the merged kernel, in both shapes | `w7/w7-catalog-coverage-repasses-on-the-merged-kernel` |
| P-013 / P-014 / P-016 / P-025 transitions | GC-024's transcribed 07 tables, executed over the genre that owns each | `w7/w7-conformance-tables-repass` |
| P-022 bounded work / P-023 incremental equivalence | the 10,000-target equivalence group over the recorded seed series, four edit kinds, incremental vs clean | `w7/w7-incremental-derivation-matches-a-clean-derivation`, `artifacts/w7-gate/trx/` |
| P-030 / P-031 fail-stop and no partial exposure | GC-027's postwrite-apply observation re-run per genre | `*/w7-recovery-postwrite-apply-fault` |
| P-032 checkpoint round trip | the smoke's envelope reload and the recovered world's own live rows | `recovery-smoke-published-envelope-reloads-from-disk`, `recovery-smoke-recovered-world-is-authoritative` |
| P-035 world lifecycle / P-048 teardown | every world the gate, the smoke and the conformance stages build is stopped and disposed; registries asserted back at their baselines | `recovery-smoke-teardown-is-clean`, `w7/w7-conformance-tables-repass` (per-stage teardown), the suite's `[TearDown]` |
| P-043 / P-045 cross-template durability | GC-024's combined narrative+cards world through the durable/idempotent reward seam; the smoke's exactly-once refusals | `w7/w7-conformance-cross-template-flow-repasses`, `recovery-smoke-refuses-a-live-source`, `…-restart-publishes-a-new-session` |
| P-049 recovery limits | a live source refused `TooLate`, a retired one `StaleHandle`, a restart from the store alone owing nothing | `recovery-smoke-refuses-a-live-source`, `…-refuses-a-stopped-source` |
| P-053 serialization discipline | the published envelope reloads with the identical stored identity and no `.partial` artifact; the restore reports what it really rebuilt | `…published-envelope-reloads-from-disk`, `…recover-publishes-a-new-session` |
| P-057 audit / P-059 genre validation | GC-024's assembly/genre audit where a player can honestly compute it (the loaded-assembly half); the build-time half stays EditMode/host | `w7/w7-conformance-genre-audit-is-clean` |
| P-058 headless reference | the coverage sequence's headless canonical step runs in the player | `w7/w7-catalog-coverage-repasses-on-the-merged-kernel` |
| P-060 evidence and release status | this gate's evidence set and the release-surface inspection over the union of markers | `artifacts/w7-gate/**`, `release-gate-surface.json` |
| TEST-001 / TEST-020 catalog coverage | GC-025's own sequence in both players | the same observation, plus `probe-catalog-coverage{,-release}.json` |
| TEST-006 / TEST-008 / TEST-010 / TEST-014 / TEST-021 | GC-024's four tables plus the combined world, re-run on the merged kernel | `w7/w7-conformance-*`, `toolchain/probe-conformance.json`, `toolchain/traces/` |
| TEST-008 / TEST-023 incremental equivalence and budgets | the equivalence group and the ten-row record assertion | `w7/w7-incremental-derivation-matches-a-clean-derivation`, `w7/w7-declared-budget-rows-are-the-recorded-ten`, `host/budget-record.json` |
| TEST-014 / TEST-016 faulted recovery and fault points | GC-027's sequence plus the two named fault points | `*/w7-recovery-sequence-repasses`, `*/w7-recovery-postwrite-apply-fault`, `*/w7-recovery-restart-without-in-process-state` |
| TEST-018 Unity worlds and Play Mode | the EditMode suite, the player mode and the host-side checks | `GameCore.W7Gate.Tests`, `-probeW7Gate` |

## 7. The GC-024 merge, as performed

The design in the previous revision of this document said a GC-024 merge would need exactly one new observation group
plus its package in the solution and the manifest, and nothing else. That is what happened:

1. `W7GateScenario` gained `ConformanceObservationNames` (three names), one `AddConformanceSteps(steps)` call in the
   documented emission position (after the per-family recovery group), and the three step methods. The frozen table
   went from 13 to 16 names, so **the digest literal changed**: `f7853e42502c146fadb44e15e612a7ee67fd2605b6d2cff1951591567c719102`.
   `tools/check_gate_sources.py` now recomputes that literal from the table's four arrays and compares it with the
   probe's constant and the harness's pin, so a group added or dropped without updating the literal fails there.
2. `ProbeW7Gate.ExpectedDigest` carries the new literal and `ExpectedObservations` follows the table automatically.
3. `run_w7_gate_probe.sh` gained the three names (its step list is 18 fragments now), the new claim clauses, and the
   new literal.
4. `tools/run_w7_gate.sh` gained GC-024's own probe (`run_conformance_probe.sh`) in the qualification probe list, so
   the player runs the conformance mode twice like every other probe, and its harness in the `bash -n` list. GC-024's
   dotnet and Unity suites needed no wiring: the gate's invocations are unfiltered over every testable assembly, so
   they participate through the solution and the manifest's `testables`.
5. The inventory's `GC-024 revision notes` section still promotes nothing; this gate's section records the merge and
   the reconciliation.

## 8. Exact commands for the Linux build host

Everything runs from the repository root. **Nothing below has been run.** The host-side checks this task DID run are in
`artifacts/w7-gate/static-checks.log`; they need no Unity and no SDK.

### 8.1 The whole gate (one command)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet \
  PROBE_RUNS=2 tools/run_w7_gate.sh
```

In order: `dotnet build` + `dotnet test dotnet/GameCore.sln -c Release` over **every** test project (trx into
`artifacts/w7-gate/trx`), then the two named equivalence suites a second time with their own trx; the host-side checks
(C# shape, gate sources with this gate's `--file` list, contract-surface parity, the four committed catalogs, the
catalog-emitter mirror, the reachability manifest, the baked coverage artifact, the fingerprint self-test, the
budget-record checker, the release-clone checker's self-test, `link.xml` for the qualification project, and `bash -n`
over six scripts); the Unity resolve; EditMode and PlayMode **unfiltered** over every testable package (every gate
assembly W1..W7 and every task suite); the five catalog codegens and the coverage bake followed by `git diff
--exit-code` over the four generated trees; the StandaloneLinux64 IL2CPP qualification player via
`tools/unity/build_probe.sh`; **every** player probe twice (GC-001 positive and expected-negative, world dispatch, the
W1..W6 gates, the three genre probes, GC-013, GC-017 faults, GC-018/GC-019/GC-021, GC-022 lifecycle stress, GC-023
replay, GC-024 conformance, GC-025 catalog coverage, GC-027 recovery and this gate's `-probeW7Gate`); the release
surface (the fault and telemetry source halves, the prepared clone and its own checker, `link.xml` for the clone, the
release player build, the latch/telemetry/gate-surface player inspections, then the kept modes twice each — the family
probes, `-probeCatalogCoverage` with `PROBE_SHAPE=release`, and `-probeRecoverySmoke`); ONE short benchmark
correctness diagnostic; and the documentation validator. Every Unity invocation is wrapped in `timeout` with one
logged retry on a timeout.

### 8.2 The pieces, if a single step needs re-running

```sh
# the plain-dotnet half
$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release
$HOME/.dotnet/dotnet test dotnet/GameCore.sln -c Release --no-build \
  --logger trx --results-directory artifacts/w7-gate/trx

# the two suites the gate names, by name
$HOME/.dotnet/dotnet test dotnet/tests/GameCore.Derivation.Tests -c Release --no-build \
  --filter "FullyQualifiedName~GameCore.Derivation.Tests.IncrementalAgreementTests|FullyQualifiedName~GameCore.Derivation.Tests.OracleAgreementTests"
$HOME/.dotnet/dotnet test dotnet/tests/GameCore.Benchmarks.Tests -c Release --no-build \
  --filter "FullyQualifiedName~BenchmarkFixtureDerivationTests"

# the host-side checks (no Unity, no SDK)
python3 tools/check_game_core_csharp.py
python3 tools/check_gate_sources.py --json artifacts/w7-gate/host/gate-sources.json
python3 tools/check_contract_surface_parity.py
python3 tools/check_budget_record.py --json artifacts/w7-gate/host/budget-record.json
python3 tools/check_release_clone.py --self-test
python3 tools/check_link_xml.py --project unity/GameCore.Validation

# the qualification player and this gate's own mode
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity UNITY_PROJECT=unity/GameCore.Validation \
  ARTIFACTS=artifacts/w7-gate/toolchain tools/unity/build_probe.sh
PROBE_RUNS=2 ARTIFACTS=artifacts/w7-gate/toolchain tools/unity/run_w7_gate_probe.sh
PROBE_RUNS=2 ARTIFACTS=artifacts/w7-gate/toolchain tools/unity/run_conformance_probe.sh
PROBE_RUNS=2 ARTIFACTS=artifacts/w7-gate/toolchain tools/unity/run_catalog_coverage_probe.sh
PROBE_RUNS=2 ARTIFACTS=artifacts/w7-gate/toolchain tools/unity/run_recovery_probe.sh

# the release shape: prepare, check the clone BEFORE the build, build, then the kept modes
python3 tools/unity/prepare_gc017_release_project.py
python3 tools/check_release_clone.py --clone unity/GameCore.ReleaseCheck
python3 tools/check_link_xml.py --project unity/GameCore.ReleaseCheck
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity UNITY_PROJECT=unity/GameCore.ReleaseCheck \
  ARTIFACTS=artifacts/w7-gate/release tools/unity/build_probe.sh
PROBE_PLAYER=unity/GameCore.ReleaseCheck/Builds/Linux64/GameCoreProbe.x86_64 UNITY_PROJECT=unity/GameCore.ReleaseCheck \
  ARTIFACTS=artifacts/w7-gate/release PROBE_RUNS=2 PROBE_SHAPE=release \
  tools/unity/run_catalog_coverage_probe.sh
PROBE_PLAYER=unity/GameCore.ReleaseCheck/Builds/Linux64/GameCoreProbe.x86_64 UNITY_PROJECT=unity/GameCore.ReleaseCheck \
  ARTIFACTS=artifacts/w7-gate/release PROBE_RUNS=2 tools/unity/run_recovery_smoke_probe.sh

# the ONE short benchmark correctness diagnostic (NOT the deferred full-duration catalogue)
BENCH_RUNS=1 BENCH_WARMUP=1 BENCH_DURATION=2 BENCH_REPETITIONS=5 BENCH_TIMEOUT=1800 \
  PROBE_PLAYER=unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 \
  UNITY_PROJECT=unity/GameCore.Validation ARTIFACTS=artifacts/w7-gate/benchmark tools/run_benchmarks.sh
```

### 8.3 The defect the new check catches (a demonstration, not a claim)

```sh
# With the local present: ok. Remove `string? resultPath = null;` from ProbeArguments.Parse and re-run:
python3 tools/check_gate_sources.py --file unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeArguments.cs
#   -> "the constructor call passes `resultPath`, which `Parse` never declares as a local (CS0103)"; exit 1
```

### 8.4 The host-side checks that ran **here** (recorded verbatim in `artifacts/w7-gate/static-checks.log`)

`check_game_core_csharp.py` (623 files, ok); `check_gate_sources.py` for the repository and for this gate's own
`--file` list (both `VERDICT: ok`, with the W7 table resolved `harness=True probe=True suite=True` and the
`Parse` call-site check reporting 26 declared locals, no problems); the merged solution's 36 projects / no duplicate
GUIDs / no incomplete configuration set; an independent recomputation of the W7 table's digest literal and its
agreement with the probe, the harness and the suite; the manifest/testables/lock cross-check; the contract-surface
parity check; `verify_generated_catalog.py` for all four catalogs; the catalog-emitter mirror's `--self-check`; the
reachability manifest and bake `--check`s; the fingerprint tool's `--self-test`; `check_budget_record.py`
(`VERDICT: pass`, 10/10 rows); `check_link_xml.py` for the qualification project and for the prepared clone, plus four
planted-defect directions; the release-clone preparation **run for real**, the clone checker clean **before** the build
and clean again on a **simulated post-import tree** (Library/PackageCache third-party sources + `Temp/`, `Logs/`,
`Builds/`, `UserSettings/` + a 40-entry regenerated lock), four planted defects on the real clone each failing
(removed type in the clone's source, qualification-only lock entry, undeclared gamecore lock entry, dangling asmdef
reference) with the restored clone passing, and `check_release_clone.py --self-test` passing all five of its cases;
`bash -n` over six scripts; `py_compile` over eight tools; the `.meta` generator (0 creations on the second run); the
documentation validator's self-test and full run.

**Not reproduced here, stated plainly.** I could not reproduce the build host's ~27 post-import problems: this host has
no Unity, so no real `Library/PackageCache` exists, and the *pre-fix* checker is already clean on the synthetic noise I
could construct (its prune list already contained `Library`). The fix is therefore expressed as (a) an explicit,
narrower scope, (b) lock rules that are correct in both states, and (c) a self-test that encodes both directions — not
as a claim to have matched a number. If the build host still sees problems on a real import, the self-test's case A
plus the `filesInspected` counter in the clone report will show immediately whether the scan is still reading a
generated tree.

## 9. Known gaps, assumptions and doc ambiguities

1. **Nothing has been built or executed.** Every projected result in this document is a projection. The gate's own
   exit claim is therefore unproven until the build host runs §8.1.
2. **All four Wave 7 tasks are now on this revision**, so the gate sentence is fully wired — but the conformance
   group's values are projections like everything else. GC-024's own build report is the evidence that its tables pass
   on *its* branch; this gate's job is to show they still do on the merged one.
3. **The full-duration TEST-023 qualification is not run**, by project-owner decision. The gate asserts the ten
   recorded budget rows, the decision record's own consistency, and one short correctness diagnostic; it never claims
   a p95/p99 plateau. `tools/check_budget_record.py` is what makes that claim checkable rather than a promise.
4. **The recovery smoke's "second source" deviation** (§5) is a constraint of the production API, not a shortcut: a
   `TryCreate`-fresh world is already `Running`, and `Recover` refuses a live source by design. The step reports
   `sourceLifecycle=Faulted->Disposed` and explains itself in the detail string.
5. **`-probeRecoverySmoke` needs `-probeResult`.** It publishes real checkpoint files beside the structured result;
   without the argument it records one failing step instead of a verdict. The harness always passes it.
6. **Probe repetition and the checkpoint files.** `PROBE_RUNS=2` means each run writes over the same two
   `.checkpoint` paths (a replace, which is the point of the file store's temp+replace publication). The harness
   asserts the files exist after every run, and the mode's own step asserts `publishCount=1` per store instance, so a
   run that failed to publish cannot pass on an earlier run's file.
7. **The `link.xml` rule is an allow-list of two assemblies** (§4.11). The doc's 04 §8 item 4 permits preservation
   for "entry points reached by Unity callbacks or native code"; the host-invoked `WorldRecovery` is read as that
   category because a marker-free clone has no managed caller for it. If the project owner reads that sentence more
   narrowly, the alternative is to keep a managed caller of `WorldRecovery` alive in the release player — this gate's
   `-probeRecoverySmoke` is exactly such a caller now, so a build host that wants to drop the exception can remove the
   `link.xml` entry and re-run this gate; the smoke fails if stripping drops the type.
8. **The W6 gate's own leak attribution** now works with a relative `ARTIFACTS`; nothing else about that harness
   changed, and `PROBE_RUNS` there is whatever the caller passes (this gate passes 2).
9. **`check_release_clone.py` is run before the clone is built.** That is the strictest moment and the order the gate
   script uses. The tool is also correct after an import (§4.10), but the *evidence* a gate should archive is the
   pre-build run: after a build the tree also contains build output, which the walk prunes rather than inspects.
10. **The conformance group asserts the loaded-assembly half of GC-024's audit.** `AuditCombinedComposition` reports
    what a player can compute; GC-024's build-time genre audit across the project tree is a host tool
    (`tools/gc024_genre_audit.py`, whose artifact is committed) and is not re-run by this gate. That split is stated
    in the observation's own detail and header rather than implied.
11. **Two merge defects and one shipped defect were found and fixed here** (§4.8, §4.9), and each is guarded now: the
    doc-comment shape by reading the merged files, the `Parse` local by the new call-site check. The class of defect
    that no interpreter-level check can catch on this host remains the compiler's (types, overloads, nullability),
    which is what the build host's `dotnet build` and Unity compile are for.
12. **Doc ambiguity recorded:** the guide's Wave 7 sentence names "cross-template flow" and "all reference transition
    tables", which GC-024 owns. This gate reads the sentence as *the join runs on the merged revision and reruns the
    affected gates*, and now records GC-024's tables as this gate's own conformance group re-run on that revision. The
    00-wins-over-05-wins-over-09 rule is not engaged: no normative statement was ambiguous, only a scope allocation
    between two tasks in the same wave.
