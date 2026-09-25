# GC-012 HANDOFF — Freeze the provisional generic execution contract in a player (Wave 4)

Branch `gc-012`, worktree `/Users/yangcao/wkspace/gc-wt/gc-012`, based on the Wave 3 integration `b697ff6`.

**Status of every executable check in this document: `NotRun (pending orchestrator build host)`.** This host has no
.NET SDK, no C# compiler, no Mono and no Unity, so nothing in this change set has been compiled, imported or
executed here. What *did* run is recorded verbatim in §7: the host-side C# static checks (272 files, `ok`), the
documentation validator (`--self-test` and full), the contract-surface parity check, three Python scripts, `bash -n`
over the new shell scripts, and a repository-wide `.meta` GUID uniqueness scan. None of those is a build, an import
or a test result.

## 1. Summary

One IL2CPP player now contains both families with **generated inactive plugin entries**, is built with High managed
stripping and **without any `link.xml` `preserve="all"` crutch for gameplay or kernel assemblies**, mounts both
families late in one process, spawns and executes them, and compares each family's outputs against its Editor/world
fixture outputs canonically. The gate reports into `-probeW4Profile` (task id `GC-012`) and into the EditMode
assembly `GameCore.W4Profile.Tests`.

All three kernel gaps the orchestrator handed to Wave 4 were addressed in code, not documented around:

1. **multi-supporter `Additive` slot** (reported by both GC-011 and GC-015) — a binding row now carries a support
   *set*, and the published ECS storage carries one support row per supporter, so P-017's "set of IDs" and P-019's
   composed value both reach a live world end-to-end. GC-015's and GC-011's reports describe the same gap and this is
   the one fix.
2. **`CommandEnvelope.ExpectedDomainVersion` dead data** — now enforced at admission against a route's declared
   domain-version authority, refused as `StalePlan`, and folded into the admission idempotency hash.
3. **P-032 reset field doc gap** (reported by GC-015) — decided, fixed in 05 and in the manifest contract, wired so
   a manifest can actually declare reset support, and recorded in §6.3 for reconciliation with GC-015's executors.

The fourth item handed over — the once-observed exit-139 player segfault — is **not** claimed as fixed; §8 records
what is and is not known about it.

## 2. Files created

### Kernel (Unity-free, also compiled by plain dotnet)

| Path | Contents |
| --- | --- |
| `Packages/com.gamecore.planning/Runtime/Plans/BindingSupport.cs` | `CapabilitySupport`: one immutable record per contributing provider, with canonical `Freeze`/`Union`/`SetEquals`/`Contains`/`Describe`. |

### Unity qualification project

| Path | Contents |
| --- | --- |
| `Assets/GameCore.Validation/Runtime/GeneratedFamilyEntries.cs` (+ `.meta`) | `IFamilyPluginEntry`, `NarrativeFamilyPluginEntry`, `CardFamilyPluginEntry` — the generated inactive family entries and the reachability roots. |
| `Assets/GameCore.Validation/Runtime/W4ProfileScenario.cs` (+ `.meta`) | `W4ProfileStep`, `W4ProfileFacts`, `W4ProfileScenarioResult`, `W4ProfileScenario` — the four gate clauses. |
| `Assets/GameCore.Validation/Runtime/ProbeW4Profile.cs` (+ `.meta`) | the `-probeW4Profile` player mode. |
| `Assets/GameCore.Validation/Tests/W4Profile.meta` | folder meta for the new test folder. |
| `Assets/GameCore.Validation/Tests/W4Profile/GameCore.W4Profile.Tests.asmdef` (+ `.meta`) | EditMode assembly `GameCore.W4Profile.Tests`. |
| `Assets/GameCore.Validation/Tests/W4Profile/W4ProfileIntegrationTests.cs` (+ `.meta`) | 11 cases: every gate clause, the additive slot's composed value and supporter count, both kernel-separation directions, and the additive fixture's own declaration set. |

### Card fixtures package (a new, clearly-labelled test fixture; no existing file in that package was edited)

| Path | Contents |
| --- | --- |
| `Packages/com.gamecore.gameplay.cards/Fixtures/Runtime/CardAdditiveScenario.cs` (+ `.meta`) | `CardAdditiveFacts`, `CardAdditiveResult`, `CardAdditiveScenario`: the end-to-end multi-supporter proof over both catalogs. |

### Tooling

| Path | Contents |
| --- | --- |
| `tools/regen_catalog_group.py` | applies one new leading registration group to a committed generated catalog on a host with no SDK/Unity, emitting the emitter's own byte format. |
| `tools/run_w4_profile_gate.sh` | the Wave 4 gate sequence. |
| `tools/unity/run_w4_profile_probe.sh` | drives `-probeW4Profile` `PROBE_RUNS` times and asserts the gate's steps, facts and both catalog fingerprints. |

