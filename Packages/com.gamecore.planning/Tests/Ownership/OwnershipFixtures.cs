// GC-007 pure test fixtures: one known ownership/slot descriptor set.
//
// Wave 2 tasks must not compile against each other's modules, so these tests declare the minimum stable identity
// fixtures they need instead of importing another task's catalog (09 implementation guide, Wave 2 rules). The W2
// exit gate substitutes the real generated declarations.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning.Ownership;

namespace GameCore.Planning.Tests.Ownership
{
    /// <summary>Deterministic identity helper: fixed salts plus ascending low words, so fixtures never collide.</summary>
    internal sealed class OwnershipFixtureIds
    {
        internal const ulong Namespace = 0x47433030374F574EUL;

        internal static Id128 Id(ulong ordinal) => new Id128(Namespace, ordinal);

        internal static SchemaId Schema(ulong ordinal) => new SchemaId(Id(0x0100UL + ordinal));

        internal static SchemaRef SchemaRefOf(ulong ordinal, uint version = 1U)
            => new SchemaRef(Schema(ordinal), version);

        internal static SlotId Slot(ulong ordinal) => new SlotId(Id(0x0200UL + ordinal));

        internal static OwnerId Owner(ulong ordinal) => new OwnerId(Id(0x0300UL + ordinal));

        internal static TargetId Target(ulong ordinal) => new TargetId(Id(0x0400UL + ordinal));

        internal static StageId Stage(ulong ordinal) => new StageId(Id(0x0500UL + ordinal));

        internal static RuleId Rule(ulong ordinal) => new RuleId(Id(0x0600UL + ordinal));

        internal static ProviderInstallationId Provider(ulong ordinal) => new ProviderInstallationId(Id(0x0700UL + ordinal));

        internal static CapabilityId Capability(ulong ordinal) => new CapabilityId(Id(0x0800UL + ordinal));

        internal static FactoryKey Key(ulong ordinal) => new FactoryKey(Id(0x0900UL + ordinal), 1U);

        internal static FactoryKey Migration(ulong ordinal) => new FactoryKey(Id(0x0A00UL + ordinal), 2U);

        internal static Id128 Partition(ulong ordinal) => Id(0x0B00UL + ordinal);
    }

    /// <summary>One access-set builder, so a test states exactly the schema/mode/partition it means.</summary>
    internal static class Access
    {
        internal static AccessSet Writer(params AccessDeclaration[] declarations) => new AccessSet(declarations);

        internal static AccessSet Whole(SchemaRef schema, AccessMode mode = AccessMode.Write)
            => new AccessSet(new[] { new AccessDeclaration(schema, mode, Id128.Zero) });

        internal static AccessSet Partitioned(SchemaRef schema, Id128 partition, AccessMode mode = AccessMode.Write)
            => new AccessSet(new[] { new AccessDeclaration(schema, mode, partition) });

        internal static AccessSet ReadOnly(SchemaRef schema)
            => new AccessSet(new[] { new AccessDeclaration(schema, AccessMode.Read, Id128.Zero) });
    }
}
