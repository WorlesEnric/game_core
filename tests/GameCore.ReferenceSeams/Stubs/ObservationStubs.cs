// Test-only deterministic stub (namespace GameCore.TestFixtures) for the W0 reference seam.
// Observation, event-reader and callback-gate doubles for W1 peers. Bounded retention is explicit and no
// wall clock, worker index or dictionary enumeration order decides an outcome (P-045, P-047).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.TestFixtures
{
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

    /// <summary>
    /// Stub observation reader with explicit bounded retention. Expiry and backpressure are returned as values,
    /// and a token from another world incarnation is refused before retention is consulted (P-004, P-007, P-045).
    /// </summary>
    public sealed class StubObservationReader : IObservationReader
    {
        private readonly Dictionary<SnapshotToken, byte[]> retained = new Dictionary<SnapshotToken, byte[]>();
        private readonly WorldId world;

        /// <summary>Maximum simultaneously retained images; <see cref="int.MaxValue"/> means unbounded.</summary>
        private readonly int capacity;

        public StubObservationReader(WorldId world, int capacity)
        {
            this.world = world;
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Retention capacity must be positive.");
            }

            this.capacity = capacity;
        }

        public int AcquireCount { get; private set; }

        public int ExpiredCount { get; private set; }

        public int BackpressureCount { get; private set; }

        public int ForeignWorldCount { get; private set; }

        public int RetainedCount => retained.Count;

        public void Retain(SnapshotToken token, byte[] state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (!token.World.Equals(world))
            {
                throw new ArgumentException("A token from another world incarnation cannot be retained here (P-004).", nameof(token));
            }

            retained[token] = (byte[])state.Clone();
        }

        /// <summary>Drops one retained image so the next acquisition of the same token reports expiry.</summary>
        public bool Release(SnapshotToken token) => retained.Remove(token);

        public SnapshotAcquireResult Acquire(SnapshotToken token)
        {
            if (!token.World.Equals(world))
            {
                ForeignWorldCount++;
                return SnapshotAcquireResult.ForeignWorld(token);
            }

            if (!retained.TryGetValue(token, out byte[]? state))
            {
                ExpiredCount++;
                return SnapshotAcquireResult.Expired(token);
            }

            if (retained.Count > capacity)
            {
                // Bounded retention never overwrites leased memory; the new lease is refused instead (P-007).
                BackpressureCount++;
                return SnapshotAcquireResult.Backpressure(token);
            }

            AcquireCount++;
            return SnapshotAcquireResult.Acquired(token, new FrozenSnapshotLease(token, state));
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
        private readonly WorldId world;

        public StubCallbackGate(WorldId world)
        {
            this.world = world;
        }

        /// <summary>Set while a staging/publication fence is active.</summary>
        public bool Fenced { get; set; }

        public void RegisterActivation(PluginInstanceId instance, InstallationGeneration generation, ActivationEpoch activationEpoch)
        {
            live[instance.Value] = new ActivationStamp(generation, activationEpoch);
        }

        public bool RetireActivation(PluginInstanceId instance) => live.Remove(instance.Value);

        public CallbackGateDecision Evaluate(AsyncWorkToken token)
        {
            if (!token.Operation.World.Equals(world))
            {
                // A completion stamped by another world incarnation never acquires authority here (P-004, P-047).
                return CallbackGateDecision.DiscardForeignWorld;
            }

            if (Fenced)
            {
                return CallbackGateDecision.DiscardPostPublicationFence;
            }

            if (!live.TryGetValue(token.PluginInstanceId.Value, out ActivationStamp stamp))
            {
                return CallbackGateDecision.DiscardRetiredRoute;
            }

            if (!stamp.Generation.Equals(token.InstallationGeneration) || !stamp.ActivationEpoch.Equals(token.ActivationEpoch))
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

    /// <summary>
    /// Stub explanation reader and staged-plan diagnostic source. Published pages are labelled
    /// <see cref="ExplanationSource.PublishedComposition"/> and staged pages <see cref="ExplanationSource.StagedPlan"/>,
    /// so a consumer cannot mistake staged plan data for world observation (05 s5, P-029).
    /// </summary>
    public sealed class StubExplanationReader : IExplanationReader, IStagedPlanDiagnostics
    {
        private readonly Dictionary<string, RecordSet> published = new Dictionary<string, RecordSet>();
        private readonly Dictionary<string, RecordSet> staged = new Dictionary<string, RecordSet>();

        public int InvalidPageRequestCount { get; private set; }

        public int UnknownStagedOperationCount { get; private set; }

        public void AddPublished(TargetId target, CapabilityId capability, SnapshotToken token, IReadOnlyList<ExplanationRecord>? matching, IReadOnlyList<ExplanationRecord>? rejected)
        {
            published[PublishedKey(target, capability, token.World)] = new RecordSet(token, matching, rejected);
        }

        public void AddStaged(OperationId operation, SnapshotToken baseToken, TargetId target, CapabilityId capability, IReadOnlyList<ExplanationRecord>? matching, IReadOnlyList<ExplanationRecord>? rejected)
        {
            staged[StagedKey(operation, target, capability)] = new RecordSet(baseToken, matching, rejected);
        }

        public ExplanationPage Explain(TargetId target, CapabilityId capability, SnapshotToken token, ExplanationPageRequest page)
        {
            published.TryGetValue(PublishedKey(target, capability, token.World), out RecordSet? records);
            return Build(records, token, target, capability, ExplanationSource.PublishedComposition, page);
        }

        public ExplanationPage ReadStaged(OperationId operation, TargetId target, CapabilityId capability, ExplanationPageRequest page)
        {
            if (!staged.TryGetValue(StagedKey(operation, target, capability), out RecordSet? records))
            {
                UnknownStagedOperationCount++;
                return Build(null, new SnapshotToken(operation.World, AssemblyEpoch.Zero, LogicalStepId.Zero), target, capability, ExplanationSource.StagedPlan, page);
            }

            return Build(records, records.Token, target, capability, ExplanationSource.StagedPlan, page);
        }

        private ExplanationPage Build(
            RecordSet? records,
            SnapshotToken token,
            TargetId target,
            CapabilityId capability,
            ExplanationSource source,
            ExplanationPageRequest page)
        {
            if (!page.IsValid)
            {
                InvalidPageRequestCount++;
                return new ExplanationPage(target, capability, token, source, page, null, null, null, PropagationMode.Automatic, 0, ContentHash.Empty, null, 0UL, 0UL);
            }

            // One global offset walks matching records first, then rejected ones, so a page boundary may fall
            // between the two lists without losing or duplicating a record (P-029).
            List<ExplanationRecord> matching = new List<ExplanationRecord>();
            List<ExplanationRecord> rejected = new List<ExplanationRecord>();
            if (records != null)
            {
                uint index = 0U;
                uint remaining = page.MaxRecords;
                index = Take(records.Matching, page.Offset, ref index, ref remaining, matching);
                Take(records.Rejected, page.Offset, ref index, ref remaining, rejected);
            }

            ulong totalMatching = records == null ? 0UL : (ulong)records.Matching.Count;
            ulong totalRejected = records == null ? 0UL : (ulong)records.Rejected.Count;
            return new ExplanationPage(target, capability, token, source, page, matching, rejected, null, PropagationMode.Automatic, 0, ContentHash.Empty, null, totalMatching, totalRejected);
        }

        private static uint Take(
            IReadOnlyList<ExplanationRecord> source,
            uint offset,
            ref uint index,
            ref uint remaining,
            List<ExplanationRecord> into)
        {
            for (int i = 0; i < source.Count && remaining > 0U; i++)
            {
                if (index >= offset)
                {
                    into.Add(source[i]);
                    remaining--;
                }

                index++;
            }

            return index;
        }

        private static string PublishedKey(TargetId target, CapabilityId capability, WorldId world) =>
            "published/" + world.ToString() + "/" + target.ToString() + "/" + capability.ToString();

        private static string StagedKey(OperationId operation, TargetId target, CapabilityId capability) =>
            "staged/" + operation.ToString() + "/" + target.ToString() + "/" + capability.ToString();

        private sealed class RecordSet
        {
            public RecordSet(SnapshotToken token, IReadOnlyList<ExplanationRecord>? matching, IReadOnlyList<ExplanationRecord>? rejected)
            {
                Token = token;
                Matching = ContractCollections.Freeze(matching);
                Rejected = ContractCollections.Freeze(rejected);
            }

            public SnapshotToken Token { get; }

            public IReadOnlyList<ExplanationRecord> Matching { get; }

            public IReadOnlyList<ExplanationRecord> Rejected { get; }
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