### Evidence

| Path | Contents |
| --- | --- |
| `artifacts/gates/w4-generic-profile/audit.md` | the generic contract/assembly audit with its classification of every genre-token hit. |
| `artifacts/gates/w4-generic-profile/generic-profile-audit.json` | the audit tool's machine-readable output for this revision. |
| `artifacts/gates/w4-generic-profile/inventory.md` / `inventory.json` | the complete P-001..P-060 / O-01..O-26 inventory plus GC-012's revision notes. |
| `artifacts/gates/w4-generic-profile/profile.md` | the protocol 1.0 candidate profile record and compatibility report. |
| `artifacts/gc-012/HANDOFF.md` | this file. |

## 3. Files modified

| Path | Change | Why |
| --- | --- | --- |
| `Packages/com.gamecore.planning/Runtime/Plans/TargetBindingTable.cs` | `TargetBindingRow` and `DerivedBindingRule` gained a `Supports` set, `SupporterCount`, `IsMultiSupport`, `SingleSupport`, and two optional constructor parameters. | P-017 needs the support set on the row, and a spawned target must inherit it (P-024). |
| `Packages/com.gamecore.planning/Runtime/Plans/PlanProposals.cs` | `ProposedCapability` gained `Supporters`, `SupporterCount`, `IsMultiSupport` and one optional constructor parameter. | The declaration must carry the composed value *and* its support set into the planner. |
| `Packages/com.gamecore.planning/Runtime/Plans/AssemblyPlanner.cs` | an `Additive` branch that consumes the composed value and support set; a support set on every row; `AddRule` carries it into the rule. | The gap: the planner published the top-ranked candidate's raw value. |
| `Packages/com.gamecore.unity.runtime/Runtime/Integration/DerivationProposalBridge.cs` | one declaration per supporter, `MultiValueSlot` outcome, pair-aligned canonical ordering, rewritten header. | The gap: the seam refused any slot with more than one supporter, so a composed value could never reach a world. |
| `Packages/com.gamecore.unity.runtime/Runtime/Assembly/AssemblyComponents.cs` | `CapabilityBinding.SupporterCount`/`IsMultiSupport`; new `CapabilitySupportRow`; `AssemblyStorage.CollectSupports`/`TryFindSupport`/`CountSupports`; usings. | The published representation the gap was missing. |
| `Packages/com.gamecore.unity.runtime/Runtime/Assembly/AssemblyPublisher.cs` | `WriteSupportRows`/`AppendSupportRows`, support rows on install/spawn/retraction, `ToBinding`/`RowOf` carry supports, `ReadSupportRows` (whole target and one slot). | Publish and observe the support set; retraction removes exactly one provider's support (P-033). |
| `Packages/com.gamecore.unity.runtime/Runtime/Messages/WorldMessagePlane.cs` | `IDomainVersionAuthority`, `BindDomainVersion`, `DomainVersionOf`, the admission guard, `WriteUInt64BigEndian`, and the guard in `HashOf`. | The dead-data gap, and P-050 idempotency. |
| `Packages/com.gamecore.contracts/Runtime/Contracts/HostContracts.cs` | documentation only: `CommandEnvelope.ExpectedDomainVersion` now says what is enforced. **No surface change.** | The old comment described the field as decoration. |
| `Packages/com.gamecore.contracts/Runtime/Manifest/Declarations.cs` | `StateSlotSpec`: the existing 11-parameter constructor now chains to a new 13-parameter overload; two new read-only properties. **Additive only** — see §6.3. | P-032's "manifest-supported" reset had no manifest field. |
| `Packages/com.gamecore.unity.runtime/Runtime/Integration/OwnershipSchedulePipeline.cs` | `OptionsOf` reads the manifest's reset support instead of always reporting "no reset". | Without this the new manifest field would be as dead as the old `ExpectedDomainVersion`. |
| `dotnet/tests/GameCore.Contracts.Tests/ContractTests.cs` | the W0-gate allowlist gains one predicate accepting the three documented `StateSlotSpec` additions. **Minimal, additive to a test.** | The frozen-surface test fails on an undocumented addition; the brief allows additions the docs force, and requires them recorded. |
| `Packages/com.gamecore.gameplay.cards/Runtime/CardTableSystems.cs` | `BindTable` binds the route's domain-version authority; new private `CardTableDomainVersion`. | The card family's declared authority. |
| `Packages/com.gamecore.gameplay.narrative/Fixtures/Runtime/NarrativeWorld.cs` | `Attach` binds the choice route's authority; new private `DialogueDomainVersion`. | The narrative family's declared authority. |
| `docs/game-core/05-contracts-and-data-model.md` | `StateSlotSpec`'s row names the reset policy; the §4 runtime-delta row names the explicit `Reset`; one sentence states the P-032 rule. | The doc gap. **00 wins**, and 00 P-032 requires a manifest field. |
| `unity/GameCore.Validation/Catalogs/ProbeCatalog.catalog.json`, `.../CardCatalog.catalog.json` | one new `FamilyEntryRegistrations` group each, plus the `GameCore.Validation.Slices` using directive. | The generated inactive entries. |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs`, `.../GeneratedCards/CardCatalog.g.cs` | regenerated for the new group. | Committed generated catalogs must match the descriptions. |
| `unity/GameCore.Validation/Assets/link.xml` | the three narrative `preserve="all"` entries removed; the remaining entries documented as native-invoked or deliberately-unreferenced fixtures. | The task's stripping requirement. |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeArguments.cs`, `ProbeRunner.cs` | `-probeW4Profile`, `W4Profile` property, mode `W4Profile` / task `GC-012`, dispatch branch. | The new probe mode. |
| `tools/verify_generated_catalog.py` | group-agnostic table discovery, line-based initializer scan, `RegistrationGroupCount` check. | The previous version silently skipped an empty array (and so under-reported the card catalog's four groups) and hard-coded the probe catalog's two table names. |
| `dotnet/README.md` | the Wave 4 gate command, the audit command and the no-SDK catalog helper. | The README lists every gate. |

No other file was touched. `Packages/com.gamecore.gameplay.cards/Fixtures/Runtime/CardMarketScenario.cs` was
**not** modified: its thirteen canonical observations are GC-011's, and the additive proof lives in a new file so
that `GameCore.Cards.Tests` and `run_cards_probe.sh` keep passing unchanged.

## 4. Exact commands for the Linux build host

Everything runs from the repository root. Nothing here has been run.

### 4.1 One command (the whole gate)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet tools/run_w4_profile_gate.sh
```

It runs, in order:

```sh
python3 tools/check_game_core_csharp.py
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gates/w4-generic-profile/trx
python3 tools/w4_generic_profile_audit.py --out artifacts/gates/w4-generic-profile/generic-profile-audit.json
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile .../unity/resolve.log
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testResults .../unity/editmode-results.xml -logFile .../unity/editmode.log
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform PlayMode \
  -testResults .../unity/playmode-results.xml -logFile .../unity/playmode.log
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation \
  -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog -logFile .../unity/card-codegen.log
UNITY="$UNITY" ARTIFACTS=artifacts/gates/w4-generic-profile/toolchain tools/unity/build_probe.sh
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs
PROBE_RUNS=5 ARTIFACTS=... tools/unity/run_probe.sh both
PROBE_RUNS=5 ARTIFACTS=... tools/unity/run_world_probe.sh
PROBE_RUNS=5 ARTIFACTS=... tools/unity/run_w1_gate_probe.sh
PROBE_RUNS=5 ARTIFACTS=... tools/unity/run_w2_gate_probe.sh
PROBE_RUNS=5 ARTIFACTS=... tools/unity/run_narrative_probe.sh
PROBE_RUNS=5 ARTIFACTS=... tools/unity/run_cards_probe.sh
PROBE_RUNS=5 ARTIFACTS=... tools/unity/run_w3_gate_probe.sh
PROBE_RUNS=5 ARTIFACTS=... tools/unity/run_w4_profile_probe.sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

`UNITY` is required (exit 2 without it): the gate is never claimed from the dotnet half alone. Do not add `-quit`
to a test-run command (04 §10).

### 4.2 The W4 profile probe alone

```sh
PROBE_RUNS=5 tools/unity/run_w4_profile_probe.sh
```

### 4.3 The W4 profile EditMode assembly alone

```sh
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.W4Profile.Tests \
  -testResults artifacts/gates/w4-generic-profile/unity/w4profile-editmode.xml \
  -logFile artifacts/gates/w4-generic-profile/unity/w4profile-editmode.log
```

### 4.4 What `run_w4_profile_probe.sh` asserts

`"task": "GC-012"`, `"mode": "W4Profile"`, `"result": "Pass"`, no `"status": "Fail"`, and these step names:
`w4-generated-inactive-family-entries`, `w4-narrative-runs-agree-with-the-declared-canonical-trace`,
`w4-card-runs-agree-canonically-generated-vs-fixture`, `w4-additive-multi-supporter-slot-in-a-live-world`,
`w4-generic-profile-no-genre-type-in-the-kernel`, `w4-both-families-mount-late-and-leave-no-world`,
`w4-profile-facts`. It also requires these fragments of the facts digest: `bothEntriesResolved=True`,
`narrativeEntrySystems=6`, `cardEntrySystems=4`, `registryAtStart=0`, `registryAfterAll=0`,
`narrativeGeneratedMatchesDeclaredTrace=True`, `narrativeFixtureMatchesDeclaredTrace=True`,
`narrativeRunsAgree=True`, `cardRunsAgree=True`, `cardGeneratedFailures=0`, `cardFixtureFailures=0`,
`additiveGeneratedPassed=True`, `additiveFixturePassed=True`, `additiveComposedValue=5`, `additiveSupporterCount=2`,
`kernelForbiddenReferences=0`, `duplicateKernelAssemblies=0`, `kernelInspectionFailures=0`; and it independently
reads both committed catalogs' fingerprint literals out of the generated sources and requires the facts digest to
name them. It additionally requires the additive observation's own detail string to carry `seatARows=1`,
`seatAComposedValue=5`, `seatASupporters=2`, `seatASupportRows=2`, `seatASupportValueSum=5`, `festivalSupport=True`,
`nestedSupport=True`, `seatBValue=2`, `seatCValue=-1`, `seatAValueAfterNestedUnmount=2`,
`seatASupportersAfterNestedUnmount=1`, `seatASupportRowsAfterNestedUnmount=1`, `festivalSupportSurvives=True`.

### 4.5 If a committed catalog differs after regeneration

`tools/regen_catalog_group.py` is the no-SDK stand-in GC-012 used. It was validated by **reproducing all 18
existing registration blocks of both committed catalogs byte-for-byte** from their original descriptions before it
was used, and its output is independently checked by `tools/verify_generated_catalog.py` (which recomputes the file
hash and the catalog fingerprint from the file's own text). If the Editor bridge's regeneration differs anyway, the
bridge is authoritative: the diff is the finding.

## 5. Requirement / test coverage mapping

| Requirement / test | Where implemented | Where observed (gate) |
| --- | --- | --- |
| P-001 genre independence | `tools/w4_generic_profile_audit.py` (source graph), `KernelAssemblyAudit` (runtime graph) | `w4-generic-profile-no-genre-type-in-the-kernel`; `audit.md` §2/§3 |
| P-004 stable identities | `CapabilitySupport` re-expresses `ContributionKey`; the family entries declare no identity | `w4-generated-inactive-family-entries` |
| P-005 generations | `CapabilitySupport.ProviderGeneration` per supporter | additive observation: two distinct providers, each generation read from its own install record |
| P-006 version domains | `AssemblyPlanner` keeps a re-derived identical row a `NoChange` | both families' `CountersJoined*` observations in the per-slice suites, re-run by the gate |
| P-009 manifest + precompiled factory | both catalog descriptions' `FamilyEntryRegistrations` group; `CatalogManifestSource` | `w4-generated-inactive-family-entries`; the per-slice probes' absent-key observation |
| P-017 contribution identity and ownership | `CapabilitySupport`, `TargetBindingRow.Supports`, `DerivedBindingRule.Supports`, `ProposedCapability.Supporters`, `CapabilitySupportRow` | `w4-additive-multi-supporter-slot-in-a-live-world` (`seatASupporters=2`, `seatASupportRows=2`) |
| P-018 precedence | support-set canonical order (descending priority, then ascending provider/rule bytes) | additive observation's `seatASupportValueSum=5` over two distinct providers |
| P-019 composition policies | `AssemblyPlanner` `Additive` branch; `DerivedCompositionProposal` one declaration per supporter | additive observation's `seatAComposedValue=5` versus the per-slice suites' `seatAValue=2` |
| P-024 spawn completeness | `DerivedBindingRule.Supports` + `AssemblyPublisher.AppendSupportRows` | the per-slice `cards-*-future-seat-*` and `narrative-forward-provider-and-spawned-target` steps |
| P-032 state dispositions (manifest reset support) | `StateSlotSpec` reset field + `OptionsOf`; the proposal field and reason already existed as `SlotChangeKind.Reset` + `SlotPolicyRequest.Reason` | `GameCore.Planning.Tests` slot-policy cases re-run by the gate; §6.3 for the reconciliation |
| P-033 exact retraction | `AssemblyPublisher.WriteSupportRows`/`RemoveBindingRow` | additive observation's `seatAValueAfterNestedUnmount=2`, `seatASupportersAfterNestedUnmount=1` |
| P-042 expected domain version | `IDomainVersionAuthority`, `WorldMessagePlane.BindDomainVersion` + admission guard, `CardTableDomainVersion`, `DialogueDomainVersion` | the families' own command observations; a refusal carries `StalePlan` |
| P-050 idempotency | `HashOf` includes the guard | the per-slice duplicate-command steps |
| P-055 protocol evolution | both catalogs still declare protocol `1.0`; every addition is optional | `profile.md` §3 compatibility table |
| P-058 IL2CPP + stripping | `link.xml` without the narrative crutch; generated roots in both catalogs | the IL2CPP build plus `w4-generated-inactive-family-entries` in the player |
| P-059 genre validation before freeze | both families run; the action family does not | `profile.md` §2, and the inventory's P-059 row |
| P-060 evidence | `profile.md` §1 pins revision, catalogs, manifest, lock, Editor, backend, stripping and `PROBE_RUNS` | `tools/run_w4_profile_gate.sh` writes all of it under `artifacts/gates/w4-generic-profile/` |
| TEST-001 toolchain/generated registration/IL2CPP | `GeneratedFamilyEntries`, both catalogs, `BuildProbe` | `w4-generated-inactive-family-entries` in the High-stripping player; GC-001's own probes re-run |
| TEST-011 temporal models / idle worlds | unchanged kernel drivers | narrative and cards idle observations |
| TEST-013 authority / requests / buffers | `AssemblyPublisher` support rows; the domain-version guard | the card transfer and narrative choice steps; the additive fixture's retraction step |
| TEST-020 baking / runtime recipes / precompiled plugins | `FamilyEntryRegistrations`; `CardTableRecipes` | `w4-generated-inactive-family-entries` |
| TEST-021 genre neutrality / cross-template | `tools/w4_generic_profile_audit.py`; `KernelAssemblyAudit` | `w4-generic-profile-no-genre-type-in-the-kernel`; `audit.md` |
| TEST-024 documentation / traceability | `docs/game-core/` | `python3 tools/validate_game_core_docs.py` in the gate |

## 6. Contract changes

### 6.1 Summary of the surface delta

| Assembly | Delta | Frozen? |
| --- | --- | --- |
| `GameCore.Contracts` | **three additions to `StateSlotSpec`, no removals, no signature changes** (§6.3) | Yes — W0 gate (GC-002). Handled as additions only, and the three lines are added to GC-003's allowlist in `dotnet/tests/GameCore.Contracts.Tests/ContractTests.cs`. |
| `GameCore.Planning` | additive optional parameters, new properties/fields, extended `HasSameContent`/`ToString` (§6.2) | No snapshot exists for this assembly. |
| `GameCore.Unity.Runtime` | additive members, one new enum value, two extended behaviours (§6.2) | No snapshot exists for this assembly. |

No change removes, renames or alters the meaning of an existing public member, and every caller in the repository was
migrated in the same commits. No `[Obsolete]`, alias, shim or deprecated path was introduced.

### 6.2 `GameCore.Planning` and `GameCore.Unity.Runtime` additions

| Member | Kind | Note |
| --- | --- | --- |
| `TargetBindingRow.Supports` | new readonly field | `IReadOnlyList<CapabilitySupport>`; never null (empty only for a default-constructed row). |
| `TargetBindingRow.SupporterCount`, `.IsMultiSupport` | new properties | derived. |
| `TargetBindingRow.SingleSupport(...)` | new static method | the one-supporter helper. |
| `TargetBindingRow(...)` | two **optional** parameters appended (`supports = null`, `rule = default`) | source-compatible: `new TargetBindingRow(9 args)` still compiles and builds a one-element support set. |
| `TargetBindingRow.HasSameContent` | semantics extended | now also compares the support set — deliberate: a slot whose composed value is unchanged but whose supporters changed IS a change (P-017, P-033). |
| `TargetBindingRow.ToString` | text extended with `[support=N]` | diagnostic only; no test asserts the literal. |
| `DerivedBindingRule.Supports`, `.SupporterCount`, `.IsMultiSupport` | new field/properties | the rule a future spawn derives from. |
| `DerivedBindingRule(...)` | two optional parameters appended | same compatibility story. |
| `ProposedCapability.Supporters`, `.SupporterCount`, `.IsMultiSupport` | new property/properties | the declaration's support set. |
| `ProposedCapability(...)` | one optional parameter appended (`supporters = null`) | same. |
| `ProposedCapability.ToString` | text extended with `[support=N]` | diagnostic only. |
| `CapabilityBinding.SupporterCount`, `.IsMultiSupport` | new field/property | `IBufferElementData` gains one `int`; blittable, still Burst/IL2CPP-safe. A pre-existing saved row reads as `0`, which `IsMultiSupport` reports as false. |
| `CapabilitySupportRow` | new `IBufferElementData` struct | the published support row. |
| `AssemblyStorage.CollectSupports`, `.TryFindSupport`, `.CountSupports` | new static methods | support-buffer queries. |
| `AssemblyPublisher.ReadSupportRows(TargetId)` and `(TargetId, CapabilityId, uint)` | new methods | observation. |
| `IDomainVersionAuthority` | new interface | declared in `GameCore.Unity.Runtime.Messages`. |
| `WorldMessagePlane.BindDomainVersion`, `.DomainVersionOf` | new methods | route → authority. |
| `WorldMessagePlane.SubmitCommand` | semantics extended | a guarded envelope can now be refused with `MissingDependency` (no authority on the route), `StaleHandle` (the domain does not hold the target) or `StalePlan` (version mismatch) — each a rejected `RequestResult` with **no lane entry and no live write**. An envelope with no guard behaves exactly as before. |
| `WorldMessagePlane` admission hash | semantics extended | the guard participates, so a reused request key with a different expected version is an `IdempotencyConflict`; it previously hashed identically. |
| `DerivationProposalOutcome.MultiValueSlot = 5` | new enum member | appended; `Built = 0`…`InvalidContribution = 4` keep their numbers. |

### 6.3 `GameCore.Contracts.StateSlotSpec` — the P-032 reset field (for GC-015 to reconcile)

**The gap, decided.** 00 P-032 says: "`Reset` requires an explicit **manifest-supported** proposal field and
reason." Three things were missing for that sentence to be true:

1. 05 §3's `StateSlotSpec` row did not list a reset policy, and the `GameCore.Contracts.StateSlotSpec` manifest DTO
   had no reset member — so a manifest could not declare reset support at all.
2. `OwnershipSchedulePipeline.OptionsOf` derived `SlotAuthorityOptions` **only** from `spec.LastSupport`, so it
   could never produce `SlotAuthorityOptions.ResetPermitted == true`. The permission could only come from hand-written
   option values, which is what GC-015 correctly reported as "GC-007's generated-option reading".
3. 05 §4's runtime-delta row did not name the reset request.

**What was already complete and is deliberately reused (no second convention).** The *proposal* half of P-032 was
already implemented by GC-007 and is unchanged: `SlotChangeKind.Reset` is the explicit proposal field and
`SlotPolicyRequest.Reason` is its required reason, validated by `SlotPolicyValidator` (an unpermitted reset and a
missing proposal reason are both rejected with `OwnershipConflict`). `SlotAuthorityOptions` keeps
`ResetPermitted` + `ResetReason` exactly as it is, so **GC-015's executors need no change**.

**The fix — three additions, all attributable to P-032:**

| File | Addition |
| --- | --- |
| `Packages/com.gamecore.contracts/Runtime/Manifest/Declarations.cs` | `StateSlotSpec` gains a second constructor overload taking `bool resetSupported, string? resetReason` **in addition to** the unchanged 11-parameter constructor, plus `ResetSupported` and `ResetReason` read-only properties. The original constructor now chains to the new one with `false, null`, so every existing declaration keeps its exact previous behaviour. |
| `Packages/com.gamecore.unity.runtime/Runtime/Integration/OwnershipSchedulePipeline.cs` | `OptionsOf` passes the declaration's reset support through, so a manifest that declares it gets `SlotAuthorityOptions.Resettable(reason, …)` and a manifest that does not gets today's `Dormant`/`DerivedData`/`Durable`. |
| `docs/game-core/05-contracts-and-data-model.md` | §3's `StateSlotSpec` row now names "reset support and its recorded reason"; §4's runtime-delta row names the explicit `Reset` request; one sentence states the two-reason rule. |

**Why an overload and not appended optional parameters.** `GameCore.Contracts` is a frozen W0 surface, and
`ApiSurfaceComparer` compares *member sets by exact line*: appending parameters to the existing constructor would
have **removed** the frozen `ctor public StateSlotSpec(…11 params…)` line and failed
`ProductionSurfaceIsAStrictSupersetOfTheFrozenSeamSnapshot` as a *removal*. A second overload adds lines without
touching the frozen one. The three added lines are accepted by a new predicate,
`ContractTests.IsStateSlotResetAddition`, which is the only edit to GC-003's test; `python3
tools/check_contract_surface_parity.py` still reports only the three pre-existing GC-003 enum additions.

**Semantics GC-015 can rely on:**

* a slot with `ResetSupported == false` behaves exactly as before, and a `Reset` request against it is rejected
  (`OwnershipConflict`) — never applied as zero initialization;
* `ResetSupported == true` with an **empty** recorded reason is a **declaration error** rejected by
  `SlotPolicyValidator` / `OwnerAuthorityValidator`, not a permissive default. A supported reset additionally needs
  the proposal's own non-empty `Reason`;
* the two reasons are independent: the declaration records why the slot may ever be reset, the proposal records why
  this publication does;
* the new overload is the only way to produce a resettable slot from content; nothing infers it.

`Packages/com.gamecore.gameplay.narrative/Runtime/NarrativeDeclarations.cs`,
`Packages/com.gamecore.gameplay.cards/Runtime/CardTableDeclarations.cs`, the W2 fixture and the existing tests all
keep using the unchanged 11-parameter constructor, so **no gameplay declaration and no generated catalog changes**;
the content compiler has no state-slot shape to regenerate (it registers `StatePolicy` as a factory *kind* only, and
no family declares one), so no catalog regeneration was needed for this item.

### 6.4 Behaviour a reviewer should check deliberately

1. **`DerivedCompositionProposal` no longer refuses a multi-supporter slot.** It refuses a multi-*value* slot
   (`DerivationProposalOutcome.MultiValueSlot`). A regression test for the old refusal's *outcome* would now fail on
   purpose; the reason is P-017/P-019 and is argued in the file header.
2. **`AssemblyPlanner` rejects an `Additive` declaration with no support set** (`MissingDependency`) rather than
   publishing the top-ranked value. No existing test declares an `Additive` slot, so nothing in the current suites
   changes behaviour — which is exactly why the new end-to-end fixture exists.
3. **`HasSameContent` now includes the support set.** A publication that only changes who supports a slot publishes
   a new epoch. That is the intended P-017 reading.
4. **`Replace`/`Exclusive`/`Incompatible` rows now carry a one-element support set.** `SupporterCount = 1` for every
   existing single-provider row, so the per-slice assertions on values and row counts are unchanged.
5. **`OptionsOf` now reads a manifest field.** A declaration that sets `resetSupported: true` without a reason will
   start failing validation — correctly, but it is a new failure mode for any content that sets the flag carelessly.
   No content in this repository sets it.

## 7. What actually ran on this host

| Command | Result |
| --- | --- |
| `python3 tools/check_game_core_csharp.py` | `checked 272 C# file(s)` / `ok` — brace balance, forbidden C# 10+ constructs, engine-type scan over the engine-free packages. |
| `python3 tools/validate_game_core_docs.py --self-test` | `Documentation validator self-test passed: 9 isolated positive/negative fixtures.` |
| `python3 tools/validate_game_core_docs.py` | `Game Core documentation validation passed: 14 Markdown documents; local links, anchors, IDs, traceability, task DAG and wave ordering checked.` |
| `python3 tools/check_contract_surface_parity.py` | `221 types … 3 addition(s) beyond the frozen snapshot` (the three pre-existing GC-003 enum values) and `every snapshotted enum and enum value exists in the production sources with the same numeric value`. |
| comparer-logic simulation for `StateSlotSpec` (ad hoc, not committed) | the frozen 11-parameter constructor line is preserved verbatim; the three new lines (13-parameter overload, `ResetSupported`, `ResetReason`) are each accepted by `IsStateSlotResetAddition`. |
| `python3 tools/w4_generic_profile_audit.py` | runs, prints valid JSON, byte-identical across runs; `kernelToGameplayEdgeCount=0`, `kernelToValidationEdgeCount=0`, `kernelNoEngineReferenceViolationCount=0`, `assemblyCount=37`, `kernelAssemblyCount=14`, `typeCount=697`. |
| `python3 tools/w4_generic_profile_audit.py | python3 -m json.tool` | exit 0. |
| `python3 tools/verify_generated_catalog.py <both committed catalogs>` | both recompute their `CatalogFileHash` and `CatalogFingerprint` correctly from their own text. |
| `python3 tools/verify_generated_catalog.py` against the **original** committed card catalog | exits 1 with a fingerprint mismatch — the tool's own bug (it hard-coded the probe catalog's table names and mis-scanned an empty array), fixed here; recorded because it means the card catalog had never actually been verified by that script before. |
| emitter-fidelity check (ad hoc, not committed) | all 18 existing registration blocks of both committed catalogs reproduce byte-for-byte from their original descriptions using `tools/regen_catalog_group.py`'s emitters. |
| `bash -n tools/run_w4_profile_gate.sh`, `bash -n tools/unity/run_w4_profile_probe.sh` | syntax ok. |
| repository-wide `.meta` GUID scan | 483 GUIDs before the new files, zero duplicates; the 9 new `.meta` files were generated unique against that set. |

**Nothing was compiled, imported or executed against Unity, IL2CPP, Burst or NUnit.** No player was built, no test
was run, no probe reported a result.

## 8. Known gaps, assumptions and doc ambiguities

1. **The exit-139 player segfault is recorded, not closed.** GC-007 saw one SIGSEGV (exit 139) during native engine
   teardown; two plausible fixes were applied and it did not reproduce in 25 subsequent runs, so the root cause is
   unproven. GC-012 changes nothing about it and keeps `PROBE_RUNS=5`, so a dirty run on any repetition fails the
   gate rather than being repaired by a later clean one. The W4 probe adds a *third* world per process (both families
   plus the additive fixture), which is the kind of teardown churn that could expose a latent native-lifetime
   defect — if the gate fails on exit 139, that is a genuine finding and not a flake to retry away.
2. **`tools/regen_catalog_group.py` is a stand-in, not the compiler.** The production emitter is authoritative and
   the gate refuses a build when a regenerated catalog differs. The stand-in only handles a group that sorts before
   every existing group (the GC-012 case) and errors otherwise rather than guessing.
3. **The card slice has no declared canonical trace.** `artifacts/gc-011/cards-trace.json` is a hand-authored
   expectations document with no reader, writer, digest or comparison code anywhere in the repository (unlike
   `NarrativeScenarioTrace`). The gate therefore compares the card family's two runs against each other by facts
   digest and does not claim a comparison against a committed declaration. Making cards symmetric with narrative is
   an open, unowned improvement; it is recorded rather than papered over.
4. **The additive proof runs in Automatic mode only.** Conservative-mode coverage of a multi-supporter slot is
   GC-013's (`SetMode` and the mode gate). The fixture records the mode it ran in.
5. **Multi-*value* slots have no published representation.** One binding row publishes one canonical `int32`, so a
   reducer-less `Additive` or an `Ordered` slot whose composed set has more than one member is refused with
   `DerivationProposalOutcome.MultiValueSlot` and `DiagnosticCode.UnsupportedVersion`. A deliberate refusal recorded
   as an open later capability, never as optional behaviour.
6. **`packages-lock.json` is expected to change on the build host.** Step 4 of the gate regenerates it; 04 §1
   forbids synthesizing it here. The committed lock's SHA-256 is in `profile.md` as the *pre-resolve* value.
7. **The P-032 decision is a reading of 00, and it is stated so it can be overturned deliberately.** 00 does not say
   whether the manifest records only "reset supported" or also the reason. This change makes the manifest record
   **both**, matching GC-007's existing two-field shape and keeping one convention. The alternative reading (manifest
   flag only, proposal carries the sole reason) would have deleted `SlotAuthorityOptions.ResetReason` and GC-007's
   two validators that require it — a larger, breaking change for no normative gain.
