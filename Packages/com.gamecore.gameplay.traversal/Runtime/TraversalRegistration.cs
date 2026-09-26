// GameCore.Gameplay.Traversal — the course plugin's registration data and its precompiled bindings (GC-020).
//
// Normative sources: 04 s8 (registration is data: the precompiled factory key, the system factory keys, the typed
// payload readers and the spawn recipes are direct typed references, and nothing is discovered by reflection), P-024
// (a precompiled `SpawnRecipe` carries the base layout a target is spawned from), P-042 (a command lane is a declared
// route with an owner, a schema and a bounded ingress buffer), P-043 (a declared buffer names its producers, its
// single consuming owner, its order key, its capacity and its overflow policy), P-036 (a fixed-step world declares its
// step duration and catch-up bound) and P-009 (an unregistered key is a miss).
//
// Two plugin-level generated bindings exist because the traversal slice must be resolvable through a generated
// catalog, not only through the hand-written fixture table: `TraversalCoursePluginFactory` is the precompiled
// `PluginFactory` registration, and `TraversalSystemFactory` is one `SystemFactory` registration per compiled system
// key. Both carry their own key and stable name, so the registered path and the direct path cannot drift (P-028).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Gameplay.Traversal
{
    /// <summary>The precompiled plugin factory a generated traversal catalog registers (a `PluginFactory` entry).</summary>
    public interface ITraversalCoursePluginFactory
    {
        /// <summary>The generated registration key this factory is bound under.</summary>
        FactoryKey Key { get; }

        /// <summary>The registration's stable name, for diagnostics and fingerprints.</summary>
        string StableName { get; }
    }

    /// <summary>
    /// The course plugin's precompiled factory. A mount resolves it by key through the catalog, and the instance it
    /// produces is the course runtime the five systems serve (04 s8).
    /// </summary>
    public sealed class TraversalCoursePluginFactory : ITraversalCoursePluginFactory
    {
        /// <summary>The stable name of the one `PluginFactory` registration of the course.</summary>
        public const string RegistrationStableName = "traversal.factory.course-plugin";

        /// <summary>Binds the factory to its generated registration key.</summary>
        public TraversalCoursePluginFactory(FactoryKey key)
        {
            Key = key;
        }

        /// <inheritdoc />
        public FactoryKey Key { get; }

        /// <inheritdoc />
        public string StableName => RegistrationStableName;
    }

    /// <summary>The precompiled dispatch factory of one compiled traversal system (a `SystemFactory` entry).</summary>
    public interface ITraversalSystemFactory
    {
        /// <summary>The generated dispatch key this factory is bound under (P-039).</summary>
        FactoryKey Key { get; }

        /// <summary>The registration's stable name, for diagnostics and fingerprints.</summary>
        string StableName { get; }

        /// <summary>The dispatch kind the guarded dispatcher must use for this key (P-040).</summary>
        SystemDispatchKind Kind { get; }
    }

    /// <summary>
    /// One traversal system's generated factory binding. The concrete <see cref="SystemBase"/> is created by the
    /// generated-style `SystemRegistration` list of <see cref="TraversalRegistration.Systems"/>, which is the same
    /// direct-typed shape the emitter writes; this type is the keyed registration the compiled schedule resolves.
    /// </summary>
    public sealed class TraversalSystemFactory : ITraversalSystemFactory
    {
        /// <summary>Binds one system factory to its generated dispatch key.</summary>
        public TraversalSystemFactory(FactoryKey key)
        {
            Key = key;
            StableName = "traversal.system-factory." + key.RegistrationKey.ToString();
        }

        /// <inheritdoc />
        public FactoryKey Key { get; }

        /// <inheritdoc />
        public string StableName { get; }

        /// <inheritdoc />
        public SystemDispatchKind Kind => SystemDispatchKind.ManagedSystem;
    }

    /// <summary>
    /// The traversal plugin's declared execution surface: the bounded movement lane and its typed reader, the five
    /// systems, the world's composition root and the fixed-step creation request of one course world.
    /// </summary>
    public static class TraversalRegistration
    {
        /// <summary>World name; the host appends the session id, so every world name is an inspectable incarnation.</summary>
        public const string WorldName = "GameCoreTraversalCourseWorld";

        /// <summary>Host ticks per second of the qualification clock (the runtime's declared clock rate).</summary>
        public const ulong TicksPerSecond = 10000000UL;

        /// <summary>World definition of a traversal course world.</summary>
        public static WorldDefinitionId WorldDefinition => TraversalKeys.WorldDefinition;

        /// <summary>
        /// The course's single bounded movement lane (07 s4.2): one route carrying captured movement inputs, owned by
        /// the input adapter, drained by `traversal.input`, which is its single consuming stage. It is reliable
        /// (`RejectBeforeMutation`), so a full lane refuses before any mutation instead of dropping input (P-042,
        /// P-043).
        /// </summary>
        public static MessagePlaneRegistration Messages()
        {
            var route = new CommandRoute(
                TraversalKeys.CommandRoute,
                TraversalKeys.InputOwner,
                TraversalKeys.CommandSchema,
                TraversalKeys.InputStage,
                TraversalKeys.InputStage,
                TraversalKeys.CommandLane,
                TraversalKeys.HostIngressProducer,
                TraversalKeys.CommandCapacity,
                false);

            var lane = new MessageBufferDescriptor(
                TraversalKeys.CommandLane,
                TraversalKeys.CommandSchema,
                new[] { TraversalKeys.HostIngressProducer },
                TraversalKeys.InputOwner,
                TraversalKeys.InputStage,
                TraversalKeys.InputStage,
                TraversalKeys.CommandOrderKey,
                BufferLifetime.Step,
                TraversalKeys.CommandCapacity,
                TraversalKeys.CommandCapacity * TraversalCommandCodec.CommandBytes,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);

            return new MessagePlaneRegistration(
                new List<CommandRoute> { route },
                new List<MessageBufferDescriptor> { lane },
                null,
                maxPendingRequests: TraversalKeys.CommandCapacity,
                maxRetainedResults: TraversalKeys.CommandCapacity,
                maxRetainedEvents: TraversalKeys.CrossingCapacity,
                maxEventsPerStep: TraversalKeys.CrossingCapacity,
                nextStepCapacity: TraversalKeys.InputCapacity);
        }

        /// <summary>The generated typed readers of the course's plane (04 s8): one per declared payload schema.</summary>
        public static CommandPayloadReaders Readers()
        {
            var readers = new CommandPayloadReaders();
            if (!readers.TryBind(new TraversalMovementInputReader(), out string failure))
            {
                throw new InvalidOperationException("the movement input reader registration failed: " + failure);
            }

            return readers;
        }

        /// <summary>The course's five systems, one per declared stage, in the compiled order (P-039).</summary>
        public static IReadOnlyList<SystemRegistration> Systems()
        {
            return new List<SystemRegistration>
            {
                new ManagedSystemRegistration<TraversalInputSystem>(
                    TraversalKeys.InputSystem, TraversalKeys.InputStage, "TraversalInputSystem"),
                new ManagedSystemRegistration<TraversalIntegrateSystem>(
                    TraversalKeys.IntegrateSystem, TraversalKeys.IntegrateStage, "TraversalIntegrateSystem"),
                new ManagedSystemRegistration<TraversalSenseSystem>(
                    TraversalKeys.SenseSystem, TraversalKeys.SenseStage, "TraversalSenseSystem"),
                new ManagedSystemRegistration<TraversalCheckpointSystem>(
                    TraversalKeys.CheckpointSystem, TraversalKeys.CheckpointStage, "TraversalCheckpointSystem"),
                new ManagedSystemRegistration<TraversalOutputSystem>(
                    TraversalKeys.OutputSystem, TraversalKeys.OutputStage, "TraversalOutputSystem"),
            };
        }

        /// <summary>
        /// The generated dispatch-kind table of the traversal systems (GC-009's own resolver input). Every compiled key
        /// must resolve here, or the schedule adaptation reports a witness instead of guessing (04 s8).
        /// </summary>
        public static ScheduleDispatchKindTable DispatchKinds()
        {
            var kinds = new ScheduleDispatchKindTable();
            for (int i = 0; i < TraversalKeys.SystemKeys.Length; i++)
            {
                kinds.Add(TraversalKeys.SystemKeys[i], SystemDispatchKind.ManagedSystem);
            }

            return kinds;
        }

        /// <summary>
        /// The world's composition root: the compiled schedule's adapted plan becomes the initial step table, and
        /// GC-008's publisher rebinds the group from the same compiled order at every publication (P-040).
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

        /// <summary>
        /// Creation request of one fixed-step course world (O-01, P-036): the reference's configured 20 ms step with
        /// the declared four-step catch-up bound of 07 s4.2.
        /// </summary>
        public static WorldCreateRequest FixedStepRequest(
            WorldId world,
            OperationId operation,
            ContentHash catalogHash)
        {
            return new WorldCreateRequest(
                world,
                WorldDefinition,
                TemporalModel.FixedStep,
                PropagationMode.Automatic,
                catalogHash,
                operation,
                new FixedStepSettings(
                    TraversalKeys.StepDurationTicks,
                    TicksPerSecond,
                     TraversalKeys.MaxStepsPerPump,
                    false));
        }
    }
}
