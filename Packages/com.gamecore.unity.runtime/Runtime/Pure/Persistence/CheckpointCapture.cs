// GameCore.Unity.Runtime (engine-free part, compiled as GameCore.Execution) - checkpoint capture (GC-018, O-20).
//
// Normative sources: 00 O-20 ("Host+serializers; snapshot boundary, command inclusion option → versioned blob and
// checksum. Preconditions: B; state stable, bounded data, explicit queue/outbox disposition, all required
// serializers available; copy then serialize off lane. Postconditions: Copy/serialization errors produce no
// checkpoint; cancel before completed file publication, remove temp artifact. Probe: active+dormant state
// round-trips without handles" — P-053–P-054), P-053 (contents and the "never ambiguously omitted" rule for queued
// external commands) and 06 s7 ("Command inclusion is explicit; the default sample adapter rejects unexecuted
// external commands with `CheckpointBoundary` before capture while preserving declared authoritative next-step
// queues.").
//
// The orchestration is a pure function of a boundary snapshot plus a queue policy: it decides the queued-command
// disposition, builds the header, frames every record through the generated codecs and produces the bytes. The
// caller owns publication (temp file, checksum, atomic replace) and performs it only after this returns true, so a
// capture that refuses leaves no artifact behind (06 s7).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
// GC-016's queue-disposition value, imported by alias: this file also names its own `QueueDisposition`, and the
// observation namespace declares `ICommittedBoundaryReader`, which would collide with this file's parameter type.
using BoundaryQueueDisposition = GameCore.Execution.Observation.BoundaryQueueDisposition;

