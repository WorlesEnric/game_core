// GameCore.Planning — state-slot policies: the declaration side the executors read (GC-015).
//
// Normative sources: docs/game-core/00-core-protocols.md P-020 (reconfiguration changes effective configuration
// only; it MUST NOT reset mutable state), P-032 (every state slot identifies an initialization, configuration-update,
// owner-transfer and last-support policy; existing state defaults to `Preserve` when owner and schema stay
// compatible; a version change uses a registered `Migrate`; `Reset` requires an explicit manifest-supported proposal
// field and reason; the last-support loss follows the declared `RemoveDerived`/`PreserveDormant`/`TransferTo`), P-033
// (shared support and component lifetime) and 05 s3 (`StateSlotSpec`).
//
// GC-007's `SlotPolicyValidator` decides *whether a declared policy permits* a request; the executor in this folder
// decides *what happens to live state* when the request is executed. Both read the same declaration, so the two
// never disagree: this file is the declaration projection, `StatePolicyExecutor` is the execution.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning.Ownership;

namespace GameCore.Planning.StatePolicies
{
    /// <summary>The one state change a policy request asks for (P-020, P-032).</summary>
    public enum StatePolicyIntent
    {
        /// <summary>Keep the live value exactly as it is: the compatibility default (P-032).</summary>
        Preserve = 0,

        /// <summary>Keep the value, but with no active writer: dormant and excluded from active queries (P-032).</summary>
        PreserveDormant = 1,

        /// <summary>Delete disposable derived data; legal only for a declaration that says so (P-032, P-033).</summary>
        RemoveDerived = 2,

        /// <summary>Move the state to a named available owner (P-032), with the declared transfer policy (P-025).</summary>
        TransferTo = 3,

        /// <summary>Cross a schema version through a declared, registered migration (P-032).</summary>
        Migrate = 4,

        /// <summary>Explicitly reinitialize from the declared initialization policy (P-032).</summary>
        Reset = 5,
    }

    /// <summary>
    /// One request against one state slot, named by its protocol key `(TargetId, OwnerId, SlotId)` (P-032). A caller
    /// that supplies no request for a live slot asks for the compatibility default the declaration implies: the schema
    /// versions decide between `Preserve` and `Migrate`, and a disposable derived slot whose last support is gone is
    /// `RemoveDerived`.
    /// </summary>
    public readonly struct StatePolicyRequest
    {
        /// <summary>The slot this request is about; every policy is resolved through this key (P-032).</summary>
        public readonly StateSlotKey Slot;

        public readonly StatePolicyIntent Intent;

        /// <summary>Destination owner of a transfer; default when the request is not a transfer.</summary>
        public readonly OwnerId DestinationOwner;

        /// <summary>Destination target of a transfer; default means the state stays on its own target.</summary>
        public readonly TargetId DestinationTarget;

        /// <summary>Migration key a `Migrate` request selects; default when it selects none.</summary>
        public readonly FactoryKey MigrationKey;

        /// <summary>Explicit reason of a `Reset`; empty means no reason was recorded (P-032).</summary>
        public readonly string Reason;

        /// <summary>
        /// True when a transfer request is the final support loss of a slot whose declared last-support policy is
        /// `TransferTo`, rather than an explicit owner transfer authorized by the declared owner-transfer policy
        /// (P-025, P-032).
        /// </summary>
        public readonly bool DeclaredLastSupportTransfer;

        public StatePolicyRequest(
            StateSlotKey slot,
            StatePolicyIntent intent,
            OwnerId destinationOwner,
            TargetId destinationTarget,
            FactoryKey migrationKey,
            string? reason,
            bool declaredLastSupportTransfer)
        {
            Slot = slot;
            Intent = intent;
            DestinationOwner = destinationOwner;
            DestinationTarget = destinationTarget;
            MigrationKey = migrationKey;
            Reason = reason ?? string.Empty;
            DeclaredLastSupportTransfer = declaredLastSupportTransfer;
        }

        public static StatePolicyRequest Preserve(StateSlotKey slot)
            => new StatePolicyRequest(slot, StatePolicyIntent.Preserve, default(OwnerId), default(TargetId), default(FactoryKey), null, false);

        /// <summary>The final support loss of a slot whose declaration says `PreserveDormant` (P-032).</summary>
        public static StatePolicyRequest PreserveDormant(StateSlotKey slot)
            => new StatePolicyRequest(slot, StatePolicyIntent.PreserveDormant, default(OwnerId), default(TargetId), default(FactoryKey), null, true);

        /// <summary>The final support loss of a slot whose declaration says `RemoveDerived` (P-032).</summary>
        public static StatePolicyRequest RemoveDerived(StateSlotKey slot)
            => new StatePolicyRequest(slot, StatePolicyIntent.RemoveDerived, default(OwnerId), default(TargetId), default(FactoryKey), null, true);

        /// <summary>The final support loss of a slot whose declaration says `TransferTo` (P-032).</summary>
        public static StatePolicyRequest LastSupportTransfer(StateSlotKey slot, OwnerId destinationOwner, TargetId destinationTarget)
            => new StatePolicyRequest(slot, StatePolicyIntent.TransferTo, destinationOwner, destinationTarget, default(FactoryKey), null, true);

        /// <summary>An explicit owner transfer, authorized by the declaration's owner-transfer policy (P-025).</summary>
        public static StatePolicyRequest OwnerTransfer(StateSlotKey slot, OwnerId destinationOwner, TargetId destinationTarget)
            => new StatePolicyRequest(slot, StatePolicyIntent.TransferTo, destinationOwner, destinationTarget, default(FactoryKey), null, false);

        /// <summary>A declared version change through a registered migration (P-032).</summary>
        public static StatePolicyRequest Migrate(StateSlotKey slot, FactoryKey migrationKey)
            => new StatePolicyRequest(slot, StatePolicyIntent.Migrate, default(OwnerId), default(TargetId), migrationKey, null, false);

        /// <summary>An explicitly permitted reset with its recorded reason (P-032).</summary>
        public static StatePolicyRequest Reset(StateSlotKey slot, string? reason)
            => new StatePolicyRequest(slot, StatePolicyIntent.Reset, default(OwnerId), default(TargetId), default(FactoryKey), reason, false);

        public override string ToString()
            => Intent.ToString() + ":" + Slot.ToString()
                + (Intent == StatePolicyIntent.TransferTo ? "->" + DestinationOwner.ToString() : string.Empty);
    }

