# Contracts and data model

The requirements in [00](00-core-protocols.md) own semantics. This document owns representative C# representation and generated schema shapes. It does not define another ECS or a universal entity API.

## 1. Code status and assembly boundary

[GameCore.Contracts.cs](examples/GameCore.Contracts.cs) is a self-contained **compilable C# 9 contract skeleton**, without Unity dependencies, runtime implementation, serialization, or generated registration. [Its project](examples/GameCore.Contracts.csproj) exists only for a host-side syntax/build check. That host build is not Unity, Burst, or IL2CPP compatibility evidence. The production assembly remains governed by the [Unity baseline](04-unity-integration.md); compiling this skeleton does not qualify a package.

The skeleton was built successfully with .NET SDK 8.0.303, C# 9, and `netstandard2.1` during this documentation delivery. See the [validation report](references/documentation-validation.md) for the command and limits of that result.

Other code/text blocks in this documentation are explicitly illustrative pseudocode or data examples unless identified otherwise. Generated runtime types in the tables below are a schema specification, not a claim that the compiler already exists. Production strongly typed wrappers are generated for all identity categories; the compact skeleton uses `Id128` for categories not expanded there.

`GameCore.Contracts` exposes immutable value DTOs. Managed arrays, strings, task objects, factories, and DI values are confined to control-plane assemblies. The compiler lowers validated data into unmanaged Unity components, blobs, native indexes, and closed job/system entry points. No reflection-based runtime serializer, `dynamic`, runtime assembly load, or universal generic component-access interface is needed.

## 2. Identity and version representation