8. **Two `[INFERENCE]`-level assumptions**, both from reading rather than running: (a) that a `CapabilitySupportRow`
   buffer added to a target entity is preserved across `AssemblyPublisher`'s existing structural operations (it is
   added by the same `InstallBindingRow` path that already adds `CapabilityBinding`, and the spawn path adds it
   beside the binding buffer); (b) that the IL2CPP linker keeps `NarrativeFamilyPluginEntry`/`CardFamilyPluginEntry`
   reachable, which is the same `BoundRegistration<T>` static-field shape the GC-001 fixture plugin already relies
   on. Both are what the first player run checks.
9. **The W3 gate is unaffected but should still be re-run.** `CardMarketScenario`'s step list, the narrative
   scenario, and both catalogs' *existing* registrations are unchanged apart from the appended group, so the W3
   suite's expectations hold. The one W3-visible delta is the catalogs' fingerprints, which the W3 probe driver
   reads out of the generated sources rather than hard-coding.
10. **No Unity scene or visual inspection was performed, and none is claimed.** The EditMode assemblies and the
    headless IL2CPP player are the exercised surfaces, as in the W2 and W3 gates.
11. **`artifacts/gates/w4-generic-profile/{trx,unity,toolchain}` are produced by the gate; nothing is pre-populated
    here.** Only `audit.md`, `generic-profile-audit.json`, `inventory.md`, `inventory.json` and `profile.md` are
    committed by this task.
