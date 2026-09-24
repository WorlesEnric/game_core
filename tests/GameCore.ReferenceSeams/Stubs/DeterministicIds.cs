// Test-only deterministic stub (namespace GameCore.TestFixtures) for the W0 reference seam.
// No randomness, no clock, no threads: every sequence is reproducible from the constructor argument.
#nullable enable
using System;
using GameCore.Contracts;

namespace GameCore.TestFixtures
{
    /// <summary>
    /// Deterministic 128-bit identity generator. The high word is a fixed category salt and the low word a
    /// strictly increasing sequence, so repeated runs produce byte-identical fixtures (P-008, TEST-022).
    /// </summary>
    public sealed class DeterministicIds
    {
        private readonly ulong salt;
        private ulong next;

        public DeterministicIds(ulong salt)
            : this(salt, 0UL)
        {
        }

        public DeterministicIds(ulong salt, ulong start)
        {
            this.salt = salt;
            next = start;
        }

        public ulong Salt => salt;

        public ulong Sequence => next;

        public Id128 NextId()
        {
            next++;
            return new Id128(salt, next);
        }

        /// <summary>Generates the next raw id and wraps it in a category type.</summary>
        public T Next<T>(Func<Id128, T> wrap)
        {
            if (wrap == null)
            {
                throw new ArgumentNullException(nameof(wrap));
            }

            return wrap(NextId());
        }

        public WorldId NextWorldId() => new WorldId(NextId());

        public ScopeId NextScopeId() => new ScopeId(NextId());

        public TargetId NextTargetId() => new TargetId(NextId());

        public PluginTypeId NextPluginTypeId() => new PluginTypeId(NextId());

        public PluginInstanceId NextPluginInstanceId() => new PluginInstanceId(NextId());

        public CapabilityId NextCapabilityId() => new CapabilityId(NextId());

        public OwnerId NextOwnerId() => new OwnerId(NextId());

        public StageId NextStageId() => new StageId(NextId());

        public RuleId NextRuleId() => new RuleId(NextId());

        public SlotId NextSlotId() => new SlotId(NextId());

        public OperationId NextOperationId(WorldId world, Id128 issuerId)
        {
            next++;
            return new OperationId(world, issuerId, next);
        }
    }
}
