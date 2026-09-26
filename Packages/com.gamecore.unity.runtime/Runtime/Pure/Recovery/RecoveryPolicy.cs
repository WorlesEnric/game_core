// GameCore.Unity.Runtime (engine-free part, compiled as GameCore.Execution) - recovery policy (GC-027).
//
// Normative sources: 00 P-049 ("Pre-mutation failure leaves the old published world usable, with staged resources
// cleaned or quarantined. Post-mutation/step failure halts simulation... Recovery creates a new `WorldId` from a
// verified checkpoint or initial catalog, validates/rebuilds composition and recipes, restores state, then reopens
// admission; old callbacks/handles never become valid. No hidden automatic replay of external side effects is
// allowed. Retryable transient resource failures use host-configured bounded attempts with new operation IDs;
// correctness failures require changed input/catalog."), O-22 ("Host; Faulted world, checkpoint or initial definition
// -> stopped old world plus new session... Failure leaves old Faulted and reports new attempt failure; no implicit
// effects replay; cancel only before new publication") and P-050 (a retry is a NEW operation id, and a reserved
// session is never handed out twice, including after a failed attempt).
//
// WHAT THIS FILE IS
//
// The policy half of O-22 and nothing else: it decides, from a diagnostic code and a host-configured bound, whether
// one recovery attempt may be followed by another, and it records what each attempt did in a bounded transcript.
// It owns no world, no store and no executor; `GameCore.Unity.Runtime.Recovery.WorldRecovery` composes those.
//
// WHY IT IS SEPARATE FROM THE COMPOSITION
//
// The W5 and W6 gates both recorded the same open item: `OperationExpirySettings.BoundedTransientAttempts` was
// "stored, never enforced" - nothing consumed it, so P-049's "host-configured bounded attempts" clause was
// unproven. Classification and bounding are pure decisions, so they are engine-free and are proven by plain-dotnet
// tests as well as in a Unity EditMode suite over the identical sources.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Delivery;

namespace GameCore.Execution.Recovery
{
    /// <summary>What a recovery attempt recovers from (O-22's "explicit source").</summary>
    public enum RecoverySourceKind
    {
        /// <summary>The compiled catalog: the world's initial definitions, with no committed state carried (GC-017).</summary>
        InitialDefinition = 0,

        /// <summary>A verified checkpoint document read from a store: committed state is carried (GC-018, GC-027).</summary>
        Checkpoint = 1,
    }

    /// <summary>
    /// How a fault is injected at one recovery boundary. GC-017's latch is the mechanism for the boundaries whose
    /// owner is production code; the other two are existing seams this task deliberately reuses rather than
    /// duplicating (a second injection mechanism beside an existing one is what 00 s9 forbids for operations).
    /// </summary>
    public enum RecoveryInjectionMechanism
    {
        /// <summary>`AssemblyFaultInjection.Arm` on the owning world's latch; compiled out of a release build.</summary>
        Latch = 0,

        /// <summary>`IDeliveryStepHook.Reach` at a `DeliveryBoundaries` name; a reporting callback, test-only caller.</summary>
        DeliveryHook = 1,

        /// <summary>`ICheckpointStore.TryRead` refusing a corrupt or absent document (a real value, not a latch).</summary>
        StoreRead = 2,
    }

    /// <summary>
    /// The permitted observable result of one injection point. Every value is an outcome a scenario asserts after
    /// the fault fired; "the failed old world never resumes" and "no hidden external replay" are the two that must
    /// hold at every one of them (P-049).
    /// </summary>
    public enum RecoveryPermittedOutcome
    {
        /// <summary>Nothing was captured and no document was published; the old world keeps running (O-20).</summary>
        NoCheckpointProduced = 0,

        /// <summary>The previous verified document - if any - is still the stored one; no partial artifact exists.</summary>
        PreviousDocumentIntact = 1,

        /// <summary>No destination world was built or exposed; the source is untouched (P-049).</summary>
        DestinationNeverBuilt = 2,

        /// <summary>A destination was staged and then destroyed: it never becomes the running published world (P-031).</summary>
        DestinationNeverExposed = 3,

        /// <summary>An obligation is durable before it is handed over, or it was never accepted at all (P-045).</summary>
        ObligationDurableBeforeDelivery = 4,

