// Test-only deterministic stub (namespace GameCore.TestFixtures) for the W0 reference seam.
// Observation, event-reader and callback-gate doubles for W1 peers. Bounded retention is explicit and no
// wall clock, worker index or dictionary enumeration order decides an outcome (P-045, P-047).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.TestFixtures
{
    /// <summary>Raised by a stub when a requested observation is expired or over capacity (P-007).</summary>
    public sealed class SnapshotUnavailableException : Exception
    {
        public SnapshotUnavailableException(DiagnosticCode code, string message)
            : base(message)
        {
            Code = code;
        }

        public DiagnosticCode Code { get; }
    }

    /// <summary>Stub snapshot lease: no writable data, single effective disposal (P-045, P-048).</summary>
    public sealed class FrozenSnapshotLease : ISnapshotLease
    {
        private byte[]? state;

        public FrozenSnapshotLease(SnapshotToken token, byte[] state)
        {
            Token = token;
            this.state = state == null ? throw new ArgumentNullException(nameof(state)) : (byte[])state.Clone();
        }

        public SnapshotToken Token { get; }

        public bool IsDisposed => state == null;

        public int DisposeCount { get; private set; }

        public FrozenPayload State
        {
            get
            {
                byte[]? current = state;
                if (current == null)
                {
                    throw new ObjectDisposedException(nameof(FrozenSnapshotLease));
                }

                return new FrozenPayload(current);
            }
        }

        public void Dispose()
        {
            if (state == null)
            {
                return;
            }

            DisposeCount++;
            state = null;
        }
    }

    /// <summary>Stub observation reader with explicit retention; unknown tokens are rejected, never guessed.</summary>
    public sealed class StubObservationReader : IObservationReader
    {
        private readonly Dictionary<SnapshotToken, byte[]> retained = new Dictionary<SnapshotToken, byte[]>();

        public int AcquireCount { get; private set; }

        public int RejectedCount { get; private set; }

        public int RetainedCount => retained.Count;

        public void Retain(SnapshotToken token, byte[] state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            retained[token] = (byte[])state.Clone();
        }

        /// <summary>Drops one retained image so the next acquisition of the same token is rejected.</summary>
        public bool Release(SnapshotToken token) => retained.Remove(token);

        public ISnapshotLease Acquire(SnapshotToken token)
        {
            if (!retained.TryGetValue(token, out byte[]? state))
            {
                RejectedCount++;
                throw new SnapshotUnavailableException(DiagnosticCode.CursorExpired, "Snapshot image is not retained.");
            }

            AcquireCount++;
            return new FrozenSnapshotLease(token, state);
        }
    }

    /// <summary>Stub bounded committed-event reader; a cursor older than retention yields CursorExpired (P-045).</summary>
    public sealed class StubCommittedEventReader : ICommittedEventReader
    {
        private readonly List<CommittedEvent> events = new List<CommittedEvent>();
        private readonly int retention;

        public StubCommittedEventReader(int retention)
        {
            if (retention <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(retention), "Retention must be positive.");
            }

            this.retention = retention;
        }

        public int DroppedCount { get; private set; }

        public void Append(CommittedEvent committedEvent)
        {
            if (committedEvent == null)
            {
                throw new ArgumentNullException(nameof(committedEvent));
            }

            events.Add(committedEvent);
            while (events.Count > retention)
            {
                events.RemoveAt(0);
                DroppedCount++;
            }
        }

        public CommittedEventPage Read(EventCursor cursor, int maxEvents)
        {
            if (maxEvents <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEvents), "maxEvents must be positive.");
            }

            if (events.Count != 0 && cursor.Sequence < events[0].Cursor.Sequence)
            {
                return new CommittedEventPage(CursorOutcome.CursorExpired, null, new EventCursor(cursor.World, events[0].Cursor.Sequence));
            }

            List<CommittedEvent> page = new List<CommittedEvent>();
            EventCursor next = cursor;
            for (int i = 0; i < events.Count && page.Count < maxEvents; i++)
            {
                CommittedEvent candidate = events[i];
                if (candidate.Cursor.Sequence <= cursor.Sequence || !candidate.Cursor.World.Equals(cursor.World))
                {
                    continue;
                }

                page.Add(candidate);
                next = candidate.Cursor;
            }

            return new CommittedEventPage(CursorOutcome.Ok, page, next);
        }
    }

    /// <summary>
    /// Stub callback gate: dispatch is allowed only for a live installation generation and activation epoch,
    /// and never behind a publication fence (P-047).
    /// </summary>
    public sealed class StubCallbackGate : ICallbackGate
    {
        private readonly Dictionary<Id128, ActivationStamp> live = new Dictionary<Id128, ActivationStamp>();

        /// <summary>Set while a staging/publication fence is active.</summary>
        public bool Fenced { get; set; }

        public void RegisterActivation(PluginInstanceId instance, InstallationGeneration generation, ActivationEpoch activationEpoch)
        {
            live[instance.Value] = new ActivationStamp(generation, activationEpoch);
        }

        public bool RetireActivation(PluginInstanceId instance) => live.Remove(instance.Value);

        public CallbackGateDecision Evaluate(AsyncWorkToken token)
        {
            if (Fenced)
            {
                return CallbackGateDecision.DiscardPostPublicationFence;
            }

            if (!live.TryGetValue(token.PluginInstanceId.Value, out ActivationStamp stamp))
            {
                return CallbackGateDecision.DiscardRetiredRoute;
            }

            if (stamp.Generation.Value != token.InstallationGeneration || stamp.ActivationEpoch.Value != token.ActivationEpoch)
            {
                return CallbackGateDecision.DiscardStaleActivation;
            }

            return CallbackGateDecision.Dispatch;
        }

        private readonly struct ActivationStamp
        {
            public readonly InstallationGeneration Generation;
            public readonly ActivationEpoch ActivationEpoch;

            public ActivationStamp(InstallationGeneration generation, ActivationEpoch activationEpoch)
            {
                Generation = generation;
                ActivationEpoch = activationEpoch;
            }
        }
    }

    /// <summary>Stub composition observer recording publications and rejections in call order (P-029).</summary>
    public sealed class RecordingCompositionObserver : ICompositionObserver
    {
        private readonly List<CompositionPublishedEvent> published = new List<CompositionPublishedEvent>();
        private readonly List<CompositionRejectedEvent> rejected = new List<CompositionRejectedEvent>();

        public IReadOnlyList<CompositionPublishedEvent> Published => published;

        public IReadOnlyList<CompositionRejectedEvent> Rejected => rejected;

        public void OnCompositionPublished(CompositionPublishedEvent published)
        {
            if (published == null)
            {
                throw new ArgumentNullException(nameof(published));
            }

            this.published.Add(published);
        }

        public void OnCompositionRejected(CompositionRejectedEvent rejected)
        {
            if (rejected == null)
            {
                throw new ArgumentNullException(nameof(rejected));
            }

            this.rejected.Add(rejected);
        }
    }
}
