// GameCore.Planning tests — the GC-015 state-policy fixture.
//
// Stable literals in this test's own namespace word, one helper per declaration shape, so every assertion names the
// same identities the production code reads. Nothing here re-implements a policy: the helpers only build the
// declarations P-032 requires, and the tests then assert on `StatePolicyExecutor`'s and `SlotLayoutGenerator`'s own
// verdicts.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning.Ownership;
using GameCore.Planning.StatePolicies;

namespace GameCore.Planning.Tests
{
    /// <summary>Stable identities of the GC-015 fixture.</summary>
    internal static class StatePolicyFixtureIds
    {
        internal const ulong Namespace = 0x4743303135504F4CUL;

        internal static Id128 Id(ulong ordinal) => new Id128(Namespace, ordinal);

        internal static SlotId Slot(ulong ordinal) => new SlotId(Id(0x0100UL + ordinal));

        internal static OwnerId Owner(ulong ordinal) => new OwnerId(Id(0x0200UL + ordinal));

        internal static OwnerId QuestOwner => Owner(1UL);

        internal static OwnerId GateOwner => Owner(2UL);

        internal static OwnerId TransferOwner => Owner(3UL);

        internal static TargetId Target(ulong ordinal) => new TargetId(Id(0x0300UL + ordinal));

        internal static FactoryKey Key(ulong ordinal) => new FactoryKey(Id(0x0400UL + ordinal), 1U);

        internal static StateSlotKey SlotKey(ulong target, SlotId slot, OwnerId owner)
            => new StateSlotKey(Target(target), owner, slot);

        internal static FactoryKey Layout(ulong ordinal) => new FactoryKey(Id(0x0500UL + ordinal), 1U);

        internal static ProviderInstallationId Provider(ulong ordinal) => new ProviderInstallationId(Id(0x0700UL + ordinal));

        internal static RuleId Rule(ulong ordinal) => new RuleId(Id(0x0800UL + ordinal));

        internal static CapabilityId Capability(ulong ordinal) => new CapabilityId(Id(0x0900UL + ordinal));

        internal static SchemaRef Schema(ulong ordinal, uint version = 1U)
            => new SchemaRef(new SchemaId(Id(0x0600UL + ordinal)), version);

        /// <summary>The quest domain: durable state that must survive a lost support (P-032).</summary>
        internal static SchemaRef QuestSchema => Schema(1UL, 2U);

        /// <summary>The trail domain: disposable derived data (P-032 `RemoveDerived`).</summary>
        internal static SchemaRef TrailSchema => Schema(2UL);

        internal static SlotId QuestValueSlot => Slot(1UL);

        internal static SlotId QuestVersionSlot => Slot(2UL);

        internal static SlotId TrailSlot => Slot(3UL);

        internal static SlotId FactSlot => Slot(4UL);

        internal static readonly FactoryKey QuestMigration = Key(1UL);

        internal static readonly FactoryKey QuestInit = Key(2UL);

        internal static readonly FactoryKey QuestConfigChange = Key(3UL);

        internal static readonly FactoryKey QuestTransfer = Key(4UL);
    }

    /// <summary>A registered migration with an explicit version pair and a pure fail-on-negative transform.</summary>
    internal sealed class PolicyMigration : ISlotMigration
    {
        private readonly int delta;
        private readonly bool refuseNegative;

        internal PolicyMigration(FactoryKey key, uint fromVersion, uint toVersion, int delta, bool refuseNegative)
        {
            Key = key;
            FromVersion = fromVersion;
            ToVersion = toVersion;
            this.delta = delta;
            this.refuseNegative = refuseNegative;
        }

        public FactoryKey Key { get; }

        public uint FromVersion { get; }

        public uint ToVersion { get; }

        internal int Invocations { get; private set; }

        public bool TryMigrate(int source, out int migrated)
        {
            Invocations++;
            if (refuseNegative && source < 0)
            {
                migrated = source;
                return false;
            }

            migrated = source + delta;
            return true;
        }
    }

    /// <summary>Declaration builders of the GC-015 fixture.</summary>
    internal static class StatePoliciesFixture
    {
        /// <summary>A durable slot: `PreserveDormant` last support, a registered version change (P-032).</summary>
        internal static SlotStatePolicy Durable(
            SlotId slot,
            OwnerId owner,
            SchemaRef schema,
            FactoryKey layoutKey,
            IReadOnlyList<FieldOwnership>? fields,
            FactoryKey migrationKey,
            bool resetPermitted = false,
            string resetReason = "")
        {
            var declaration = new SlotAuthorityDeclaration(
                slot,
                owner,
                schema,
                layoutKey,
                fields,
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                migrationKey.RegistrationKey.IsDefault ? null : new List<FactoryKey> { migrationKey },
                resetPermitted
                    ? SlotAuthorityOptions.Resettable(resetReason, true, false)
                    : SlotAuthorityOptions.Dormant());
            return new SlotStatePolicy(declaration, StatePolicyFixtureIds.QuestInit, StatePolicyFixtureIds.QuestConfigChange, migrationKey);
        }