        /// <summary>A redelivery reuses the obligation's idempotency key; the destination mutates at most once (P-045).</summary>
        RedeliveryReusesIdempotencyKey = 5,

        /// <summary>An acknowledgement is either recorded or redelivered, and the destination effect stays single.</summary>
        AcknowledgementRecordedOrRedeliveredOnce = 6,

        /// <summary>A new session is built from the stored document, or no world at all exists; never the old one.</summary>
        NewSessionFromStoreOrNoWorld = 7,
    }

    /// <summary>
    /// What is lost when the injection point fires, stated as a class rather than as an estimate: this is the
    /// data-loss boundary P-049 and the task require to be documented from executed evidence.
    /// </summary>
    public enum RecoveryDataLossClass
    {
        /// <summary>Nothing: the boundary is crossed before any committed state is at risk.</summary>
        None = 0,

        /// <summary>The failed attempt's own work only; no committed state and no durable obligation.</summary>
        UncommittedAttemptWork = 1,

        /// <summary>An obligation that was never made durable; a caller that required durability was refused.</summary>
        UnpersistedObligation = 2,

        /// <summary>State committed after the last verified checkpoint: replayable only from a later checkpoint.</summary>
        UncommittedSinceCheckpoint = 3,
    }

    /// <summary>
    /// One recovery boundary a fault can be injected at, with everything a scenario and an evidence file need to
    /// agree on: a stable id, the mechanism, the boundary names it covers, the permitted observable result and the
    /// data-loss class (P-049, TEST-016).
    /// </summary>
    public readonly struct RecoveryFaultPoint
    {
        public RecoveryFaultPoint(
            string id,
            RecoveryInjectionMechanism mechanism,
            IReadOnlyList<string> boundaryNames,
            RecoveryPermittedOutcome permitted,
            RecoveryDataLossClass dataLoss,
            string statement)
        {
            Id = id ?? string.Empty;
            Mechanism = mechanism;
            BoundaryNames = boundaryNames ?? Array.Empty<string>();
            Permitted = permitted;
            DataLoss = dataLoss;
            Statement = statement ?? string.Empty;
        }

        /// <summary>Stable id of the injection point; the name a transcript and an assertion share (P-052).</summary>
        public string Id { get; }

        public RecoveryInjectionMechanism Mechanism { get; }

        /// <summary>
        /// The boundary names this point covers: one `FaultBoundaryText` name for a latch, or the pair of
        /// `DeliveryBoundaries` names either side of a delivery step.
        /// </summary>
        public IReadOnlyList<string> BoundaryNames { get; }

        public RecoveryPermittedOutcome Permitted { get; }

        public RecoveryDataLossClass DataLoss { get; }

        /// <summary>One sentence stating what must be observable after the fault fired.</summary>
        public string Statement { get; }

        public override string ToString() =>
            Id + "(" + Mechanism.ToString() + ":" + string.Join("+", BoundaryNames) + ")";
    }

    /// <summary>
    /// The eight injection points GC-027 exists to cover, in the order the task names them: capture copy, file
    /// publication, restore reference repair, postwrite apply, outbox append, delivery, acknowledgement, restart.
    /// The table is production data - a scenario, a fixture file and an evidence table all assert against it - so it
    /// lives beside the policy rather than inside one test assembly.
    /// </summary>
    public static class RecoveryFaultPoints
    {
        /// <summary>Capture copy: the boundary snapshot is being copied and serialized (O-20).</summary>
        public const string CaptureCopy = "capture-copy";

        /// <summary>File publication: the captured document is being published to the store (06 s7).</summary>
        public const string CheckpointPublication = "checkpoint-publication";

        /// <summary>Restore reference repair: the destination's composition, recipes and references are rebuilt.</summary>
        public const string RestoreReferenceRepair = "restore-reference-repair";

        /// <summary>Postwrite apply during restore: the destination was written to and is not yet published.</summary>
        public const string RestorePostwriteApply = "restore-postwrite-apply";

        /// <summary>Outbox append: an obligation is made durable before it is handed over (P-045).</summary>
        public const string OutboxAppend = "outbox-append";

        /// <summary>Delivery: the obligation is handed to its destination port.</summary>
        public const string OutboxDelivery = "outbox-delivery";