| Type | Fields | Use and invariant source |
|---|---|---|
| `Id128` / generated category wrappers | `High`, `Low` unsigned 64-bit values | Compare/write big-endian high then low; never use platform Guid memory order as canonical bytes. [P-004](00-core-protocols.md#p-004). |
| `WorldId` | Session `Id128` | Caller reserves fresh ID for create/restore; one session only. |
| `TargetHandle` | World, runtime slot, 64-bit generation | Resolve through world-owned map; not persisted. [P-005](00-core-protocols.md#p-005). |
| `DefinitionRef` | DefinitionId, SchemaId, SchemaVersion, content revision | Immutable revision lookup; does not itself hold an asset lease. |
| `OperationId` | World, IssuerId, issuer sequence | Shared sequence namespace for control operations and commands, dedup/high-water ledger. [P-050](00-core-protocols.md#p-050). |
| `SnapshotToken` | World, AssemblyEpoch, LogicalStepId | Immutable observation identity; composition publication may keep same step. |
| `AsyncWorkToken` | World, PluginInstanceId, installation generation, activation epoch, operation ID, work ordinal | Validated both at ingress and completion dispatch. |
| `ContributionKey` | Provider installation, RuleId, TargetId, CapabilityId, output-slot integer | Identity stable across payload reconfiguration; provenance retains activation/revision separately. |
| `StateSlotKey` | TargetId, OwnerId, SlotId | Mutable state lifetime distinct from the provider's contribution key. |

All counters reject overflow. A fresh session uses zero as the pre-publication revision/epoch/step; initial assembly publishes epoch/revision 1 while step stays 0. Default zero IDs are invalid catalog identities. `PluginHandle` survives an ordinary reconfigure as an installation reference; an execution callback also needs the current activation epoch. Unmount/remount invalidates that handle through generation even if the stable installation key is reused.

## 3. Manifest schema

The production compiler accepts a versioned declarative manifest and emits a validated immutable catalog. The following field table is the canonical implementation shape for [P-009](00-core-protocols.md#p-009); semantic rules still live in 00.

| Record | Required data |
|---|---|
| `PluginManifest` | PluginTypeId, package version/content hash, supported protocol major/min/max minor, required features, config SchemaId/version, factory key, all following declaration arrays (empty allowed). |
| `InstallationSpec` | PluginInstanceId, ScopeId, config revision/blob, priority, instance service selections; identity is explicit saved assembly data. |
| `ServiceExport` | Contract/version, generated factory key, private/descendant visibility, explicit override flag, single/multi binding contract. |
| `ServiceDependency` | Contract/version range, required/optional, allowed resolution domain, optional selected provider, fallback key. |
| `CapabilityContract` | CapabilityId/version, stratum, output-slot schemas, per-slot composition policy and reducer key/version, incompatibility IDs. |
| `DerivationRule` | RuleId, output capability/stratum, fixed output-slot bound, selector/contract versions, static predicate key, lower-stratum inputs, reach, export flag, priority, immutable payload definition. |
| `TargetDescriptor` | Recipe/version, supported schemas/capabilities, immutable tag set, asset-adapter descriptor, sparse local patches/imports/opt-ins/exclusions. |
| `StateSlotSpec` | SlotId, OwnerId, schema/version, physical layout key/field ownership, init/config-change/version-change/last-support/transfer policies, **reset support and its recorded reason**, and migration keys. |
| `StageSpec` | StageId/version, factory keys, system multiplicity, required/optional stage edges, per-system keys/access declarations and inner-DAG edges, buffer ports, affinity. |
| `BufferSpec` | BufferId/schema, producer IDs, one owner/consumer stage, order key, lifetime, bounded capacity, overflow and cancellation/rebind policy. |
| `ResourceSpec` | Resource key, factory, dependency keys, preparation gate, lifetime owner, disposer, failure classification. |

Catalog constructors validate duplicates and version ranges. Runtime mounts cannot fill an omitted owner policy from a convenient default or discover missing types by reflection. A slot is resettable only when its own declaration carries `resetSupported` together with a recorded reason; an unpermitted reset is rejected rather than applied as zero initialization, and a supported reset without a reason is a declaration error (P-032). The reset a *proposal* requests carries its own explicit reason as well, so the two reasons are independent: the declaration says why the slot may ever be reset, the proposal says why this publication does. A schema can explicitly declare `PreserveDormant`; this is recorded policy, not an implicit behavior of arbitrary components.

Illustrative **pseudodata** for a reusable descriptor and rule:

```text
recipe market.basic-card@1:
  targetContracts: [card.selectable@1]
  baseLayouts: [CardIdentity, CardOwnership]
  imports: []

rule match-selection-limit:
  output: selection.limit@1, stratum: 0, maxOutputSlots: 1
  accepts: card.selectable@1
  reach: SelfAndDescendants, propagation: Descendants
  exportToDescendants: false
  policy: Replace, priority: 0, value: 3
```

In Automatic every compatible card beneath the installation receives the limit. In Conservative this example does not propagate until an explicit complete opt-in exists or export plus import is configured. The recipe remains reusable and does not gain a persisted import when Automatic evaluates it.

## 4. Plans and results

| Plan/result field | Representation / validation |
|---|---|
| Identity | OperationId, input hash, base CompositionRevision, catalog hash, plan hash. |
| Composition delta | Scope/install/membership/config/mode updates with old/new keys. |
| Derivation delta | Added/removed/changed contribution keys, effective slot changes, supports, explanation references. |
| Runtime delta | Generated recipe/layout operations, owner-grant table, state disposition/migration keys (including an explicit `Reset` request with its own reason, legal only where the slot's manifest declares reset support), execution plan, buffer binding table. |
| Resources | Staged acquisition handles, dependencies, readiness status, retiring lease IDs, scratch capacity. |
| Validity/cost | Invalidation summary, preconditions, affected counts, prepare/apply estimates, hard budget usage. |
| `OperationResult` | Outcome, old/new revision/epoch, diagnostics, publication snapshot token if any, cleanup/quarantine references. |

An immutable `ChangePlan` does not contain captured writable EntityManager access, stale component pointers, or arbitrary closure delegates supplied by unregistered code. Prepared resource handles are process-local and never serialized as the plan's stable hash. Plan hashing uses semantic inputs, canonical IDs, revisions, and generated handler keys, excluding timestamps and object addresses.

`PublishedWithCleanupErrors` means the new assembly is already authoritative and cannot be “cancelled back.” `Rejected` means no live writes were made. `Faulted` means live storage is no longer safe to execute. `NoChange` has no revision increment. The API must preserve these distinctions rather than returning only `bool`.

## 5. API boundary contracts

These API names are representative mappings of [O-01–O-26](00-core-protocols.md#10-operation-catalogue).

| API | Input / output and ownership | Preconditions / postconditions / errors |
|---|---|---|
| `ICompositionCommands.Submit` | Frozen `CompositionProposal` → OperationId/status handle; host owns queued copy | Valid session/issuer sequence/base revision; no live changes until publication. Returns validation, conflict, stale, budget, cancellation/fault outcomes from protocol. |
| `ICommandIngress.Submit` | Immutable command envelope → admission receipt | Registered route/schema, capacity and dedup pass; owner later decides gameplay. Caller cannot retain a mutable payload buffer. |
| `IOperationReader.Read` | OperationId → immutable pending/terminal status | No mutation; expired ledger returns ResultExpired. |
| `IOperationControl.Cancel` | Own operation key + target OperationId → Cancelled/TooLate | Serialized cutoff; cancellation of preparation releases/records resources. |
| `IObservationReader.Acquire` | SnapshotToken → disposable immutable lease | Retained token and pool capacity; reject expired token/backpressure, never expose writable ECS data. |
| `IExplanationReader.Explain` | Target/capability/token → immutable explanation pages | Matches published epoch; staged plan diagnostics use distinct API/label. |
| `IManagedResourceFactory.Prepare` | Frozen config + async token → gated lease | Control-plane only, registered factory, disposer recorded immediately; no irreversible gameplay output. |
| Generated `ApplyRecipe_*` | Validated layout/target + fenced world state → complete ECS assembly | Unity bridge only, current base epoch and completed jobs; exception after first live write faults. |
| Generated `Migrate_*` | Bounded copied old state + config → scratch new state/result | Pure, versioned, no I/O/ECS writes; failure leaves old live state. |

Public status completion may be exposed as an awaitable managed API, but no job awaits managed tasks. Data passed to jobs is copied/generated into unmanaged memory with an explicit fence/lifetime. The skeleton intentionally omits task scheduling implementation, full manifest constructors, and Unity apply APIs.

## 6. Serialization envelope and schema evolution

The V1 content/checkpoint format is a generated length-delimited binary envelope. It uses a fixed magic/version header, big-endian integer scalars, bounded UTF-8 strings, 128-bit IDs as high/low words, explicit null markers, and `(fieldId, wireType, byteLength, payload)` fields. Lists have checked counts and maximum byte lengths. Floating-point fields use explicit IEEE-754 bit encoding with schema-defined treatment of NaN; canonical replay fixtures avoid unordered NaN comparisons. Dictionary entries serialize by canonical key order. Checksums detect corruption, not adversarial tampering.

Required schema fields reject when missing or incompatible. Unknown optional fields can be skipped by length; unknown required feature/schema IDs reject before allocation of a live world. Entity references serialize as stable target IDs, fixed schema requirements and optional/null semantics. A two-pass restore first creates identity mappings, then patches references and validates referential integrity. Missing required targets reject; optional references become the declared None state with diagnostics.

Migration registration is a directed graph per schema. Unique explicit path `v1→v2→v3` is allowed; two possible paths to the same destination reject unless the catalog designates exactly one active path. Gameplay schema version, immutable definition revision, plugin package revision, and protocol version are independent fields. Changing a reducer's meaning increments its version and changes catalog/plan hashes even if its C# method name is unchanged.

Generated artifacts include typed ID wrappers, serializers/read limits, migration table, catalog hashes, managed factory table, closed system/job/handler references, link.xml roots, and a manifest of every intended AOT instantiation. IL2CPP tests execute those paths in the player; code generation alone cannot prove stripping preserved them.

## 7. Ownership and lifetime review checklist

For every public field, record whether it is a stable persisted identity, runtime handle, owned buffer, borrowed immutable snapshot, generated registration key, or resource lease. A Unity object reference cannot masquerade as a stable DefinitionRef. A native buffer cannot escape a job fence through an `object`. A nullable service binding does not give its consumer permission to invent an alternative authoritative state store. These checks implement existing [P-007](00-core-protocols.md#p-007), [P-034](00-core-protocols.md#p-034), and [P-054](00-core-protocols.md#p-054).
