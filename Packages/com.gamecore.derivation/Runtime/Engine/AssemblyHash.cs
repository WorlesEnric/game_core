// GameCore.Derivation — canonical recipe/result hashes (GC-006).
//
// P-024 caches derived variants by "recipe revision, scope inheritance fingerprint, mode, and catalog hash", so
// the derivation result needs one canonical, provider-independent hash of what a target's assembly *is*: its base
// recipe plus the values of its effective slots. P-027's plan hash excludes timestamps and object addresses; this
// hash is the same kind of semantic input.
//
// Provider identities are deliberately not part of the recipe hash: two different providers that derive the same
// effective values produce the same assembly, and a source swap that changes nothing must not invalidate a cached
// variant. Support identities remain separately reconstructable through the P-017/P-026 provenance.
#nullable enable
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Canonical hashes of one target assembly and of one whole derivation result.</summary>
    public static class AssemblyHash
    {
        /// <summary>
        /// Hash of the base recipe plus the effective slot values in canonical (capability, version, slot) order.
        /// Two derivations with equal hashes have equal effective values for this target.
        /// </summary>
        public static ContentHash Compute(DerivationTarget target, IReadOnlyList<EffectiveSlot> slots)
        {
            StringBuilder text = new StringBuilder();
            text.Append("target=").Append(target.Target.ToString()).Append('\n');
            text.Append("scope=").Append(target.Scope.ToString()).Append('\n');
            text.Append("recipe=").Append(target.Descriptor.Recipe.ToString()).Append('\n');
            for (int i = 0; i < slots.Count; i++)
            {
                EffectiveSlot slot = slots[i];
                text.Append("slot=").Append(slot.Capability.ToString())
                    .Append('/').Append(slot.Version).Append('/').Append(slot.Slot)
                    .Append('/').Append(slot.Schema.ToString())
                    .Append('/').Append(slot.Policy.ToString()).Append('\n');
                for (int v = 0; v < slot.Values.Count; v++)
                {
                    text.Append("  value=");
                    PayloadCodec.AppendCanonical(text, slot.Values[v]);
                    text.Append('\n');
                }
            }

            return ContentHash.Compute(Encoding.UTF8.GetBytes(text.ToString()));
        }

        /// <summary>
        /// Hash of the whole accepted derivation: the identity of every target and its recipe hash in canonical
        /// order. Replaying the same input twice produces the same hash, so a mount operation cannot
        /// accidentally create duplicate assembly (TEST-004).
        /// </summary>
        public static ContentHash ComputeResult(
            DerivationSnapshot snapshot,
            IReadOnlyList<TargetAssembly> assemblies)
        {
            StringBuilder text = new StringBuilder();
            text.Append("snapshot=").Append(snapshot.SnapshotHash.ToHex()).Append('\n');
            for (int i = 0; i < assemblies.Count; i++)
            {
                text.Append("assembly=").Append(assemblies[i].Target.ToString())
                    .Append('/').Append(assemblies[i].RecipeHash.ToHex()).Append('\n');
            }

            return ContentHash.Compute(Encoding.UTF8.GetBytes(text.ToString()));
        }
    }
}