        /// <summary>Acknowledgement: the destination's mutation is recorded locally.</summary>
        public const string OutboxAcknowledgement = "outbox-acknowledge";

        /// <summary>Restart: a fresh process/session builds a world from the stored document, or from nothing.</summary>
        public const string Restart = "restart";

        /// <summary>The eight points, in the task's order; the order is the evidence table's order.</summary>
        public static IReadOnlyList<RecoveryFaultPoint> All { get; } = Array.AsReadOnly(new[]
        {
            new RecoveryFaultPoint(
                CaptureCopy,
                RecoveryInjectionMechanism.Latch,
                Array.AsReadOnly(new[] { "checkpoint-capture-copy" }),
                RecoveryPermittedOutcome.NoCheckpointProduced,
                RecoveryDataLossClass.None,
                "the capture refused before it copied the boundary, so no document exists and the world that was "
                + "being captured keeps running (O-20, P-049)."),
            new RecoveryFaultPoint(
                CheckpointPublication,
                RecoveryInjectionMechanism.Latch,
                Array.AsReadOnly(new[] { "checkpoint-publication" }),
                RecoveryPermittedOutcome.PreviousDocumentIntact,
                RecoveryDataLossClass.None,
                "publication was refused, the temporary artifact was removed and any previously stored document is "
                + "still the stored one (06 s7, O-20)."),
            new RecoveryFaultPoint(
                RestoreReferenceRepair,
                RecoveryInjectionMechanism.Latch,
                Array.AsReadOnly(new[] { "restore-reference-repair" }),
                RecoveryPermittedOutcome.DestinationNeverBuilt,
                RecoveryDataLossClass.None,
                "the destination's reference tables were never rebuilt, so nothing was staged, exposed or reserved "
                + "beyond the attempt, and the source is unchanged (P-049, TEST-016 row 10)."),
            new RecoveryFaultPoint(
                RestorePostwriteApply,
                RecoveryInjectionMechanism.Latch,
                // The postwrite-apply boundary is reached twice in the restore sequence - after the staged world was
                // written to (`restore-apply`) and as the validated world is about to be published
                // (`recovery-publication`) - and both reaches must leave the same observable result: a destination
                // that never became the running world (P-030, P-031).
                Array.AsReadOnly(new[] { "restore-apply", "recovery-publication" }),
                RecoveryPermittedOutcome.DestinationNeverExposed,
                RecoveryDataLossClass.UncommittedAttemptWork,
                "the staged world was destroyed instead of being published; no epoch, revision or session became "
                + "reachable and the source world is untouched (P-031, P-049)."),
            new RecoveryFaultPoint(
                OutboxAppend,
                RecoveryInjectionMechanism.DeliveryHook,
                Array.AsReadOnly(new[] { DeliveryBoundaries.BeforeAppend, DeliveryBoundaries.AfterAppend }),
                RecoveryPermittedOutcome.ObligationDurableBeforeDelivery,
                RecoveryDataLossClass.UnpersistedObligation,
                "an obligation is handed over only after its frame is durable; a fault before the append refuses the "
                + "commit rather than delivering an obligation no journal holds (P-045)."),
            new RecoveryFaultPoint(
                OutboxDelivery,
                RecoveryInjectionMechanism.DeliveryHook,
                Array.AsReadOnly(new[] { DeliveryBoundaries.BeforeDelivery, DeliveryBoundaries.AfterDelivery }),
                RecoveryPermittedOutcome.RedeliveryReusesIdempotencyKey,
                RecoveryDataLossClass.UncommittedAttemptWork,
                "a fault after the destination was asked and before the attempt was recorded is the acknowledgement-"
                + "loss window: the obligation stays open and a redelivery carries the same idempotency key, so the "
                + "destination mutates once (P-045)."),
            new RecoveryFaultPoint(
                OutboxAcknowledgement,
                RecoveryInjectionMechanism.DeliveryHook,
                Array.AsReadOnly(new[] { DeliveryBoundaries.BeforeAcknowledge, DeliveryBoundaries.AfterAcknowledge }),
                RecoveryPermittedOutcome.AcknowledgementRecordedOrRedeliveredOnce,
                RecoveryDataLossClass.None,
                "an acknowledgement is persisted before it is applied, so a fault before it leaves the obligation "
                + "redeliverable and a fault after it leaves it settled with the destination effect unchanged (P-045)."),
            new RecoveryFaultPoint(
                Restart,
                RecoveryInjectionMechanism.StoreRead,
                Array.AsReadOnly(new[] { "store-read" }),
                RecoveryPermittedOutcome.NewSessionFromStoreOrNoWorld,
                RecoveryDataLossClass.UncommittedSinceCheckpoint,
                "a restart has no in-process state: the verified document either produces a new session or, when it "
                + "is absent or corrupt, no world at all; the faulted session is never resumed (P-049, P-053)."),
        });

