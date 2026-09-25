# GC-010 HANDOFF — narrative Automatic vertical slice

Branch `gc-010` (worktree `/Users/yangcao/wkspace/gc-wt/gc-010`).

**Status of every executable check below: `NotRun (pending orchestrator build host)`.** This host has no .NET SDK,
no C# compiler and no Unity, so nothing in this change set has been compiled, imported or executed here. The only
things that ran are interpreter-level host checks, listed in §7 with their verbatim output. Nothing in this document
claims that a test passed or that a build succeeded.

Sentence implemented (verbatim, `docs/game-core/09-implementation-guide.md`, GC-010):

> Mount at a chapter, create a future target and observe a gate decision through a committed snapshot; use reusable
> descriptors with no per-instance capability imports.

## 1. Summary

Two new packages plus the qualification wiring. The slice runs through exactly the Wave 2 kernel pipeline
(`CompositionHost.SubmitEdit`/`Drain` → `CompositionDerivationInput`/`DerivationEngine` → `DerivedCompositionProposal`
→ `OwnershipSchedulePipeline` → `AssemblyPlanner`/`AssemblyPublisher` → `UnityWorldHost.Submit`/`WorldMessagePlane`
→ `UnityExecutionDriver`/`WorldTimeDriver` → committed snapshot + `AssemblyPublisher.Spawn`), with no private seam
fixture and no second implementation of any module:

| Requirement | Where it is produced | How it is observed |
|---|---|---|
| a chapter mounted at a chapter scope | `NarrativeScenario.MountChapterOneAndPublish` | `narrative-chapter-one-mounted-and-published` (lane 2/2 == world 2) |
| existing descendants bound automatically, no per-instance import | chapter rules select the reusable recipes (`NarrativeDeclarations.Rules`) | `narrative-derived-layout-in-entities`: `npc-mara` 2 rows, `gate-east` 1, `encounter-oak` 1; `crowd-prop` 0; museum `npc-display` 0 (P-015 ineligible + P-016 isolation) |
| a future target gains it too | `DerivedAssemblyPipeline.PublishSpawn` after a no-target-change publication | `narrative-forward-provider-and-spawned-target`: 2 rows, value 1, stamp == published epoch |
| a typed command changes the authoritative gate outcome | admitted `CommandEnvelope` → dialogue owner → quest fact → gate owner | `narrative-one-choice-command-committed`: fact 1@v2, gate decision 0→1, 2 committed events, 1 step |
| ten idle seconds advance ZERO steps | `WorldTimeDriver.PumpFrame` × 600 frames | `narrative-idle-world-performs-zero-steps`: 0 steps, 0 dispatches, 0 images, demand 0 |
| no actor/vitality/physics schema or stage | `NarrativeInventory` (96 content names + gameplay names + component inventory) | `narrative-genre-neutrality`: audit neutral, checked == declared counts |

## 2. Files created

Pure rules package `Packages/com.gamecore.rules.narrative/` (assembly `GameCore.Rules.Narrative`, Unity-free,
`noEngineReferences: true`):

