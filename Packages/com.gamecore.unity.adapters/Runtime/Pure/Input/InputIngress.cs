// GameCore.Unity.Adapters — stamped typed input ingress (GC-019).
//
// Normative sources: 00 P-007 (an async work token carries world, installation generation, activation epoch and
// operation id; a stale completion may release its own resources but MUST NOT publish state or reacquire execution
// authority), P-037 (a host-assigned admission sequence seals each step's batch; commands arriving after the cutoff
// wait for the next step; duplicate request keys return their recorded result), P-042 (the envelope carries request
// key, target stable ids, payload schema/revision and expected domain version; admission acceptance is not gameplay
// success), P-047 (callback gates are checked on dispatch and on completion of in-flight work) and 04 s7 ("Host
// samples UI/device input; validates and stamps typed commands with source sequence/world identity"; "A sampled key
// press is not itself a committed game event").
//
// Two rules shape this file:
//   * The adapter never writes ECS and never stages a committed event. Its whole output is a `CommandEnvelope`
//     offered to the world's *existing* command port (`ICommandIngress`), so input keeps the one admission path the
//     kernel already owns (P-002, O-13).
//   * A completion that arrives after the world, the installation generation or the activation epoch moved on is
//     discarded and only its own acquisition is released (P-007, P-047). That check is order-independent and reads
//     the real serialized gate, not a clock.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Unity.Adapters.Input
{
    /// <summary>
    /// Identity of one sampled input event: world incarnation, the sampling source and that source's own strictly
    /// increasing sequence, plus the committed boundary current when the sample was taken (P-004, P-050).
    /// </summary>
    public readonly struct InputSourceStamp : IEquatable<InputSourceStamp>
    {
        public readonly WorldId World;
        public readonly Id128 Source;
        public readonly ulong Sequence;

        /// <summary>Committed step the source observed when it sampled; diagnostics and replay provenance only.</summary>
        public readonly LogicalStepId SampledStep;

        /// <summary>Committed assembly epoch the source observed when it sampled; must match to be acted on.</summary>
        public readonly AssemblyEpoch SampledEpoch;

        public InputSourceStamp(
            WorldId world,
            Id128 source,
            ulong sequence,
            LogicalStepId sampledStep,
            AssemblyEpoch sampledEpoch)
        {
            World = world;
            Source = source;
            Sequence = sequence;
            SampledStep = sampledStep;
            SampledEpoch = sampledEpoch;
        }

        /// <summary>
        /// False for a degenerate stamp. Sequence 0 is reserved exactly like generation 0 in P-005, so a default
        /// stamp is never a live sample and is refused instead of being read as the first event of a source.
        /// </summary>
        public bool IsAllocated => !Source.IsDefault && Sequence != 0UL && !World.Session.IsDefault;

        /// <summary>The operation identity the sample becomes: one source sequence is one issuer sequence (P-050).</summary>
        public OperationId Operation => new OperationId(World, Source, Sequence);

        public bool Equals(InputSourceStamp other) =>
            World.Equals(other.World)
            && Source.Equals(other.Source)
            && Sequence == other.Sequence
            && SampledStep.Equals(other.SampledStep)
            && SampledEpoch.Equals(other.SampledEpoch);

        public override bool Equals(object? obj) => obj is InputSourceStamp other && Equals(other);

        public override int GetHashCode() =>
            unchecked((World.GetHashCode() * 397) ^ (Source.GetHashCode() * 31) ^ Sequence.GetHashCode());

        public static bool operator ==(InputSourceStamp left, InputSourceStamp right) => left.Equals(right);

        public static bool operator !=(InputSourceStamp left, InputSourceStamp right) => !left.Equals(right);

        public override string ToString() =>
            Source.ToString() + "#" + Sequence.ToString(CultureInfo.InvariantCulture)
            + "@" + SampledStep.Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One typed command a source sampled. It is an unadmitted proposal: nothing in it is authoritative until the
    /// world's command port admits it and a committed step executes it (P-042, 04 s7).
    /// </summary>
    public sealed class SampledInputCommand
    {
        public SampledInputCommand(
            InputSourceStamp stamp,
            RouteId route,
            TargetId target,
            SchemaRef schema,
            ulong? expectedDomainVersion,
            FrozenPayload payload)
        {
            Stamp = stamp;
            Route = route;
            Target = target;
            Schema = schema;
            ExpectedDomainVersion = expectedDomainVersion;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public InputSourceStamp Stamp { get; }

        public RouteId Route { get; }

        public TargetId Target { get; }

        public SchemaRef Schema { get; }

        /// <summary>Optional guard on the target domain's own version, enforced at host admission (P-042).</summary>
        public ulong? ExpectedDomainVersion { get; }

        public FrozenPayload Payload { get; }

        /// <summary>
        /// The immutable envelope the world's command port receives. The request key is the sample's operation
        /// identity, so a retransmitted sample reuses one key and can never execute twice (P-037, P-050).
        /// </summary>
        public CommandEnvelope ToEnvelope() =>
            new CommandEnvelope(Stamp.Operation, Route, Target, Schema, ExpectedDomainVersion, Payload);

        /// <summary>
        /// Canonical identity of the sampled content. Two samples with one request key and the same hash are a
        /// retransmission; the same key with a different hash is an `IdempotencyConflict`, never a second attempt.
        /// </summary>
        public ulong InputHash()
        {
            ulong hash = 1469598103934665603UL;
            hash = Mix(hash, Route.Value.High);
            hash = Mix(hash, Route.Value.Low);
            hash = Mix(hash, Target.Value.High);
            hash = Mix(hash, Target.Value.Low);
            hash = Mix(hash, Schema.Id.Value.High);
            hash = Mix(hash, Schema.Id.Value.Low);
            hash = Mix(hash, (ulong)Schema.Version);
            hash = Mix(hash, ExpectedDomainVersion.HasValue ? ExpectedDomainVersion.Value + 1UL : 0UL);
            for (int i = 0; i < Payload.Bytes.Count; i++)
            {
                hash = Mix(hash, Payload.Bytes[i]);
            }

            return hash;
        }

        public override string ToString() =>
            "input(" + Route.ToString() + "->" + Target.ToString() + ", " + Schema.ToString() + ")";

        private static ulong Mix(ulong hash, ulong value)
        {
            unchecked
            {
                hash ^= value;
                return hash * 1099511628211UL;
            }
        }
    }

    public enum InputAdmissionOutcome
    {
        /// <summary>The command port admitted a new request.</summary>
        Admitted = 0,

        /// <summary>The same request key and the same input returned the recorded admission (P-050).</summary>
        Retransmission = 1,

        /// <summary>The stamp names another world incarnation (P-004).</summary>
        RejectedForeignWorld = 2,

        /// <summary>The source sequence went backwards; a source's sequences are strictly increasing (P-050).</summary>
        RejectedSequenceRegression = 3,

        /// <summary>The stamp or payload is degenerate; a default stamp is never a live sample (P-005).</summary>
        RejectedMalformedSample = 4,

        /// <summary>The recorded result is outside retention and cannot re-execute (P-050).</summary>
        RejectedResultExpired = 5,

        /// <summary>The same request key carried different content (P-050).</summary>
        IdempotencyConflict = 6,

        /// <summary>The command port itself refused or cancelled the request (route, capacity, lifecycle, domain).</summary>
        Refused = 7,
    }

    /// <summary>One admission result. Admission acceptance is not gameplay success (P-042).</summary>
    public sealed class InputAdmissionResult
    {
        public InputAdmissionResult(
            InputAdmissionOutcome outcome,
            DiagnosticCode code,
            string detail,
            InputSourceStamp stamp,
            CommandAdmissionReceipt? admission)
        {
            Outcome = outcome;
            Code = code;
            Detail = detail ?? string.Empty;
            Stamp = stamp;
            Admission = admission;
        }

        public InputAdmissionOutcome Outcome { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        public InputSourceStamp Stamp { get; }

        /// <summary>The world's own receipt; null when the adapter refused the sample before offering it.</summary>
        public CommandAdmissionReceipt? Admission { get; }

        public bool Accepted => Outcome == InputAdmissionOutcome.Admitted || Outcome == InputAdmissionOutcome.Retransmission;

        public override string ToString() => Outcome + "(" + DiagnosticCodeText.Of(Code) + ": " + Detail + ")";
    }

    /// <summary>
    /// The input adapter's typed ingress. It stamps and validates samples and offers them to the world's existing
    /// command port; it owns no state that gameplay can read and it never stages a committed event.
    /// </summary>
    public sealed class TypedInputIngress
    {
        private readonly ICommandIngress ingress;
        private readonly Dictionary<Id128, ulong> lastSequenceBySource = new Dictionary<Id128, ulong>();
        private readonly Dictionary<Id128, Dictionary<ulong, RetainedAdmission>> retained =
            new Dictionary<Id128, Dictionary<ulong, RetainedAdmission>>();
        private readonly List<RetainedKey> retainedOrder = new List<RetainedKey>();

        public TypedInputIngress(WorldId world, ICommandIngress ingress, uint retainedAdmissions = 64U)
        {
            if (world.Session.IsDefault)
            {
                throw new ArgumentException("The input ingress must name a live world session (P-004).", nameof(world));
            }

            World = world;
            this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
            RetainedAdmissionCapacity = retainedAdmissions == 0U ? 1U : retainedAdmissions;
        }

        public WorldId World { get; }

        /// <summary>Bounded retention of admission results; a hit outside it is `ResultExpired` (P-050).</summary>
        public uint RetainedAdmissionCapacity { get; }

        public int SubmittedCount { get; private set; }

        public int AdmittedCount { get; private set; }

        public int RetransmissionCount { get; private set; }

        public int RefusedCount { get; private set; }

        public int ConflictCount { get; private set; }

        public int ExpiredResultCount { get; private set; }

        /// <summary>Distinct sampling sources this ingress has seen; one sequence namespace per source (P-050).</summary>
        public int SourceCount => lastSequenceBySource.Count;

        /// <summary>Samples refused because a later sequence of the same source had already been issued (P-050).</summary>
        public int RegressionCount { get; private set; }

        /// <summary>Recorded admissions kept for idempotent retry; outside this bound a retry is `ResultExpired`.</summary>
        public int RetainedAdmissionCount => retainedOrder.Count;

        /// <summary>
        /// Validates, stamps and submits one sample. The adapter's checks are exactly the ones the protocol gives an
        /// input adapter: world identity, a live stamp, a strictly increasing source sequence, and idempotent retry.
        /// Route, capacity, lifecycle and domain validation stay with the world's own command port (P-037, O-13).
        /// </summary>
        public InputAdmissionResult Submit(SampledInputCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            InputSourceStamp stamp = command.Stamp;
            if (!stamp.World.Equals(World))
            {
                RefusedCount++;
                return Reject(
                    InputAdmissionOutcome.RejectedForeignWorld,
                    DiagnosticCode.StaleHandle,
                    "the sample was stamped by world " + stamp.World.Session.ToString() + ", not this world (P-004)",
                    stamp);
            }

            if (!stamp.IsAllocated || command.Schema.Id.IsDefault)
            {
                RefusedCount++;
                return Reject(
                    InputAdmissionOutcome.RejectedMalformedSample,
                    DiagnosticCode.UnsupportedVersion,
                    "the sample carries a default stamp or schema, so it is not a typed command (P-005)",
                    stamp);
            }

            ulong last = lastSequenceBySource.TryGetValue(stamp.Source, out ulong seen) ? seen : 0UL;
            if (stamp.Sequence < last)
            {
                RefusedCount++;
                return Reject(
                    InputAdmissionOutcome.RejectedSequenceRegression,
                    DiagnosticCode.IdempotencyConflict,
                    "source sequence " + stamp.Sequence.ToString(CultureInfo.InvariantCulture)
                    + " is behind the last issued " + last.ToString(CultureInfo.InvariantCulture)
                    + "; a source's sequences are strictly increasing (P-050)",
                    stamp);
            }

            if (stamp.Sequence == last)
            {
                return Retry(command, stamp);
            }

            // A gap is legal: a source's sequence identifies its own events, it is not required to be contiguous.
            lastSequenceBySource[stamp.Source] = stamp.Sequence;
            CommandAdmissionReceipt receipt = ingress.Submit(command.ToEnvelope());
            Retain(stamp.Source, stamp.Sequence, command.InputHash(), receipt);
            return Classify(receipt, stamp);
        }

        private InputAdmissionResult Retry(SampledInputCommand command, InputSourceStamp stamp)
        {
            if (!retained.TryGetValue(stamp.Source, out Dictionary<ulong, RetainedAdmission>? bySequence)
                || bySequence == null
                || !bySequence.TryGetValue(stamp.Sequence, out RetainedAdmission previous))
            {
                RegressionCount++;
                ExpiredResultCount++;
                RefusedCount++;
                return Reject(
                    InputAdmissionOutcome.RejectedResultExpired,
                    DiagnosticCode.ResultExpired,
                    "the request key was issued earlier but its recorded result is outside retention (P-050)",
                    stamp);
            }

            if (previous.InputHash != command.InputHash())
            {
                ConflictCount++;
                RefusedCount++;
                return Reject(
                    InputAdmissionOutcome.IdempotencyConflict,
                    DiagnosticCode.IdempotencyConflict,
                    "the request key was already used with different content (P-050)",
                    stamp);
            }

            RetransmissionCount++;
            return new InputAdmissionResult(
                InputAdmissionOutcome.Retransmission,
                DiagnosticCode.None,
                "the recorded admission is returned; the command does not execute twice (P-050)",
                stamp,
                previous.Receipt);
        }

        private InputAdmissionResult Classify(CommandAdmissionReceipt receipt, InputSourceStamp stamp)
        {
            if (receipt.Admitted)
            {
                AdmittedCount++;
                return new InputAdmissionResult(
                    InputAdmissionOutcome.Admitted,
                    DiagnosticCode.None,
                    "the world's command port admitted the request; admission is not gameplay success (P-042)",
                    stamp,
                    receipt);
            }

            RefusedCount++;
            return new InputAdmissionResult(
                InputAdmissionOutcome.Refused,
                receipt.Result.Reason,
                "the world's command port refused or cancelled the request (O-13)",
                stamp,
                receipt);
        }

        private static InputAdmissionResult Reject(
            InputAdmissionOutcome outcome,
            DiagnosticCode code,
            string detail,
            InputSourceStamp stamp) =>
            new InputAdmissionResult(outcome, code, detail, stamp, null);

        private void Retain(Id128 source, ulong sequence, ulong inputHash, CommandAdmissionReceipt receipt)
        {
            if (!retained.TryGetValue(source, out Dictionary<ulong, RetainedAdmission>? bySequence)
                || bySequence == null)
            {
                bySequence = new Dictionary<ulong, RetainedAdmission>();
                retained.Add(source, bySequence);
            }

            bySequence[sequence] = new RetainedAdmission(inputHash, receipt);
            retainedOrder.Add(new RetainedKey(source, sequence));
            while ((uint)retainedOrder.Count > RetainedAdmissionCapacity)
            {
                RetainedKey oldest = retainedOrder[0];
                retainedOrder.RemoveAt(0);
                if (retained.TryGetValue(oldest.Source, out Dictionary<ulong, RetainedAdmission>? table)
                    && table != null)
                {
                    table.Remove(oldest.Sequence);
                }
            }
        }

        private readonly struct RetainedAdmission
        {
            public readonly ulong InputHash;
            public readonly CommandAdmissionReceipt Receipt;

            public RetainedAdmission(ulong inputHash, CommandAdmissionReceipt receipt)
            {
                InputHash = inputHash;
                Receipt = receipt;
            }
        }

        private readonly struct RetainedKey
        {
            public readonly Id128 Source;
            public readonly ulong Sequence;

            public RetainedKey(Id128 source, ulong sequence)
            {
                Source = source;
                Sequence = sequence;
            }
        }
    }

    /// <summary>Outcome of one delayed input completion (P-047: a stale completion is discarded, not delivered).</summary>
    public enum InputCompletionOutcome
    {
        /// <summary>The gate still admitted the token, so the sampled command reached the command port.</summary>
        Dispatched = 0,

        /// <summary>A world, route, activation or publication-fence check discarded it (P-047).</summary>
        Discarded = 1,

        /// <summary>The pending table is full; the work is refused explicitly rather than silently dropped (P-043).</summary>
        RefusedBackpressure = 2,

        /// <summary>No such pending completion; a duplicate completion is not a second dispatch.</summary>
        UnknownRequest = 3,
    }

    /// <summary>What one delayed completion did, with the gate decision that produced it.</summary>
    public sealed class InputCompletionResult
    {
        public InputCompletionResult(
            InputCompletionOutcome outcome,
            CallbackGateDecision gate,
            DiagnosticCode code,
            string detail,
            InputAdmissionResult? admission)
        {
            Outcome = outcome;
            Gate = gate;
            Code = code;
            Detail = detail ?? string.Empty;
            Admission = admission;
        }

        public InputCompletionOutcome Outcome { get; }

        /// <summary>The real gate decision; a discard names which check refused it (P-047).</summary>
        public CallbackGateDecision Gate { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>Admission of the sampled command, when the gate dispatched it.</summary>
        public InputAdmissionResult? Admission { get; }

        public override string ToString() =>
            Outcome + "(" + Gate + ", " + DiagnosticCodeText.Of(Code) + ": " + Detail + ")";
    }

    /// <summary>
    /// Bounded table of delayed input completions. An input source that cannot answer synchronously (a platform
    /// dialog, an OS gesture recognizer, a remote device) registers its async work token here; the completion is
    /// validated against the world's real callback gate at completion, so a sample whose world, installation
    /// generation, activation epoch or route moved on cannot reach the command port (P-007, P-047).
    /// </summary>
    public sealed class PendingInputCompletionTable
    {
        private readonly ICallbackGate gate;
        private readonly TypedInputIngress ingress;
        // The pending set is keyed by the async work token itself, whose identity is the whole stamp (P-007). A
        // folded hash as a key would let two distinct stamps collide, which is exactly the ambiguity to avoid.
        private readonly HashSet<AsyncWorkToken> pending = new HashSet<AsyncWorkToken>();
        private readonly List<AsyncWorkToken> order = new List<AsyncWorkToken>();

        public PendingInputCompletionTable(ICallbackGate gate, TypedInputIngress ingress, uint capacity)
        {
            this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
            this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
            if (capacity == 0U)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "A pending table must have a positive capacity.");
            }

            Capacity = capacity;
        }

        public uint Capacity { get; }

        public int PendingCount => pending.Count;

        public int RegisteredCount { get; private set; }

        public int DispatchedCount { get; private set; }

        public int DiscardedCount { get; private set; }

        public int BackpressureCount { get; private set; }

        public int UnknownCompletionCount { get; private set; }

        /// <summary>Staged acquisitions released because their completion was discarded (P-007, P-048).</summary>
        public int ReleasedStagedCount { get; private set; }

        /// <summary>
        /// Registers one in-flight input request. A full table refuses with `BudgetExceeded` rather than evicting an
        /// in-flight request, because dropping one silently is what P-043 forbids.
        /// </summary>
        public bool TryRegister(AsyncWorkToken token, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (!token.IsAllocated)
            {
                code = DiagnosticCode.StaleHandle;
                detail = "a pending completion must carry an allocated async work token (P-005)";
                return false;
            }

            if (pending.Contains(token))
            {
                code = DiagnosticCode.IdempotencyConflict;
                detail = "that async work identity is already pending (P-050)";
                return false;
            }

            if ((uint)pending.Count >= Capacity)
            {
                BackpressureCount++;
                code = DiagnosticCode.BudgetExceeded;
                detail = "the pending input table is at capacity "
                    + Capacity.ToString(CultureInfo.InvariantCulture)
                    + "; the request is refused, never silently dropped (P-043)";
                return false;
            }

            RegisteredCount++;
            pending.Add(token);
            order.Add(token);
            return true;
        }

        /// <summary>
        /// Completes one pending request. The gate is evaluated *now*, on completion, so a token issued before an
        /// unload or a world replacement is discarded and only its own staged acquisition is released (P-007, P-047).
        /// </summary>
        public InputCompletionResult Complete(AsyncWorkToken token, SampledInputCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            AsyncWorkToken key = token;
            if (!pending.Contains(key))
            {
                UnknownCompletionCount++;
                return new InputCompletionResult(
                    InputCompletionOutcome.UnknownRequest,
                    CallbackGateDecision.DiscardRetiredRoute,
                    DiagnosticCode.ResultExpired,
                    "no such pending input completion; a duplicate completion is not a second dispatch (P-050)",
                    null);
            }

            pending.Remove(key);
            RemoveOrder(key);

            CallbackGateDecision decision = gate.Evaluate(token);
            if (decision != CallbackGateDecision.Dispatch)
            {
                DiscardedCount++;
                ReleasedStagedCount++;
                return new InputCompletionResult(
                    InputCompletionOutcome.Discarded,
                    decision,
                    DecisionCode(decision),
                    "the completion is discarded and only its own acquisition is released (P-007, P-047)",
                    null);
            }

            InputAdmissionResult admission = ingress.Submit(command);
            if (admission.Accepted)
            {
                DispatchedCount++;
            }
            else
            {
                // The gate admitted the token but the sample itself did not become a command: that is an adapter
                // rejection, reported as a refusal rather than counted as a dispatch.
                DiscardedCount++;
            }

            return new InputCompletionResult(
                InputCompletionOutcome.Dispatched,
                decision,
                admission.Code,
                "the gate admitted the token at completion, so the sampled command reached the command port (P-047)",
                admission);
        }

        /// <summary>Cancels one pending request; the caller owns the acquisition and releases it (P-007).</summary>
        public bool Cancel(AsyncWorkToken token)
        {
            AsyncWorkToken key = token;
            if (!pending.Remove(key))
            {
                return false;
            }

            RemoveOrder(key);
            DiscardedCount++;
            return true;
        }

        public void Clear()
        {
            pending.Clear();
            order.Clear();
        }

        private static DiagnosticCode DecisionCode(CallbackGateDecision decision)
        {
            switch (decision)
            {
                case CallbackGateDecision.DiscardForeignWorld:
                    return DiagnosticCode.StaleHandle;
                case CallbackGateDecision.DiscardStaleActivation:
                    return DiagnosticCode.UnsupportedVersion;
                case CallbackGateDecision.DiscardRetiredRoute:
                    return DiagnosticCode.Cancelled;
                default:
                    return DiagnosticCode.TooLate;
            }
        }

        private void RemoveOrder(AsyncWorkToken token)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i].Equals(token))
                {
                    order.RemoveAt(i);
                    return;
                }
            }
        }
    }
}
