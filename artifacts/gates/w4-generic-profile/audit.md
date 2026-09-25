# GC-012 — generic contract / assembly audit (Wave 4)

**Status: static and runtime halves evidenced on the Linux build host.** The regenerated
`generic-profile-audit.json` scans 38 assembly definitions (including the dedicated family-entry assembly);
the full EditMode suite passed 613/613 and the High-stripping IL2CPP `-probeW4Profile` ran cleanly 5/5.
Its runtime audit reports zero forbidden kernel references, zero duplicate kernel assemblies and zero inspection
failures. See `artifacts/gc-012/BUILD_REPORT.md` and the committed XML/JSON results for command and revision pins.

## 1. What was audited, and against what

| Question | Answer this revision gives | Where the evidence is |
| --- | --- | --- |
| Does any **kernel** assembly reference a **gameplay/rules/validation/generated** assembly? | No. Zero edges, in both directions checked. | `generic-profile-audit.json` → `forbiddenEdges.kernelToGameplay`, `forbiddenEdges.kernelToValidation` (both empty); and at runtime `KernelAssemblyAudit.AuditLoadedAssemblies` in `-probeW4Profile`. |
| Does any kernel assembly that declares `noEngineReferences: true` reference a `Unity*` assembly? | No. Zero violations. | `generic-profile-audit.json` → `kernelNoEngineReferenceViolations` (empty). |
| Are any kernel assemblies loaded twice in the player (two copies of the kernel)? | Refused by the runtime audit; `duplicateKernelAssemblies` must be 0. | `KernelAssemblyAudit.AuditLoadedAssemblies`; the probe driver requires `duplicateKernelAssemblies=0` in the facts digest. |
| Does every loaded gameplay/rules assembly reference at least one kernel assembly? | Refused by the runtime audit if not; the `.asmdef` half requires it for every gameplay package. | `KernelAssemblyAudit` `GameplayFamilyOnKernel.Count == GameplayFamilyAssemblies.Count`; `tools/run_w4_profile_gate.sh` runs the EditMode suite that asserts it. |
| Does any kernel/generic type **require** a genre-specific type? | No such type exists, and no kernel *runtime* file names one except through generic mechanisms named after unrelated concepts. See §3. | `generic-profile-audit.json` → `typeInventory` (697 public/internal top-level types) and `genreTokenScan`. |

The rule encoded here is 04 §2's assembly table ("`GameCore.Contracts` … `GameCore.Content.Compiler` may reference
only the assemblies listed in their rows; `.asmdef` references must remain acyclic") and P-001 ("The kernel MUST
provide composition, identity, assembly, execution coordination, lifecycle, and observation contracts without
requiring an actor, action, turn, combat, physics, animation, resource, or reward schema").

Reproduce the host-side half with:

```sh
python3 tools/w4_generic_profile_audit.py --out artifacts/gates/w4-generic-profile/generic-profile-audit.json
```

The tool is deterministic (sorted output, repo-relative POSIX paths, no timestamps); two consecutive runs are
byte-identical.

## 2. Assembly reference graph

Extracted from every `*.asmdef` under `Packages/` and `unity/GameCore.Validation/Assets/` — 38 assemblies:

| Classification | Count | Members |
| --- | ---: | --- |
| kernel | 14 | `GameCore.Contracts`, `GameCore.Composition`, `GameCore.Derivation`, `GameCore.Planning`, `GameCore.Unity.Runtime`, `GameCore.Unity.Adapters`, `GameCore.Content.Compiler` (+ `Editor`), `GameCore.Derivation.Fixtures`, and five Editor test assemblies. |
| gameplay / rules | 8 | `GameCore.Gameplay.{Narrative,Cards}` (+ both `Fixtures`), `GameCore.Rules.{Narrative,Cards}` (+ both `Tests`). |
| validation | 7 | `Probe`, `ProbeHost`, `Editor`, `Generated`, `GeneratedCards`, `Fixture` and the separate `FamilyEntries` assembly. |
| fixture | 1 | `GameCore.Unity.Fixtures`. |
| other | 8 | Remaining `GameCore.Unity.*` and `GameCore.*.Tests` Editor assemblies. |

**Forbidden edges found: none.** Every kernel assembly's declared references resolve inside the kernel set (or to
`Unity.*`/`UnityEngine.TestRunner` for the Unity-side kernel assemblies), and every gameplay/rules assembly
references the kernel. `guidReferences: []` and `unresolvedReferences: []` for all 37, so no edge is invisible to
the audit for lack of a name (the W3 gate noted this as a precondition: a `GUID:`-only reference would be reported
as unresolved rather than silently passing).

The cross-package direction that matters most for P-001 is the *runtime* kernel:

| Kernel assembly | References |
| --- | --- |
| `GameCore.Contracts` | *(none)* — `noEngineReferences: true` |
| `GameCore.Composition` | `GameCore.Contracts` |
| `GameCore.Derivation` | `GameCore.Contracts` |
| `GameCore.Planning` | `GameCore.Contracts` |
| `GameCore.Unity.Runtime` | `GameCore.Composition`, `GameCore.Contracts`, `GameCore.Derivation`, `GameCore.Planning` |
| `GameCore.Unity.Adapters` | `GameCore.Contracts`, `GameCore.Unity.Runtime` |

No kernel assembly's reference list contains a `GameCore.Gameplay.*`, `GameCore.Rules.*`, `GameCore.Validation*` or
`GameCore.Generated*` name.

## 3. Type inventory and the genre-name scan