        /// <summary>Declared point count; a table consumer sizes itself from this rather than from a literal.</summary>
        public static int Count => All.Count;

        /// <summary>One point by id; false when the id is unknown, which a caller reports rather than guesses.</summary>
        public static bool TryGet(string id, out RecoveryFaultPoint point)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].Id, id, StringComparison.Ordinal))
                {
                    point = All[i];
                    return true;
                }
            }

            point = default(RecoveryFaultPoint);
            return false;
        }

        /// <summary>Every boundary name the table covers, in table order, so a scenario can prove it armed each one.</summary>
        public static IReadOnlyList<string> BoundaryNames()
        {
            var names = new List<string>();
            for (int i = 0; i < All.Count; i++)
            {
                for (int n = 0; n < All[i].BoundaryNames.Count; n++)
                {
                    names.Add(All[i].BoundaryNames[n]);
                }
            }

            return names;
        }

    }

    /// <summary>
    /// Classifies one outcome for the retry rule of P-049: a transient resource failure may be attempted again under
    /// the host's bound, a correctness failure needs changed input or catalog, and a terminal state is not retried at
    /// all. The classification is a pure function of the code, so two call sites cannot disagree about it.
    /// </summary>
    public static class RecoveryFailureClassification
    {
        /// <summary>
        /// P-049's retry classification for one diagnostic code. Only `ResourceUnavailable` is retryable with the
        /// same input: it is the code a resource acquisition, a file publication or a store read reports when the
        /// failure is environmental rather than a statement about the data. `BudgetExceeded` is deliberately NOT
        /// retryable - P-022 says raising a budget is an explicit configuration change, not a retry loop - and every
        /// version, migration, ownership, identity or fault code needs changed input, a changed catalog or a
        /// different world.
        /// </summary>
        public static RetryClassification RetryOf(DiagnosticCode code)
        {
            switch (code)
            {
                case DiagnosticCode.None:
                    return RetryClassification.NotRetryable;

                case DiagnosticCode.ResourceUnavailable:
                    return RetryClassification.RetrySameInput;

                case DiagnosticCode.TooLate:
                case DiagnosticCode.StaleHandle:
                case DiagnosticCode.Cancelled:
                case DiagnosticCode.ResultExpired:
                case DiagnosticCode.ApplyFault:
                case DiagnosticCode.TeardownBlocked:
                    return RetryClassification.NotRetryable;

                default:
                    return RetryClassification.RequiresChangedInput;
            }
        }

        /// <summary>The failure classification P-049's bound applies to, derived from the retry classification.</summary>
        public static FailureClassification Of(DiagnosticCode code)
        {
            switch (RetryOf(code))
            {
                case RetryClassification.RetrySameInput:
                    return FailureClassification.Retriable;

                case RetryClassification.RequiresChangedInput:
                    return FailureClassification.CorrectnessRequiresChangedInput;

                default:
                    return FailureClassification.Fatal;
            }
        }

        /// <summary>One-line description of a classification, for a report or an evidence line.</summary>
        public static string Describe(DiagnosticCode code) =>
            DiagnosticCodeText.Of(code) + ":" + Of(code).ToString() + "/" + RetryOf(code).ToString();
    }

    /// <summary>
    /// How many attempts one recovery may make, and whether a given outcome permits the next one (P-049). The bound
    /// comes from the host configuration (`OperationExpirySettings.BoundedTransientAttempts`) instead of a constant,
    /// which is what closes the "stored, never enforced" open item the W5/W6 gates recorded. It is clamped because a
    /// host setting is still configuration: P-022's rule that raising a bound is an explicit decision, never an
    /// unbounded loop, applies to attempts exactly as it does to a budget.
    /// </summary>
    public sealed class RecoveryRetryPolicy
    {
        /// <summary>Hardest bound any host setting can ask for; a larger configured value is clamped and reported.</summary>
        public const int MaxConfiguredAttempts = 16;

        /// <summary>A policy that performs exactly one attempt; the default when no host setting is supplied.</summary>
        public static RecoveryRetryPolicy SingleAttempt { get; } = new RecoveryRetryPolicy(1, 0, false);

        private RecoveryRetryPolicy(int maxAttempts, int configuredAttempts, bool clamped)
        {
            MaxAttempts = maxAttempts;
            ConfiguredAttempts = configuredAttempts;
            WasClamped = clamped;
        }

        /// <summary>Total attempts one recovery may make; one means "no retry at all".</summary>
        public int MaxAttempts { get; }

        /// <summary>Attempts the host configuration asked for, before clamping; zero when none was supplied.</summary>
        public int ConfiguredAttempts { get; }

        /// <summary>True when the configured bound was larger than <see cref="MaxConfiguredAttempts"/>.</summary>
        public bool WasClamped { get; }

        /// <summary>Retries allowed beyond the first attempt.</summary>
        public int MaxRetries => MaxAttempts - 1;

        /// <summary>
        /// Builds the policy from the host's own expiry/retry settings. A null setting or a non-positive bound means
        /// one attempt: P-049 permits bounded retries, it never requires them.
        /// </summary>
        public static RecoveryRetryPolicy FromHostSettings(OperationExpirySettings? settings)
        {
            if (settings == null || settings.BoundedTransientAttempts <= 0)
            {
                return SingleAttempt;
            }

            int configured = settings.BoundedTransientAttempts;
            bool clamped = configured > MaxConfiguredAttempts;
            return new RecoveryRetryPolicy(clamped ? MaxConfiguredAttempts : configured, configured, clamped);
        }

        /// <summary>
        /// True when another attempt is permitted after <paramref name="attemptsMade"/> attempts whose last outcome
        /// was <paramref name="lastCode"/>. Both conditions are required: the host bound must not be exhausted and
        /// the failure must be retryable with the same input.
        /// </summary>
        public bool AllowsRetry(int attemptsMade, DiagnosticCode lastCode) =>
            attemptsMade > 0
            && attemptsMade < MaxAttempts
            && RecoveryFailureClassification.RetryOf(lastCode) == RetryClassification.RetrySameInput;

        /// <summary>Attempts this policy would make at most, given one retryable failure each time.</summary>
        public override string ToString() =>
            "retry(max=" + MaxAttempts.ToString(CultureInfo.InvariantCulture)
            + (WasClamped ? ",clampedFrom=" + ConfiguredAttempts.ToString(CultureInfo.InvariantCulture) : string.Empty)
            + ")";
    }

    /// <summary>Why one attempt exists: it is the attempt the caller asked for, or a bounded retry of a transient failure.</summary>
    public enum RecoveryAttemptKind
    {
        /// <summary>The attempt the caller requested.</summary>
        Initial = 0,

        /// <summary>A retry under the host's bound, with a new operation id and a newly reserved session (P-049, P-050).</summary>
        Retry = 1,
    }

    /// <summary>
    /// What one recovery attempt did. The destination session and the operation id are recorded because P-050's
    /// answer to "retry with new operation IDs" is only observable if the transcript shows both changing; the
    /// injection point id is recorded so a faulted attempt is attributable (P-052).
    /// </summary>
    public sealed class RecoveryAttemptRecord
    {
        public RecoveryAttemptRecord(
            int ordinal,
            RecoveryAttemptKind kind,
            RecoverySourceKind source,
            WorldId destination,
            OperationId operation,
            Outcome outcome,
            DiagnosticCode code,
            string detail,
            string faultPointId)
        {
            Ordinal = ordinal;
            Kind = kind;
            Source = source;
            Destination = destination;
            Operation = operation;
            Outcome = outcome;
            Code = code;
            Detail = detail ?? string.Empty;
            FaultPointId = faultPointId ?? string.Empty;
        }

        /// <summary>Zero-based attempt ordinal inside one recovery call.</summary>
        public int Ordinal { get; }

        public RecoveryAttemptKind Kind { get; }

        public RecoverySourceKind Source { get; }

        /// <summary>The session this attempt reserved; a retry reserves a fresh one (P-004, P-050).</summary>
        public WorldId Destination { get; }

        public OperationId Operation { get; }

        public Outcome Outcome { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>The injection point this attempt reached, or empty when none fired.</summary>
        public string FaultPointId { get; }

        public bool Failed => Outcome != Outcome.Published && Outcome != Outcome.PublishedWithCleanupErrors;

        /// <summary>Canonical one-line form, so two attempts compare as text in an evidence file.</summary>
        public string ToLine() =>
            "attempt=" + Ordinal.ToString(CultureInfo.InvariantCulture)
            + " kind=" + Kind.ToString()
            + " source=" + Source.ToString()
            + " session=" + Destination.Session.ToString()
            + " op=" + Operation.ToString()
            + " outcome=" + Outcome.ToString()
            + " code=" + DiagnosticCodeText.Of(Code)
            + (FaultPointId.Length == 0 ? string.Empty : " at=" + FaultPointId)
            + (Detail.Length == 0 ? string.Empty : " detail=" + Detail);

        public override string ToString() => ToLine();
    }

    /// <summary>
    /// The bounded transcript of one recovery: every attempt in order, so a report can show "attempt 1 refused
    /// transiently, attempt 2 published into a new session" instead of only the final answer (P-052). It is bounded
    /// because P-043's rule for a bounded buffer applies to a transcript too: the bound is the host's own retry bound
    /// plus one, and an overflow is counted rather than growing without end.
    /// </summary>
    public sealed class RecoveryAttemptLog
    {
        private readonly List<RecoveryAttemptRecord> records = new List<RecoveryAttemptRecord>();
        private readonly int capacity;

        public RecoveryAttemptLog(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "An attempt log requires positive capacity.");
            }

            this.capacity = capacity;
        }

        public int Capacity => capacity;

        /// <summary>Attempts recorded; never more than <see cref="Capacity"/>.</summary>
        public int Count => records.Count;

        /// <summary>Attempts refused because the log was full; reported, never silently dropped (P-043).</summary>
        public int OverflowCount { get; private set; }

        /// <summary>Retries among the recorded attempts.</summary>
        public int RetryCount
        {
            get
            {
                int retries = 0;
                for (int i = 0; i < records.Count; i++)
                {
                    if (records[i].Kind == RecoveryAttemptKind.Retry)
                    {
                        retries++;
                    }
                }

                return retries;
            }
        }

        /// <summary>Attempts whose outcome was a published new session.</summary>
        public int PublishedCount
        {
            get
            {
                int published = 0;
                for (int i = 0; i < records.Count; i++)
                {
                    if (!records[i].Failed)
                    {
                        published++;
                    }
                }

                return published;
            }
        }

        public IReadOnlyList<RecoveryAttemptRecord> Records => records;

        public RecoveryAttemptRecord? Last => records.Count == 0 ? null : records[records.Count - 1];

        /// <summary>Appends one attempt; false (counted) when the log is already full.</summary>
        public bool TryAdd(RecoveryAttemptRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            if (records.Count >= capacity)
            {
                OverflowCount++;
                return false;
            }

            records.Add(record);
            return true;
        }

        /// <summary>The log as LF-separated canonical lines, in order; `<empty>` when nothing was attempted.</summary>
        public string Describe()
        {
            if (records.Count == 0)
            {
                return "<empty>";
            }

            var lines = new string[records.Count];
            for (int i = 0; i < records.Count; i++)
            {
                lines[i] = records[i].ToLine();
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// True when every recorded attempt named a distinct session and a distinct operation. P-050's "new operation
        /// IDs" and "a session is never reused" are only satisfied if this holds, so a report exposes it directly.
        /// </summary>
        public bool AttemptsAreDistinct()
        {
            for (int i = 0; i < records.Count; i++)
            {
                for (int j = i + 1; j < records.Count; j++)
                {
                    if (records[i].Destination.Session.Equals(records[j].Destination.Session)
                        || records[i].Operation.Equals(records[j].Operation))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public override string ToString() =>
            "attempts(" + records.Count.ToString(CultureInfo.InvariantCulture) + "/"
            + capacity.ToString(CultureInfo.InvariantCulture) + ",retries="
            + RetryCount.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