        /// <summary>A disposable derived slot: `RemoveDerived` last support, no migration (P-032, P-033).</summary>
        internal static SlotStatePolicy Derived(SlotId slot, OwnerId owner, SchemaRef schema, FactoryKey layoutKey)
        {
            var declaration = new SlotAuthorityDeclaration(
                slot,
                owner,
                schema,
                layoutKey,
                new List<FieldOwnership> { new FieldOwnership(schema, StatePolicyFixtureIds.Key(9UL)) },
                LastSupportPolicy.RemoveDerived,
                default(FactoryKey),
                null,
                SlotAuthorityOptions.DerivedData());
            return new SlotStatePolicy(declaration, StatePolicyFixtureIds.QuestInit, StatePolicyFixtureIds.QuestConfigChange, default(FactoryKey));
        }

        /// <summary>A slot whose last support transfers to a named available owner (P-032).</summary>
        internal static SlotStatePolicy Transferable(
            SlotId slot,
            OwnerId owner,
            SchemaRef schema,
            FactoryKey layoutKey,
            FactoryKey transferPolicy)
        {
            var declaration = new SlotAuthorityDeclaration(
                slot,
                owner,
                schema,
                layoutKey,
                new List<FieldOwnership> { new FieldOwnership(schema, StatePolicyFixtureIds.Key(9UL)) },
                LastSupportPolicy.TransferTo,
                transferPolicy,
                null,
                SlotAuthorityOptions.Durable());
            return new SlotStatePolicy(declaration, StatePolicyFixtureIds.QuestInit, StatePolicyFixtureIds.QuestConfigChange, default(FactoryKey));
        }

        /// <summary>The fixture's migration registry: the quest key moves 1 to 2 by adding ten.</summary>
        internal static MigrationRegistry Migrations(out PolicyMigration migration)
        {
            migration = new PolicyMigration(
                StatePolicyFixtureIds.QuestMigration, 1U, StatePolicyFixtureIds.QuestSchema.Version, 10, true);
            return new MigrationRegistry(new List<ISlotMigration> { migration });
        }

        /// <summary>The fixture's initialization values: the quest init policy writes seven, not zero (P-032).</summary>
        internal static InitializationPolicyRegistry InitialValues()
        {
            var registry = new InitializationPolicyRegistry();
            registry.Register(StatePolicyFixtureIds.QuestInit, StatePolicyFixtureIds.QuestSchema, 7);
            registry.Register(StatePolicyFixtureIds.QuestInit, StatePolicyFixtureIds.TrailSchema, 3);
            return registry;
        }

        /// <summary>One state slot specification, so the layout generator and the policy set read the same input.</summary>
        internal static StateSlotSpec Spec(
            SlotId slot,
            OwnerId owner,
            SchemaRef schema,
            FactoryKey layoutKey,
            IReadOnlyList<FieldOwnership>? fields,
            LastSupportPolicy lastSupport,
            FactoryKey versionChangePolicy,
            FactoryKey transferPolicy)
        {
            return new StateSlotSpec(
                slot,
                owner,
                schema,
                layoutKey,
                fields,
                StatePolicyFixtureIds.QuestInit,
                StatePolicyFixtureIds.QuestConfigChange,
                versionChangePolicy,
                lastSupport,
                transferPolicy,
                versionChangePolicy.RegistrationKey.IsDefault ? null : new List<FactoryKey> { versionChangePolicy });
        }

        /// <summary>The quest value field of the fixture's shared quest layout.</summary>
        internal static IReadOnlyList<FieldOwnership> QuestFields(params ulong[] ordinals)
        {
            var fields = new List<FieldOwnership>(ordinals.Length);
            for (int i = 0; i < ordinals.Length; i++)
            {
                fields.Add(new FieldOwnership(
                    StatePolicyFixtureIds.QuestSchema,
                    StatePolicyFixtureIds.Key(ordinals[i]).RegistrationKey));
            }

            return fields;
        }

        internal static LiveSlotState Live(SlotId slot, uint version, int value)
            => new LiveSlotState(
                new StateSlotKey(StatePolicyFixtureIds.Target(1UL), StatePolicyFixtureIds.QuestOwner, slot),
                version,
                value);

        internal static StateSlotKey Key(SlotId slot, OwnerId owner)
            => new StateSlotKey(StatePolicyFixtureIds.Target(1UL), owner, slot);

        internal static StateSlotKey Key(ulong target, SlotId slot, OwnerId owner)
            => new StateSlotKey(StatePolicyFixtureIds.Target(target), owner, slot);

        internal static SlotStatePolicySet Set(params SlotStatePolicy[] policies)
            => new SlotStatePolicySet(policies);

        internal static SlotStatePolicySet SetOf(IReadOnlyList<SlotStatePolicy> policies)
            => new SlotStatePolicySet(policies);
    }
}