    /// <summary>
    /// Registered initialization policies: the declared init policy key resolves to the value a `Reset` writes
    /// (P-032). A reset never invents a zero: an unregistered init policy is `MissingDependency`, because
    /// "a missing compatible policy is a validation error, not implicit zero initialization".
    /// </summary>
    public interface IInitializationPolicyRegistry
    {
        bool TryGetInitialValue(FactoryKey initPolicy, SchemaRef schema, out int value);
    }

    /// <summary>In-memory initialization policies, keyed by declared key plus schema identity.</summary>
    public sealed class InitializationPolicyRegistry : IInitializationPolicyRegistry
    {
        private readonly List<Entry> entries = new List<Entry>();

        public int Count => entries.Count;

        /// <summary>Registers one initialization value. A repeated identical registration coalesces.</summary>
        public void Register(FactoryKey initPolicy, SchemaRef schema, int value)
        {
            if (initPolicy.RegistrationKey.IsDefault)
            {
                throw new ArgumentException("An initialization policy key cannot be a default zero key (P-032).", nameof(initPolicy));
            }

            for (int i = 0; i < entries.Count; i++)
            {
                Entry existing = entries[i];
                if (existing.Key.Equals(initPolicy)
                    && existing.Schema.Id.Value.Equals(schema.Id.Value)
                    && existing.Schema.Version == schema.Version)
                {
                    return;
                }
            }

            entries.Add(new Entry(initPolicy, schema, value));
        }

        public bool TryGetInitialValue(FactoryKey initPolicy, SchemaRef schema, out int value)
        {
            if (initPolicy.RegistrationKey.IsDefault)
            {
                value = 0;
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry.Key.Equals(initPolicy)
                    && entry.Schema.Id.Value.Equals(schema.Id.Value)
                    && entry.Schema.Version == schema.Version)
                {
                    value = entry.Value;
                    return true;
                }
            }

            value = 0;
            return false;
        }

