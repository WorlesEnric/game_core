# Composition and propagation implementation

Normative sources: [manifests/services/modes](00-core-protocols.md#p-009), [derivation](00-core-protocols.md#p-015), [assembly](00-core-protocols.md#p-027), and [lifecycle](00-core-protocols.md#p-046). This document chooses data structures and algorithms for those contracts.

## 1. What Cordis contributes

The inspiration is context-scoped services, dependency-driven plugin activation, configuration, and managed lifetimes. Cordis `Context.extend`, `isolate`, and `intercept` provide concrete examples of derived contexts and per-service isolation. They do not implement Game Core target eligibility or ECS propagation. [Cordis context source, v4.0.0-rc.10](https://raw.githubusercontent.com/cordiverse/cordis/v4.0.0-rc.10/packages/core/src/context.ts).

Cordis fibers track activation state, dependency changes, epochs, and disposers. Within one `effect` group, cleanup runs in reverse order, while fiber unloading starts multiple disposers through `Promise.all`. Game Core therefore specifies its own dependency-ordered retirement rather than claiming Cordis already guarantees a whole-graph teardown order. [Cordis fiber source, v4.0.0-rc.10](https://raw.githubusercontent.com/cordiverse/cordis/v4.0.0-rc.10/packages/core/src/fiber.ts).

Cordis service access is tied to injection and isolation-aware implementation lookup. Game Core's explicit manifest resolution and binding leases are a C# design, not a line-by-line JavaScript port. [Cordis reflect source, v4.0.0-rc.10](https://raw.githubusercontent.com/cordiverse/cordis/v4.0.0-rc.10/packages/core/src/reflect.ts). The versioned package source is a research reference only; Cordis is not a Unity runtime dependency.

## 2. One runtime, three data paths

```text
Manifest services --> service resolver --> epoch-bound service leases
Manifest rules + scope/target descriptors --> derivation --> CapabilityContribution set
Admitted commands + runtime ECS state --> owner systems --> committed gameplay output
```

The first path connects managed participants. The second builds configuration, bindings, and component requirements. The third changes runtime state. A narrative plugin can acquire an asset service, derive gate bindings on every eligible gate, and later change a quest fact through a system. These are separate operations with separate ownership. Unmount releases the asset lease and retracts bindings; the fact follows its state policy, not a generic “undo effect” callback.

The service resolver runs for affected installation dependencies during planning. `ServiceBinding` objects never carry per-entity state. The rule engine processes reusable descriptors and interned configurations, then writes compact indexes to ECS bindings. There is no service lookup or scope traversal in a per-target gameplay job.

## 3. Stored and derived records

| Record/index | Representation and update | Why it exists |
|---|---|---|
| `ScopeRecord` | Stable ID, parent, depth, children set, boundary/exclusion/import revision | Authoritative composition metadata, separate from ECS state. |
| `TargetDescriptor` | Target ID, scope, recipe revision, schema IDs/versions, interned tags | Compatibility is authored once per type/recipe; instance overrides sparse. |
| `InstallationRecord` | Manifest key/config, lifecycle state, activation generation, scope | Distinguishes plugin type from mounted instance. |
| `TargetsByScope` | Dense target ID lists plus slot lookup | Enumerate a changed branch without scanning every target. |
| `TargetsByContract` / `TargetsByTag` | Sorted ID sets or bitsets keyed by reusable descriptor | Intersect the rule's eligible population. |
| `RulesBySourceScope` / `RulesByInputContract` | Reverse lookup and cached ancestry fingerprints | Reevaluate only rules affected by a provider/input change. |
| `ContributionsByProvider` / `ContributionsByTarget` | Stable contribution keys with interned payload/provenance | Exact retraction and inspection. |
| `SupportByStateSlot` | Effective support-ID set and final state disposition | Prevent removing data still needed by another contribution or base recipe. |
| `ConsumersByServiceProvider` | Reverse dependency adjacency | Quiesce/rebind closure when a provider changes. |
| `DerivedRecipeCache` | Recipe + ancestry fingerprint + mode + catalog hash | Fast repeated spawning, invalidated on relevant edits. |

V1 uses adjacency lists and explicit affected-subtree enumeration; it does not need a sophisticated fully dynamic tree structure. Depth and cached inheritance fingerprints update only for moved/changed branches. Index changes are staged beside a plan and publish with it; a rejected proposal must not poison live caches. Debug builds can rebuild all indexes from the committed composition and compare them. Reference counts alone are not sufficient provenance: support identities are needed to know exactly what retracts.

## 4. Derivation algorithm

The following is **pseudocode**, not executable C#; the normative limits and ordering are [P-018–P-023](00-core-protocols.md#p-018).

```text
BuildPlan(baseSnapshot, proposal):
  validate expected revision and catalog hash
  next = apply proposal to a private composition snapshot
  dirty = indexed invalidation closure(baseSnapshot, next, proposal)
  resolve required/optional services and activation closure
  select active rule instances and target descriptor candidates in dirty
  for stratum in 0..maxCatalogStratum:
    for (rule, target) in stable indexed candidate order:
      account candidate budget
      test ancestry, reach, isolation/exclusion, mode grant, schema, predicate
      if eligible: emit bounded fixed output slots with stable contribution keys
      else: record compact explanation reason
    compose all changed (target, capability, outputSlot) groups
    finalize this stratum's effective values before higher-stratum evaluation
  diff old/new contributions, bindings, state support and cached recipes
  compile ownership and execution graph; validate state dispositions
  estimate resources/application work and validate hard budgets
  return immutable ChangePlan and canonical hash, or diagnostics
```

A full oracle ignores indexes and recomputes every candidate using the same pure rule contracts but a separate traversal implementation. It is deliberately slow. Property tests compare effective values, supports, recipe hashes, and explanation reason sets, not just component counts. The optimized engine and oracle share schema definitions, but should not share invalidation code.

Negative eligibility can read a lower stratum that is already final. Example: stratum 0 derives `quest.participant`; stratum 1 derives `dialogue.choice` only if that capability is present; stratum 2 derives an optional reward binding. A rule cannot derive `quest.participant` based on its own absence. Rules cannot spawn child scopes or targets. Gameplay can request a later spawn through O-12, which creates a new bounded planning problem rather than recursively extending the current closure.

## 5. Exact mode delta

After ancestry, isolation, exclusion, and basic compatibility checks, the mode gate is:

```text
local rule                          => permitted in both modes
Automatic + Descendants reach       => permitted
Conservative + exported + imported  => permitted
Conservative + full TargetOptIn     => permitted
otherwise                           => not permitted
```

“Local rule” means `LocalOnly` applied to targets at its installation scope; it is not a descendant loophole. A propagated `Descendants` rule whose reach includes the provider's own scope still uses the selected mode gate. Imports can be supplied by reusable recipes or scope configuration; Conservative need not duplicate them on every instance. Automatic compatibility never requires these imports at all. Rule exclusions and capability boundaries take priority over both modes. Services do not pass through this gate.

Switching modes simply invalidates inherited contributions and replans under the other predicate. No separate assembly engine, target confirmation process, or automatically persisted opt-in list exists. A proposed Conservative→Automatic switch may reveal an exclusive conflict; the result is a diagnostic and the old mode intact, followed by an explicit configuration edit/retry. This makes ambiguity visible without weakening default automatic application.

## 6. Multi-provider composition worked example

Suppose a reusable `market.CardTemplate` advertises `card.selectable/v1`. Providers A and B both contribute `selection.limit` under a `Replace` contract:

| Provider | Scope depth | Priority | Value |
|---|---:|---:|---:|
| A, match scope | 1 | 10 | 3 |
| B, table scope | 2 | 10 | 2 |

The result is 2, with A retained as a shadowed candidate. Removing B reveals A's 3 without reconstructing the card's runtime ownership or score. Raising A's explicit priority above B chooses 3, even though B is nearer. An explicit eligible `SelectProvider=A` also chooses 3. If the contract were `Exclusive`, two candidates would reject without selection; rank would not hide the conflict. If the slot were `Additive`, a versioned reducer would determine the meaning of both values. This difference is declared by the capability contract, not inferred from their numeric type.

If C and D jointly support the same derived component, removing C preserves D's support. If the base recipe owns that component, neither C nor D can delete it. Config payloads can change while its mutable ECS state is preserved. Those distinctions are checked before `ChangePlan` is marked Validated.

## 7. Invalidation cases

| Change | Dirty seed | Closure and state behavior |
|---|---|---|
| Mount/unmount provider | Its source scope, rule contracts, service consumers | Matching targets and higher-stratum dependents; retract only provider keys. |
| Reconfigure rule payload | Rule instances and their prior/possible matches | Recompose output; preserve runtime state unless explicit disposition. |
| New target | Its recipe descriptor and ancestor provider fingerprint | Derive complete recipe before exposure, including higher strata. |
| Descriptor/tag patch | Old and new descriptor index buckets | Reevaluate entering/leaving matches; do not mutate eligibility tags directly in jobs. |
| Reparent subtree | Moved targets plus old/new ancestor rules/services | Update inheritance, including shadowed candidates; keep same target/state slot IDs. |
| Isolation/exclusion edit | Boundary subtree and blocked provider keys | Denial/reopening closure; no sibling spillover. |
| Service provider replacement | Consumer graph | Rebinding can activate/suspend rules, causing normal propagation closure. |
| Mode switch | All descendant rules/targets potentially gated | Broad cost accepted, bounded preparation; one publication. |

Reparent preparation uses a private next-tree snapshot; published queries continue to use the old parent until the epoch changes. The same applies to spawn recipe fingerprints. Queued stale spawns can be rebuilt at a new revision; the host returns the original stale attempt's result and creates a new operation for the replan rather than mutating an existing idempotency record.

## 8. Applying derived data

The bridge compiles one target layout from the base recipe and effective capability slots. It produces generated component-add/remove/update functions, compact active binding tables, and explicit state-slot dispositions. Persistent mutable data stays in its ECS components; `PreserveDormant` gates active queries and retains the data in checkpoint serialization. A shared component implementing multiple slots has one physical ownership mapping declared by the catalog.

The prepared plan carries scratch capacity for migrations but does not capture live state while jobs run. At the fenced boundary it copies needed state, runs migration validation in scratch, then starts application. A rejected migration leaves the old assembly untouched. Once live mutation begins, recovery uses the [fail-stop protocol](06-lifecycle-and-recovery.md), not a best-effort undo of arbitrary ECS operations.

## 9. Cost visibility

Each operation reports targets/candidates visited, contributions added/removed/changed/shadowed, invalidation reasons, cache hit/miss counts, prepare wall time, fenced apply duration, allocation bytes, and top provider fan-out. The protocol limits are safety bounds; [performance tests](08-validation-and-performance.md#test-023) establish practical budgets separately.

Broad subtree behavior is intentional. The diagnostic question is “which rule and index caused this scope of change?” A host may choose a paused-world publication or a larger configured budget. The implementation must not reinterpret broad fan-out as a requirement to ask each target for permission, drop contributions silently, or scan the scope tree on every frame.