namespace GameCore.Execution.Persistence
{
    /// <summary>What one capture decided about the queued external commands it found (P-053).</summary>
    public readonly struct QueueDisposition
    {
        /// <summary>The policy the caller requested.</summary>
        public readonly CheckpointQueuePolicy Policy;

        /// <summary>Commands offered to the capture before the policy was applied.</summary>
        public readonly int Offered;

        /// <summary>Commands written into the document; zero for <see cref="CheckpointQueuePolicy.RejectQueued"/>.</summary>
        public readonly int Included;

        /// <summary>Commands explicitly rejected instead of being silently omitted (P-053).</summary>
        public readonly int Rejected;

        /// <summary>The sealed admission cutoff recorded in the header, so a restore knows where the batch ended.</summary>
        public readonly AdmissionSequence Cutoff;

        public QueueDisposition(
            CheckpointQueuePolicy policy,
            int offered,
            int included,
            int rejected,
            AdmissionSequence cutoff)
        {
            Policy = policy;
            Offered = offered;
            Included = included;
            Rejected = rejected;
            Cutoff = cutoff;
        }

        /// <summary>True when the disposition accounts for every offered command, which is the "never ambiguous" rule.</summary>
        public bool IsAccountedFor => Included + Rejected == Offered;

        public override string ToString() =>
            "queue(" + Policy.ToString() + ",offered=" + Offered.ToString(CultureInfo.InvariantCulture)
            + ",included=" + Included.ToString(CultureInfo.InvariantCulture)
            + ",rejected=" + Rejected.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The outcome of one capture: the document bytes, its checksum and the exact contents it recorded.</summary>
    public sealed class CheckpointCaptureResult
    {
        private CheckpointCaptureResult(
            bool captured,
            DiagnosticCode code,
            string detail,
            byte[] document,
            ContentHash documentHash,
            HeaderRecordValue header,
            QueueDisposition queue,
            CheckpointCounts counts,
            SnapshotToken boundary,
            WorldId sourceWorld)
        {
            Captured = captured;
            Code = code;
            Detail = detail;
            Document = document;
            DocumentHash = documentHash;
            Header = header;
            Queue = queue;
            Counts = counts;
            Boundary = boundary;
            SourceWorld = sourceWorld;
        }

        public bool Captured { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>Canonical document bytes, complete with the container header and trailing checksum (05 s6).</summary>
        public byte[] Document { get; }

        /// <summary>SHA-256 of <see cref="Document"/>; the value a local adapter writes beside the file (06 s7).</summary>
        public ContentHash DocumentHash { get; }

        public HeaderRecordValue Header { get; }

        public QueueDisposition Queue { get; }

        public CheckpointCounts Counts { get; }

        /// <summary>The committed image the capture was taken at (P-053).</summary>
        public SnapshotToken Boundary { get; }

        /// <summary>The session captured from; the restored session is always a different, fresh id (P-004, P-049).</summary>
        public WorldId SourceWorld { get; }

        internal static CheckpointCaptureResult Refused(DiagnosticCode code, string detail, WorldId source) =>
            new CheckpointCaptureResult(
                false,
                code,
                detail,
                Array.Empty<byte>(),
                ContentHash.Empty,
                default(HeaderRecordValue),
                default(QueueDisposition),
                default(CheckpointCounts),
                default(SnapshotToken),
                source);

        internal static CheckpointCaptureResult Success(
            byte[] document,
            HeaderRecordValue header,
            QueueDisposition queue,
            CheckpointCounts counts,
            SnapshotToken boundary,
            WorldId source) =>
            new CheckpointCaptureResult(
                true,
                DiagnosticCode.None,
                "checkpoint captured at epoch "
                + boundary.AssemblyEpoch.Value.ToString(CultureInfo.InvariantCulture) + " step "
                + boundary.LogicalStepId.Value.ToString(CultureInfo.InvariantCulture) + ".",
                document,
                ContentHash.Compute(document),
                header,
                queue,
                counts,
                boundary,
                source);

        public override string ToString() =>
            Captured
                ? "captured(" + Counts.ToString() + "," + Queue.ToString() + ")"
                : "refused(" + DiagnosticCodeText.Of(Code) + ")";
    }

    /// <summary>Captures one world's committed boundary into a canonical checkpoint document (O-20, P-053).</summary>
    public static class CheckpointCapture
    {
        /// <summary>
        /// Captures through a boundary reader. The reader must own <paramref name="request"/>.World and must be at a
        /// committed boundary; a refusal writes no bytes and creates no artifact (O-20's postcondition).
        /// </summary>
        public static CheckpointCaptureResult Capture(
            ICommittedBoundaryReader reader,
            CheckpointCaptureRequest request)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!reader.World.Session.Equals(request.World.Session))
            {
                return CheckpointCaptureResult.Refused(
                    DiagnosticCode.StaleHandle,
                    "the boundary reader owns world " + reader.World.Session.ToString() + " and the request names "
                    + request.World.Session.ToString() + " (P-004).",
                    request.World);
            }

            if (!reader.IsAtCommittedBoundary)
            {
                return CheckpointCaptureResult.Refused(
                    DiagnosticCode.TooLate,
                    "a checkpoint is taken at an end-of-step or idle boundary after required jobs complete (P-053).",
                    request.World);
            }

            CheckpointCodecSet codecs = request.Codecs ?? throw new ArgumentNullException(nameof(request));
            if (!codecs.IsComplete)
            {
                return CheckpointCaptureResult.Refused(
                    DiagnosticCode.MissingDependency,
                    "the codec set is missing " + Describe(codecs.MissingKinds())
                    + "; a capture without every required serializer is refused before any copy (P-053, P-054).",
                    request.World);
            }

            if (!reader.TryRead(
                    request.World,
                    out CommittedBoundarySnapshot? snapshot,
                    out BoundaryRefusal refusal,
                    out DiagnosticCode refusalCode,
                    out string refusalDetail)
                || snapshot == null)
            {
                return CheckpointCaptureResult.Refused(refusalCode, refusalDetail, request.World);
            }

            // The queue disposition is decided before anything is framed, so the header's counts and the document's
            // records are produced from the same decision (P-053).
            var included = new List<CommandRecordValue>();
            int rejected = 0;
            for (int i = 0; i < snapshot.QueuedCommands.Count; i++)
            {
                if (request.QueuePolicy == CheckpointQueuePolicy.IncludeQueued)
                {
                    included.Add(snapshot.QueuedCommands[i]);
                }
                else
                {
                    // Rejected explicitly, before capture, rather than omitted ambiguously (P-053, 06 s7).
                    rejected++;
                }
            }

            var queue = new QueueDisposition(
                request.QueuePolicy,
                snapshot.QueuedCommands.Count,
                included.Count,
                rejected,
                snapshot.AdmissionCutoff);

            if (!queue.IsAccountedFor)
            {
                return CheckpointCaptureResult.Refused(
                    DiagnosticCode.ApplyFault,
                    "the queue disposition " + queue.ToString()
                    + " does not account for every queued command (P-053).",
                    request.World);
            }

            // W5-GATE reconciliation (see the seam header in CommittedBoundary.cs): when the world leases its
            // boundary through GC-016's observation and declares its own facts, those facts must agree with what
            // this capture copied. A world that says "three commands are queued here" while the reader could copy
            // two has an ambiguity P-053 forbids, so the capture refuses instead of recording the smaller number.
            // `Unspecified` is the honest answer of a world with no facts source and is never treated as empty.
            if (snapshot.DeclaredQueueDisposition != BoundaryQueueDisposition.Unspecified
                && snapshot.DeclaredQueuedCommandCount != queue.Offered)
            {
                return CheckpointCaptureResult.Refused(
                    DiagnosticCode.ApplyFault,
                    "the world declares " + snapshot.DeclaredQueuedCommandCount.ToString(CultureInfo.InvariantCulture)
                    + " queued command(s) at this boundary (" + snapshot.DeclaredQueueDisposition.ToString()
                    + ") but the boundary reader copied " + queue.Offered.ToString(CultureInfo.InvariantCulture)
                    + "; a capture never records an ambiguous queue (P-053).",
                    request.World);
            }

            ContentHash catalogFingerprint = request.CatalogFingerprint ?? snapshot.CatalogFingerprint;
            CanonicalId32.Split(catalogFingerprint, out ulong f0, out ulong f1, out ulong f2, out ulong f3);

            var header = new HeaderRecordValue(
                snapshot.Definition.Value.High,
                snapshot.Definition.Value.Low,
                snapshot.SourceWorld.Session.High,
                snapshot.SourceWorld.Session.Low,
                request.ProtocolMajor,
                request.ProtocolMinor,
                (uint)snapshot.Model,
                snapshot.StepDurationTicks,
                snapshot.TicksPerSecond,
                snapshot.MaxStepsPerPump,
                snapshot.UsesUnscaledHostClock,
                snapshot.LogicalStep.Value,
                snapshot.RetainedDebt.Ticks,
                snapshot.DomainSeconds,
                snapshot.PendingDemand,
                (uint)snapshot.Mode,
                f0,
                f1,
                f2,
                f3,
                (uint)request.QueuePolicy,
                snapshot.AdmissionCutoff.Value,
                (uint)rejected,
                snapshot.LastEventSequence.Value,
                (uint)snapshot.Scopes.Count,
                (uint)snapshot.Installs.Count,
                (uint)snapshot.Selections.Count,
                (uint)snapshot.Targets.Count,
                (uint)snapshot.Slots.Count,
                (uint)snapshot.Grants.Count,
                (uint)snapshot.Clocks.Count,
                (uint)included.Count,
                (uint)snapshot.NextStepMessages.Count,
                (uint)snapshot.RngStreams.Count,
                (uint)snapshot.Cursors.Count,
                snapshot.PublishedRevision.Value,
                snapshot.PublishedEpoch.Value,
                snapshot.HostTicksPerSecond,
                (uint)request.ContentRevisionCount,
                (uint)snapshot.Outbox.Count);

            var serializer = new CheckpointSerializer(codecs, request.Limits);
            if (!AddAll(serializer, CheckpointRecordKind.Scope, snapshot.Scopes, out DiagnosticCode code, out string detail)
                || !AddAll(serializer, CheckpointRecordKind.Install, snapshot.Installs, out code, out detail)
                || !AddAll(serializer, CheckpointRecordKind.Selection, snapshot.Selections, out code, out detail)
                || !AddAll(serializer, CheckpointRecordKind.Target, snapshot.Targets, out code, out detail)
                || !AddAll(serializer, CheckpointRecordKind.Slot, snapshot.Slots, out code, out detail)
                || !AddAll(serializer, CheckpointRecordKind.Grant, snapshot.Grants, out code, out detail)
                || !AddAll(serializer, CheckpointRecordKind.Clock, snapshot.Clocks, out code, out detail)
                || !AddAll(serializer, CheckpointRecordKind.Command, included, out code, out detail)
                || !AddAll(serializer, CheckpointRecordKind.Message, snapshot.NextStepMessages, out code, out detail)
                || !AddAll(serializer, CheckpointRecordKind.Rng, snapshot.RngStreams, out code, out detail)
                || !AddAll(serializer, CheckpointRecordKind.Cursor, snapshot.Cursors, out code, out detail)
                || !AddAll(serializer, CheckpointRecordKind.Outbox, snapshot.Outbox, out code, out detail))
            {
                return CheckpointCaptureResult.Refused(code, detail, request.World);
            }

            if (!serializer.TrySerialize(header, out byte[] document, out code, out detail))
            {
                return CheckpointCaptureResult.Refused(code, detail, request.World);
            }

            var boundary = new SnapshotToken(request.World, snapshot.PublishedEpoch, snapshot.LogicalStep);
            return CheckpointCaptureResult.Success(document, header, queue, serializer.Counts, boundary, request.World);
        }

        private static bool AddAll<TValue>(
            CheckpointSerializer serializer,
            CheckpointRecordKind kind,
            IReadOnlyList<TValue> values,
            out DiagnosticCode code,
            out string detail)
            where TValue : struct
        {
            code = DiagnosticCode.None;
            detail = string.Empty;

            for (int i = 0; i < values.Count; i++)
            {
                if (!serializer.TryAdd(kind, values[i], out code, out detail))
                {
                    detail = "record " + i.ToString(CultureInfo.InvariantCulture) + " of "
                        + typeof(TValue).Name + ": " + detail;
                    return false;
                }
            }

            return true;
        }

        private static string Describe(IReadOnlyList<CheckpointRecordKind> kinds)
        {
            if (kinds.Count == 0)
            {
                return "no record kind";
            }

            var text = new System.Text.StringBuilder();
            for (int i = 0; i < kinds.Count; i++)
            {
                if (i != 0)
                {
                    text.Append(", ");
                }

                text.Append(kinds[i].ToString());
            }

            return text.ToString();
        }
    }

