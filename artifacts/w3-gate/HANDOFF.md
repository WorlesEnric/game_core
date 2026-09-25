# W3-GATE HANDOFF — Wave 3 integration gate (narrative + cards on one kernel)

Branch `w3-gate` (worktree `/Users/yangcao/wkspace/gc-wt/w3-gate`) = `main` + GC-010 (narrative), merged with
`origin/gc-011` (cards).

**Status of every executable check in this document: `NotRun (pending orchestrator build host)`.** This host has no
.NET SDK, no C# compiler, no Mono and no Unity, so nothing in this change set has been compiled, imported or
executed here. The only things that ran are interpreter-level host checks, recorded verbatim in
`artifacts/w3-gate/static-checks.log`: `python3 tools/check_game_core_csharp.py` (266 files, `ok`),
`python3 tools/validate_game_core_docs.py --self-test` and `python3 tools/validate_game_core_docs.py` (passed),
`bash -n` over six shell scripts, a repository-wide `.meta` GUID uniqueness scan (483 GUIDs, zero duplicates) and
the two host-side contract scans described in §7. None of those is a build or a test result.

Gate sentence implemented (verbatim, `docs/game-core/09-implementation-guide.md`, Wave 3 — Two genuinely different
running compositions):

> Run narrative and cards with the same kernel. Show zero idle command steps, automatic existing/future targets, a
> narrative state change and a card domain transfer. Both Unity-world fixtures must pass before provisional generic
> execution review.

## 1. Merge and kernel reconciliation (task 1)

`git merge --no-ff origin/gc-011 -m "Merge GC-011 into W3 gate"` conflicts in ten files. Every conflict was read on
both sides (`git diff <merge-base> origin/gc-010 -- <path>` and `... origin/gc-011 -- <path>`) before being
resolved. The merge commit `ddcd0c2` has two parents (`c5acc6e` = `main`+GC-010, `a5c1540` = `origin/gc-011`).

### 1.1 Kernel reconciliation decisions (one coherent generic kernel, no side picked blindly)

| # | Shared kernel surface | GC-010 (`kernel:` commits) | GC-011 (build-host fixes) | Decision and why |
|---|---|---|---|---|
| K1 | `CompositionLaneSeed`, `CompositionState.CreateEmpty`, `CompositionHost` — a world definition's declared scope tree | added optional `InitialScopes`, `WithScopes`, `DeclaresScopes`; host passes `seed.InitialScopes` | *no change* | **GC-010 kept verbatim.** GC-011's problem was different: it published its scope tree as `ScopeCreate` edits and then could not advance the world's assembly for a scope-only revision. GC-010's declared-tree seed removes exactly that publishable-revision need, so GC-011's `PublishUnchangedAssembly` (K4) is what its remaining scope-freeing edits use. Both mechanisms are present; neither substitutes for the other. |
| K2 | `ScopeRegistry` constructor — parent-first insertion | sorted the additional records by depth only | sorted by depth **then** `CompareRecords` | **GC-011's tie-break kept, GC-010's depth rule kept.** Depth ordering is what makes a child addable after its parent; the canonical tie-break removes the dependence on any unstable sort when two records share a depth. `Add` also validates depth against the parent, so the canonical tie-break cannot mask a malformed tree. |
| K3 | `ScopeRegistry` root identity and invariants | remembered the real `worldRoot`; `Root` returns it; refused a second root; refused a child whose depth disagrees with its parent | *no change* | **GC-010 kept verbatim.** The narrative tree has `village` sorting below `story-world`, so `canonicalScopes[0]` was the wrong root; the same defect would misroute `PlanModeSet`/`ScopeCreate` in the card tree, so this is generic, not narrative-shaped. GC-011's new test `NestedScopesSurviveCanonicalIdentityOrdering` (a child id below its parent) still passes because K2 orders by depth before identity. |
| K4 | `ProposedCapability` target eligibility | `EligibleTargets` (from derivation) + `AppliesTo(TargetDefinition)`; hash includes the ids | `TargetIds` + `AppliesTo(DefinitionRef, TargetId)` | **ONE API, GC-010's shape.** Both are the same mechanism (a declaration may name the exact eligible live targets, and a recipe-only declaration keeps applying broadly); GC-010's form takes the `TargetDefinition` the planner already holds at both call sites, so one overload serves new candidates *and* existing rows and no caller needs a second argument pair. The property name `EligibleTargets` is kept because it says what the ids mean (post-scope/boundary-check derivation output, P-015/P-016) rather than where they came from. |
| K5 | `AssemblyPlanner` call sites + proposal hash | `AppliesTo(definition)` at both sites; ids folded into the proposal hash | `AppliesTo(recipe, target)` at both sites; no hash change | **GC-010 kept.** Same filter semantics; GC-010 additionally sorts the eligible ids into the proposal hash, so two proposals that differ only in eligibility cannot collide (a real defect GC-011's version would have kept). |
| K6 | `DerivationProposalBridge` — a derived contribution names its own target | `new List<TargetId> { assembly.Target }` | identical change | **Identical on both sides; kept once.** This is the fix that stops a mount's capability from being broadcast to every live target sharing the recipe (the observed GC-010 failure: 8 rows instead of 4, museum/sailor wrongly bound, no gate transition; the observed GC-011 failure: League B's bonus overriding League A and the isolated practice seat). |
| K7 | `AssemblyPublisher.PublishUnchangedAssembly` | *no change* | added for a validated no-target-change derivation | **GC-011 kept.** P-006 has one publication series, so a composition edit whose derivation changes no target binding still needs a world image before the next edit can be adopted. `DerivedAssemblyPipeline.PublishDerived`'s no-target-change behaviour is untouched, so the W2/GC-010 spawn can still consume its pending publication. Verified generic: it names only `CompositionRevision`, `AssemblyEpoch`, `TargetBindingTable`, `DerivedBindingRule` and `WorldLifecycleState`. |