| File | Contents |
|---|---|
| `package.json`, `Runtime/GameCore.Rules.Narrative.asmdef` | package + assembly declarations |
| `Runtime/NarrativeCompositionNames.cs` | every stable **name** of the composition (scopes, recipes, targets, capabilities, schemas, domains, owners, slots, stages, systems, buffers, routes, migrations) plus `NarrativeIds` identity helpers (`Id/Scope/Target/Instance/Installation/Definition/Capability/Slot/Rule/Owner/Stage/Buffer/Route/Key/SchemaRef/CapabilityRef/Recipe`) |
| `Runtime/NarrativeChapters.cs` | `ChapterDefinition` (tag, binding ordinal, opening node, gate fact key, definition names, hook plan), `NarrativeDefinitionSuffixes`, `NarrativeChapters` (lookup by tag and by ordinal) |
| `Runtime/NarrativeFacts.cs` | the declared durable facts, fact ordinals, slot tags, and the pure transition rule (`TryTransition` with a stable refusal code) |
| `Runtime/NarrativeDialogueRules.cs` | `NarrativeChoice`, `NarrativeConversationStatus`, `ChoiceValidation`, `NarrativeDialogueRules.Validate`, the conversation transition table and the registered node migration |
| `Runtime/NarrativeGateRules.cs` | gate evaluation, `TryEvaluate`, the `RebindGate` function (`TryRebind`), decision text |
| `Runtime/NarrativeEncounterRules.cs` | encounter status, the hook plan and its lookup, the reaction rule, the session identity |
| `Runtime/NarrativeDerivationPlan.cs` | the four bindings a chapter contributes (capability, stratum, rule suffix, selector recipe, schema, input capability, policy) |
| `Runtime/NarrativeRegistrations.cs` | every content name the rules package registers (96, asserted unique) |
| `Runtime/NarrativeGenreAudit.cs` | the forbidden genre vocabulary and the audit (`Audit` refuses null/empty) |
| `Runtime/NarrativeRefusals.cs` | stable refusal codes (P-052) |
| `Runtime/NarrativeDigest.cs` | one canonical digest function (`OfText`, `OfLines`) |
| `Runtime/NarrativeTrace.cs` | deterministic writer/reader of the canonical trace document (`RulesSectionLines`, `RulesDigest`, `Write`, `TryReadPipelineEntries`) |
| `Runtime/NarrativeScenarioTrace.cs` | the declared canonical trace: `ExpectedPipeline()` (46 keys), `ExpectedDocument()`, `TryCompare` |
| `Tests/**` (12 files, authored by the dotnet-wiring worker under this task's spec) | 112 NUnit test methods/cases: identity agreement with the GC-006 fixture, chapter content, fact rules, dialogue, conversation transitions, gates, encounters, the derivation plan, the genre audit, digests, trace documents |
| `Tests/GameCore.Rules.Narrative.Tests.asmdef` | the package's EditMode assembly, so the same 112 cases that run under plain dotnet also run in Unity (`testables` already lists the package) |
| `Tests/Trace/CommittedTraceTests.cs` | the committed-trace regression: regenerate offline, byte-compare, pipeline/expectation comparison, slot tags |

Gameplay package `Packages/com.gamecore.gameplay.narrative/` (assembly `GameCore.Gameplay.Narrative`):

| File | Contents |
|---|---|
| `package.json`, `Runtime/GameCore.Gameplay.Narrative.asmdef` | package + assembly declarations |
| `Runtime/NarrativeKeys.cs` | every stable identity of the slice, derived from the rules package's names |
| `Runtime/NarrativeDeclarations.cs` | the chapter provider's manifest (4 contracts, 4 rules, 6 stages, 6 systems, 5 buffers, 15 state slots), the sibling chapter's manifest, the forward provider's manifest |
| `Runtime/NarrativeComponents.cs` | the one ECS component (`NarrativeTargetMarker`) and the declared component inventory |
| `Runtime/NarrativePayloadCodec.cs` | the routes' canonical big-endian payload encoding (choice, mutation, observation, gate change, choice outcome) |
| `Runtime/NarrativePayloadReaders.cs` | the three generated-style payload readers (no reflection) |
| `Runtime/NarrativeInventory.cs` | the full registered-name inventory + `Audit`/`AuditComponents` |

Fixture assembly `Packages/com.gamecore.gameplay.narrative/Fixtures/Runtime/` (assembly `…Fixtures`):

| File | Contents |
|---|---|
| `GameCore.Gameplay.Narrative.Fixtures.asmdef` | assembly declaration |
| `NarrativeScenarioCatalog.cs` | hand-written generated-style catalog (factory key, schema, serializer, `Build`, `Fingerprint`) |
| `NarrativeRecipes.cs` | the base-layout applier and the six recipes + the closed recipe catalog |
| `NarrativeWorld.cs` | `NarrativeState` (owner-slot access + chapter resolution from the effective assembly), `NarrativeModule`, and the six stages: input, dialogue, quest, gates, encounters, output |
| `NarrativeRegistration.cs` | the message plane (1 route, 5 bounded lanes), the readers, the six system registrations, the world registration and the creation request |
| `NarrativeMigrations.cs` | the two registered conversation migrations and the validation-side `ISlotMigrationRegistry` |
| `NarrativeScenario.cs` | `NarrativeStep`, `NarrativeFacts` (observed facts + `PipelineEntries`), `NarrativeScenarioResult`, `NarrativeMounts` (mount + scope-create payloads) and the eleven-step scenario |

Unity qualification project (authored by the Unity-wiring worker under this task's spec): `Runtime/NarrativeScenarioHost.cs`,
`Runtime/ProbeNarrative.cs`, the `-probeNarrative` mode in `ProbeArguments.cs`/`ProbeRunner.cs`,
`Tests/Narrative/{GameCore.Narrative.Tests.asmdef, NarrativeIntegrationTests.cs}`, `tools/unity/run_narrative_probe.sh`,
`tools/run_gc010_gate.sh`, plus their `.meta` files.

Plain-dotnet wiring (authored by the dotnet-wiring worker under this task's spec):
`dotnet/src/GameCore.Rules.Narrative/GameCore.Rules.Narrative.csproj`,
`dotnet/tests/GameCore.Rules.Narrative.Tests/GameCore.Rules.Narrative.Tests.csproj`, two `GameCore.sln` entries,
`dotnet/README.md` rows.

Evidence: `artifacts/gc-010/narrative-trace.json` — the canonical narrative trace (§5).

## 3. Files modified (each minimal and explained)

| Path | Change | Why |
|---|---|---|
| `unity/GameCore.Validation/Packages/manifest.json` | `+ com.gamecore.rules.narrative`, `+ com.gamecore.gameplay.narrative` in `dependencies`; `+ com.gamecore.rules.narrative` in `testables` | the two new packages must resolve for the Editor and the player |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeArguments.cs`, `ProbeRunner.cs` | the sixth probe mode `-probeNarrative` (task `GC-010`, mode `Narrative`), existing modes untouched | the player half of the slice |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/GameCore.Validation.ProbeHost.asmdef` | `+ GameCore.Rules.Narrative`, `+ GameCore.Gameplay.Narrative`, `+ GameCore.Gameplay.Narrative.Fixtures` | the probe host names the scenario and the declarations |
| `unity/GameCore.Validation/Assets/link.xml` | `preserve="all"` for the three new assemblies | High stripping must not remove the scenario, the systems or the readers |
| `dotnet/GameCore.sln`, `dotnet/README.md` | two project entries + documentation rows | the pure rules package builds under plain dotnet |
| `tools/check_game_core_csharp.py` | `+ Packages/com.gamecore.rules.narrative` (TARGETS and `engine_free`), `+ Packages/com.gamecore.gameplay.narrative` (TARGETS only) | the host-side balance/forbidden-construct checks now cover both new packages; the gameplay package legitimately references `Unity.Entities`, so it is not in `engine_free` |

No existing kernel file was modified. **Kernel changes: none.** No package outside `Packages/com.gamecore.rules.narrative`
and `Packages/com.gamecore.gameplay.narrative` was touched except the shared wiring files above.

## 4. Exact commands for the Linux build host

Everything runs from the repository root. Nothing here has been run.

### 4.1 One command (the whole gate)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet tools/run_gc010_gate.sh
```

It runs the pure solution build and test, a Unity package resolve, the whole EditMode suite (every `testables`
package plus `GameCore.W1Gate.Tests`, `GameCore.W2Gate.Tests` and `GameCore.Narrative.Tests`), the PlayMode suite,
`tools/unity/build_probe.sh` (catalog codegen + StandaloneLinux64 IL2CPP with High stripping), the four player probes
(`run_probe.sh both`, `run_world_probe.sh`, `run_w1_gate_probe.sh`, `run_w2_gate_probe.sh`, `run_narrative_probe.sh`)
each repeated `PROBE_RUNS` times (default 5), and the documentation validator.

### 4.2 The pieces

```sh
# dotnet half
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-010/trx

# the narrative rules package alone
dotnet test dotnet/tests/GameCore.Rules.Narrative.Tests/GameCore.Rules.Narrative.Tests.csproj -c Release

# the GC-010 EditMode assembly alone (no -quit on a test run, 04 section 10)
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Narrative.Tests \
  -testResults artifacts/gc-010/unity/narrative-editmode.xml -logFile artifacts/gc-010/unity/narrative-editmode.log

# the player probe, five times
PROBE_RUNS=5 tools/unity/run_narrative_probe.sh

# what the earlier gates must still pass unchanged on this revision
UNITY="$UNITY" tools/run_w1_gate.sh
UNITY="$UNITY" tools/run_w2_gate.sh
```

`run_narrative_probe.sh` requires `"task": "GC-010"`, `"mode": "Narrative"`, `"result": "Pass"`, no `"status": "Fail"`,
all eleven steps twice (plain and `fixture:`-prefixed), the two facts digests, and that the generated run's facts name
the committed generated catalog's fingerprint literal.

### 4.3 The narrative EditMode cases

`GameCore.Narrative.Tests` asserts the eleven named observations over both catalogs (generated and
hand-written), their exact order, and these values: `LiveTargetCount == 7`,
`DerivedTargetCountAfterChapterOne == 3`, `ChapterOneInstalledRows == 4`, `MigratedConversationSlotCount == 2`,
`PublishedBindingRowCountAfterChapterTwo == 6`, `SpawnedBindingRowCount == 2`, `SpawnedBindingValue == 1`,
`PublishedBindingRowCountAfterSpawn == 8`, `GateDecisionBeforeCommand == 0`, `GateDecisionAfterCommand == 1`,
`QuestFactValueAfterCommand == 1`, `QuestFactVersionAfterCommand == 2`, `CommittedEventCountAfterCommand == 2`,
`StepsAfterCommand == 1`, `StepsAfterDuplicate == 1`, `IdleFrames == 600`, `IdleStepsCommitted == 0`,
`GenreAuditChecked == NarrativeRegistrations.Count`, `GenreAuditNeutral`, `ForbiddenGenreNameCount == 0`.

## 5. The canonical trace

`artifacts/gc-010/narrative-trace.json` is the narrative slice's canonical trace. Its two halves are deliberately
different kinds of evidence:

* the **rules section** (32 lines), the **name audit** (96 names) and their digests are pure functions of the rules
  package's declarations. Any process — a plain dotnet test, the Editor, an IL2CPP player — regenerates the document
  byte for byte. `Tests/Trace/CommittedTraceTests.cs` regenerates it offline and requires byte equality with the
  committed file, so a moved rule, stratum, name or expectation fails without a Unity build.
* the **pipeline section** (46 keys) is the declared expectation of one run, not a recording of one: the scenario
  reports its observed values under exactly those keys, and `NarrativeScenarioTrace.TryCompare` refuses a run that
  disagrees with the declaration or records an undeclared key. The run's own observed trace is written by the probe
  under `artifacts/gc-010/toolchain/`; it never overwrites the committed canonical document.

Consequence a reviewer should notice: the committed document's pipeline values are **expectations**, and the evidence
for them is the scenario's assertions (`NotRun` here; the gate above produces it). The rules section, by contrast, is
already reproducible on this host — an independent Python replication of `RulesSectionLines()`/`ExpectedPipeline()`
produced byte-identical content, which is how the committed file was generated in the first place.

## 6. Requirement and test coverage mapping

| Normative requirement | Where implemented | Where observed |
|---|---|---|
| P-001 genre independence | `NarrativeInventory`, six stages that name no genre concept, one component type | `narrative-genre-neutrality`; `NarrativeComponentInventory.Audit()`; the rules suite's `GenreNeutralityTests` |
| P-003 three kinds of change | the contribution (derived binding) is retracted by removing an installation; committed facts survive (07 section 3.3) | chapter-one/chapter-two mounts and their rows; GC-006's own unmount suite covers the retraction case |
| P-013 `Automatic` propagation, no target imports | `DerivationRule.Reach = SelfAndDescendants`, `ExportToDescendants = true`, selectors are reusable recipes | `narrative-derived-layout-in-entities`, `narrative-forward-provider-and-spawned-target`; rules suite `IdentityAgreementTests` |
| P-015 eligibility | ineligible recipe (`crowd-prop`), isolated branch (museum) untouched | same two steps assert 0 rows on both |
| P-024 spawn with a complete first image | `NarrativeRecipeApplier` base layout + `PublishedWorldView.RulesFor` rows inside the fence | `narrative-forward-provider-and-spawned-target` |
| P-026 explanation | GC-006's explanations are unchanged; the slice adds no second provenance store | GC-006 suite; the trace's `rules` section records the declaration |
| P-032 state dispositions, one owner per domain | 15 declared slots over 5 domains, one layout per domain with one field per slot; conversation domain declares version 2 | `narrative-chapter-one-mounted-and-published` (`migratedConversationSlotCount == 2`), rules suite's migration tests |
| P-034 state authority | `OwnershipSchedulePipeline` over the declared writers; each stage writes only its own domain | `narrative-world-and-live-targets` (descriptor built), trace's `compiledStageCount/compiledSystemCount` |
| P-036/P-037/P-038 temporal model and step admission | CommandDriven world, one admitted command is one step, no kernel rate | `narrative-one-choice-command-committed`, `narrative-duplicate-request-commits-nothing-new`, `narrative-idle-world-performs-zero-steps` |
| P-042/P-043 typed command, declared buffers | one route + five bounded lanes with one owner, one producer and one lifetime each | `narrative-one-choice-command-committed` (one ledger row committed, none pending) |
| P-044/P-045 commit and observation | two committed events (choice, gate) exposed together with the snapshot; the fact is in the committed slot storage | `narrative-one-choice-command-committed` |
| P-046/P-048 lifecycle and teardown | `host.Stop`/`Dispose`, module detach, ledger counters | `narrative-teardown-settles-and-disposes` |
| P-056 extension points | the slice uses only registered reducers/predicates/stages/ports/rule functions | the declarations and the six stages |
| P-059 genre validation before freeze | this is the narrative half of the two-family requirement; the card half is GC-011 | the wave gate |
| TEST-004 | mount binds existing descendants *and* the future spawned target, with no per-instance import | `narrative-derived-layout-in-entities`, `narrative-forward-provider-and-spawned-target` |
| TEST-013 | a narrative flag transition that either commits or is refused; duplicate request transfers nothing | `narrative-one-choice-command-committed`, `narrative-duplicate-request-commits-nothing-new` |
| TEST-015 | the scenario mounts three installations, tears the world down and asserts the registry and ledger return to baseline | `narrative-teardown-settles-and-disposes` (the full 1,000-cycle churn is GC-016/GC-021) |
| TEST-021 | the neutrality audit over the content names, the full inventory and the component inventory | `narrative-genre-neutrality` |

## 7. Host-side checks that did run here

```sh
python3 tools/check_game_core_csharp.py
# checked 237 C# file(s)
# ok

python3 tools/validate_game_core_docs.py
# Game Core documentation validation passed: 14 Markdown documents; local links, anchors, IDs, traceability,
# task DAG and wave ordering checked.

bash -n tools/run_gc010_gate.sh tools/unity/run_narrative_probe.sh
# bash -n ok
```

None of those is a build or a test result. In addition, a brace/paren balance scan (comment-, string- and
char-literal-aware) over every file of the three new source roots reports zero imbalance, and the committed trace was
generated by an independent Python replication of the rules section and the declared pipeline.

## 8. Decisions, assumptions and doc ambiguities

Recorded because 00 wins over 05, which wins over 09.

1. **The gameplay package declares the composition's stable names itself instead of referencing the GC-006 fixture.**
   09 says the derivation fixture holds "reusable descriptors so GC-010 can run the narrative slice", but a gameplay
   assembly may not reference `GameCore.Derivation.Fixtures` (dependency direction: gameplay → kernel only), and 04
   §2's allowed-references row for `GameCore.Gameplay.<Name>` is "corresponding Rules assembly, Unity Runtime and
   explicitly required adapters". So the *names* live in `GameCore.Rules.Narrative.NarrativeCompositionNames` and
   `GameCore.Rules.Narrative.NarrativeIds` derives the same identities with the production rule; the rules suite's
   `IdentityAgreementTests` asserts, name by name, that `NarrativeIds.X(name) == FixtureIds.X(name)` for every
   scope, recipe and target GC-006 declares. The fixture's *rule builders* are not reused: their payloads are
   16-byte definition identities, while a published binding row carries exactly one canonical int32 (05 §6). This is
   the same seam limit the W2 gate recorded (its handoff §7.3), and the slice's own rules therefore contribute the
   chapter's **binding ordinal** as the row value, with the chapter-provenance identifying the installation (P-017).
2. **The chapter's binding ordinal is the derived value.** A binding row holds one int32 per
   `(target, capability, output slot)`, and each narrative capability declares `Replace` with one supporter, so the
   derived value transfers as-is. Every scenario and rules assertion reads that number.
3. **A fact is one owned state slot.** P-032 addresses state as `(TargetId, OwnerId, SlotId)` and a state slot's
   value is an `int`, so one declared fact occupies a value slot and a version slot on the world-level ledger target;
   the fact's *key* is a content string and its **slot identity is derived from a lowercase slot tag**
   (`bridge-permit`), because a stable name may not contain uppercase characters (05 §3, `StableNameKeyDerivation`).
   The ledger is therefore a real target of `narrative.quest-ledger-recipe` with slots, not a kernel concept.
4. **The command's terminal result belongs to the dialogue owner, and the gate decision is the second committed
   event.** 07 §3.2 gives the choice to the dialogue/chapter path, the fact to the ledger and the condition to the
   gate owner, and says the fact and the open gate are exposed together. The scenario asserts exactly two committed
   events per accepted choice — the accepted choice and the gate change — with the fact transition observable in the
   committed slot storage. A third event (a separate fact-transition event) would double-report one step's outcome.
5. **The input stage forwards the admitted choice to the dialogue owner rather than answering it.** 07 §3.2's graph is
   `input → dialogue`, and P-042 routes a request to the owner that validates gameplay, so the ingress lane is owned
   by a routing owner, and the forwarded row keeps the original identity and causal request. The dialogue owner's
   single `Commit` therefore settles the host's ledger row (P-037, P-042).
6. **The encounter rule reports one refusal code for "not a transition"** (`encounter-unchanged`), whether an idle
   encounter's condition does not hold or a live encounter keeps running; the fact rule's code is reserved for the
   fact domain.
7. **Twenty-five declared names the rules package does not own are added by the gameplay inventory** (the trail
   domain, the gameplay slots and fields, the extra lane/order/schema and migration identities, the routing owners and
   the provider words). `NarrativeInventory.Count == NarrativeRegistrations.Count + GameplayNames.Count` is asserted,
   so the neutrality claim covers every name either package registers.
8. **`NarrativeScenarioTrace` declares the trace's pipeline values.** The committed document is therefore an
   *expectation* file for the run, not a recording: the run's own trace is written beside it by the probe. The rules
   half is genuinely reproducible offline and is byte-compared by a dotnet test.
9. **The museum target is a villager recipe under a `CapabilityIsolation: *` scope** and receives nothing, which is
   the slice's proof that P-016's boundary is not bypassed by a compatible recipe (07 §3.1). The sibling chapter is
   the second proof: chapter two's rows appear only on `npc-sailor`.

## 9. Independent review findings, and what was fixed

Two read-only reviewers audited the new packages against the real kernel sources (they also reported the rules
package's `Tests/` folder shipped without an EditMode asmdef, which is now added). The gameplay audit found seven
hard defects, all fixed in this change set:

| Defect | Fix |
|---|---|
| `NarrativeFacts` — the fixture's observed-facts class sits in the same namespace as the fixture's *uses* of the rules type, and C# namespace lookup precedes `using` imports, so every `NarrativeFacts.X` in `Fixtures/**` bound to the instance class | the three fixture files now alias the rules type (`using RulesNarrativeFacts = GameCore.Rules.Narrative.NarrativeFacts;`) and use the alias. The public name the Unity suite aliases is unchanged |
| `Id128.ToHex(value)` — `ToHex` is a static member of `Id128Codec`, not of `Id128` | `Id128Codec.ToHex(...)` |
| `BufferSpec` was constructed with eleven arguments; it declares ten and has no byte capacity (that field belongs to the plane's `MessageBufferDescriptor`) | the extra argument is gone; the plane lanes still declare their byte capacity |
| the shared `Manifest(...)` helper passed fifteen arguments to `PluginManifest`'s sixteen-parameter constructor (the `targetDescriptors` slot was missing) | the missing `null` is passed explicitly |
| `ForwardProvider` passed fourteen arguments to the same constructor | all five trailing declaration lists are supplied explicitly |
| `NarrativeCatalogSerializerKeyHolder` — a left-over property naming a type that does not exist | deleted |
| `NarrativeIds.SchemaRef` built a `SchemaRef` from a raw `Id128`; the constructor takes a `SchemaId` | `new SchemaRef(Schema(stableName), version)`, matching `FixtureIds.SchemaRef` |
| `BuildScopeTree()` ran before the control lane existed, so it always returned false and the world would have been empty | the lane and the bridge are created first, the scope tree next, and the targets after it |
| the second chapter's manifest re-declared the shared state slots, stages and buffers; the schedule compiler rejects a duplicate buffer contract, so the whole catalog revision would have been uncompilable | only the first provider declares the execution and ownership surface; a second chapter declares exactly what it contributes (its contracts and its rules), like the W2 gate's second provider |

Reviewer-verified as correct and unchanged: the whole-chain call sequence against `W2GateScenario` (world creation,
publisher construction, mount payload, `PublishDerived`/`PublishSpawn`, `host.Submit` + `PumpFrame`, teardown), every
constructor arity in the rules package, the rules package's 112 test expectations, the 96-name audit, and the
committed trace (an independent regeneration was byte-identical). The trace artifact was regenerated after the
encounter refusal code was unified (`Idle + condition-not-holding` now reports `encounter-unchanged` rather than the
fact rule's code), and the one test that asserted the old value was updated with it.

## 10. Known gaps and risks

* **Nothing here has been compiled, imported or executed.** The most likely first failures, in order:
  1. a member-name/arity slip in `NarrativeScenario.cs` or `NarrativeWorld.cs` against the real kernel surface;
  2. an asmdef reference error (`GameCore.Gameplay.Narrative` → `GameCore.Unity.Runtime`/`GameCore.Planning`, and the
     fixtures assembly's `GameCore.Derivation.Fixtures` reference) — this is the first consumer of those edges;
  3. an expectations mismatch in the scenario's literal numbers (they are asserted exactly, and the same numbers are
     declared in `NarrativeScenarioTrace.ExpectedPipeline`);
  4. `unity/GameCore.Validation/Packages/packages-lock.json` is stale relative to the two new packages — the gate's
     resolve step regenerates it and the regenerated lock must be committed with the result (04 §1 forbids
     synthesising it here);
  5. `.meta` files: the two new packages intentionally ship **without** `.meta` files. Unity generates them on first
     import; the build host should commit them (the brief left `.meta` generation to the build host). The Unity
     project's own new assets (`NarrativeScenarioHost.cs`, `ProbeNarrative.cs`, the `Tests/Narrative/` folder, its
     asmdef and test file) do ship hand-authored `.meta` files with deterministic GUIDs, which only an import can
     confirm Unity keeps.
* **A reliable lane with rows must be drained by its owner in the same step**, or the plane's commit validation
  refuses the step (`BoundedBufferRules.ValidateDrain`). Each of the six stages calls `ReleaseConsumed` on its own
  owner every step, including the steps where its lane is empty, which is what makes an idle step a no-op rather than
  a refusal.
* **The slice has no job and no native container**, so it does not exercise P-041's producer/consumer fence (the W2
  gate does, and GC-020/GC-023 own the job-heavy fixtures). Correspondingly there is no native lifetime risk in its
  teardown, which is why `narrative-teardown-settles-and-disposes` asserts zero outstanding jobs and zero retained
  resources rather than a fence.
* **TEST-015 is claimed only for one mount/teardown cycle per catalog**, not the 1,000-cycle churn the suite also
  contains; that belongs to GC-016/GC-021 and is not claimed here.
* **No IL2CPP/stripping evidence exists yet** for the new assemblies; `link.xml` preserves them, and the gate above is
  what produces the player evidence (TEST-001/TEST-020's player half).
* **The EditMode half of the rules package must now be listed by its asmdef** (`Tests/GameCore.Rules.Narrative.Tests.asmdef`);
  without it the `testables` entry is inert. The package's tests reference `GameCore.Derivation.Fixtures`, which is a
  Unity assembly, so the asmdef references it (07's reuse rule keeps that a test-only dependency; the package's own
  `Runtime/` is Unity-free and has no such reference).
* **`dotnet/README.md`, `dotnet/GameCore.sln` and the rules package's own tests were authored by a delegated worker**
  under this task's specification; their content was reviewed here, but they carry the same `NotRun` status.