697 public/internal top-level types are declared across the kernel packages. The scan looks for 25 genre-specific
tokens as whole identifiers in those files: `actor, action, turn, combat, hit, vitality, quest, inventory, damage,
card, deck, hand, narrative, chapter, physics, animation, reward, transform, gameobject, monobehaviour, prefab,
story, villager, seat, gate`. It found 324 textual hits, and **not one is a genre-specific type requirement**. The
classification below is the audit's actual result, including the cases that look alarming:

| Token group | Hits | What they are | Verdict |
| --- | ---: | --- | --- |
| `gate` | 135 | Two generic mechanisms: the **propagation-mode gate** (`DerivationPolicy.ModeGateDecision`, `EvaluateModeGate` — the P-013 mode gate) and the **resource/acquisition gate** (`IPlanResourceGate`, `ManagedResourceGate`, `InertAcquisitionSet.gate`, the ingress-callback gate). Also prose in comments. | Not a narrative gate. Generic. |
| `action` | 9 | `System.Action<T>` — the BCL delegate, in `Documents.cs` (envelope writer callback) and `ManagedResources.cs` (dispose callback). | Not an action schema. BCL. |
| `chapter`, `story`, `villager`, `seat`, `narrative`, `card`, `quest`, `reward` | 97 | Test/fixture-local names: a derivation test names the scope or target it is deriving for (`CardComposition.SeatB`, `Story`, `Village`), and a fixture key uses `fixture.migrate.quest.v1-v2` as its diagnostic stable name. **All in `Tests/` and `Fixtures/` inside the kernel packages, none in kernel `Runtime/` production code.** | Fixture data, not a kernel dependency. |
| `turn`, `combat`, `physics`, `animation`, `actor`, `vitality` | 12 | Literal entries of *negative* assertions: `string[] forbidden = { "combat", "physics", "animation", "turn", "actor", "vitality" }` in `ScheduleCompilerTests`/`ScheduleAdapterTests`. The kernel tests assert these names never reach a compiled schedule. | The opposite of a dependency. |
| `hand` | 10 | Prose ("do not edit by hand", "hand the world"). The whole-word match means `Handle`/`handful`/`shader` never match (proved with a synthetic file during tool development). | Prose. |
| `transform` | 3 | Prose ("the pure transform ran on the copied value") for a state migration function. | Prose. |
| `gameobject`, `monobehaviour`, `deck`, `hit`, `inventory`, `damage` | 0 | — | Absent. |
| `prefab` | 1 | Comment in `SpawnRecipeCatalog.cs` describing allowed recipe references; no runtime type dependency. | Prose. |

The one kernel-runtime token concentration worth naming explicitly: `Packages/com.gamecore.derivation/Runtime/Policy/DerivationPolicy.cs`
(13 hits, all `gate`, all `ModeGateDecision`/`EvaluateModeGate`) and
`Packages/com.gamecore.planning/Runtime/Plans/InertAcquisitions.cs` (10 hits, all `gate`, all `IPlanResourceGate`).
Both mechanisms derive from 00 §4/§6 and 05 §4, not from a game family.

### 3.1 The genre-specific types that *do* exist, and where they live

They exist, by design, in the families:

* `GameCore.Rules.Narrative` / `GameCore.Gameplay.Narrative` — chapters, dialogue, quest facts, gates, encounter
  hooks;
* `GameCore.Rules.Cards` / `GameCore.Gameplay.Cards` — cards, seats, hands, set bonuses, settlement.

Both are `GameCore.Rules.*`/`GameCore.Gameplay.*` assemblies, which no kernel assembly references. T-021's
acceptance clause ("Any generic API needing an actor, hit, turn, quest or Transform for an unrelated template blocks
protocol stabilization and must be corrected in the normative source and traceability") found nothing to correct in
the kernel this revision.

## 4. The GC-012 additions, audited

GC-012 added kernel surface; each addition was checked against P-001 before it landed:

| Addition | Generic? | Why |
| --- | --- | --- |
| `GameCore.Planning.CapabilitySupport` (+ `TargetBindingRow.Supports`, `DerivedBindingRule.Supports`, `ProposedCapability.Supporters`) | Yes | Provider installation, activation generation, derivation rule, an `int` value and a priority — the five fields of `ContributionKey` plus rank data. No family concept. |
| `GameCore.Unity.Runtime.CapabilitySupportRow` (`IBufferElementData`) | Yes | The published form of the same record; capability, output slot, provider, rule, value, priority. |
| `GameCore.Unity.Runtime.IDomainVersionAuthority` | Yes | "Read the current version of the domain you own for one target." The kernel never learns what the number means; the card table reports its table version, the narrative dialogue reports its conversation node. |
| `WorldMessagePlane.BindDomainVersion` / `DomainVersionOf` | Yes | Route → authority binding, keyed by the declared route identity. |

`generic-profile-audit.json` was regenerated *after* those additions and reports the same zero forbidden edges.

## 5. What this audit does NOT establish

1. **Runtime checked, not universal.** The loaded-assembly half passed in five Linux IL2CPP runs; it says nothing about other target platforms or an assembly not loaded by these scenarios.
2. **No claim about unexecuted native generic instantiations.** The player ran the registered narrative and card systems, readers, reducer, predicate and additive fixture; an unrelated unreachable generic shape would need its own root and probe.
3. **The token scan is heuristic.** Its hits require classification; the reference graph and the runtime audit establish the assembly boundary for the tested binary.
4. **No third-party assembly audit.** Only project assemblies are classified; `Unity.*` and the BCL are listed as external references and not inspected.
