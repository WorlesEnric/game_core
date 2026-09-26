// GameCore.Composition — callback gates (P-047) and the publication fence (P-030).
//
// A completion is only allowed to install data when it still belongs to the current world incarnation, the
// current installation generation and the current activation epoch, and when no publication fence is closed
// over its route. The gate is evaluated both when the work was issued and again when it completes, so a token
// validated at issue time cannot be reused after a retire or a restart (P-047, O-24).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>One live activation stamp: the generation and epoch a completion must still match (P-005, P-007).</summary>
    public readonly struct ActivationStamp
    {
        public readonly InstallationGeneration Generation;
        public readonly ActivationEpoch ActivationEpoch;

        public ActivationStamp(InstallationGeneration generation, ActivationEpoch activationEpoch)
        {
            Generation = generation;
            ActivationEpoch = activationEpoch;
        }
    }

    /// <summary>
    /// Serialized callback gate of one world. The fence is closed while a composition edit prepares and while a
    /// publication is in flight; inside a closed fence nothing is delivered, and a retired activation can never
    /// come back to life through a late completion (P-047).
    /// </summary>
    public sealed class CallbackGate : ICallbackGate, ITelemetryOwner
    {
        string ITelemetryOwner.TelemetryOwner => "gamecore.composition.callbacks";

        private readonly Dictionary<Id128, ActivationStamp> live = new Dictionary<Id128, ActivationStamp>();

        public CallbackGate(WorldId world)
        {
            World = world;
        }

        public WorldId World { get; }

        /// <summary>Set while a staging or publication fence is active (P-030).</summary>
        public bool Fenced { get; private set; }

        /// <summary>Completions evaluated, kept for the control-lane counters of P-053/TEST-002 evidence.</summary>
        public int EvaluatedCount { get; private set; }

        public int DiscardedCount { get; private set; }

        /// <summary>Closes the fence for the duration of one prepare/apply pass.</summary>
        public void CloseFence() => Fenced = true;

        /// <summary>Reopens the fence only after the published gate table has switched (P-030).</summary>
        public void OpenFence() => Fenced = false;

        /// <summary>Registers the activation of one installation; a remount with a new generation replaces it.</summary>
        public void RegisterActivation(PluginInstanceId instance, InstallationGeneration generation, ActivationEpoch activationEpoch)
        {
            live[instance.Value] = new ActivationStamp(generation, activationEpoch);
        }

        /// <summary>Retires an activation so its late completions are discarded rather than delivered (P-047).</summary>
        public bool RetireActivation(PluginInstanceId instance) => live.Remove(instance.Value);

        public bool TryGetActivation(PluginInstanceId instance, out ActivationStamp stamp) =>
            live.TryGetValue(instance.Value, out stamp);

        public int LiveActivationCount => live.Count;

        /// <summary>
        /// Writes the callback counters through the fixed compact schema (GC-023): live activations are the
        /// outstanding callbacks, and a discarded completion is a stale result (P-047, TEST-023).
        /// </summary>
        public void WriteTelemetry(TelemetryCounterSet into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.ObserveMax(TelemetryCounter.OutstandingCallbacks, LiveActivationCount);
            into.Add(TelemetryCounter.DiscardedCallbacks, DiscardedCount);
            into.Add(TelemetryCounter.StaleResults, DiscardedCount);
        }

        /// <summary>
        /// Validation order is world, then fence, then liveness, then generation/epoch. The order is observable in
        /// the returned decision, and it never depends on thread timing because it runs on the control lane.
        /// </summary>
        public CallbackGateDecision Evaluate(AsyncWorkToken token)
        {
            EvaluatedCount++;

            if (!token.Operation.World.Equals(World))
            {
                // Another world incarnation: the completion can release its own resources but never publish here.
                DiscardedCount++;
                return CallbackGateDecision.DiscardForeignWorld;
            }

            if (Fenced)
            {
                DiscardedCount++;
                return CallbackGateDecision.DiscardPostPublicationFence;
            }

            if (!live.TryGetValue(token.PluginInstanceId.Value, out ActivationStamp stamp))
            {
                DiscardedCount++;
                return CallbackGateDecision.DiscardRetiredRoute;
            }

            if (!stamp.Generation.Equals(token.InstallationGeneration) || !stamp.ActivationEpoch.Equals(token.ActivationEpoch))
            {
                DiscardedCount++;
                return CallbackGateDecision.DiscardStaleActivation;
            }

            return CallbackGateDecision.Dispatch;
        }
    }

    /// <summary>
    /// The full dispatch check for one prepared managed callback: the resource gate must already be open (the
    /// lease published), and the callback gate must accept the token. A closed resource gate drops the work, so
    /// a subscription prepared for an unpublished activation can never run gameplay code (P-029, P-047).
    /// </summary>
    public static class GatedCallbackPath
    {
        public static CallbackGateDecision Evaluate(IManagedResourceLease lease, ICallbackGate callbacks, AsyncWorkToken token)
        {
            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
            }

            if (callbacks == null)
            {
                throw new ArgumentNullException(nameof(callbacks));
            }

            if (lease.Gate is ManagedResourceGate gate)
            {
                if (!gate.TryDispatch())
                {
                    // Inert before publication, and still closed after retirement: the work is dropped.
                    return CallbackGateDecision.DiscardPostPublicationFence;
                }
            }
            else if (!lease.Gate.IsOpen)
            {
                return CallbackGateDecision.DiscardPostPublicationFence;
            }

            return callbacks.Evaluate(token);
        }
    }
}
