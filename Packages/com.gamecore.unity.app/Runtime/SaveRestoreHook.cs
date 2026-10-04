// GameCore.Unity.App - the SADR-010 restore seam filled with the SADR-012 production restore builder.
//
// `IGameApplicationRestoreHook` was left open by P0.4 for this packet: a game that declares this hook gets a
// `ProductionRestoreTargetBuilder` from `GameApplicationRoot.TryCreateRestoreTargetBuilder`, composed through the root's
// own composition path (`SaveRestoreComposer`). `SaveService` builds the same pair directly; the hook is for a caller
// that runs `CheckpointRestoreExecutor` itself (an operator recovery tool, the Studio admission path).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Time;
using GameCore.Unity.Runtime.Delivery;
using GameCore.Unity.Runtime.Persistence;

namespace GameCore.Unity.App
{
    /// <summary>
    /// Supplies a production restore builder for a booted root (SADR-010, SADR-012). Each builder gets fresh
    /// <see cref="ProductionWorldModules"/> with the declared clocks, wake bound and next-step buffers, so the restored
    /// world's clocks, RNG, next-step messages, command payloads and outbox start from the checkpoint alone.
    /// </summary>
    public sealed class SaveRestoreHook : IGameApplicationRestoreHook
    {
        private readonly IReadOnlyList<PluginClockSpec> declaredClocks;
        private readonly int maxWakes;
        private readonly IReadOnlyList<BufferId> nextStepBuffers;
        private readonly GameApplicationBootOptions? bootOptions;
        private readonly Func<GameApplicationRoot, ProductionWorldModules, WorldDeliveryOwner?>? deliveryFactory;

        public SaveRestoreHook(
            IReadOnlyList<PluginClockSpec>? declaredClocks = null,
            int maxWakes = 64,
            IReadOnlyList<BufferId>? nextStepBuffers = null,
            GameApplicationBootOptions? bootOptions = null,
            Func<GameApplicationRoot, ProductionWorldModules, WorldDeliveryOwner?>? deliveryFactory = null)
        {
            if (maxWakes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWakes), "The wake bound is positive (P-022).");
            }

            this.declaredClocks = declaredClocks ?? Array.Empty<PluginClockSpec>();
            this.maxWakes = maxWakes;
            this.nextStepBuffers = nextStepBuffers ?? Array.Empty<BufferId>();
            this.bootOptions = bootOptions;
            this.deliveryFactory = deliveryFactory;
        }

        /// <summary>The composer of the last builder this hook created, for its typed composition failure.</summary>
        public SaveRestoreComposer? LastComposer { get; private set; }

        public IRestoreTargetBuilder CreateRestoreTargetBuilder(GameApplicationRoot root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            IReadOnlyList<PluginClockSpec> clocks = declaredClocks;
            int wakes = maxWakes;
            IReadOnlyList<BufferId> buffers = nextStepBuffers;
            var composer = new SaveRestoreComposer(
                root.Definition,
                bootOptions,
                () => new ProductionWorldModules(clocks, wakes, buffers),
                deliveryFactory);
            LastComposer = composer;
            return new ProductionRestoreTargetBuilder(composer, maxWakes);
        }
    }
}
