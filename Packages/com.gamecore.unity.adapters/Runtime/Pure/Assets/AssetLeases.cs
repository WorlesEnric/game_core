// GameCore.Unity.Adapters — asynchronous asset leases for one world (GC-019).
//
// Normative sources: 00 P-003 (an asset lease is a `ManagedResourceEffect`: it has a lifetime and cleanup; disposing
// it does not undo committed gameplay), P-007 (an async work token contains world, installation generation,
// activation epoch and operation id; a stale completion can release its own resources but MUST NOT publish state or
// reacquire execution authority; bounded retention rejects rather than overwrites), P-024 (a `SpawnRecipe` declares
// its required asset leases), P-029 (registrations/subscriptions acquired during preparation stay gated and cannot
// emit gameplay commands before publication), P-041/P-043 (bounded work; a full buffer rejects the affected request
// explicitly, never silently) and P-048 (dispose each lease at most once, attempt every independent cleanup, retain
// what unfinished work may still reach), plus 04 s7's Assets row: "Completed load results carrying WorldId,
// installation generation, ActivationEpoch, and operation/work identity; leases keep assets alive through their last
// consumer job/frame; late results are discarded/released after teardown or reconfiguration. Asset loading does not
// activate a half-built recipe."
//
// Three rules shape this file:
//   * A completed load is *data*, not authority. The lease hands payload bytes to a caller that already owns the
//     state; it never writes ECS, and it never becomes a second copy of gameplay state (P-034, "adapter mirrors are
//     never separately authoritative").
//   * A completion is validated at completion against the real serialized callback gate and against the world's
//     incarnation. After `Retire()` no completion can write anything: it releases its own acquisition and is
//     counted as a stale discard (P-007, P-047).
//   * Every lease is registered in the world's resource ledger, so the existing P-048 teardown path fences and
//     retires adapter leases exactly like any other managed lease instead of needing a second teardown story.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution;

namespace GameCore.Unity.Adapters.Assets
{
    /// <summary>Lifecycle state of one asynchronous asset acquisition (P-007, P-048).</summary>
    public enum AssetLeaseState
    {
        /// <summary>Handed to the engine backend; the completion has not arrived.</summary>
        Loading = 0,

        /// <summary>The load completed and its token still held authority, so the payload is usable.</summary>
        Ready = 1,

        /// <summary>The backend reported a failure; nothing is exposed and the lease is retained for cleanup.</summary>
        Failed = 2,

        /// <summary>The completion arrived after its world/activation/route moved on and was released (P-007).</summary>
        DiscardedStale = 3,

        /// <summary>The lease was retired or quarantined by teardown (P-048).</summary>
        Retired = 4,

        /// <summary>Retained because unfinished work may still reach it; a timeout never frees it (P-048).</summary>
        Quarantined = 5,
    }

    /// <summary>What the engine backend reported for one load, keyed by the backend's own opaque handle.</summary>
    public enum AssetLoadStatus
    {
        /// <summary>Still loading; the completion has not arrived.</summary>
        Pending = 0,

        /// <summary>The load finished; the payload is the engine's value.</summary>
        Ready = 1,

        /// <summary>The load failed; the backend reports the reason.</summary>
        Failed = 2,
    }

