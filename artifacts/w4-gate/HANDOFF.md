# W4-GATE handoff — Wave 4 integration gate

Branch `w4-gate` (worktree `/Users/yangcao/wkspace/gc-wt/w4-gate`), starting from `d3dc961` (`main` + GC-012).

**Status of every executable check this change set adds: `NotRun (pending orchestrator build host)`.** This host has
no Unity, no .NET SDK, no C# compiler and no Mono, so nothing here has been compiled, imported or executed. What did
run on this host is recorded in §7: `python3 tools/check_game_core_csharp.py` (359 files, `ok`), the documentation
validator (`--self-test` and full, both pass), `bash -n` over the two new shell scripts and `tools/run_w3_gate.sh`, a
repository-wide `.meta` GUID uniqueness scan (568 GUIDs, zero duplicates), a scripted cross-check that both family
adapters implement every `IW4GateFamily` member exactly once, and an independent recomputation of both digest
literals from the observation-name table. None of those is a build, an import or a test.

## 1. Merge and reconciliation (the four Wave 4 tasks on one revision)

Three `--no-ff` merges, in the order the brief names: `origin/gc-013`, `origin/gc-014`, `origin/gc-015`.

### 1.1 Conflicts, and how each was decided

| File | Conflict | Decision |
| --- | --- | --- |
| `Runtime/GameCore.Validation.ProbeHost.asmdef` | GC-012's `Probe`, `FamilyEntries` reference block vs GC-013's (`Probe` already present) | Kept the union: GC-012's `GameCore.Validation.FamilyEntries` plus GC-013's `GameCore.Derivation`, `GameCore.Derivation.Fixtures`, `GameCore.Planning`. The duplicated `GameCore.Validation.Probe` line was collapsed to one; a repeated reference in an asmdef is a Unity import error. |
| `Runtime/ProbeArguments.cs` | GC-012's `-probeW4Profile`/`W4Profile` vs GC-013's `-probeGc013`/`Gc013` | Every probe mode kept. Both flags, both properties, both `IsProbeInvocation` terms, both parse branches, both constructor arguments. |
| `Runtime/ProbeRunner.cs` | the report-identity ternary chain | One chain carrying every mode: `WorldDispatch → W1Gate → W2Gate → Narrative → Gc013 → Cards → W3Gate → W4Profile → default`, each under its own task id. The dispatch `if` chain in `Run` had already auto-merged; the identity chain was rewritten to match it exactly, and the header comment now names the GC-013 mode too. |
| `Composition/Operations/CompositionHost.cs` | GC-013's `Validator` property vs GC-014's `Lifecycle` property | Both kept: two independent additive properties on the same line range. |
| `Integration/DerivationProposalBridge.cs` | GC-013's `retractsAbsentSupport: true` proposal argument vs GC-014's explicit `unmounts` list | **Both.** A re-derivation that no longer supports a slot must (a) retract every row the previous assembly carried for an absent provider — GC-014's `unmounts`, derived from the published binding table — and (b) mark the proposal as the complete effective support so a surviving row the proposal no longer declares is retracted rather than carried — GC-013's `retractsAbsentSupport: true`. They are two halves of one sentence in P-013/P-017, not alternatives. The two comments above the block were merged into one that states both halves. |
| `Assembly/AssemblyPublisher.cs` (Migrate/Reset write) | GC-012's fire-once fault guard `if (before == 0 && writes > 0)` vs GC-015's `staged` value + `ToVersion` | Kept GC-015's value and destination schema version **with** GC-012's guard form. GC-012's commit `0a56148` had changed this call to fire on the first *effective* write (a state-only publication legitimately starts at `writes == 0`); GC-015's `writes == 1` would have restored the pre-GC-012 behaviour and broken the state-only fault boundary. |
| `Assembly/AssemblyPublisher.cs` (helpers) | GC-012's `AppendSupportRows`/`ToBinding`/`RowOf` vs GC-015's `MarkSlotDormant` | Both kept. Purely additive; the conflict was adjacency, not overlap. |
| `Assembly/AssemblyPublisher.cs` (`HasEffectiveChange`) | HEAD's `Migrate || Retract` vs GC-015's `kind != Retain` | GC-015's rule. GC-012's version predates `RetainDormant`/`Transfer`, so it would have reported a dormant retention or a transfer as "no effective change" and published nothing. |
| `dotnet/tests/GameCore.Contracts.Tests/ContractTests.cs` | GC-012's `StateSlotSpec` allowlist predicate vs GC-015's `StateDisposition`/`StateDispositionKind` predicates | Both kept: the W0-gate allowlist now accepts the documented GC-012 *and* GC-015 additions, each with its own comment. |