    /// <summary>One capture request: which world, which codecs, which queue policy and which fingerprint (O-20).</summary>
    public sealed class CheckpointCaptureRequest
    {
        public CheckpointCaptureRequest(
            WorldId world,
            CheckpointCodecSet codecs,
            CheckpointQueuePolicy queuePolicy,
            ContentHash? catalogFingerprint = null,
            SerializationLimits? limits = null,
            uint contentRevisionCount = 0U,
            byte protocolMajor = CheckpointFormat.ProtocolMajor,
            byte protocolMinor = CheckpointFormat.ProtocolMinor)
        {
            World = world;
            Codecs = codecs ?? throw new ArgumentNullException(nameof(codecs));
            QueuePolicy = queuePolicy;
            CatalogFingerprint = catalogFingerprint;
            Limits = limits ?? CheckpointFormat.Limits;
            ContentRevisionCount = contentRevisionCount;
            ProtocolMajor = protocolMajor;
            ProtocolMinor = protocolMinor;
        }

        public WorldId World { get; }

        public CheckpointCodecSet Codecs { get; }

        /// <summary>The capture's explicit queued-command disposition; the default rejects them (P-053, 06 s7).</summary>
        public CheckpointQueuePolicy QueuePolicy { get; }

        /// <summary>
        /// Catalog fingerprint to record. Null records the boundary's own value; a caller that has already verified
        /// the running catalog passes it explicitly so the document names the catalog the state belongs to (P-028).
        /// </summary>
        public ContentHash? CatalogFingerprint { get; }

        public SerializationLimits Limits { get; }

        /// <summary>Immutable-definition revisions this document depends on; zero when the world declares none.</summary>
        public uint ContentRevisionCount { get; }

        public byte ProtocolMajor { get; }

        public byte ProtocolMinor { get; }
    }
}
