// GameCore.Gameplay.Rewards — the O-03/O-07 payloads of the reward installation (GC-024).
//
// Normative sources: 00 O-03 ("mount a precompiled plugin instance at a scope") and O-07 ("unmount an
// installation"), P-020 (a mount's declared configuration hash is the canonical hash of the effective
// configuration, which is what the composition applier recomputes) and P-010 (an installation lives in exactly one
// scope).
//
// The shape mirrors the two shipped gameplay payload builders: `NarrativeMounts.Mount`
// (Packages/com.gamecore.gameplay.narrative/Fixtures/Runtime/NarrativeScenario.cs:556-594),
// `CardTablePayloads.Mount`/`Unmount` (Packages/com.gamecore.gameplay.cards/Runtime/CardMarketComposition.cs:165,
// :241) and `NarrativeRegistration.Messages()` for the command plane
// (Packages/com.gamecore.gameplay.narrative/Fixtures/Runtime/NarrativeRegistration.cs:36-111). The frozen
// surface's `Mount(PluginInstanceId, ScopeId)` resolves this package's own declaration, so a caller cannot mount an
// installation whose manifest is not the one this package declares.
#nullable enable
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Unity.Runtime.Messages;

namespace GameCore.Gameplay.Rewards
{
    /// <summary>Mount and unmount payloads of the reward installation, in the shape the control lane's applier validates.</summary>
    public static class RewardsMounts
    {
        /// <summary>
        /// O-03: mount this package's declared installation at one scope. The declared configuration hash is the
        /// canonical hash of the effective configuration — this manifest's schema defaults (none) composed with the
        /// instance's empty local patch — exactly as the applier recomputes it (P-020).
        /// </summary>
        public static CompositionEditPayload Mount(PluginInstanceId instance, ScopeId scope) =>
            Mount(RewardsDeclaration.Manifest(), instance, scope, ConfigDocument.Empty);

        /// <summary>O-03 with an explicit manifest and its schema defaults, for a caller that carries its own table.</summary>
        public static CompositionEditPayload Mount(
            PluginManifest manifest,
            PluginInstanceId instance,
            ScopeId scope,
            ConfigDocument? schemaDefaults)
        {
            if (manifest == null)
            {
                throw new System.ArgumentNullException(nameof(manifest));
            }

            ConfigDocument local = ConfigDocument.Empty;
            ConfigDocument effective = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(
                    ConfigLayerOrigin.SchemaDefaults,
                    manifest.ConfigSchema.Id.Value,
                    schemaDefaults ?? ConfigDocument.Empty),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, instance.Value, local),
            }).Value;

            return new CompositionEditPayload(
                CompositionEditSubject.InstallMount,
                scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                manifest.PluginTypeId,
                instance,
                DefinitionRevision.First,
                ConfigDocumentCodec.HashOf(effective),
                local,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>
        /// O-07: unmount one installation. The publication carries its removal; the P-048 teardown order that
        /// decides whether the removal can settle is the installation's own (`RewardsInstallation.TryUnmount`).
        /// </summary>
        public static CompositionEditPayload Unmount(PluginInstanceId instance) =>
            new CompositionEditPayload(
                CompositionEditSubject.InstallUnmount,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                instance,
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);

        /// <summary>
        /// The world-message-plane registration this package declares: its own command route, its ingress lane and
        /// the one consumer stage, in the generated shape `NarrativeRegistration.Messages()` uses
        /// (Packages/com.gamecore.gameplay.narrative/Fixtures/Runtime/NarrativeRegistration.cs:36-111). 07 s5's
        /// "missing command endpoint" clause has no kernel rule of its own — a manifest is never required to
        /// declare a route — so the only shipped statement about a command endpoint is this registration's own:
        /// a route whose ingress buffer the registration does not declare is
        /// `DiagnosticCode.MissingDependency` (`MessagePlaneRegistration.TryValidate`,
        /// Packages/com.gamecore.unity.runtime/Runtime/Messages/WorldMessagePlane.cs:92-155). A caller that wants
        /// to assert the clause registers this shape and asks it, or drops the lane to observe the refusal.
        /// </summary>
        public static MessagePlaneRegistration Messages()
        {
            var route = new CommandRoute(
                RewardsKeys.StatusRoute,
                RewardsKeys.OutboxOwner,
                RewardsKeys.StatusSchema,
                RewardsKeys.EnqueueStage,
                RewardsKeys.EnqueueStage,
                RewardsKeys.StatusLane,
                RewardsKeys.HostIngressProducer,
                RewardsKeys.LaneCapacity,
                false);

            var buffers = new List<MessageBufferDescriptor>
            {
                new MessageBufferDescriptor(
                    RewardsKeys.StatusLane,
                    RewardsKeys.StatusSchema,
                    new List<FactoryKey> { RewardsKeys.HostIngressProducer },
                    RewardsKeys.OutboxOwner,
                    RewardsKeys.EnqueueStage,
                    RewardsKeys.EnqueueStage,
                    RewardsKeys.StatusOrderKey,
                    BufferLifetime.Step,
                    RewardsKeys.LaneCapacity,
                    RewardsKeys.LaneByteCapacity,
                    BufferOverflowPolicy.RejectBeforeMutation,
                    BufferCancellationPolicy.Drain),
            };

            return new MessagePlaneRegistration(
                new List<CommandRoute> { route },
                buffers,
                null,
                RewardsKeys.LaneCapacity,
                RewardsKeys.LaneCapacity,
                RewardsKeys.LaneCapacity,
                RewardsKeys.LaneCapacity,
                2);
        }
    }
}