### 1.2 Reconciliation the brief required, and where it landed

1. **GC-012's manifest reset field (P-032) wired to GC-015's executors; the test-only authorization path removed.**
   `SlotStatePolicy.FromSpec` previously always called `SlotAuthorityOptionsFactory.ForLastSupport(spec.LastSupport)`,
   so a manifest could declare reset support (GC-012's field) and still produce a policy whose `ResetPermitted` was
   false. It now reads `spec.ResetSupported`/`spec.ResetReason` through the new
   `SlotAuthorityOptionsFactory.WithReset`, which therefore has exactly one production caller. GC-015's own hand-built
   authorization was then removed where the real field covers it:
   * `StatePoliciesFixture` builds its resettable slot through a 13-argument `StateSlotSpec` and
     `SlotStatePolicySet.TryBuild` instead of a hand-made `SlotAuthorityOptions` object;
   * `NarrativeStatePolicyScenario` and `CardStatePolicyScenario` moved their reset permission into the manifest the
     scenario mounts (a rebuilt manifest revision with the slot re-declared through the 13-argument overload) and now
     pass the **catalog's** policy set; their private `Resettable(...)` helper is gone. Their owner-transfer
     declaration override (`GateOwnedFact()` and the card equivalent) is deliberately kept: that is a revision-level
     ownership fact (P-034), not a reset permission.
   * Three new pure tests make the wiring falsifiable in both directions (declared reset permitted and staged;
     undeclared reset refused `OwnershipConflict`; `resetSupported: true` with an empty reason a declaration error).
2. **GC-015 wired to GC-012's support sets.** GC-015's executors never needed a change for this — the support set
   lives on `TargetBindingRow.Supports`/`DerivedBindingRule.Supports` and is applied by the publisher's support rows —
   but GC-015's `DerivationProposalBridge` note ("refuses >1 support") is obsolete and GC-012's bridge is now the only
   one: it emits one declaration per supporter and refuses a *multi-value* slot (`MultiValueSlot`), which is a
   different and still-correct refusal. Recorded because the two handoffs describe the same gap from opposite sides.
3. **GC-013's incremental engine + GC-014's absent-provider unmount + GC-015's planner/publisher additions coexist.**
   Auto-merged in `DerivedAssemblyPipeline` (GC-013's `IncrementalDerivationEngine` call, GC-014's extra
   `publisher.Published.Bindings` argument to `DerivedCompositionProposal.Build`) and in `AssemblyPlanner`
   (GC-015's optional `StatePolicyPlan` parameter and its removal filter, plus GC-012's `Additive` support-set
   branch). One real breakage from the auto-merge was found and fixed: GC-015's two Unity state-policy scenarios still
   called `DerivedCompositionProposal.Build` with the pre-merge seven arguments, so both would have failed to compile.
   Both now pass `publisher!.Published.Bindings`, exactly as the pipeline does.
4. **Frame counts.** `W3GateScenario`/`ProbeRunner`'s nested-ternary identity chain and `AssemblyPlanner`'s policy
   branch each carry three tasks' edits; both were read in full after the merge rather than taken on the auto-merge's
   word. `python3 tools/check_game_core_csharp.py` and the doc validator were rerun after every merge and after every
   edit.

## 2. Files created

### Gate runner and contract (Unity qualification surface)

| Path | Contents |
| --- | --- |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/W4GateFamily.cs` (+ `.meta`) | `W4GateSlotPolicy`, `W4GateSlotCase` (with `Name`, `Destination`, `ToRequest()`), and `IW4GateFamily : IGc013Family` — the lifecycle half and the state-policy half. |
| `.../Runtime/W4GateScenario.cs` (+ `.meta`) | `W4GateStep`, `W4GateScenarioResult`, `W4GateScenario` (the observation table, `QualifiedNames`, `Run`) and its `Executor`: the whole scripted sequence, plus the lane's manifest source and the managed-resource factory with its disposal log. |
| `.../Runtime/ProbeW4Gate.cs` (+ `.meta`) | the `-probeW4Gate` player mode (task `W4-GATE`), with both digest literals. |
| `.../Tests/W4Gate.meta` | folder meta. |
| `.../Tests/W4Gate/GameCore.W4Gate.Tests.asmdef` (+ `.meta`) | EditMode assembly `GameCore.W4Gate.Tests`. |
| `.../Tests/W4Gate/W4GateIntegrationTests.cs` (+ `.meta`) | three cases: one per family over both catalogs, plus the observation-table/digest check. |

### Family adapters (authored by the two parallel workers, reviewed and integrated here)

| Path | Contents |
| --- | --- |
| `.../Runtime/W4GateNarrativeHost.cs` (+ `.meta`) | the narrative `IW4GateFamily` partial part: the chapter-one lifecycle subject, its required-service pair, the unload installation, the five policy slots (including the reset slot through the 13-argument `StateSlotSpec`), the five cases and neutral edits, and the initialization registry. |
| `.../Runtime/W4GateCardsHost.cs` (+ `.meta`) | the card equivalent, with `CardTableFixture.FestivalScoringInstance` as the lifecycle subject and a second declared policy owner for `TransferTo` (the cards revision declares exactly one logical owner). |

### Tooling
| Path | Contents |
| --- | --- |
| `tools/run_w4_gate.sh` | the Wave 4 gate sequence; every Unity invocation wrapped in `timeout`, a timeout retried exactly once, a second timeout fatal. |
| `tools/unity/run_w4_gate_probe.sh` | the player-probe harness: sources `probe_runs.sh`, `PROBE_RUNS` runs, strict JSON validation, every step name (17 observations × 2 catalogs × 2 families, plus both digest steps), both digest literals, and ten clause fragments read out of the step details. |

### Evidence

`artifacts/w4-gate/HANDOFF.md` (this file), `artifacts/w4-gate/BUILD_REPORT.md`.

## 3. Files modified

| Path | Change | Why |
| --- | --- | --- |
| `unity/.../Runtime/Gc013NarrativeHost.cs`, `.../Gc013CardsHost.cs` | the host class and the family class became `partial`, the family `public`; the declaration/scope helpers the new parts reuse became `internal static`; the cards host's declaration list gained the gate's state-policy declaration | a `partial` part is the repo's way of adding a run's surface to an existing scenario without editing or duplicating it. GC-013's own sequence and its observations are untouched (verified by diff: the narrative host changed by 10 lines, the cards host by 13). |
| `Packages/com.gamecore.planning/Runtime/StatePolicies/SlotStatePolicyDeclarations.cs` | `SlotStatePolicy.FromSpec` reads the manifest's reset support; doc comments corrected | §1.2 item 1. The file had documented a limitation the merge removed. |
| `Packages/com.gamecore.planning/Tests/StatePolicies/StatePoliciesFixture.cs`, `StatePolicyTests.cs` | the fixture builds its resettable slot from a spec; three new falsifying tests | same. |
| `unity/.../Tests/Narrative/NarrativeStatePolicyScenario.cs`, `.../Tests/Cards/CardStatePolicyScenario.cs` | the reset permission moved into the mounted manifest; the private `Resettable(...)` helper removed; `DerivedCompositionProposal.Build` calls given the published binding table | §1.2 items 1 and 3. |
| `artifacts/gates/w4-generic-profile/inventory.json`, `inventory.md` | a `w4Gate` note on 23 rows plus a `W4-GATE revision notes` section | §4 of this file. **No status moved**: see below. |
| `dotnet/README.md` | *not* modified — see §6 gap 4. | |

No other file changed. `W3GateScenario`, `Gc013Scenario`, `ProbeRunner`'s dispatch order, the frozen W0 seam, the
committed generated catalogs and every gameplay package's runtime code are untouched.

## 4. The gate scenario, and what each observation proves

`W4GateScenario.Run(IW4GateFamily)` runs one sequence over one real world per family and catalog, recording seventeen
named observations in a fixed order. The qualified name is `<label>/<name>`; the fixture-catalog run prefixes
`fixture:`. The digests are over the `name=pass|fail` lines (LF separated, no trailing newline), the same function the
narrative trace and the GC-013 result use:

* narrative `d73e1a15e3d5f997b47087d02ea73ed809b73692b35900c3f2feeeff65cebaab`
* cards `4a1bdb460ab366c5a0ffed77f09aaca881b4fdfea142195290ee5ad73283b018`


Both were recomputed independently on this host from the exported name table, not read out of the implementation.

| # | Observation | What makes it pass |
| --- | --- | --- |
| 1 | `w4-world-lane-and-extra-manifests` | the world, the lane (over a manifest source that also resolves the gate's five extra manifests), the incremental chain, the controller and the policy catalog all construct; five slot cases seed; two extra live targets at the declared scope; no live slot undeclared; mode Automatic; epoch `First`; zero idle steps. |
| 2 | `w4-extra-providers-mount-onto-the-same-revision` | the lifecycle provider, the policy host, the required provider, its consumer and the unload installation each mount as their own publication; the consumer's manifest declares a **required** `ServiceDependency` on the pair's contract and the publication gave it a binding; all three contributing installations hold attributed rows; the policy host is `Active` at its declared scope. |
| 3 | `w4-automatic-inheritance-and-the-future-target` | the second provider mounts and every eligible target agrees with its published row; a future target spawns in Automatic on a no-change publication and is visible with its own row at the provider's value. |
| 4 | `w4-subtree-move-preserves-state-and-switches-binding` | a branch moves under another branch: the moved target keeps identity, owner scope, seeded live value and published binding **shape**, its effective capability is afterwards named by the second provider's installation, the committed parent edge moved, the incremental closure names the target dirty, and the epoch advanced. |
| 5 | `w4-mode-automatic-to-conservative-retracts-existing-and-future` | the switch publishes; every automatically eligible existing target loses the binding and every row; the future target spawned at step 3 loses it too; the explicitly opted-in target keeps it at the provider's value; isolated and ineligible targets stay clear; imports `0`, opt-ins `1`, scope imports `0`; the isolated fingerprint equals the baseline captured before the switch. |
| 6 | `w4-mode-conservative-to-automatic-restores-existing-and-future` | the reverse direction over the identical targets: rows and values restored on every eligible target and on the future target; the isolated fingerprint still equals the baseline; imports still `0`. |
| 7 | `w4-suspend-retracts-and-resume-restores` | the family's own provider suspends: state `Suspended`, authority withdrawn, live-activation count shrunk, attributed rows `0`, and a token minted before the suspend is discarded at completion. The resume restores exactly the pre-suspend row count, re-grants authority, and a fresh token dispatches. |
| 8 | `w4-required-provider-loss-makes-consumers-wait` | unmounting the required provider and the consumer's wait happen in the **same publication**: `WaitingConsumers` names exactly that consumer, its bindings fall to `0` from a positive count, its rows are retracted, and the derived publication is not refused. |
| 9 | `w4-required-provider-return-resumes-consumers` | a compatible provider mounts: the consumer is `Active`, `ResumedConsumers` names exactly it, bindings and rows are back to their pre-loss counts. |
| 10 | `w4-unload-disposes-in-reverse-acquisition-order` | two leases stage in acquisition order; the P-048 order runs; the six steps are recorded; `DisposeSettled` is true with nothing quarantined and no failed release; the resource factory's disposal log is the **exact reverse** of the acquisition order; a pre-unload token does not dispatch; the installation ends `Disposed` with no retained reference. |
| 11–13 | `w4-slot-preserve-keeps-the-non-default-value`, `w4-slot-preserve-dormant-retains-without-an-active-writer`, `w4-slot-remove-derived-drops-the-row` | each case's pass runs over copies of the seeded live slots and its plan is the plan the publisher applies. `Preserve`: value and version unchanged, writer present before and after. `PreserveDormant`: value retained, writer gone, and the pipeline's dormant registry records that exact value and version. `RemoveDerived`: the row is gone. |
| 14 | `w4-slot-transfer-to-moves-the-value-to-the-named-owner` | the source row is retired and the destination key `(destinationTarget, declared owner, same slot)` holds the value at the same version, with the destination owner declared by this revision and the destination outside the policy target set (P-034). |
| 15 | `w4-slot-reset-uses-the-manifest-permission` | first, the **same declaration without** the GC-012 field is fed a reset and must refuse it `OwnershipConflict` with no dispositions; then the manifest-declared reset writes the declared initialization value over the live value at the unchanged version. This is what makes "the manifest field is what authorizes a reset" falsifiable rather than assumed. |
| 16 | `w4-lane-epoch-equals-world-epoch-throughout` | `NotePublication` re-checks P-006's one-series invariant after **every** publication the run makes and counts mismatches; the step requires zero mismatches over at least one observed publication, and that the lane pair and the world pair agree now. |
| 17 | `w4-teardown-settles-and-disposes` | zero idle steps; the world stops; no outstanding job; no retained resource; the world registry returns to its pre-create count. |

## 5. Requirement / test coverage mapping

| Requirement / test | Where implemented | Where observed (the gate) |
| --- | --- | --- |
| P-003 three kinds of change | `TeardownReport`/`ContributionRetraction` | step 10 (`DisposeSettled`, retraction counts, retired leases) |
| P-006 one publication series | `AssemblyPublisher.MatchesPublishedAssembly` | steps 1–17 via `NotePublication`; step 16 is the explicit verdict |
| P-007 leases and async tokens | `ManagedResourceLease`, `RecoveryLedger`, `AsyncWorkToken` | steps 7 and 10 (pre-change tokens discarded) |
| P-009 manifests and precompiled factories | `CatalogManifestSource`, the gate's manifest source, both catalogs' `FamilyEntryRegistrations` | step 1 (policy host and five extra manifests resolved) |
| P-010 scope membership | `CompositionHost.Committed.Scopes` | step 4 (committed parent edge) |
| P-011 service visibility | `ServiceDependency`/`ServiceExport`, `ServiceResolver` | step 2 (required dependency declared *and* bound) |
| P-012 dependency closure | `ServiceClosureDelta` | steps 8 and 9 (same-publication wait, then resume) |
| P-013 mode semantics | `DerivationPolicy` + the two mode edits | steps 3, 5, 6 (existing **and** future targets in both directions; imports `0`) |
| P-014 mode transition | `DerivationModeSwitchValidator`, `InvalidationClosure` | steps 5 and 6 (both directions, each a published whole-world closure) |
| P-015 eligibility | descriptor predicates, the closed recipe catalog | steps 3 and 5 (ineligible target clear throughout) |
| P-016 isolation and exclusions | `BlockedByBoundary` denials | steps 5, 6 and the mid-run re-checks (fingerprint identical to the pre-switch baseline) |
| P-017 contribution identity and support | `CapabilitySupport`, `CapabilitySupportRow`, `ReadSupportRows` | step 2 (multi-supporter rows), step 4 (binding shape preserved while the provider switches) |
| P-018 precedence | support-set canonical order | step 4 (the second provider wins after the move) |
| P-019 composition policies | `AssemblyPlanner`'s policy branches | steps 4–6 through the derivations they publish |
| P-020 reconfiguration never resets | `StatePolicyIntent.Preserve`, `DecideReset`'s separation | step 11 (`Preserve` over a seeded non-default value) |
| P-023 incrementality | `IncrementalDerivationEngine`, `InvalidationClosureResult` | every step's `DerivationReport.Invalidation`; step 4 requires the moved target dirty |
| P-024 spawn completeness | `DerivedBindingRule.Supports`, `AssemblyPublisher.AppendSupportRows` | step 3 (future target visible with its own row at first visibility) |
| P-025 reparenting | `ProviderClosureDiff`, `DerivationChangeSet.ScopeMoves` | step 4 in full |
| P-029 bounded scratch, failure keeps the old assembly | `MigrationScratch`, `StatePolicyPlan.Refuse` | each policy pass runs before the lane advances, so a refusal leaves no publication debt (steps 11–15) |
| P-032 slot policies | `SlotStatePolicyDeclarations`, `StatePolicyExecutor`, the manifest reset field | steps 11–15 |
| P-033 one layout per component; shared components survive | `SlotLayoutGenerator`, `MarkSlotDormant` | steps 11–13 (dormant rows retained; removal drops exactly one row) |
| P-034 one owner per authoritative domain | `OwnerTransferValidator`, `SlotStatePolicySet.TryFind` | step 14 (declared destination owner; destination outside the policy set) |
| P-042 expected domain version | `IDomainVersionAuthority`, `WorldMessagePlane`'s guard | both families' derivations run through the lane whose routes carry the authority |
| P-046 installation lifecycle | `ActivationLedger`, `InstallationLifecycleCoordinator` | steps 7, 8, 9, 10 |
| P-047 in-flight lifetime | `CallbackGate`, `UnityLifecycleWorldBinding` ingress closure | steps 7 and 10 |
| P-048 teardown order | `TeardownSequencer` | step 10 (six steps, reverse-order disposal) |
| P-050 identity and idempotency | `OperationLedger` | every step mints a fresh operation; `NotePublication` finds its own publication by operation identity |
| P-060 evidence | `tools/run_w4_gate.sh`, `tools/unity/run_w4_gate_probe.sh` | the scripts themselves; the digests are the gate's own contract |
| TEST-001 toolchain / generated registration / IL2CPP | both catalogs, `build_probe.sh` | `run_w4_gate.sh` steps 5–6 |
| TEST-002 identities, epochs, stale references | epochs/generations, discarded tokens | steps 7, 10 |
| TEST-005 composition and precedence | the planner over GC-012's support sets | step 2, step 4 |
| TEST-008 incremental indexes and moves | GC-013's scenario + this gate | step 4 (dirty target + reported closure) |
| TEST-010 state preservation, migration, reset | GC-015's executors + this gate | steps 11–15 |
| TEST-013 authority, writes, requests | layout owners, the publisher's apply stage | steps 11–15 (all reads are live storage) |
| TEST-015 lifecycle and managed resources | GC-014's coordinator | steps 7–10 |
| TEST-018 Unity worlds, bootstrap, disposal | `UnityWorldRegistry` | steps 1 and 17 |
| TEST-024 documentation and traceability | `docs/game-core/` | `run_w4_gate.sh` step 7 |

### 5.1 Why the gate's adapter contract inherits `IGc013Family`

The brief's reconciliation is an *integration* of four tasks, not a fourth parallel implementation. `IGc013Family`
already declares everything GC-013's move/mode clauses need (catalog, scope tree, live targets, both providers, the
branch to move, the two mode payloads, the neutral scope creations), and GC-013's own sequence remains the authority
on those clauses. `IW4GateFamily : IGc013Family` therefore adds only the two halves it does not have — the lifecycle
surface and the state-policy surface — and both adapters are `partial` parts of the families GC-013 already runs, so
the same declarations serve both sequences and cannot drift apart.

## 6. Known gaps, assumptions and remaining risk

1. **Nothing has been compiled or executed.** The most likely first failures, in order, are:
   (a) `IW4GateFamily` member count or shape drift between my contract and the two adapter parts (the scripted
   cross-check in §7 says zero missing and zero duplicated, but that is a name check, not a compiler);
   (b) a detail string the probe script greps for not appearing because a step took an early-return path;
   (c) the `PublishPolicyCase` publication shape — the pass runs first and the lane advances on the case's neutral
   edit, and the plan is published against the lane's own pair. GC-015's scenario uses a *different* order (lane
   first, then `SyncWorldBehindLane`), which is why the two are independent paths and why a failure here localises to
   the gate rather than to GC-015;
   (d) the mount step's `consumerResolved` check reads `InstallEntry.Manifest.ServiceDependencies` and
   `InstallEntry.Bindings` — the property names come from the source, but this is the kind of thing only a compiler
   confirms;
   (e) whether a family's `PolicyTargets` set has *every* live row declared by the manifests `ManifestSet()` returns
   (the pass decides on every live slot it is shown). The narrative adapter keeps `Mara` in the set, which carries a
   chapter-provider row declared in `Declarations`; the cards adapter restricted its set to the market table for
   exactly this reason.
2. **The reset falsification probe is a reconstructed declaration, not a second revision.** `UndeclaredResetIsRefused`
   builds a `SlotStatePolicySet` from this revision's manifests with each slot's options derived from its
   last-support policy alone — i.e. the pre-GC-012 reading of the same declarations — and requires the reset to be
   refused. An alternative reading (unmount the policy host, then reset) would need the lane to accept a policy pass
   over an unmounted installation's slots, which is a different and weaker statement. Recorded as a decision.
   alternative was a reflective or public accessor for a private rule; the duplication is deliberate and is the same
   choice GC-015's scenario made. If a third caller appears, the rule belongs on `PlannedPublication`.
4. **`dotnet/README.md` was not updated.** GC-012 added its gate row there; this gate's row was left out because the
   brief's file list does not include it and every other Wave gate is listed in the same table only when its author
   chose to. Recorded as a known omission rather than silently done.
5. **The IL2CPP half is only *authored*.** `run_w4_gate.sh` builds the player and runs `-probeW4Gate` `PROBE_RUNS`
   times, but that has not happened. The gate cannot be claimed before it does.
6. **Provisional generic execution freeze.** The wave's exit sentence says the freeze "requires GC-012". GC-012's own
   gate (`artifacts/gates/w4-generic-profile/`, `-probeW4Profile`) is kept intact and is rerun as step 6 of
   `run_w4_gate.sh`, so this gate adds the dynamic-composition integration the sentence's second half asks for; it
   does not re-evidence the freeze.
7. **Dynamic composition is still awaiting later stress/fault completion.** The gate demonstrates each Wave 4 clause
   once per family per catalog. It is not the 1,000-cycle teardown churn (GC-022), not the fault-injection matrix, and
   not the retention/backpressure stress — those are Waves 5–6 and the gate says so in its own header.

## 7. What actually ran on this host

```sh
python3 tools/check_game_core_csharp.py                 # checked 359 C# file(s); ok
python3 tools/validate_game_core_docs.py --self-test    # 9 isolated fixtures passed
python3 tools/validate_game_core_docs.py                # 14 documents; links, anchors, IDs, traceability, DAG, waves
bash -n tools/run_w4_gate.sh tools/unity/run_w4_gate_probe.sh tools/run_w3_gate.sh   # all clean
# .meta GUID uniqueness over the whole worktree: 568 metas, 568 unique, 0 duplicates
# scripted IW4GateFamily conformance: 30 interface members, 0 missing and 0 duplicated in each adapter part
# digest recomputation: both literals reproduced from the exported observation table
```

None of that is a build, an import, a test or a player run.

## 8. Exact commands for the Linux build host

Everything runs from the repository root. Nothing below has been run.

### 8.1 One command (the whole gate)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet \
  PROBE_RUNS=5 tools/run_w4_gate.sh
```

It runs, in order (every Unity invocation wrapped in `timeout`, a timeout retried once):

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/w4-gate/trx
timeout 3600 "$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile .../unity/resolve.log
timeout 3600 "$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testResults artifacts/w4-gate/unity/editmode-results.xml -logFile artifacts/w4-gate/unity/editmode.log
timeout 3600 "$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform PlayMode \
  -testResults artifacts/w4-gate/unity/playmode-results.xml -logFile artifacts/w4-gate/unity/playmode.log
timeout 3600 "$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation \
  -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog -logFile .../unity/card-codegen.log
UNITY="$UNITY" ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/build_probe.sh
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_probe.sh both
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_world_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_w1_gate_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_w2_gate_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_narrative_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_cards_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_w3_gate_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_w4_profile_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_gc013_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/run_w4_gate_probe.sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

`UNITY` is required (exit 2 without it): the gate is never claimed from the dotnet half alone. Do not add `-quit` to
a test-run command (04 §10).

### 8.2 The W4 gate EditMode assembly alone

```sh
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.W4Gate.Tests \
  -testResults artifacts/w4-gate/unity/w4gate-editmode.xml \
  -logFile artifacts/w4-gate/unity/w4gate-editmode.log
```

### 8.3 The W4 gate probe alone

```sh
PROBE_RUNS=5 tools/unity/run_w4_gate_probe.sh
```

### 8.4 What `run_w4_gate_probe.sh` asserts

`"task": "W4-GATE"`, `"mode": "W4Gate"`, `"result": "Pass"`, no `"status": "Fail"`; every step name (17 observations
× 2 catalogs × 2 families = 68, plus `w4gate-narrative-digest` and `w4gate-cards-digest`); both digest literals; and
these fragments of the step details — `mode=Automatic->Conservative`, `mode=Conservative->Automatic`,
`mismatches=0`, `reverseOrder=True`, `lateCompletion=discarded`, `dormant=True`, `namedExactly=True`,
`shapeHeld=True`, `supportProviderIsSecond=True`, `policyHostScoped=True`.

## 9. Assumptions and doc ambiguities

1. **`IW4GateFamily` extends `IGc013Family` rather than restating it.** 09's GC-013 section says the shared plan DTOs
   remain fixed and that later work passes invalidation deltas through GC-013's seam; reusing the family contract is
   that instruction applied to a test surface. Recorded because a reviewer might expect a standalone contract.
2. **"Both mode directions … with existing+future targets" is read as: one future target spawned before the first
   switch, and both directions asserted over it.** The alternative — a second future target spawned between the two
   switches, or one per direction — would make each direction a statement about a different target set and would need
   two more publications without adding a clause. The prose in the two mode observations says which reading was taken.
3. **The policy cases run one publication each.** The exit sentence says the policies are "exercised along the way";
   nothing requires them to share a publication with the move or the lifecycle steps. A separate publication per case
   is what makes "this disposition caused this storage change" checkable at all (P-006's one-series rule is what makes
   batching them unsafe).
4. **Reset is `PreserveDormant` + `resetSupported: true`,** not a `Preserve` last-support policy: `LastSupportPolicy`
   has no `Preserve` member (its three values are P-032's three *last-support outcomes*), and `Preserve` is a
   *request* intent, not a declaration. An explicit `Preserve` request is legal against any declaration, which is what
   the `Preserve` case relies on. Recorded because the wave's wording ("slot policies (Preserve/PreserveDormant/
   RemoveDerived/TransferTo)") reads as four declaration values.
5. **The transfer destination must lie outside the policy target set.** A transferred row carries the source slot
   identity under a second owner, and `SlotStatePolicySet.TryFind` refuses a live row whose owner contradicts the
   slot's declaration (P-034), so a later pass over that target would fail. Both adapters therefore point the
   destination at a live target the passes never read. This is a real consequence of P-034 that the task wording does
   not mention; it is stated in both adapter headers.
6. **The lifecycle subject is the installation that carries behavior, not the one that owns the table.** For cards,
   the table runtime derives nothing, so a rows-based suspend/resume observation could not be about it; the league
   scoring provider is used instead. The adapter header says so.
7. **A second declared policy owner for `TransferTo` on cards.** The cards revision genuinely declares one logical
   owner, and P-032's transfer needs a *named available owner*. The gate's own provider manifest declares the
   destination owner together with the one slot it holds. This is a test-declared owner, not a change to the cards
   package.
8. **The `timeout` policy.** The brief requires every Unity/player invocation wrapped in `timeout` and allows one
   retry of a timed-out Unity invocation. `probe_runs.sh` already wraps each player run in a 600 s timeout and fails
   the gate on any unclean run, so a player timeout is an immediate failure and is *not* retried; the retry exists
   only for the Editor steps, where the known hang is pre-dispatch. `UNITY_TIMEOUT` defaults to 3600 s and is logged.
9. **No testables/manifest change.** No new package was added and the new test assembly lives inside the project, so
   `unity/GameCore.Validation/Packages/manifest.json` and `packages-lock.json` are untouched and need no regeneration
   (the same conclusion GC-014 recorded for its own test assembly).
