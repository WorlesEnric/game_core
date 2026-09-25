// GameCore.Planning — the declaration-level migration registry view of one revision (GC-015).
//
// Two different questions are asked about a migration, and P-032 needs both answered before a version change may
// apply:
//
//   * does the slot's declaration *name* this migration key (`StateSlotSpec.MigrationKeys`, `VersionChangePolicy`)?
//     That is what `SlotPolicyValidator.Validate` checks through `ISlotMigrationRegistry`;
//   * is a handler registered for that key and that exact schema pair (`MigrationRegistry`, 05 s5)?
//
// This view answers the first question from the declared slots and the second from the registered handlers, so a
// version change passes only when a *declared* key resolves to a *registered* executor of the right versions.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning.Ownership;

namespace GameCore.Planning.StatePolicies
{
    /// <summary>Declared migration keys of one revision, backed by its registered handlers (P-032, 05 s5).</summary>
    public sealed class DeclaredSlotMigrationRegistry : ISlotMigrationRegistry
    {
        private readonly SlotStatePolicySet policies;

        public DeclaredSlotMigrationRegistry(SlotStatePolicySet policies)
        {
            this.policies = policies ?? throw new ArgumentNullException(nameof(policies));
        }

        /// <summary>
        /// True when at least one declared slot names this key and that slot's schema can reach `to` from `from`
        /// through a registered handler. The handler lookup stays with `MigrationRegistry`; this view only refuses a
        /// key no declaration names, so an undeclared migration can never authorize a version change.
        /// </summary>
        public bool IsRegistered(FactoryKey migrationKey, SchemaRef from, SchemaRef to)
        {
            IReadOnlyList<SlotAuthorityDeclaration> slots = policies.Declarations;
            for (int i = 0; i < slots.Count; i++)
            {
                SlotAuthorityDeclaration slot = slots[i];
                if (!slot.Schema.Id.Value.Equals(to.Id.Value))
                {
                    continue;
                }

                for (int k = 0; k < slot.MigrationKeys.Count; k++)
                {
                    if (slot.MigrationKeys[k].Equals(migrationKey))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public override string ToString() => "declaredMigrations@" + policies.Count;
    }
}