12. **Likely first failures on the build host, in order:** (a) a member-name slip in the new fixture/scenario files
    (Unity does not treat warnings as errors, but a wrong member name is a hard error; the highest-risk spots are the
    constructor calls into `AssemblyPublisher`/`DerivedAssemblyPipeline`/`OwnershipSchedulePipeline` in
    `CardAdditiveScenario`); (b) the IL2CPP linker removing a narrative type the entry's constructor does not
    actually touch — the fix is to widen the entry's real references, never `link.xml`; (c) an expectation mismatch
    in the additive fixture's literals (composed 5, two supporters, and `seatCValue=-1` because League B's quiet
    provider is not mounted by that fixture); (d) the `.meta` GUIDs, authored here and verified unique
    repository-wide, but only an import can confirm Unity keeps them — commit any rewrite; (e) the new
    `StateSlotSpec` overload being reported as an addition the allowlist predicate does not match, which would mean
    the generator renders the parameter list differently than assumed in §6.3.
13. **Doc ambiguity resolved by the simplest reading of 00 (P-042).** P-042 says the envelope carries an "expected
    domain version if needed" but does not say who compares it. Two readings were possible: the host at admission, or
    the state owner at its validate stage. Admission was chosen because 00 §10's `O-13` gives the host
    "validate world/sequence/schema/route/capacity" as a *pre-enqueue* duty and because the card slice already had a
    payload-carried `ExpectedTableVersion` doing the owner-side job — implementing the envelope field at the owner
    would have made it a second spelling of the same check. The authority interface is the mechanism that keeps the
    comparison domain-agnostic, which is what P-001 requires.
14. **`GameCore.Planning`'s `SingleSupport` allocates one small array per binding row per publication.** That is
    control-plane work, not per-frame work, so it is left simple rather than pooled; a publication already allocates
    far more. Recorded so a later performance task does not have to rediscover the decision.
