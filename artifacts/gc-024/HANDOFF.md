# GC-024 HANDOFF — validate all genre transitions and the cross-template composition (Wave 7)

Branch `gc-024` (worktree `/Users/yangcao/wkspace/gc-wt/gc-024`), forked from `main` at `700c3a9` (the Wave 6
integration gate).

**Status of every build/test command in this document: `NotRun (pending orchestrator build host)`.** This host has no
Unity, no .NET SDK, no Mono and no C# compiler, so nothing in this change set has been compiled, imported, executed or
built here. What *did* run is interpreter-level only and is recorded verbatim in `artifacts/gc-024/static-checks.log`:
the host-side C# checker over 562 files (balance, forbidden constructs, engine-free surfaces), the GC-024 genre audit
(clean, with every falsifiability count positive), the documentation validator (self-test + full), the committed-catalog
verifier, the contract-surface parity check, both `--no-build` release checks, `bash -n` over both new shell scripts and
`py_compile` over every Python tool touched. None of those is a build or a test result.

## 1. Summary

GC-024's sentences: *"Execute all before/after tables from 07 for cards, narrative and action, including future
descendants, exclusion, move, provider loss, suspend/unload and both mode directions. Run the narrative-to-card reward
combination through the durable/idempotent seam. Audit assembly references for gameplay-to-kernel dependency
inversion."* Acceptance: *"All documented states match after each transition; cross-template delivery commits once and
preserves unrelated state. Kernel assemblies depend on no example package, actor, quest or card type."*

The change set has four layers, and each is a different kind of evidence:

