// GameCore.Gameplay.Narrative.Fixtures — the narrative world's registration: plane, readers, systems and request.
//
// The message plane is where a typed command enters and a committed event becomes observable (P-042, P-045). Its
// route and lanes are generated data, not a discovery step: the route names the owner that answers a choice and the
// bounded lane it waits in, each declared buffer names exactly one producer and one consuming owner, and each lane's
// lifetime fixes when its rows must be drained (P-037, P-043).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Planning;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;

namespace GameCore.Gameplay.Narrative.Fixtures
{
    /// <summary>Generated-style plane, readers, systems and creation request of one narrative world.</summary>
    public static class NarrativeRegistration
    {
        /// <summary>World name; the host appends the session id, so every world name is an inspectable incarnation.</summary>
        public const string WorldName = "GameCoreNarrativeWorld";

        /// <summary>Row capacity of one bounded lane: the step's declared input bound (P-043).</summary>
        public const int LaneCapacity = 4;

        /// <summary>Byte capacity of one bounded lane; the largest narrative payload is twelve bytes.</summary>
        public const int LaneByteCapacity = 64;

        /// <summary>
        /// The slice's bounded lanes: the host's ingress lane plus the four declared stage-local edges of the
        /// reference graph (`input → dialogue`, `dialogue → quest`, `quest → gates`, `quest → encounters`).
        /// </summary>
        public static MessagePlaneRegistration Messages()
        {
            var route = new CommandRoute(
                NarrativeKeys.ChoiceRoute,
                NarrativeKeys.IngressOwner,
                NarrativeKeys.ChoiceCommandSchema,
                NarrativeKeys.InputStage,
                NarrativeKeys.InputStage,
                NarrativeKeys.ChoiceBuffer,
                NarrativeKeys.HostIngressProducer,
                LaneCapacity,
                false);

            var buffers = new List<MessageBufferDescriptor>
            {
                Lane(
                    NarrativeKeys.ChoiceBuffer,
                    NarrativeKeys.ChoiceCommandSchema,
                    new[] { NarrativeKeys.HostIngressProducer },
                    NarrativeKeys.IngressOwner,
                    NarrativeKeys.InputStage,
                    NarrativeKeys.InputStage,
                    NarrativeKeys.ChoiceOrderKey,
                    BufferLifetime.Step),

                Lane(
                    NarrativeKeys.ChoiceRequestBuffer,
                    NarrativeKeys.ChoiceCommandSchema,
                    new[] { NarrativeKeys.InputSystem },
                    NarrativeKeys.DialogueOwner,
                    NarrativeKeys.DialogueStage,
                    NarrativeKeys.DialogueStage,
                    NarrativeKeys.ChoiceRequestOrderKey,
                    BufferLifetime.Stage),

                Lane(
                    NarrativeKeys.FactChangeBuffer,
                    NarrativeKeys.QuestMutationSchema,
                    new[] { NarrativeKeys.DialogueSystem },
                    NarrativeKeys.QuestOwner,
                    NarrativeKeys.QuestStage,
                    NarrativeKeys.QuestStage,
                    NarrativeKeys.FactChangeOrderKey,
                    BufferLifetime.Stage),

                Lane(
                    NarrativeKeys.FactObservedBuffer,
                    NarrativeKeys.FactObservedSchema,
                    new[] { NarrativeKeys.QuestSystem },
                    NarrativeKeys.GateOwner,
                    NarrativeKeys.GateStage,
                    NarrativeKeys.GateStage,
                    NarrativeKeys.FactObservedOrderKey,
                    BufferLifetime.Stage),

                Lane(
                    NarrativeKeys.EncounterObservedBuffer,
                    NarrativeKeys.FactObservedSchema,
                    new[] { NarrativeKeys.QuestSystem },
                    NarrativeKeys.EncounterOwner,
                    NarrativeKeys.EncounterStage,
                    NarrativeKeys.EncounterStage,
                    NarrativeKeys.EncounterObservedOrderKey,
                    BufferLifetime.Stage),
            };

            return new MessagePlaneRegistration(
                new List<CommandRoute> { route },
                buffers,
                null,
                maxPendingRequests: 8,
                maxRetainedResults: 8,
                maxRetainedEvents: 8,
                maxEventsPerStep: 4,
                nextStepCapacity: 2);
        }