No genre-specific type entered a kernel package: every reconciled member is a scope/identity/capability/publication
record from 05, and `Packages/com.gamecore.{contracts,composition,derivation,planning}` reference nothing above
`GameCore.Contracts` (checked by the gate's own asmdef audit, §3).

### 1.2 Non-kernel shared files

| File | Resolution | Why |
|---|---|---|
| `dotnet/GameCore.sln` | **both sides kept**: narrative and card project entries and their four `Build.0` configuration lines | every project keeps a unique GUID (verified: no duplicate project GUID), and the solution must build both slices' rules packages |
| `dotnet/README.md` | **both rows kept**, plus the W3 gate command | the table documents every project in the solution |
| `tools/check_game_core_csharp.py` | **both sides kept** in `TARGETS` and in `engine_free` (`rules.narrative`, `rules.cards`, `dotnet/src`) | both rules packages are engine-free, so both belong in the engine-free set |
| `unity/GameCore.Validation/Packages/manifest.json` | **both sides kept** (four dependencies, two `testables`) | the qualification project must resolve both families |
| `unity/GameCore.Validation/Packages/packages-lock.json` | **GC-010's side** (`com.gamecore.gameplay.narrative` and its `rules.narrative` dependency) | the build host regenerates this file in step 2 of the gate and its freshness is committed there; the brief allows either side. The card entries are added by that regeneration, which is why the gate re-runs `unity-resolve` before anything else and why `packages-lock.json` is expected to change on the build host. |
| `GameCore.Validation.ProbeHost.asmdef` | **all references kept** (cards, narrative, both fixture assemblies, `GameCore.Rules.Cards`, `GameCore.Rules.Narrative`, `GameCore.Validation.GeneratedCards`) | the one probe host runs every mode; `GameCore.Rules.Cards` is new here because the W3 scenario names `CardVocabulary` |
| `ProbeArguments.cs`, `ProbeRunner.cs` | every probe mode kept, plus `-probeW3Gate` | "keep every probe mode" (brief) |

## 2. The gate scenario (task 2)

### 2.1 Why two worlds in one process, not one world with both families

`07 §5`'s single world with a `NarrativeCardRewards` bridge is the **cross-family** composition and belongs to the
Wave 7 gate; the Wave 3 sentence asks only that narrative and cards run *with the same kernel*.

* P-010 makes scopes one rooted acyclic tree per world, and P-006 gives every world one publication series. The
  chapter tree (`NarrativeScopes`, root `story-world`) and the market tree (`CardMarketComposition`, root `market`)
  are two world definitions with two roots and two lanes; one world cannot hold both roots without one composition
  owning the other's root, which nothing asks for.
* `04 §3`: "Each protocol world has one owning `UnityWorldHost` and one `Unity.Entities.World`. Multiple protocol
  worlds require separate hosts and worlds, not global singleton state." Two worlds in one process is therefore the
  supported way to run two compositions at once, and it is the stronger proof: one kernel image (one loaded copy of
  every kernel assembly, asserted), two owners, two lanes, two catalogs — and neither family's state visible to the
  other.

Both compositions run over their **committed generated catalog** (validated by the production `ImmutableCatalog`);
the fixture catalogs are covered by the per-slice gates on the same revision (`tools/run_w3_gate.sh` runs
`-probeNarrative` and `-probeCards`, each of which runs **both** catalogs, and the narrative/card EditMode
assemblies, which run both catalogs too). That is how "both Unity-world fixtures must pass" is satisfied without the
wave gate becoming a second copy of the two slices.

### 2.2 Clause → observation map

| Gate clause | Where observed | Values asserted |
|---|---|---|
| run narrative **and** cards with the same kernel | `w3-narrative-composition-passes-on-this-kernel`, `w3-card-composition-passes-on-this-kernel`, `w3-two-worlds-on-one-kernel-image`, `w3-kernel-assemblies-reference-no-gameplay` | 11 narrative observations and 13 card observations, zero failures, each run over its committed generated catalog; distinct world sessions; registry returned to baseline after each run; zero forbidden kernel→gameplay edges, zero duplicate kernel assemblies, zero unreadable reference sets, every loaded gameplay/rules assembly referencing the kernel |
| zero idle command steps | `w3-zero-idle-command-steps-in-both` | narrative 600 idle frames → 0 steps, 0 dispatch runs, 0 pending demand; cards idle frames → 0 steps, 0 dispatch runs, 0 pending demand |
| automatic existing/future targets | `w3-automatic-existing-and-future-targets-in-both` | narrative: 3 derived targets (`mara` 2 rows, `gate-east` 1, `encounter-oak` 1), `crowd-prop` 0 (P-015), museum 0 (P-016), sailor 0 before chapter two and 2 after; future villager 2 rows at value 1, published stamp at the spawn epoch, counters rejoined. cards: both festival seats `+2`, quiet seat `+1`, scoreboard 0, isolated practice seat 0; future seat `+2` with a published stamp, counters rejoined |
| a narrative state change | `w3-narrative-state-change-through-committed-snapshot` | one admitted choice = one step; `gate-east` decision 0 → 1; fact 1 at version 2; the gate evaluated that committed version; exactly 2 committed events at step 1 and the chapter-two epoch; ledger 1 committed / 0 pending; the step group dispatched ≥ 1 entry |
| a card domain transfer | `w3-card-domain-transfer-committed-atomically` | giver −1, receiver +1, the moved card present exactly once in the receiver and not in the giver, table version advanced, plus the slice's own duplicate-rejection and rejected-settlement counters in the same world |
| kernel assemblies reference neither gameplay package | `w3-kernel-assemblies-reference-no-gameplay` (player + Editor) and `NoKernelAsmdefReferencesAGameplayAssembly` (Editor-only, build-time `.asmdef` audit) | see §3 |

The two slices' own facts digests are archived beside the gate's reading (`w3-narrative-facts`, `w3-card-facts`),
so the evidence carries the values rather than only the verdicts.

## 3. The kernel-separation proof (two directions, neither substituting for the other)

* **Loaded assembly graph, in the running process (Editor *and* IL2CPP player).**
  `KernelAssemblyAudit.AuditLoadedAssemblies()` walks `AppDomain.CurrentDomain.GetAssemblies()`: for each of the
  ten declared kernel assemblies (04 §2's rows above `GameCore.Gameplay.<Name>`, plus the kernel fixture assemblies
  and the Editor-only compiler) it reads `GetReferencedAssemblies()` and refuses any reference into
  `GameCore.Gameplay.*`, `GameCore.Rules.*`, `GameCore.Validation.*` or `GameCore.Generated*`; it also refuses a
  kernel assembly loaded twice (two copies of the kernel would make "the same kernel" false) and records every
  reference set it could not read (an unreadable set proves nothing). In the other direction every loaded
  gameplay/rules assembly must reference at least one kernel assembly. The player runs this same code path, which is
  what makes the clause provable on the shipped binary.
* **Build-time assembly definitions (Editor/build host only).** `KernelAssemblyAudit.AuditAsmdefReferences(root)`
  discovers **every** `.asmdef` inside the seven kernel package directories (so a new kernel assembly cannot enter
  the tree unaudited), refuses any declared reference into a gameplay/rules/validation/generated assembly, and
  requires every `.asmdef` inside the four gameplay/rules package directories to declare a kernel reference. A
  player cannot see a project tree, so this half runs in the Editor test and reports `missingPackages` explicitly
  instead of passing vacuously. `TryFindRepositoryRoot` locates the checkout from either the working directory or
  the test assembly directory.

The audit also records the loaded kernel image identity (`name=fullName` per assembly) in the facts, so the artifact
names exactly which kernel binaries ran both families.

## 4. Files created

Unity qualification project:

| Path | Contents |
|---|---|
| `Assets/GameCore.Validation/Runtime/W3GateScenario.cs` | `W3GateStep`, `W3GateFacts`, `W3GateScenarioResult`, `W3GateScenario`: runs both compositions in one process and computes the eight gate observations plus the two facts digests |
| `Assets/GameCore.Validation/Runtime/KernelAssemblyAudit.cs` | `LoadedAssemblyReport`, `AsmdefReferenceReport`, `KernelAssemblyAudit` (loaded-assembly audit, asmdef audit, repository-root search, the declared 04 §2 sets) |
| `Assets/GameCore.Validation/Runtime/ProbeW3Gate.cs` | the `-probeW3Gate` player mode |
| `Assets/GameCore.Validation/Tests/W3Gate/GameCore.W3Gate.Tests.asmdef` | EditMode assembly `GameCore.W3Gate.Tests` |
| `Assets/GameCore.Validation/Tests/W3Gate/W3GateIntegrationTests.cs` | 9 cases: every gate observation, both kernel-separation directions, two worlds on one kernel image, both idle clauses, both automatic-target clauses, the narrative committed snapshot, the card transfer, and both slices completing |
| the four `.meta` files above plus `Tests/W3Gate.meta` | deterministic GUIDs, verified unique repository-wide (483 GUIDs, no duplicates) |

Tooling and evidence: `tools/unity/run_w3_gate_probe.sh`, `tools/run_w3_gate.sh`,
`artifacts/w3-gate/HANDOFF.md` (this file), `artifacts/w3-gate/static-checks.log`.

## 5. Files modified (each minimal and explained)

| Path | Change | Why |
|---|---|---|
| `Assets/GameCore.Validation/Runtime/ProbeArguments.cs` | `-probeW3Gate` constant, `w3Gate` constructor parameter, `W3Gate` property, parse branch, `IsProbeInvocation` | a new probe mode; every existing argument, default and exit code unchanged |
| `Assets/GameCore.Validation/Runtime/ProbeRunner.cs` | `W3Gate` report branch (task `W3-GATE`) and dispatch branch after the card branch | the same player runs every mode; every existing branch and task id kept |
| `Assets/GameCore.Validation/Runtime/GameCore.Validation.ProbeHost.asmdef` | `+ "GameCore.Rules.Cards"` | the W3 scenario names `CardVocabulary` at compile time |
| `dotnet/README.md` | the W3 gate command paragraph | the README lists every wave gate command |
| kernel files | see §1.1 | the merge |

No file outside the W3 gate's own files, the merge's shared files and `dotnet/README.md` was modified by this task.

## 6. Exact commands for the Linux build host

Everything runs from the repository root. Nothing here has been run.

### 6.1 One command (the whole gate)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet tools/run_w3_gate.sh
```

It runs, in order:

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/w3-gate/trx
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile artifacts/w3-gate/unity/resolve.log
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testResults artifacts/w3-gate/unity/editmode-results.xml -logFile artifacts/w3-gate/unity/editmode.log
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform PlayMode \
  -testResults artifacts/w3-gate/unity/playmode-results.xml -logFile artifacts/w3-gate/unity/playmode.log
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation \
  -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog -logFile artifacts/w3-gate/unity/card-codegen.log
UNITY="$UNITY" ARTIFACTS=artifacts/w3-gate/toolchain tools/unity/build_probe.sh
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs
PROBE_RUNS=5 ARTIFACTS=artifacts/w3-gate/toolchain tools/unity/run_probe.sh both          # GC-001 positive + negative
PROBE_RUNS=5 ARTIFACTS=artifacts/w3-gate/toolchain tools/unity/run_world_probe.sh         # GC-005
PROBE_RUNS=5 ARTIFACTS=artifacts/w3-gate/toolchain tools/unity/run_w1_gate_probe.sh       # W1-GATE
PROBE_RUNS=5 ARTIFACTS=artifacts/w3-gate/toolchain tools/unity/run_w2_gate_probe.sh       # W2-GATE
PROBE_RUNS=5 ARTIFACTS=artifacts/w3-gate/toolchain tools/unity/run_narrative_probe.sh     # GC-010 (both catalogs)
PROBE_RUNS=5 ARTIFACTS=artifacts/w3-gate/toolchain tools/unity/run_cards_probe.sh         # GC-011 (both catalogs)
PROBE_RUNS=5 ARTIFACTS=artifacts/w3-gate/toolchain tools/unity/run_w3_gate_probe.sh       # W3-GATE
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

`UNITY` is required (exit 2 without it): the gate is never claimed from the dotnet half alone, and `PROBE_RUNS`
(default 5) makes any crashing player run fail the gate.

Do not add `-quit` to a test-run command (04 §10).

### 6.2 The W3 gate EditMode assembly alone

```sh
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.W3Gate.Tests \
  -testResults artifacts/w3-gate/unity/w3gate-editmode.xml -logFile artifacts/w3-gate/unity/w3gate-editmode.log
```

### 6.3 What `run_w3_gate_probe.sh` asserts

`"task": "W3-GATE"`, `"mode": "W3Gate"`, `"result": "Pass"`, no `"status": "Fail"`, all ten scenario observations
plus `w3-gate-facts`, and these fragments of the gate's own facts digest: `kernelForbiddenReferences=0`,
`duplicateKernelAssemblies=0`, `kernelInspectionFailures=0`, `distinctSessions=True`, `narrativeSteps=11/0 failed`,
`cardSteps=13/0 failed`, `narrativeIdle=600 frames/0 steps`, `narrativeExistingTargets=3`,
`narrativeFutureRows=2`, `narrativeCommittedEvents=2`, `narrativeCommittedFactVersion=2`, `cardFutureBonus=2`,
`cardTransferGiver=1`, `cardTransferReceiver=1`; and it independently reads both committed catalogs' fingerprint
literals out of the generated sources and requires the two archived facts digests to name them.

## 7. Requirement and test coverage mapping

| Normative requirement / test | Where implemented | Where observed (gate) | Slice evidence on the same revision |
|---|---|---|---|
| Wave 3 exit gate, clause 1 (same kernel) | `W3GateScenario`, `KernelAssemblyAudit` | `w3-*-composition-passes-on-this-kernel`, `w3-two-worlds-on-one-kernel-image`, `w3-kernel-assemblies-reference-no-gameplay`; `EveryGateCheckPasses`, `BothCompositionsRunInTheirOwnWorldOnOneKernelImage`, `NoLoadedKernelAssemblyReferencesAGameplayPackage`, `NoKernelAsmdefReferencesAGameplayAssembly`, `BothCompositionsCompletedWithoutAFailedObservation` | GC-010's and GC-011's own suites re-run by `tools/run_w3_gate.sh` |
| Wave 3 exit gate, clause 2 (zero idle command steps) | both slices' own idle steps | `w3-zero-idle-command-steps-in-both`; `NoIdleCommandStepInEitherFamily` | `narrative-idle-world-performs-zero-steps`, `cards-idle-before-command-commits-zero-steps`, `cards-idle-world-performs-zero-steps` |
| Wave 3 exit gate, clause 3 (automatic existing/future targets) | both slices' own derivation paths | `w3-automatic-existing-and-future-targets-in-both`; `TheNarrativeChapterReachesExistingAndFutureTargetsAutomatically`, `TheCardModifierReachesExistingAndFutureSeatsAutomatically` | `narrative-derived-layout-in-entities`, `narrative-forward-provider-and-spawned-target`, `cards-mount-reaches-existing-seats`, `cards-future-seat-inherits-modifier` |
| Wave 3 exit gate, clause 4 (a narrative state change) | GC-010's command path | `w3-narrative-state-change-through-committed-snapshot`; `TheNarrativeStateChangeIsVisibleInTheCommittedSnapshot` | `narrative-one-choice-command-committed` |
| Wave 3 exit gate, clause 5 (a card domain transfer) | GC-011's settlement path | `w3-card-domain-transfer-committed-atomically`; `TheCardDomainTransferCommitsBothSides` | `cards-transfer-commits-both-sides`, `cards-one-command-commits-both-sides`, `cards-rejected-settlement-changes-nothing`, `cards-duplicate-command-transfers-once` |
| Wave 3 exit gate, clause 6 (both Unity-world fixtures pass) | per-slice probes + EditMode assemblies | `tools/run_w3_gate.sh` runs `-probeNarrative` and `-probeCards` (each over generated **and** fixture catalogs) and the whole EditMode suite (which includes `GameCore.Narrative.Tests` and `GameCore.Cards.Tests`) | the two slices' own suites |
| P-001 genre independence | kernel packages stay free of gameplay types | `w3-kernel-assemblies-reference-no-gameplay` | `narrative-genre-neutrality`, GC-011's inventory audit |
| P-006 one publication series | the merged kernel | every slice observation that asserts counters joined (`CountersJoinedAfterSpawn`, `CountersJoinedAfterSetup`) plus `PublishUnchangedAssembly` | GC-010/GC-011 suites |
| P-010 one rooted tree per world, real depth-0 root | the merged `ScopeRegistry` | both slices' scope trees open (narrative `story-world` tree, card `market` tree) and their runs pass | `DeclaredScopeTreeTests`, `ScopeAndConfigurationTests` (incl. `NestedScopesSurviveCanonicalIdentityOrdering`) |
| P-013/P-015/P-016/P-024 propagation and eligibility | both slices' derivation | `w3-automatic-existing-and-future-targets-in-both` | TEST-004 in both slice suites |
| P-030/P-032/P-044/P-045 commit, authority, observation | both slices | the narrative snapshot step and the card transfer step | TEST-013 in both slice suites |
| P-036/P-038 temporal models | both slices | `w3-zero-idle-command-steps-in-both` | TEST-011 in both slice suites |
| 04 §2 dependency direction | merged asmdefs | the loaded-assembly and asmdef audits | `tools/check_game_core_csharp.py` (engine-free scan) |
| 04 §3 one owning host and world per protocol world | `W3GateScenario` | registry baseline before/after each run; distinct sessions | `UnityWorldRegistry.ResetAll`, each slice's teardown step |

## 8. Known gaps, assumptions and doc ambiguities

1. **The gate runs each family over its committed generated catalog only; the fixture catalogs are covered by the
   per-slice gates on the same revision.** Running all four combinations inside the W3 probe would have doubled the
   gate's runtime and duplicated the slices' assertions; the wave clause ("both Unity-world fixtures must pass") is
   satisfied because `tools/run_w3_gate.sh` runs `-probeNarrative`, `-probeCards`, `GameCore.Narrative.Tests` and
   `GameCore.Cards.Tests` — each of which executes both catalogs — on the same revision as the W3 gate. The W3 probe
   additionally proves the two committed generated catalogs are the ones this process used, by reading their
   fingerprint literals out of the generated sources.
2. **Two worlds rather than one world with both families** (§2.1). If a reviewer wants the single-world cross-family
   composition, that is 07 §5 and the Wave 7 gate; mounting both into one world here would need one composition to
   own the other's scope root.
3. **"Committed atomically" for the card transfer** is asserted as: giver −1 and receiver +1 with the moved card
   present exactly once in the receiver and absent from the giver (the gate), plus the slice's own
   `cards-transfer-commits-both-sides` observation asserting `StepsAfterTransfer == stepBefore + 1` in the same
   world (reported inside `w3-card-facts`/the run step). The card facts do not expose the step *before* the
   transfer, so the one-step half is the slice's assertion rather than a second gate computation; this is recorded
   rather than hidden.
4. **`GameplayAsmdefsOnKernel.Count == GameplayAsmdefs.Count` requires the `.asmdef` reference lists to name
   assemblies (not `GUID:` references).** Every GameCore asmdef in this repository names its references; the audit
   would report a `GUID:`-only reference as "not on the kernel", which is a false positive rather than a silent
   pass.
5. **`packages-lock.json` is expected to change on the build host.** The merge took GC-010's lock side (the brief
   allows either), and step 2 of the gate regenerates it with all four new packages; the regenerated lock must be
   committed with the build result (04 §1 forbids synthesizing it here).
6. **The loaded-assembly audit sees only assemblies this process loads.** In the player that is every kernel
   assembly except the Editor-only `GameCore.Content.Compiler*`; the Editor sees all ten. The `.asmdef` audit is the
   discovery-complete half and runs where a project tree exists. Neither half alone is claimed as the whole proof,
   which is why both exist.
7. **`GameCore.Execution` is a namespace inside the `GameCore.Unity.Runtime` assembly, and
   `Packages/com.gamecore.gameplay.narrative/Fixtures/Runtime/GameCore.Gameplay.Narrative.Fixtures.asmdef`
   declares a reference to an assembly of that name.** No Unity assembly definition in the repository declares
   `GameCore.Execution` (the plain-dotnet project `dotnet/src/GameCore.Execution` has that name, but it compiles the
   same `Runtime/Pure` sources into a non-Unity assembly and is not a Unity assembly definition); the referenced
   types resolve through the `GameCore.Unity.Runtime` reference that the same asmdef already declares. GC-010's
   Linux build report records a Unity import and a passing narrative suite with
   this reference present, so the gate does not treat it as fatal — but it is a stale reference that Unity logs as
   unresolved. **Left unchanged deliberately** (it is GC-010's file, the type resolution does not depend on it, and
   removing it is a one-line change the owning task should make); reported here so it is not mistaken for part of
   the gate.
8. **No Unity scene/visual inspection was performed.** The EditMode scenario/assembly and the headless IL2CPP player
   are the exercised surfaces, exactly as in the W2 gate.
9. **`artifacts/w3-gate/{trx,unity,toolchain}` are produced by the gate; nothing is pre-populated here.** Only
   `HANDOFF.md` and `static-checks.log` are committed by this task.
10. **Nothing in this change set has been compiled, imported or executed.** The most likely first failures, in
    order: (a) a member-name slip in `KernelAssemblyAudit`/`W3GateScenario` (Unity does not treat warnings as
    errors, but a wrong member name is a hard error; the highest-risk spots are the audit's assembly iteration and
    the facts property names of the two slices); (b) an asmdef reference error for `GameCore.Rules.Cards` in the
    probe host (added here) or `GameCore.Rules.Cards`/`GameCore.Rules.Narrative` in `GameCore.W3Gate.Tests.asmdef`;
    (c) an expectation mismatch in the gate's literal values (3 derived narrative targets, 4+2+1+1 narrative rows,
    `+2/+2/+1/+2` card bonuses, 2 committed narrative events, 1-by-1 card transfer) — each of those literals is
    copied from the corresponding slice's own suite, which is the safest available source; (d) the `.meta` GUIDs
    were authored here and verified unique repository-wide, but only an import can confirm Unity keeps them — commit
    any rewrite.