        private readonly struct Entry
        {
            internal readonly FactoryKey Key;
            internal readonly SchemaRef Schema;
            internal readonly int Value;

            internal Entry(FactoryKey key, SchemaRef schema, int value)
            {
                Key = key;
                Schema = schema;
                Value = value;
            }
        }
    }

    /// <summary>
    /// One state slot as a policy executor reads it: the declaration (identity, owner, schema, physical layout,
    /// last-support and transfer policies, migration keys and generated options) plus the declared initialization and
    /// configuration-update policy keys and the version-change policy the plan carries (P-032, 05 s3).
    /// </summary>
    public sealed class SlotStatePolicy
    {
        public SlotStatePolicy(
            SlotAuthorityDeclaration declaration,
            FactoryKey initializationPolicy,
            FactoryKey configurationChangePolicy,
            FactoryKey versionChangePolicy)
        {
            Declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));
            InitializationPolicy = initializationPolicy;
            ConfigurationChangePolicy = configurationChangePolicy;
            VersionChangePolicy = versionChangePolicy;
        }

        /// <summary>Projects one catalog declaration, keeping its generated policy keys (never inferring them).</summary>
        public static SlotStatePolicy FromSpec(StateSlotSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            return new SlotStatePolicy(
                SlotAuthorityDeclaration.FromSpec(spec, SlotAuthorityOptionsFactory.ForLastSupport(spec.LastSupport)),
                spec.InitPolicy,
                spec.ConfigChangePolicy,
                FirstMigrationKeyOf(spec));
        }

        public SlotAuthorityDeclaration Declaration { get; }

        public FactoryKey InitializationPolicy { get; }

        public FactoryKey ConfigurationChangePolicy { get; }

        /// <summary>Registered migration key of a version change; default means the slot declares none (P-032).</summary>
        public FactoryKey VersionChangePolicy { get; }

        public SlotId SlotId => Declaration.SlotId;

        public OwnerId Owner => Declaration.Owner;

        public SchemaRef Schema => Declaration.Schema;

        public LastSupportPolicy LastSupport => Declaration.LastSupport;

        public SlotAuthorityOptions Options => Declaration.Options;

        public bool HasInitializationPolicy => !InitializationPolicy.RegistrationKey.IsDefault;

        public bool HasConfigurationChangePolicy => !ConfigurationChangePolicy.RegistrationKey.IsDefault;

        public bool HasVersionChangePolicy => !VersionChangePolicy.RegistrationKey.IsDefault;

        public bool HasTransferPolicy => Declaration.HasTransferPolicy;

        public override string ToString() => SlotId.ToString() + "@" + Owner.ToString();

        private static FactoryKey FirstMigrationKeyOf(StateSlotSpec spec)
        {
            if (!spec.VersionChangePolicy.RegistrationKey.IsDefault)
            {
                return spec.VersionChangePolicy;
            }

            for (int i = 0; i < spec.MigrationKeys.Count; i++)
            {
                if (!spec.MigrationKeys[i].RegistrationKey.IsDefault)
                {
                    return spec.MigrationKeys[i];
                }
            }

            return default(FactoryKey);
        }
    }

    /// <summary>
    /// The one mapping from a declared last-support policy to the generated slot options, so the ownership validator
    /// and this folder read the same policy (P-032, P-033). `ResetPermitted` stays false here: only a manifest that
    /// explicitly supports a reset records the reason that permits one.
    /// </summary>
    public static class SlotAuthorityOptionsFactory
    {
        public static SlotAuthorityOptions ForLastSupport(LastSupportPolicy lastSupport)
        {
            switch (lastSupport)
            {
                case LastSupportPolicy.PreserveDormant:
                    return SlotAuthorityOptions.Dormant();
                case LastSupportPolicy.RemoveDerived:
                    return SlotAuthorityOptions.DerivedData();
                default:
                    return SlotAuthorityOptions.Durable();
            }
        }

        /// <summary>
        /// The same options with an explicit manifest-supported reset. The reason is part of the declaration: a
        /// permitted reset without a recorded reason is rejected by `SlotPolicyValidator` (P-032).
        /// </summary>
        public static SlotAuthorityOptions WithReset(LastSupportPolicy lastSupport, string? reason)
        {
            SlotAuthorityOptions baseOptions = ForLastSupport(lastSupport);
            return SlotAuthorityOptions.Resettable(
                reason ?? string.Empty,
                baseOptions.PreserveDormantPermitted,
                baseOptions.DisposableDerived);
        }
    }

    /// <summary>
    /// Every state slot of one catalog revision, keyed by slot identity, with the owners the revision knows. This is
    /// the declaration set the executor reads; it is built once per revision and never modified afterwards.
    /// </summary>
    public sealed class SlotStatePolicySet
    {
        private readonly List<SlotStatePolicy> policies;
        private readonly List<SlotAuthorityDeclaration> declarations;
        private readonly List<OwnerId> owners;

        public SlotStatePolicySet(IReadOnlyList<SlotStatePolicy>? policies)
        {
            this.policies = policies == null ? new List<SlotStatePolicy>() : new List<SlotStatePolicy>(policies);
            var declarations = new List<SlotAuthorityDeclaration>(this.policies.Count);
            for (int i = 0; i < this.policies.Count; i++)
            {
                declarations.Add(this.policies[i].Declaration);
            }

            this.declarations = declarations;
            owners = new List<OwnerId>();
            for (int i = 0; i < this.policies.Count; i++)
            {
                AddOwner(this.policies[i].Owner);
            }
        }

        public int Count => policies.Count;

        public IReadOnlyList<SlotStatePolicy> Policies => policies;

        /// <summary>Owners this revision declares, in canonical ascending identity order (P-008).</summary>
        public IReadOnlyList<OwnerId> Owners => owners;

        /// <summary>The declarations alone, for the ownership validators (P-032, P-034).</summary>
        public IReadOnlyList<SlotAuthorityDeclaration> Declarations => declarations;

        /// <summary>
        /// Builds the set from one catalog revision's declared slots. A slot declared twice with different owners or
        /// schemas is `OwnershipConflict`: one slot has one declaration per revision (P-032, P-034).
        /// </summary>
        public static bool TryBuild(
            IReadOnlyList<StateSlotSpec>? specs,
            out SlotStatePolicySet? set,
            out DiagnosticCode code,
            out string detail)
        {
            set = null;
            code = DiagnosticCode.None;
            detail = string.Empty;
            var built = new List<SlotStatePolicy>();
            if (specs != null)
            {
                for (int i = 0; i < specs.Count; i++)
                {
                    StateSlotSpec spec = specs[i];
                    if (spec == null)
                    {
                        continue;
                    }

                    bool merged = false;
                    for (int j = 0; j < built.Count; j++)
                    {
                        if (!built[j].SlotId.Equals(spec.SlotId))
                        {
                            continue;
                        }

                        if (built[j].Owner.Equals(spec.Owner) && built[j].Schema.Equals(spec.Schema))
                        {
                            merged = true;
                            break;
                        }

                        code = DiagnosticCode.OwnershipConflict;
                        detail = "state slot " + spec.SlotId.ToString()
                            + " is declared twice with different owners or schemas; one slot has one declaration per"
                            + " catalog revision (P-032, P-034).";
                        return false;
                    }

                    if (!merged)
                    {
                        built.Add(SlotStatePolicy.FromSpec(spec));
                    }
                }
            }

            set = new SlotStatePolicySet(built);
            return true;
        }

        /// <summary>Builds the set from the union of every mounted manifest's declared slots (P-009).</summary>
        public static bool TryBuildFromManifests(
            IReadOnlyList<PluginManifest>? manifests,
            out SlotStatePolicySet? set,
            out DiagnosticCode code,
            out string detail)
        {
            var specs = new List<StateSlotSpec>();
            if (manifests != null)
            {
                for (int i = 0; i < manifests.Count; i++)
                {
                    PluginManifest manifest = manifests[i];
                    if (manifest == null)
                    {
                        continue;
                    }

                    for (int s = 0; s < manifest.StateSlots.Count; s++)
                    {
                        specs.Add(manifest.StateSlots[s]);
                    }
                }
            }

            return TryBuild(specs, out set, out code, out detail);
        }

        public bool TryFind(SlotId slotId, out SlotStatePolicy? policy)
        {
            for (int i = 0; i < policies.Count; i++)
            {
                if (policies[i].SlotId.Equals(slotId))
                {
                    policy = policies[i];
                    return true;
                }
            }

            policy = null;
            return false;
        }

        /// <summary>
        /// Finds the declaration of one live slot key and checks that its owner agrees with the key (P-034). A key
        /// whose owner differs from the declaration is an ownership conflict, not a missing policy.
        /// </summary>
        public bool TryFind(
            StateSlotKey key,
            out SlotStatePolicy? policy,
            out DiagnosticCode code,
            out string detail)
        {
            policy = null;
            for (int i = 0; i < policies.Count; i++)
            {
                if (!policies[i].SlotId.Equals(key.Slot))
                {
                    continue;
                }

                if (!policies[i].Owner.Equals(key.Owner))
                {
                    code = DiagnosticCode.OwnershipConflict;
                    detail = "state slot key " + key.ToString() + " names owner " + key.Owner.ToString()
                        + " but the declaration owns it as " + policies[i].Owner.ToString() + " (P-034).";
                    return false;
                }

                policy = policies[i];
                code = DiagnosticCode.None;
                detail = string.Empty;
                return true;
            }

            code = DiagnosticCode.MissingDependency;
            detail = "no slot declaration covers " + key.ToString()
                + "; a missing compatible policy is a validation error, not implicit initialization (P-032).";
            return false;
        }

        /// <summary>Validates every declared policy set with no executor involved (P-028, P-032).</summary>
        public bool TryValidateDeclarations(out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            for (int i = 0; i < policies.Count; i++)
            {
                SlotPolicyResult result = SlotPolicyValidator.ValidateDeclaration(policies[i].Declaration);
                if (!result.Succeeded)
                {
                    code = result.Code;
                    detail = result.Detail;
                    return false;
                }
            }

            return true;
        }

        private void AddOwner(OwnerId owner)
        {
            for (int i = 0; i < owners.Count; i++)
            {
                if (owners[i].Equals(owner))
                {
                    return;
                }
            }

            owners.Add(owner);
            owners.Sort(CompareOwners);
        }

        private static int CompareOwners(OwnerId left, OwnerId right)
            => left.Value.CompareTo(right.Value);
    }
}