1. **A pure fixture family** (`tests/GameCore.ReferenceConformance/`, assemblies `GameCore.ReferenceConformance` and
   `GameCore.ReferenceConformance.Tests`). Every before/after table of 07 is transcribed as **data** with the 07 anchor
   it came from — s2.4 for the card market (plus the section's prose rows at `07:51`, `07:108`, `07:110`), s3.3 for the
   chapter quest, s4.3 for the traversal challenge (plus `07:247`'s numeric sequence), s5 for the cross-family
   combination — together with the ordered **script** that executes each table (one fresh world per stage, with
   precondition steps that establish each row's `Before` state), the canonical **trace** document a run records, the
   **oracle** that compares a trace against its table field by field, and the **projections** that recompute every
   number the tables assert from the rules package that owns it. Unity-free by design, so it compiles and runs in plain
   dotnet, in EditMode and in the player.
2. **The Unity worlds** (`unity/GameCore.Validation/.../Conformance*.cs`). `ConformanceWorld` builds the same module
   chain every earlier genre gate builds (target registry, assembly publisher, live target index/seeder, the control
   lane and its world join, the derivation pipeline, the temporal driver) and attaches the genre's own stage runtime
   through the Wave 6 contract, so no kernel module is re-implemented. `ConformanceScenario` executes a table's script
   stage by stage, records the before/after facts of every step plus a full-vocabulary `/state` snapshot, and hands the
   trace to the fixture's own oracle. One partial part per genre host implements `IConformanceFamily`: `Apply` (the
   operation keys, built with that package's own payload builders) and `TryReadField` (the canonical field vocabulary,
   read from the published assembly and the genre's own ECS storage). A third hook, `PrepareConformanceWorld`, is where
   a genre seeds state its own `SeedTargets` does not (see §5.2).
3. **The combined cross-family world** (`ConformanceCrossWorld.cs`, written by a delegated worker — see §7): one
   `CommandDriven` world carrying both families' targets, both families' modules, one merged schedule and one lane,
   with the narrative→card reward crossing GC-021's durable outbox end-to-end including acknowledgement loss and
   redelivery.
4. **The audit and its tooling** (`AssemblyReferenceAudit` + `GenreAuditDocument` in the fixture, `tools/gc024_genre_audit.py`
   on the host): kernel assemblies, projects and sources checked against the families in both directions, with the
   document written to `artifacts/gc-024/genre-audit.json`.

## 2. Commits on this branch

| Commit | Contents |
|---|---|
| `GC-024: the reference-conformance fixture core (07 tables, scripts, trace, oracle)` | `ConformanceTable.cs`, `ReferenceTables.cs`, `ConformanceFields.cs`, `ConformanceTrace.cs`, `ConformanceScript.cs`, `ReferenceScripts.cs`, `ConformanceOracle.cs`. |
| `GC-024: pure-rule projections, the assembly-reference audit and the dotnet wiring` | `ReferenceProjections.cs`, `AssemblyReferenceAudit.cs`, `GenreAuditDocument.cs`, the NUnit suite, `package.json`, two `.asmdef`s, two `.csproj`s, the `.sln` entries, the README. |
| `GC-024: align the transcribed tables and the card conformance host with the real family surfaces` | The mode rows' opted-in targets and mode values, the traversal rows' real future runner and controls, and `ConformanceCardsHost.cs`. |
| `GC-024: the Unity conformance family contract, real world and runner` | `ConformanceFamily.cs` (`IConformanceFamily`, `ConformanceWorld`), `ConformanceScenario.cs`. |
| `GC-024: the conformance hosts, probe mode, EditMode suite and world preparation` | The narrative/traversal hosts, `ConformanceWorldPreparation.cs`, `ProbeConformance.cs`, `ConformanceHosts.cs`, the probe plumbing, the manifest, `Tests/Conformance/**`. |
| `GC-024: the genre audit (host tool + artifact), the probe harness and the release strip` | `tools/gc024_genre_audit.py`, `artifacts/gc-024/genre-audit.json`, `tools/unity/run_conformance_probe.sh`, the release strip entries. |
| `shared: the conformance probe mode and the audit fixes the C# checker does not cover` (if present) | The two rules the sample audit needed: dotnet project classification by name, and a comment-aware kernel token scan. |

## 3. Files created

### The pure fixture (`tests/GameCore.ReferenceConformance/`)

| File | Contents |
|---|---|
| `package.json`, `README.md` | The Unity local package `com.gamecore.reference-conformance`. |
| `Runtime/GameCore.ReferenceConformance.asmdef` | `noEngineReferences: true`, references Contracts + the three rules assemblies. |
| `Runtime/ConformanceTable.cs` | `ConformanceExpectationKind` (`Require` / `Unchanged` / `Absent` / `Preserved`), `ConformanceExpectation`, `ConformanceRowOutcome`, `ConformanceRow`, `ConformanceTable`. |
| `Runtime/ReferenceTables.cs` | The four tables: 12 card rows, 9 narrative rows, 9 traversal rows, 4 cross rows, each with its 07 anchor. |
| `Runtime/ConformanceFields.cs` | The closed canonical field vocabulary (derived values, providers, owned state, projections, world and outbox readings) and one `ConformanceField` list per table. |
| `Runtime/ConformanceTrace.cs` | `ConformancePhase`, `ConformanceValue` (the canonical token vocabulary), `ConformanceTraceEntry`, `ConformanceTrace` with `ToDocument`/`TryParse`/`Digest` (`gamecore.reference-conformance-trace/1`). |
| `Runtime/ConformanceScript.cs` | `ConformanceStepKind`, `ConformanceStep`, `ConformanceStage`, `ConformanceScript`, `ConformanceOperations` (22 operation keys). |
| `Runtime/ReferenceScripts.cs` | The four scripts: one stage per fresh world, every 07 row executed as a row, preconditions for each row's before state. |
| `Runtime/ConformanceOracle.cs` | `ConformanceFailure`, `ConformanceVerdict` (with its digest), `ConformanceOracle.Compare`/`CompareScript`, `RowOutcomeReport`. |
| `Runtime/ReferenceProjections.cs` | Every 07 number recomputed from `CardSetRules`, `TraversalMotionRules`, `NarrativeFacts`/`NarrativeGateRules`/`NarrativeDialogueRules`/`NarrativeChapters`/`NarrativeDerivationPlan`. |
| `Runtime/AssemblyReferenceAudit.cs` | `AssemblyClass`, `ReferenceKind`, `AssemblyRecord`, `AssemblyViolation`, `GenreTokenFinding`, `GenreAuditReport`, `AssemblyReferenceAudit.Audit`. |
| `Runtime/GenreAuditDocument.cs` | The deterministic writer/reader of `artifacts/gc-024/genre-audit.json`. |
| `Runtime/ConformanceDocGaps.cs` | The declared documentation gaps (`ConformanceDocGap`, `ConformanceDocGaps`): the clause a row offends, the mechanism the revision lacks, the evidence and a proposed resolution. |
| `Tests/GameCore.ReferenceConformance.Tests.asmdef`, `Tests/ReferenceConformanceTests.cs` | The pure suite: fixture self-consistency, trace round-trip and tamper refusal, oracle falsifiability, projection agreement, audit classification. |

### The Unity half (`unity/GameCore.Validation/Assets/GameCore.Validation/`)

| File | Contents |
|---|---|
| `Runtime/ConformanceFamily.cs` | `ConformanceOperationOutcome`, `ConformanceOperationResult`, `IConformanceFamily` (extends `IW6Family`), `ConformanceWorld` (the real module chain, `PublishEdit`, `SubmitAndPump`, `PumpDeclaredStep`, `MatchesPublishedAssembly`, `StopAndDispose`). |
| `Runtime/ConformanceScenario.cs` | `ConformanceStep`, `ConformanceTableResult`, `ConformanceScenario.Run(family, tableId)` and the stage/step executor. |
| `Runtime/ConformanceCardsHost.cs` | `Gc013CardsHost.CardFamily : IConformanceFamily` — the card operation keys and field reads, plus two lane-only declarations (the nested `+3` provider of `07:51` and the `+4` replacement of `07:100`). |
| `Runtime/ConformanceNarrativeHost.cs` | `Gc013NarrativeHost.NarrativeFamily : IConformanceFamily` (delegated worker, §7). |
| `Runtime/ConformanceTraversalHost.cs` | `Gc020TraversalHost.CourseFamily : IConformanceFamily` (delegated worker, §7). |
| `Runtime/ConformanceWorldPreparation.cs` | The three `PrepareConformanceWorld` halves: the narrative slice's recipe base layouts, its world-level quest ledger and the ledger bound as the module's root entity; the card and traversal halves are the negative assertions that their own seeding is complete. |
| `Runtime/ConformanceHosts.cs` | The three entry points (`RunConformanceCards`, `RunConformanceNarrative`, `RunConformanceTraversal`) over the fixture catalogs. |
| `Runtime/ConformanceCrossWorld.cs` | The combined cross-family world and `CrossCompositionAudit` (delegated worker, §7). |
| `Runtime/ProbeConformance.cs` | The `-probeConformance` player mode: every table, the combined world, the genre audit, and the trace/audit artifacts. |
| `Tests/Conformance/GameCore.Conformance.Tests.asmdef`, `Tests/Conformance/ConformanceIntegrationTests.cs` | The EditMode suite; `[Timeout]` on every world-building test (the unresolved pre-dispatch hang). |
| `Runtime/GameCore.Validation.ProbeHost.asmdef` | One added reference: `GameCore.ReferenceConformance`. |

### Tooling and evidence

| File | Contents |
|---|---|
| `tools/gc024_genre_audit.py` | The host-side twin of `AssemblyReferenceAudit`: runs the same rule from the source tree and writes the artifact. |
| `tools/unity/run_conformance_probe.sh` | The player harness: `PROBE_RUNS` runs, strict JSON, every structural and per-row step by name, the cross-family clauses, the per-table trace digests compared across runs (and pinned exactly when `GC024_TRACE_DIGEST_*` is supplied), and a diff of the produced traces against the committed ones. |
| `tools/run_conformance.sh` | The whole gate on the build host (§6). |
| `artifacts/gc-024/genre-audit.json`, `static-checks.log`, `traces/README.md`. | Evidence produced on this host, and the trace-format note explaining why no trace is committed before a run. |

### Modified (shared surfaces)

| File | Change | Why it is safe |
|---|---|---|
| `unity/.../Runtime/ProbeArguments.cs` | One const, one ctor parameter, one assignment, one property with its doc comment, one `IsProbeInvocation` term, one local, one parse branch and one ctor argument for `-probeConformance`. | The additive block every earlier task added for its own mode; every earlier mode's branch is untouched. |
| `unity/.../Runtime/ProbeRunner.cs` | One dispatch arm and one report-identity arm. | Additive; both follow the existing shape exactly (each arm calls `CompletePositive` itself). |
| `unity/.../Runtime/GameCore.Validation.ProbeHost.asmdef` | One reference (`GameCore.ReferenceConformance`). | One line; every existing reference is kept. |
| `unity/GameCore.Validation/Packages/manifest.json` | The fixture package as a dependency and a testable. | Two added entries; the resolve regenerates `packages-lock.json` on the build host. |
| `tools/unity/prepare_gc017_release_project.py` | The mode's needles, its nine files, its package dependency and its asmdef reference join the strip. | The same whole-block surgery every earlier mode uses; every `replace_once` needle was verified against the merged source by hand and the script parses (§5.3). |
| `tools/check_game_core_csharp.py` | Three `TARGETS` entries and the fixture package in `engine_free`. | Additive; the checker passes over 562 files. |
| `dotnet/GameCore.sln` | Two projects appended with their own GUIDs and four configuration rows each. | The shape every earlier task used. |

## 4. Requirement → implementation → observation mapping

| Requirement / test | Where implemented | Where observed (this change set) |
|---|---|---|
| **P-001** genre independence and boundaries | each genre's own `IConformanceFamily` part; the kernel touches no genre type; `AssemblyReferenceAudit` + `CrossCompositionAudit` | `conformance/genre-audit`, `cross/no-action-surface-in-card-or-narrative`, the EditMode audit tests, `artifacts/gc-024/genre-audit.json` |
| **P-003** three distinct kinds of change | nothing here reverts: a provider loss retracts a contribution and the committed gameplay state (totals, facts, progress, cards) is asserted to survive | `cards/unmount-festival`, `cards/nested-retraction`, `narrative/unmount-chapter`, `traversal/unmount-tailwind`, `cross/reward-settle` |
| **P-013** mode semantics (Automatic default, Conservative export/import/opt-in gate) | both mode directions over the automatically eligible targets and the family's own complete-opt-in target, in every table | `cards/mode-conservative`, `cards/mode-automatic`, `narrative/mode-conservative`, `narrative/mode-automatic`, `traversal/mode-conservative`, `traversal/mode-automatic` |
| **P-014** mode transition is atomic and keeps the old mode on refusal | the `07:108` row: the switch that would expose the unresolved exclusive pair must be refused with the old assembly kept | `cards/exclusive-conflict-rejected` (a `RefusedKeepsAssembly` row: the oracle requires the refusal *and* identical before/after readings) |
| **P-015** eligibility from immutable descriptors | future descendants (the spawned seat, the spawned villager, the spawned runner) derive the same contribution before their first step; the crowd prop and the scoreboard stay ineligible | `cards/spawn-seat-d`, `narrative/spawn-villager`, `traversal/spawn-runner-c` |
| **P-016** isolation and exclusions | an exclusion row per family plus the isolated/ineligible targets asserted clear in every stage | `cards/exclude-seat-b`, `narrative/exclude-mara`, `traversal/exclude-runner-a`, and the `practice-seat`/`scoreboard`/`npc-display`/`runner-display` expectations |
| **P-017 / P-019** contribution identity, provenance and policies | the provider field of each derived row names the supporting installation, so a provider change is observable; the nested `+3` and the `+4` replacement are distinct contributions of one `Additive` slot | `cards/reconfigure-festival`, `cards/nested-retraction`, `cards/reparent-seat-a`, `traversal/reparent-runner-subtree` |
| **P-024** spawn publishes a fully assembled target | one spawn row per family; the target is registered and its storage installed before the next publication | `cards/spawn-seat-d`, `narrative/spawn-villager`, `traversal/spawn-runner-c` |
| **P-025** reparenting preserves state and swaps inherited configuration | the moved target keeps its `TargetId`, its scope, its hand/cards, its score, its pose/velocity/jump/progress and its conversation state while its derived configuration changes | `cards/reparent-seat-a`, `narrative/reparent-village`, `traversal/reparent-runner-subtree` |
| **P-032** state dispositions | every owned read is a real slot the owner wrote; `Preserved` expectations assert survival without inventing a value 07 never stated | every `Preserved` expectation, and `ConformanceWorldPreparation.cs` |
| **P-034** one authority per state domain | the runner reads only through the published assembly and the genre's own storage; no second writer is introduced anywhere | `ConformanceWorld.TryRead*`, every genre's `TryReadField` |
| **P-036** temporal models | the command-driven tables commit exactly one step per admitted command; the fixed-step table integrates exactly its declared step per pump | `ConformanceWorld.SubmitAndPump` / `PumpDeclaredStep`; `traversal/reparent-runner-subtree` (`1000 -> 1040 -> 1020`) |
| **P-042 / P-043** typed requests and bounded work | an operation key is a declared edit or a typed command, never an ad-hoc write; every pass is bounded | every `Apply` implementation; `cross/reward-settle`'s pass counts |
| **P-044 / P-045** commit boundaries; observation and external output | the reward is persisted before it is applied, the destination mutates once per external key, and a redelivery after acknowledgement loss is a no-op with unrelated state preserved | `cross/reward-enqueue`, `cross/reward-settle`, `cross/reward-redelivery`, `cross/reward-bridge-removal` |
| **P-046 / P-048** installation lifecycle and teardown | suspend/resume rows per family; every stage's world is stopped and disposed inside a `finally`, and the harness/registry asserts the count returns to baseline | `cards/suspend-festival`, `cards/resume-festival`, `narrative/suspend-chapter`, `narrative/resume-chapter`, `traversal/suspend-tailwind`, `traversal/resume-tailwind`, `<table>/<stage>/teardown` |
| **P-054** serialization discipline | the trace document is versioned, canonically ordered and digest-checked; a tampered body is refused | `ConformanceTrace.TryParse`; the pure suite's tamper test |
| **P-057** conformance needs real execution | the fixture proves the tables' numbers against the rules in pure dotnet; the Unity half executes the same tables in real worlds; neither substitutes for the other | `ReferenceProjections`, the EditMode suite, `-probeConformance` |
| **P-059** genre validation before freeze | all three families plus the cross-family combination on the same built kernel, in the same player | `conformance/coverage` (`tables=4`, `allPassed=True`), the per-table verdicts |
| **TEST-006** isolation, exclusions, mode switching | both mode directions with isolation and exclusions held in every table | the mode rows and the exclusion rows above |
| **TEST-008** subtree movement and incremental indexes | the move rows assert the moved branch's parent, the target's identity/scope and its preserved state | `cards/reparent-seat-a`, `narrative/reparent-village`, `traversal/reparent-runner-subtree` |
| **TEST-010** state preservation, migration and reset | provider loss, reconfiguration and suspension keep committed gameplay state | `cards/unmount-festival`, `cards/reconfigure-festival`, `narrative/unmount-chapter`, `traversal/unmount-tailwind` |
| **TEST-013** authority, requests and buffers | the card settlement is the family's own command through the world's port; a refused command is asserted to write nothing | `cards/*/pre1` (the commit preconditions), `ConformanceCardsHost.TrySubmitOneSet` |
| **TEST-014** committed events and consistent observation | a step's before and after readings are taken on opposite sides of one publication; the trace carries a full-vocabulary snapshot per row | every row's `/state` entries |
| **TEST-021** genre neutrality and cross-template conformance | all three tables plus the combined world on one kernel, with the action surface absent from the card and narrative stages | `conformance/coverage`, `cross/no-action-surface-in-card-or-narrative` |

Operations exercised: **O-02** (`reparent-moved-scope`), **O-03** (`mount-*`), **O-04** (`resume-*`), **O-05**
(`reconfigure-provider`), **O-06** (`suspend-*`), **O-07** (`unmount-*`), **O-08** (`mode-*`), **O-16/O-17** (the
committed events the cross-family reward reads), **O-23** (the service/lease bindings a mount creates), plus the
spawn path of **P-024** and the exclusion edit of **P-016**.

## 5. Design decisions, defects found and doc ambiguities

`09` invites the simplest reading consistent with `00`, and `00` wins over `05`, `05` over `09`. Recorded:

### 5.1 Where 07's prose IS the table

Five of the rows GC-024 must execute are stated in 07's prose rather than its table cells: `07:51`'s nested-festival
retraction (REF-C04), `07:108`'s refused mode switch, `07:110`'s dormant table executor, `07:247`'s numeric
acceleration sequence (REF-A01) and `07:276`'s bridge removal with pending work. The task requires every one of them,
so they are rows too, and each row's `SourceRow` names the sentence it came from rather than a table cell. No value was
invented: where 07 states no number (a hand's card identities, a pose, the conversation's node), the expectation is
`Preserved`, whose whole claim is "the operation left this alone", and the oracle compares the run's own two readings.

### 5.2 The narrative slice's state does not come from `SeedTargets`

This is the one substantive gap the tables exposed, and it is a *scenario* gap rather than a kernel or package defect:
`Gc013NarrativeHost.SeedTargets` seeds the chapter's own targets for the GC-013 sequence, and the world-level
`QuestLedger` (07 s3.1: "`QuestLedger` stores durable facts at the world level") is seeded by the scenarios that need
its facts — `W4GateNarrativeHost` seeds it through its own policy case, `Gc018NarrativeHost` maps it when it is live.
Three consequences showed up while wiring the narrative table, all reported by the delegated host worker before being
fixed:

1. no ledger target, so `quest-ledger.bridge-permit`/`-version` resolved no entity at all;
2. `LiveTargetSeeder.TrySeed` does not run `ISpawnApplier.ApplyBaseLayout` (only `AssemblyPublisher.Spawn` does), so
   Mara had no conversation-status slot, the gate no decision slot and the encounter no status slot;
3. `Gc019StageRuntime.AttachNarrative` maps live targets but never calls `NarrativeModule.SetRootEntity`, so even a
   seeded ledger would receive the quest owner's fact write on `Entity.Null`.

The fix belongs in the conformance run and not in the shared `SeedTargets`: adding a target there would change every
earlier gate's live-target count and therefore its archived observations, which a task in Wave 7 has no business
doing. `IConformanceFamily.PrepareConformanceWorld` is therefore the seam, and its narrative half installs each
declared recipe's base layout, seeds the ledger at the world root with `NarrativeKeys.QuestLedgerRecipe` and binds it
as the module's root entity — all through the narrative package's own applier and module. **If the orchestrator prefers
the family itself to seed the ledger, that is a separate change to `Gc013NarrativeHost.SeedTargets` plus a re-run of
every earlier gate whose observation details record a live-target count; this change set deliberately does not do it.**

### 5.3 The card package's `+4` cannot be an in-place reconfigure

`07:100`'s row is "Reconfigure festival bonus from `+2` to `+4`". The card package bakes a scoring provider's value
into its immutable **declaration** payload (`CardTableDeclarations.SetBonusRule` freezes the bonus into the rule), so an
O-05 reconfigure of that installation has no configuration field to change. The row is executed as the
declared-compatible-provider replacement P-025 describes: the same installation identity is unmounted and remounted
with a replacement declaration, so the derived row's provenance is the same source while its value becomes `+4`. The
unmount's own publication exists and the row does not observe it (it reads the last published state before and the
first after). **Recorded as the one place where 07's operation and this package's declaration shape differ**; the
alternative — teaching the card package a configurable bonus — would change GC-011's frozen declarations and its
archived evidence, which is out of this task's scope. The narrative slice's equivalent row IS a real O-05 reconfigure
(the chapter provider's schema defaults carry the schema, so the hash recomposes), and the traversal course declares no
reconfiguration row at all (returned `Unsupported` with that reason).

### 5.4 The traversal table's models follow the course 07 s4.1 actually declares

Two transcription changes were needed once the course's real surface was read: the automatically eligible runner is
the course's own valley runner (a runner seeded in `Ridge` is a sibling branch of `Valley` and no valley modifier
reaches it), and the runner that keeps the modifier in `Conservative` is the course's complete-opt-in target
(`Gc020TraversalHost.OptedInTarget`), not `runner-a`. The rows now name the opted-in target through
`ConformanceFields.OptedInRunner` and the spawned runner as the automatically eligible descendant that loses and then
regains the contribution. `07:247`'s sequence is observed through the expectations the script's own preconditions
declare, and the projection test recomputes `1000 -> 1040 -> 1020` from `TraversalMotionRules` rather than restating it.

### 5.5 Two rules the audit needed, found by running it

The first run of `tools/gc024_genre_audit.py` reported four findings, all of them the audit's own defects and all now
fixed in **both** implementations (the tool and `AssemblyReferenceAudit`):

* a `dotnet/src` project is a kernel project only when its own assembly name is a kernel one — the directory also holds
  the plain-dotnet shells of the fixture suites (replay, protocol, reference-seam, conformance), and those
  legitimately reference the rules packages they exercise (P-057);
* the kernel-token scan must strip comments and literals: several kernel sources document a generated qualification
  assembly by name, and a mention in prose is not a dependency.

The second finding was in a file this task does not own (`CheckpointCodecAdapter.cs`), which is exactly the kind of
false positive the rule's refinement removes.

### 5.6 One 07 row cannot be performed in this revision, and is recorded as a gap

`07:276` says that `NarrativeCardRewards` "declares `PreserveDormant` for its completed outbox, with a
scratch-migration precondition that no pending work remains ... Unmounting with pending work therefore rejects until
it drains or transfers". The combined world performs the row as far as this revision allows — it reads the pending
obligation from the live world, so the state the refusal would protect is visible — but the *refusal itself* has
nowhere to happen: the bridge is an ordinary caller-owned object rather than a mounted installation, so there is no
installation identity, no manifest and no slot policy for the lane to enforce. GC-021 shipped that shape and recorded
the same absence (`artifacts/gc-021/HANDOFF.md` §7 item 5).

Rather than let a red step hide inside a green verdict, or let a fabricated pass stand in for a real one, the gap is
a value: `GameCore.ReferenceConformance.ConformanceDocGaps` declares it with the 07 clause it offends, the mechanism
this revision lacks, the recorded evidence and a proposed resolution; the run reports that step with
`ConformanceStepStatus.RecordedGap`; `ConformanceTableResult.AllPassed` is **false while any gap is open**, because a
gap is not a pass; the probe reports the step as its own `ExpectedNegative` status and names the gap identifier; and
the harness and the EditMode suite both require that name, so the gap cannot vanish silently and a new one cannot
appear unnoticed. **The orchestrator should decide whether that is acceptable for the W7 gate** (the W7 sentence
requires the cross-template flow to pass; it does pass, with one row honestly reported as unperformable), or whether
the bridge should be mounted as an installation in a follow-up task.

### 5.7 `-probeConformance` reports which half of the audit it computed

A player has no project tree, so the build-time half of the genre audit cannot run there. Rather than let a player
report a clean audit it never looked at, the audit step names both halves: the loaded-assembly half it did compute,
and `tree=no project tree on this host` when there is none (or the computed tree verdict when the host has the tree,
as the Editor does). The harness accepts either, so a run cannot claim the half it did not do.

## 6. Exact commands for the Linux build host

Everything runs from the repository root. **Nothing below has been run.**

### 6.1 The whole gate (one command)


```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet \
  PROBE_RUNS=5 tools/run_conformance.sh
```

In order: `dotnet build` + `dotnet test dotnet/GameCore.sln -c Release` (trx into `artifacts/gc-024/trx`); the host-side
genre audit; the Unity resolve; EditMode (every testable package plus `GameCore.Conformance.Tests`); PlayMode;
`tools/unity/build_probe.sh` (catalog regeneration + the StandaloneLinux64 IL2CPP player, High stripping); the
byte-identity check of the committed catalogs; every player probe `PROBE_RUNS` times (`run_probe`, `run_world_probe`,
`run_narrative_probe`, `run_cards_probe`, `run_gc013_probe`, `run_traversal_probe`, `run_gc021_probe`,
`run_replay_probe`, `run_conformance_probe`); both release-surface checks; the documentation validator. Every Unity
invocation is wrapped in `timeout` with one logged retry on a timeout.

### 6.2 The pieces, if a step needs isolating

```sh
# the pure fixture alone (sub-second, no Unity)
dotnet test dotnet/tests/GameCore.ReferenceConformance.Tests/GameCore.ReferenceConformance.Tests.csproj -c Release

# the genre audit alone
python3 tools/gc024_genre_audit.py            # writes artifacts/gc-024/genre-audit.json, exit 1 on any finding

# the EditMode suite for this task alone
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
  "$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Conformance.Tests \
  -testResults artifacts/gc-024/unity/conformance-editmode.xml -logFile -

# the player mode alone
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity ARTIFACTS=artifacts/gc-024/toolchain tools/unity/build_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-024/toolchain tools/unity/run_conformance_probe.sh
```

Promoting a digest baseline (after a first clean run):

```sh
for t in cards narrative traversal cross; do
  echo "$t: $(grep -o "conformance/$t/trace-digest[^\"]*digest=[0-9a-f]*" artifacts/gc-024/toolchain/probe-conformance.json | grep -o 'digest=[0-9a-f]*')"
done
# then re-run with the pins, e.g. GC024_TRACE_DIGEST_CARDS=<digest>, and copy
# artifacts/gc-024/toolchain/traces/*.txt to artifacts/gc-024/traces/ once the run is accepted.
```

### 6.3 The release clone

```sh
python3 tools/unity/prepare_gc017_release_project.py    # strips the mode, its nine files, its package and its asmdef ref
python3 tools/check_release_clone.py                    # asserts the clone's own invariants (run it on the clone)
```

## 7. Delegation, and what was verified about it

Four read-only reconnaissance passes mapped the packages, the qualification project, the dotnet conventions and the
kernel/outbox/composition APIs before any code was written; their findings are the file/line references this change set
is built on. Three write tasks were delegated with a frozen interface contract
(`local://gc024-conformance-hosts-contract.md`, `local://gc024-cross-world-contract.md`): the narrative host, the
traversal host and the combined cross-family world. Every delegated file was read here before being committed, its
brace/paren balance re-checked with a comment- and literal-aware checker, and the two blockers the narrative worker
reported (a duplicated `ConformanceOperations` constant and a dropped `CommandDrivenPumpTicks` reference in code this
task wrote) were fixed here. The traversal worker's three table mismatches were resolved by re-aligning the fixture
(the opted-in target, the future runner, the exclusion target) rather than by weakening it, and its
`apply-exclusion` target was parameterised so the row exercises the automatically eligible runner.

## 8. Known gaps, assumptions and doc ambiguities

1. **Nothing has been compiled or executed on this host** — every command above is `NotRun`. The highest-risk items, in
   the order a compiler would find them: (a) the merged schedule and merged registration of the combined world (one
   `OwnershipSchedulePipeline.Build` over both families' manifests, one lane over both scope trees) — the pieces are the
   same calls every genre gate makes, but the union has never been compiled; (b) the narrative world-preparation hook's
   use of `NarrativeRecipeApplier.ApplyBaseLayout` on already-created entities (the applier is written for a spawn
   path); (c) `ConformanceWorld.PublishEdit`'s `PublishUnchangedAssembly` branch for a `NoTargetChange` derivation,
   which is the GC-013 sequence's own idiom but has not run here; (d) the traversal host's `commit-command-rejected`,
   which reports `Refused` only when the input stage's rejection counter is observed.
2. **No trace is committed.** A trace is an observation, and nothing has observed anything on this host; committing one
   would be fabricating evidence. `artifacts/gc-024/traces/README.md` states the format, the producer and the
   comparison target, and the harness diffs the produced traces against the committed copies once they exist.
3. **The digest literals are not pinned.** The per-table digests are compared across the harness's `PROBE_RUNS`
   repetitions and pinned exactly only when `GC024_TRACE_DIGEST_*` is supplied — the same completion path GC-021's
   `ProbeGc021` and the Wave 5 gate used. Inventing a literal would be a fabricated result.
4. **`07:108`'s refusal is asserted through the lane's own answer.** The row requires the operation to be refused and
   the assembly to stay published; the oracle checks the reported outcome *and* that every field reads identically, so a
   no-op switch that published would fail. Whether the lane refuses it with `CapabilityConflict` at *plan* time (as the
   GC-013 sequence observes) or at publication is not asserted beyond "not published", because 07 says only that the
   old mode and assembly remain.
5. **`07:276` is an OPEN RECORDED GAP, not a passing row.** `ConformanceDocGaps` declares it (clause, missing
   mechanism, evidence, proposed resolution), the run reports the step as a `RecordedGap`, `AllPassed` is false while
   it is open, and both the probe harness and the EditMode suite require it by name — so the gate will report the
   cross-family table as *not fully passed* until the bridge is a mounted installation with a declared policy. That is
   the honest state, and §5.6 explains why no other outcome was acceptable. **The orchestrator must decide whether to
   accept it for the W7 gate or to open a follow-up task** for the bridge's manifest and mount (the alternative,
   weakening the row, would be exactly the silent special case 09's non-goals forbid).
6. **`Preserved` is a weaker claim than a value.** Where 07 states no number, the row demands "unchanged" and the oracle
   compares the run's two readings. That is honest but strictly weaker than a literal; the rows where 07 does state a
   number all carry one.
7. **`ConformanceFields.Cross()`'s `card-tent.*` subjects are new names.** 07 s5 draws a `CardTent` scope; the card
   market's own scope is `cards.table-area`, so the combined world reuses the package's scope and the fixture's fields
   name the combined world's seats with the `card-tent` prefix. Recorded rather than renaming a gameplay scope.
8. **The gate script's probe list is the Wave 6 gate's whole list.** Every mode that gate drove is driven again here
   (twenty harnesses, each `PROBE_RUNS` times) plus this task's own, which is the strongest available reading of
   P-059's "with the same built kernel". The consequence is deliberate: a GC-024 gate run also fails if an earlier
   family probe regresses, which is exactly the intended reading of "the same revision".

## 9. Inventory proposals (PROPOSALS ONLY — the build host promotes after running)

Nothing is promoted here: no row can move on the strength of a `NotRun` change set. If the gate passes on the build
host, the candidate promotions are:

| Id | Current | Candidate | Why this change set would justify it |
|---|---|---|---|
| `P-001` | Partial | `Implemented+Evidenced` | all three families plus the cross-family combination run on one built kernel in one player, and the audit proves the dependence direction in both halves of the tree |
| `P-013`, `P-014`, `P-016` | Partial | `Implemented+Evidenced` | both mode directions, the refused switch and the exclusion rows pass in real worlds for all three families |
| `P-025` | Partial | `Implemented+Evidenced` | the three move rows assert identity, scope and preserved gameplay state |
| `P-041`/`P-042`-adjacent delivery rows | — | unchanged | delivery stays as GC-021 left it; this task adds the cross-family destination effect and its idempotency observation |
| `P-045` | Partial | `Implemented+Evidenced` (only if the cross-world rows pass) | the reward crosses the durable seam in one world with acknowledgement loss and redelivery |
| `P-057`, `P-059` | Partial | `Implemented+Evidenced` (only if the gate passes) | the pure half and the Unity half both execute the same tables, and the audit is a real both-directions check |
| `TEST-006`, `TEST-008`, `TEST-010`, `TEST-013`, `TEST-014`, `TEST-021` | Partial | unchanged until the gate runs | the suites now execute these cases; the evidence is the gate's |
