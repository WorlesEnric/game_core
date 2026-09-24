#nullable enable
using System;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Unity.Runtime;

namespace GameCore.Unity.Adapters
{
    /// <summary>
    /// The application's composition root for its designated gameplay world: the generated-style registration plus
    /// the creation inputs (definition, temporal model, fixed-step configuration) the host needs (P-002, P-035).
    /// </summary>
    public sealed class GameCoreApplicationCompositionRoot
    {
        public GameCoreApplicationCompositionRoot(
            UnityWorldRegistration registration,
            WorldDefinitionId definition,
            TemporalModel temporalModel,
            FixedStepSettings? fixedStep)
        {
            Registration = registration ?? throw new ArgumentNullException(nameof(registration));
            Definition = definition;
            TemporalModel = temporalModel;
            FixedStep = fixedStep;
        }

        public UnityWorldRegistration Registration { get; }

        public WorldDefinitionId Definition { get; }

        public TemporalModel TemporalModel { get; }

        public FixedStepSettings? FixedStep { get; }

        /// <summary>Builds the creation request for one freshly reserved session (O-01, P-004).</summary>
        public WorldCreateRequest CreateRequest(WorldId world, OperationId operation, PropagationMode mode, ContentHash catalogHash)
        {
            return new WorldCreateRequest(world, Definition, TemporalModel, mode, catalogHash, operation, FixedStep);
        }
    }

    /// <summary>
    /// Application composition state of the single bootstrap. The application composition root assigns
    /// <see cref="RootFactory"/> from its own <c>RuntimeInitializeOnLoadMethod(SubsystemRegistration)</c> method,
    /// which runs before world bootstrapping; the factory is a pure function of application configuration, so it is
    /// deliberately not cleared by the domain-reload reset (04 s9).
    /// </summary>
    public static class GameCoreApplicationComposition
    {
        /// <summary>Issuer identity of bootstrap-created operations (a host issuer, not a plugin identity).</summary>
        public static readonly Id128 BootstrapIssuerId = new Id128(0x47434F5245424F4FUL, 0x5453545241505553UL);

        /// <summary>Fallback definition identity of the infrastructure-only world (no gameplay catalog is implied).</summary>
        public static readonly WorldDefinitionId DefaultWorldDefinitionId =
            new WorldDefinitionId(new Id128(0x47434F52454E4F43UL, 0x4154414C4F470001UL));

        private static ulong sessionSequence;

        public static Func<GameCoreApplicationCompositionRoot>? RootFactory { get; set; }

        public static bool HasRegistration => RootFactory != null;

        /// <summary>Composition roots built since process start; evidence that the application registered once.</summary>
        public static int RootCount { get; private set; }

        public static GameCoreApplicationCompositionRoot? TryCreateRoot()
        {
            Func<GameCoreApplicationCompositionRoot>? factory = RootFactory;
            if (factory == null)
            {
                return null;
            }

            RootCount++;
            return factory();
        }

        /// <summary>
        /// Infrastructure-only root: one owned world, one update path, no gameplay stage. Used when the application
        /// registered no composition, so the bootstrap never falls back to Unity's default world with every
        /// auto-created system (04 s3).
        /// </summary>
        public static GameCoreApplicationCompositionRoot DefaultRoot()
        {
            var registration = new UnityWorldRegistration(
                "GameCoreApplicationWorld",
                null,
                null,
                GuardedDispatchPlan.Empty,
                GuardedDispatchPlan.Empty,
                GuardedDispatchPlan.Empty,
                null);

            return new GameCoreApplicationCompositionRoot(
                registration,
                DefaultWorldDefinitionId,
                TemporalModel.CommandDriven,
                null);
        }

        /// <summary>
        /// Reserves a fresh world session id. It is never reused, including after checkpoint restore, and it is not
        /// derived from the clock or from a native handle (P-004).
        /// </summary>
        public static WorldId ReserveSessionId()
        {
            byte[] bytes = Guid.NewGuid().ToByteArray();
            ulong high = BitConverter.ToUInt64(bytes, 0) | 0x8000000000000000UL;
            ulong low = BitConverter.ToUInt64(bytes, 8);
            sessionSequence++;
            low ^= sessionSequence;
            return new WorldId(new Id128(high, low == 0UL ? sessionSequence : low));
        }

        internal static void ResetSequence() => sessionSequence = 0UL;
    }
}