        /// <summary>The generated typed readers of the narrative plane (04 section 8).</summary>
        public static CommandPayloadReaders Readers()
        {
            var readers = new CommandPayloadReaders();
            if (!readers.TryBind(new NarrativeChoicePayloadReader(), out string failure))
            {
                throw new InvalidOperationException("the choice reader registration failed: " + failure);
            }

            if (!readers.TryBind(new NarrativeMutationPayloadReader(), out failure))
            {
                throw new InvalidOperationException("the mutation reader registration failed: " + failure);
            }

            if (!readers.TryBind(new NarrativeObservationPayloadReader(), out failure))
            {
                throw new InvalidOperationException("the observation reader registration failed: " + failure);
            }

            return readers;
        }

        /// <summary>The six stages of the reference graph, one system each, in the compiled order (P-039, P-040).</summary>
        public static IReadOnlyList<SystemRegistration> Systems()
        {
            return new List<SystemRegistration>
            {
                new ManagedSystemRegistration<NarrativeInputSystem>(
                    NarrativeKeys.InputSystem, NarrativeKeys.InputStage, "NarrativeInputSystem"),
                new ManagedSystemRegistration<NarrativeDialogueSystem>(
                    NarrativeKeys.DialogueSystem, NarrativeKeys.DialogueStage, "NarrativeDialogueSystem"),
                new ManagedSystemRegistration<NarrativeQuestSystem>(
                    NarrativeKeys.QuestSystem, NarrativeKeys.QuestStage, "NarrativeQuestSystem"),
                new ManagedSystemRegistration<NarrativeGateSystem>(
                    NarrativeKeys.GateSystem, NarrativeKeys.GateStage, "NarrativeGateSystem"),
                new ManagedSystemRegistration<NarrativeEncounterSystem>(
                    NarrativeKeys.EncounterSystem, NarrativeKeys.EncounterStage, "NarrativeEncounterSystem"),
                new ManagedSystemRegistration<NarrativeOutputSystem>(
                    NarrativeKeys.OutputSystem, NarrativeKeys.OutputStage, "NarrativeOutputSystem"),
            };
        }

        /// <summary>
        /// The world's composition root: the compiled schedule the adapter installed becomes its initial step plan,
        /// and GC-008's publisher rebinds the group from the same compiled order at every publication.
        /// </summary>
        public static UnityWorldRegistration Create(
            ScheduleAdaptation adaptation,
            IReadOnlyList<SystemRegistration> systems)
        {
            if (adaptation == null)
            {
                throw new ArgumentNullException(nameof(adaptation));
            }

            if (!adaptation.Succeeded || adaptation.StepPlan == null)
            {
                throw new ArgumentException(
                    "a rejected schedule adaptation has no dispatch table to register: " + adaptation.Explain(),
                    nameof(adaptation));
            }

            return new UnityWorldRegistration(
                WorldName,
                adaptation.Stages,
                systems,
                GuardedDispatchPlan.Empty,
                adaptation.StepPlan,
                GuardedDispatchPlan.Empty,
                null,
                Messages(),
                Readers());
        }

        /// <summary>Creation request of one command-driven narrative world (O-01, P-035, P-036).</summary>
        public static WorldCreateRequest CommandDrivenRequest(WorldId world, OperationId operation, ContentHash catalogHash)
        {
            return new WorldCreateRequest(
                world,
                NarrativeKeys.WorldDefinition,
                TemporalModel.CommandDriven,
                PropagationMode.Automatic,
                catalogHash,
                operation,
                null);
        }

        private static MessageBufferDescriptor Lane(
            BufferId buffer,
            SchemaRef schema,
            IReadOnlyList<FactoryKey> producers,
            OwnerId owner,
            StageId ownerStage,
            StageId consumerStage,
            FactoryKey orderKey,
            BufferLifetime lifetime)
        {
            return new MessageBufferDescriptor(
                buffer,
                schema,
                producers,
                owner,
                ownerStage,
                consumerStage,
                orderKey,
                lifetime,
                LaneCapacity,
                LaneByteCapacity,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);
        }
    }
}