    /// <summary>Result of one backend poll. The payload is opaque to the kernel: adapters interpret it.</summary>
    public readonly struct AssetLoadPoll
    {
        public readonly AssetLoadStatus Status;

        /// <summary>Engine-side value of a ready load; empty for a pending or failed one.</summary>
        public readonly FrozenPayload Payload;

        /// <summary>Reason of a failure, as a diagnostic code (P-052).</summary>
        public readonly DiagnosticCode Code;

        public readonly string Detail;

        private AssetLoadPoll(AssetLoadStatus status, FrozenPayload payload, DiagnosticCode code, string detail)
        {
            Status = status;
            Payload = payload;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public static AssetLoadPoll Pending() =>
            new AssetLoadPoll(AssetLoadStatus.Pending, new FrozenPayload(Array.Empty<byte>()), DiagnosticCode.None, string.Empty);

        public static AssetLoadPoll Ready(FrozenPayload payload) =>
            new AssetLoadPoll(AssetLoadStatus.Ready, payload ?? throw new ArgumentNullException(nameof(payload)), DiagnosticCode.None, string.Empty);

        public static AssetLoadPoll Failed(DiagnosticCode code, string detail) =>
            new AssetLoadPoll(AssetLoadStatus.Failed, new FrozenPayload(Array.Empty<byte>()), code, detail);

        public override string ToString() => Status + "(" + Detail + ")";
    }

    /// <summary>
    /// The engine seam of one asset backend. It owns real loading and real storage; the adapter owns identity,
    /// validation and lifetime. `Release` is called at most once per handle by the lease table (P-048).
    /// </summary>
    public interface IAssetBackend
    {
        /// <summary>
        /// Starts one load and returns its opaque handle. A refusal is a value: the caller rejects the operation
        /// rather than waiting for a load that never started (P-029, P-049).
        /// </summary>
        bool TryBeginLoad(AssetLoadRequest request, out long handle, out DiagnosticCode code, out string detail);

        /// <summary>Polls one handle. Pending is the ordinary answer while the load is in flight.</summary>
        AssetLoadPoll Poll(long handle);

        /// <summary>Drops the backend's own reference to one load; called exactly once per successful begin.</summary>
        void Release(long handle);

        /// <summary>Loads the backend started and has not released; zero proves a leak-free teardown (TEST-015).</summary>
        int OutstandingLoadCount { get; }
    }

    /// <summary>One frozen request to a backend: which resource, and the identity that validates its completion.</summary>
    public sealed class AssetLoadRequest
    {
        public AssetLoadRequest(ResourceKey resource, AsyncWorkToken token, FrozenPayload frozenConfig)
        {
            Resource = resource;
            Token = token;
            FrozenConfig = frozenConfig ?? throw new ArgumentNullException(nameof(frozenConfig));
        }

        public ResourceKey Resource { get; }

        /// <summary>The identity every completion is validated against (P-007).</summary>
        public AsyncWorkToken Token { get; }

        /// <summary>Immutable configuration of this load; never read back as authoritative state.</summary>
        public FrozenPayload FrozenConfig { get; }
    }

    /// <summary>Why a completion did or did not become a usable lease (P-007, P-047).</summary>
    public enum AssetCompletionOutcome
    {
        /// <summary>The token still held authority, so the payload became usable (read-only) data.</summary>
        Completed = 0,

        /// <summary>The world/activation/route check discarded it and released its own acquisition (P-047).</summary>
        DiscardedStale = 1,

        /// <summary>The backend reported a failure; nothing is exposed (P-049).</summary>
        Failed = 2,

        /// <summary>The lease was already terminal; a repeated completion is not a second completion (P-050).</summary>
        AlreadyTerminal = 3,

        /// <summary>No such lease in this table (P-005).</summary>
        UnknownLease = 4,
    }

    /// <summary>One completion result, carrying the real gate decision that produced it.</summary>
    public sealed class AssetCompletionResult
    {
        public AssetCompletionResult(
            AssetCompletionOutcome outcome,
            Id128 leaseId,
            CallbackGateDecision gate,
            DiagnosticCode code,
            string detail)
        {
            Outcome = outcome;
            LeaseId = leaseId;
            Gate = gate;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public AssetCompletionOutcome Outcome { get; }

        public Id128 LeaseId { get; }

        /// <summary>The gate decision for this completion; `Dispatch` means it held authority.</summary>
        public CallbackGateDecision Gate { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        public override string ToString() => Outcome + "(" + Gate + ", " + DiagnosticCodeText.Of(Code) + ")";
    }

    /// <summary>Outcome of one lease release attempt (P-048).</summary>
    public enum AssetReleaseOutcome
    {
        /// <summary>The backend reference was dropped and the ledger recorded the retirement.</summary>
        Released = 0,

        /// <summary>Consumers are still registered, so the lease is retained instead of freed (P-048).</summary>
        RetainedByConsumers = 1,

        /// <summary>The lease was already terminal; releasing twice is not a second release (P-048).</summary>
        AlreadyReleased = 2,

        /// <summary>The engine release threw; the lease is quarantined and never reported as released (P-048).</summary>
        ReleaseFailed = 3,

        /// <summary>No such lease (P-005).</summary>
        UnknownLease = 4,
    }

    /// <summary>One record of one asset acquisition.</summary>
    public sealed class AssetLease
    {
        internal AssetLease(
            Id128 leaseId,
            ResourceKey resource,
            AsyncWorkToken token,
            long handle,
            Id128 ledgerResourceId,
            ulong bytes)
        {
            LeaseId = leaseId;
            Resource = resource;
            Token = token;
            Handle = handle;
            LedgerResourceId = ledgerResourceId;
            Bytes = bytes;
        }

        public Id128 LeaseId { get; }

        public ResourceKey Resource { get; }

        public AsyncWorkToken Token { get; }

        /// <summary>Opaque engine handle; never a serializable identity (P-054).</summary>
        public long Handle { get; }

        /// <summary>World resource ledger record of this lease, so P-048 retires it with everything else.</summary>
        public Id128 LedgerResourceId { get; }

        public ulong Bytes { get; }

        public AssetLeaseState State { get; internal set; } = AssetLeaseState.Loading;

        /// <summary>Payload of a completed load. Presentation/asset data only, never writable gameplay state.</summary>
        public FrozenPayload? Payload { get; internal set; }

        /// <summary>Registered consumers; a lease with live consumers is retained, not freed (P-048).</summary>
        public int Consumers { get; internal set; }

        public int CompletionCount { get; internal set; }

        public bool IsTerminal =>
            State == AssetLeaseState.Retired
            || State == AssetLeaseState.Quarantined
            || State == AssetLeaseState.DiscardedStale;

        public bool IsUsable => State == AssetLeaseState.Ready && Payload != null;

        public override string ToString() =>
            Resource.ToString() + "#" + LeaseId.ToString() + "=" + State
            + "(consumers=" + Consumers.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Aggregate release report of one table pass (P-048: attempt all, aggregate failures).</summary>
    public sealed class AssetReleaseReport
    {
        public AssetReleaseReport(
            IReadOnlyList<Id128>? released,
            IReadOnlyList<Id128>? retained,
            IReadOnlyList<Id128>? failed)
        {
            Released = ContractCollections.Freeze(released);
            Retained = ContractCollections.Freeze(retained);
            Failed = ContractCollections.Freeze(failed);
        }

        public IReadOnlyList<Id128> Released { get; }

        /// <summary>Retained because consumers or unfinished work may still reach them (P-048).</summary>
        public IReadOnlyList<Id128> Retained { get; }

        public IReadOnlyList<Id128> Failed { get; }

        public bool Blocked => Retained.Count != 0 || Failed.Count != 0;

        public bool AllReleased => Retained.Count == 0 && Failed.Count == 0;

        public override string ToString() =>
            "released=" + Released.Count.ToString(CultureInfo.InvariantCulture)
            + " retained=" + Retained.Count.ToString(CultureInfo.InvariantCulture)
            + " failed=" + Failed.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Bounded table of asynchronous asset leases of one world. It owns identity, bounding, completion validation
    /// and release; the backend owns loading and storage. Nothing here reads a clock, so "a timeout never authorizes
    /// free" holds by construction: only a consumer count or a quarantine can retain a lease (P-048).
    /// </summary>
    public sealed class AssetLeaseTable
    {
        private readonly IAssetBackend backend;
        private readonly ICallbackGate gate;
        private readonly WorldResourceLedger ledger;
        private readonly OwnerId owner;
        private readonly PluginInstanceId instance;
        private readonly Dictionary<Id128, AssetLease> leases = new Dictionary<Id128, AssetLease>();
        private readonly List<Id128> order = new List<Id128>();
        private readonly IdSequence leaseIds;

        public AssetLeaseTable(
            WorldId world,
            IAssetBackend backend,
            ICallbackGate gate,
            WorldResourceLedger ledger,
            OwnerId owner,
            PluginInstanceId instance,
            uint capacity,
            ulong byteBudget,
            ulong leaseIdSalt = 0x6763617373657431UL)
        {
            if (world.Session.IsDefault)
            {
                throw new ArgumentException("An asset lease table must name a live world session (P-004).", nameof(world));
            }

            if (capacity == 0U)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "A bounded lease table must have a positive capacity.");
            }

            World = world;
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            this.owner = owner;
            this.instance = instance;
            Capacity = capacity;
            ByteBudget = byteBudget;
            leaseIds = new IdSequence(leaseIdSalt);
        }

        public WorldId World { get; }

        public uint Capacity { get; }

        /// <summary>Total bytes the table may hold across live leases; exceeding it rejects the operation (P-022).</summary>
        public ulong ByteBudget { get; }

        /// <summary>Sum of registered lease sizes; the bounded accounting the budget is checked against.</summary>
        public ulong ReservedBytes { get; private set; }

        public int RequestCount { get; private set; }

        public int CompletedCount { get; private set; }

        /// <summary>Completions discarded because their world/activation/route had moved on (P-007, P-047).</summary>
        public int StaleDiscardCount { get; private set; }

        /// <summary>Completions that arrived after `Retire()`; none of them may write anything (P-007).</summary>
        public int PostRetireCompletionCount { get; private set; }

        public int FailureCount { get; private set; }

        public int BackpressureCount { get; private set; }

        public int ReleasedCount { get; private set; }

        public int QuarantinedCount { get; private set; }

        /// <summary>True once teardown retired the table; a retired table accepts no new request.</summary>
        public bool IsRetired { get; private set; }

        public int LiveLeaseCount
        {
            get
            {
                int live = 0;
                for (int i = 0; i < order.Count; i++)
                {
                    if (!leases[order[i]].IsTerminal)
                    {
                        live++;
                    }
                }

                return live;
            }
        }

        public int LeaseCount => leases.Count;

        public IReadOnlyList<AssetLease> Leases()
        {
            var all = new List<AssetLease>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                all.Add(leases[order[i]]);
            }

            return all;
        }

        public bool TryGet(Id128 leaseId, out AssetLease? lease) => leases.TryGetValue(leaseId, out lease);

        /// <summary>
        /// Starts one bounded asynchronous acquisition. A full table or an exhausted byte budget is an explicit
        /// `BudgetExceeded`; a retired table refuses outright, because after teardown nothing may acquire (P-007).
        /// </summary>
        public bool TryRequest(
            ResourceKey resource,
            AsyncWorkToken token,
            FrozenPayload frozenConfig,
            ulong bytes,
            out Id128 leaseId,
            out DiagnosticCode code,
            out string detail)
        {
            leaseId = Id128.Zero;
            code = DiagnosticCode.None;
            detail = string.Empty;
            RequestCount++;

            if (IsRetired)
            {
                code = DiagnosticCode.StaleHandle;
                detail = "the lease table was retired; a retired world accepts no new acquisition (P-007)";
                return false;
            }

            if (!token.IsAllocated)
            {
                code = DiagnosticCode.StaleHandle;
                detail = "an asset request must carry an allocated async work token (P-005)";
                return false;
            }

            if (!token.Operation.World.Equals(World))
            {
                code = DiagnosticCode.StaleHandle;
                detail = "the async work token belongs to another world incarnation (P-004)";
                return false;
            }

            if ((uint)leases.Count >= Capacity)
            {
                BackpressureCount++;
                code = DiagnosticCode.BudgetExceeded;
                detail = "the asset lease table is at capacity "
                    + Capacity.ToString(CultureInfo.InvariantCulture)
                    + "; the request is rejected rather than dropping an acquisition (P-043)";
                return false;
            }

            if (ByteBudget != 0UL && ReservedBytes + bytes > ByteBudget)
            {
                BackpressureCount++;
                code = DiagnosticCode.BudgetExceeded;
                detail = "reserving " + bytes.ToString(CultureInfo.InvariantCulture)
                    + " bytes would exceed the asset byte budget "
                    + ByteBudget.ToString(CultureInfo.InvariantCulture) + " (P-022)";
                return false;
            }

            var request = new AssetLoadRequest(resource, token, frozenConfig);
            if (!backend.TryBeginLoad(request, out long handle, out code, out detail))
            {
                // A backend refusal never leaves a half-started load behind (P-029).
                return false;
            }

            Id128 ledgerResourceId = ledger.Acquire(
                WorldResourceKind.ManagedLease,
                resource,
                owner,
                instance,
                Id128.Zero,
                bytes);

            leaseId = leaseIds.Next();
            var lease = new AssetLease(leaseId, resource, token, handle, ledgerResourceId, bytes);
            leases.Add(leaseId, lease);
            order.Add(leaseId);
            order.Sort(CompareIds);
            ReservedBytes += bytes;
            return true;
        }

        /// <summary>
        /// Polls every pending load and completes the ones the backend finished. This is the only place a completion
        /// is installed, so "the token is validated at completion" is one code path, not several.
        /// </summary>
        public int PumpCompletions()
        {
            int completed = 0;
            for (int i = 0; i < order.Count; i++)
            {
                AssetLease lease = leases[order[i]];
                if (lease.State != AssetLeaseState.Loading)
                {
                    continue;
                }

                AssetLoadPoll poll = backend.Poll(lease.Handle);
                if (poll.Status == AssetLoadStatus.Pending)
                {
                    continue;
                }

                if (poll.Status == AssetLoadStatus.Failed)
                {
                    Fail(lease, poll.Code, poll.Detail);
                    continue;
                }

                Complete(lease.LeaseId, poll.Payload);
                completed++;
            }

            return completed;
        }

        /// <summary>
        /// Installs one completion. The completion is validated *now*: a token whose world, installation generation,
        /// activation epoch or route has moved on is discarded and only its own acquisition is released (P-007).
        /// </summary>
        public AssetCompletionResult Complete(Id128 leaseId, FrozenPayload payload)
        {
            if (!leases.TryGetValue(leaseId, out AssetLease? lease) || lease == null)
            {
                return new AssetCompletionResult(
                    AssetCompletionOutcome.UnknownLease,
                    leaseId,
                    CallbackGateDecision.DiscardRetiredRoute,
                    DiagnosticCode.StaleHandle,
                    "no such asset lease in this table (P-005)");
            }

            if (lease.IsTerminal || lease.State == AssetLeaseState.Failed)
            {
                // A repeated completion is not a second completion, and it never re-acquires authority (P-050).
                return new AssetCompletionResult(
                    AssetCompletionOutcome.AlreadyTerminal,
                    leaseId,
                    CallbackGateDecision.DiscardRetiredRoute,
                    DiagnosticCode.IdempotencyConflict,
                    "the lease is already terminal (" + lease.State + ")");
            }

            if (IsRetired)
            {
                // The world is gone: the completion may only release its own acquisition (P-007).
                PostRetireCompletionCount++;
                StaleDiscardCount++;
                Discard(lease);
                return new AssetCompletionResult(
                    AssetCompletionOutcome.DiscardedStale,
                    leaseId,
                    CallbackGateDecision.DiscardRetiredRoute,
                    DiagnosticCode.Cancelled,
                    "the table was retired before the completion arrived; nothing is installed (P-007)");
            }

            CallbackGateDecision decision = gate.Evaluate(lease.Token);
            if (decision != CallbackGateDecision.Dispatch)
            {
                StaleDiscardCount++;
                Discard(lease);
                return new AssetCompletionResult(
                    AssetCompletionOutcome.DiscardedStale,
                    leaseId,
                    decision,
                    DiagnosticCode.Cancelled,
                    "the completion did not hold authority at completion, so it is discarded and its own lease released (P-047)");
            }

            lease.Payload = payload;
            lease.State = AssetLeaseState.Ready;
            lease.CompletionCount++;
            CompletedCount++;
            ledger.MarkReady(lease.LedgerResourceId);
            return new AssetCompletionResult(
                AssetCompletionOutcome.Completed,
                leaseId,
                decision,
                DiagnosticCode.None,
                "the token still held authority, so the payload became a read-only lease (P-007)");
        }

        /// <summary>
        /// Registers one consumer of a ready lease. A lease stays alive through its last consumer, which is how
        /// "leases keep assets alive through their last consumer job/frame" is enforced rather than hoped for (04 s7).
        /// </summary>
        public bool AcquireConsumer(Id128 leaseId, out DiagnosticCode code)
        {
            code = DiagnosticCode.None;
            if (!leases.TryGetValue(leaseId, out AssetLease? lease) || lease == null || !lease.IsUsable)
            {
                code = DiagnosticCode.StaleHandle;
                return false;
            }

            lease.Consumers++;
            return true;
        }

        /// <summary>Drops one registered consumer. The lease does not release itself; teardown decides that.</summary>
        public bool ReleaseConsumer(Id128 leaseId, out DiagnosticCode code)
        {
            code = DiagnosticCode.None;
            if (!leases.TryGetValue(leaseId, out AssetLease? lease) || lease == null)
            {
                code = DiagnosticCode.StaleHandle;
                return false;
            }

            if (lease.Consumers == 0)
            {
                code = DiagnosticCode.OwnershipConflict;
                return false;
            }

            lease.Consumers--;
            return true;
        }

        /// <summary>
        /// Releases one lease: the engine reference is dropped and the ledger records the retirement. A lease with a
        /// live consumer, or one already quarantined, is retained instead of freed, and a throwing engine release is
        /// quarantined rather than reported as a success (P-048).
        /// </summary>
        public AssetReleaseOutcome Release(Id128 leaseId)
        {
            if (!leases.TryGetValue(leaseId, out AssetLease? lease) || lease == null)
            {
                return AssetReleaseOutcome.UnknownLease;
            }

            if (lease.State == AssetLeaseState.Retired)
            {
                return AssetReleaseOutcome.AlreadyReleased;
            }

            if (lease.Consumers != 0)
            {
                lease.State = AssetLeaseState.Quarantined;
                ledger.Quarantine(lease.LedgerResourceId);
                QuarantinedCount++;
                return AssetReleaseOutcome.RetainedByConsumers;
            }

            try
            {
                backend.Release(lease.Handle);
            }
            catch (Exception)
            {
                lease.State = AssetLeaseState.Quarantined;
                ledger.Quarantine(lease.LedgerResourceId);
                QuarantinedCount++;
                return AssetReleaseOutcome.ReleaseFailed;
            }

            lease.Payload = null;
            lease.State = AssetLeaseState.Retired;
            ledger.Retire(lease.LedgerResourceId);
            ReservedBytes -= lease.Bytes;
            ReleasedCount++;
            return AssetReleaseOutcome.Released;
        }

        /// <summary>
        /// Retires the whole table: pending loads are discarded (their completions can no longer install anything,
        /// even if they arrive later), ready leases are released in reverse acquisition order, and leases held by
        /// consumers are retained and reported instead of freed (P-007, P-048).
        /// </summary>
        public AssetReleaseReport Retire()
        {
            IsRetired = true;
            var ordered = new List<AssetLease>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                ordered.Add(leases[order[i]]);
            }

            // Reverse acquisition order, the P-048 disposal order for one owner's leases.
            ordered.Sort((left, right) => right.LeaseId.CompareTo(left.LeaseId));

            var released = new List<Id128>();
            var retained = new List<Id128>();
            var failed = new List<Id128>();
            for (int i = 0; i < ordered.Count; i++)
            {
                AssetLease lease = ordered[i];
                if (lease.State == AssetLeaseState.Loading)
                {
                    // A pending load is discarded here, so a completion that arrives later can install nothing (P-007).
                    StaleDiscardCount++;
                    Discard(lease);
                }

                if (lease.State == AssetLeaseState.DiscardedStale || lease.State == AssetLeaseState.Retired)
                {
                    // Its engine reference is already dropped and its ledger record retired, so nothing is retained.
                    released.Add(lease.LeaseId);
                    continue;
                }

                if (lease.State == AssetLeaseState.Quarantined)
                {
                    // Retained because unfinished work may still reach it; teardown never frees a quarantine (P-048).
                    retained.Add(lease.LeaseId);
                    continue;
                }

                if (Release(lease.LeaseId) == AssetReleaseOutcome.Released)
                {
                    released.Add(lease.LeaseId);
                }
                else if (lease.State == AssetLeaseState.Quarantined)
                {
                    retained.Add(lease.LeaseId);
                }
                else
                {
                    failed.Add(lease.LeaseId);
                }
            }

            return new AssetReleaseReport(released, retained, failed);
        }

        /// <summary>Releases one quarantine after every user ended, allowing a later safe release (P-048).</summary>
        public bool ReleaseQuarantine(Id128 leaseId)
        {
            if (!leases.TryGetValue(leaseId, out AssetLease? lease) || lease == null
                || lease.State != AssetLeaseState.Quarantined)
            {
                return false;
            }

            if (lease.Consumers != 0)
            {
                return false;
            }

            lease.State = AssetLeaseState.Ready;
            ledger.ReleaseQuarantine(lease.LedgerResourceId);
            QuarantinedCount--;
            return true;
        }

        /// <summary>Most recent state of one lease, for evidence and for the presentation adapter's own records.</summary>
        public bool TryReadPayload(Id128 leaseId, out FrozenPayload? payload)
        {
            payload = null;
            if (!leases.TryGetValue(leaseId, out AssetLease? lease) || lease == null || !lease.IsUsable)
            {
                return false;
            }

            payload = lease.Payload;
            return true;
        }

        private void Fail(AssetLease lease, DiagnosticCode code, string detail)
        {
            _ = code;
            _ = detail;
            lease.State = AssetLeaseState.Failed;
            lease.CompletionCount++;
            FailureCount++;
            // A failed acquisition is released immediately: nothing was installed, so nothing may be retained.
            Release(lease.LeaseId);
        }

        private void Discard(AssetLease lease)
        {
            lease.State = AssetLeaseState.DiscardedStale;
            lease.Payload = null;
            lease.CompletionCount++;
            try
            {
                backend.Release(lease.Handle);
            }
            catch (Exception)
            {
                // A discarding release that throws is quarantined; it is never reported as a clean release (P-048).
                lease.State = AssetLeaseState.Quarantined;
                ledger.Quarantine(lease.LedgerResourceId);
                QuarantinedCount++;
                return;
            }

            ledger.Retire(lease.LedgerResourceId);
            ReservedBytes -= lease.Bytes;
            ReleasedCount++;
        }

        private static int CompareIds(Id128 left, Id128 right) => left.CompareTo(right);
    }
}
