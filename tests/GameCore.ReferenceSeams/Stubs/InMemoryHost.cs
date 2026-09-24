// Test-only deterministic stub (namespace GameCore.TestFixtures) for the W0 reference seam.
// In-memory control-lane double: submission order is a list, lookups use dictionaries, and no enumeration
// order ever decides an outcome (P-008, P-050).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.TestFixtures
{
    /// <summary>
    /// Deterministic in-memory host double implementing the admission, status and cancellation seams.
    /// A retransmission with the same input hash returns the original result; a reused operation id with a
    /// different input hash reports <see cref="DiagnosticCode.IdempotencyConflict"/> and leaves the original
    /// ledger row untouched, so a conflicting reuse can never overwrite a stored outcome (P-050).
    /// </summary>
    public sealed class InMemoryHost : ICompositionCommands, ICommandIngress, IOperationReader, IOperationControl
    {
        private readonly Dictionary<OperationId, OperationRecord> records = new Dictionary<OperationId, OperationRecord>();
        private readonly List<OperationId> submissionOrder = new List<OperationId>();
        private readonly HashSet<OperationId> expired = new HashSet<OperationId>();
        private readonly int retainedResultLimit;
        private readonly Outcome defaultOutcome;
        private AdmissionSequence nextSequence = AdmissionSequence.Zero;

        public InMemoryHost(Outcome defaultOutcome, int retainedResultLimit)
        {
            this.defaultOutcome = defaultOutcome;
            if (retainedResultLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(retainedResultLimit), "A retained-result limit must be positive.");
            }

            this.retainedResultLimit = retainedResultLimit;
        }

        /// <summary>Last conflict observed, or <see cref="DiagnosticCode.None"/> when none has occurred.</summary>
        public DiagnosticCode LastConflictCode { get; private set; }

        /// <summary>Every accepted operation id in submission order; the only ordering this stub exposes.</summary>
        public IReadOnlyList<OperationId> SubmissionOrder => submissionOrder;

        public int SubmissionCount => submissionOrder.Count;

        public int DuplicateCount { get; private set; }

        public int ConflictCount { get; private set; }

        /// <summary>Operations whose retained result was dropped by retention, observable as Expired.</summary>
        public int ExpiredCount => expired.Count;

        public OperationStatusHandle Submit(CompositionProposal proposal)
        {
            if (proposal == null)
            {
                throw new ArgumentNullException(nameof(proposal));
            }

            ContentHash inputHash = HashOf(proposal.EditPayload);
            Record(proposal.Operation, inputHash);
            CompositionRevision submittedAgainst = records.TryGetValue(proposal.Operation, out OperationRecord stored)
                ? stored.Entry.Handle.SubmittedAgainst
                : CompositionRevision.Zero;
            return new OperationStatusHandle(proposal.Operation, submittedAgainst);
        }

        public CommandAdmissionReceipt Submit(CommandEnvelope command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            ContentHash inputHash = HashOf(command.Payload);
            bool admitted = Record(command.RequestId, inputHash);
            RequestResult result = admitted
                ? new RequestResult(RequestResultKind.Accepted, DiagnosticCode.None, default(EventCursor))
                : new RequestResult(RequestResultKind.Rejected, LastConflictCode, default(EventCursor));
            return new CommandAdmissionReceipt(command.RequestId, result, nextSequence);
        }

        /// <summary>
        /// Distinguishes a handle that was never admitted (<see cref="OperationReadOutcome.Unknown"/>) from one
        /// whose retained result has been dropped (<see cref="OperationReadOutcome.Expired"/>) (05 s5, P-050).
        /// </summary>
        public OperationReadResult Read(OperationId operation)
        {
            if (expired.Contains(operation))
            {
                return OperationReadResult.Expired();
            }

            return records.TryGetValue(operation, out OperationRecord record)
                ? OperationReadResult.Found(record.Entry)
                : OperationReadResult.Unknown();
        }

        public CancelOutcome Cancel(OperationId cancellationOperation, OperationId target)
        {
            if (!records.TryGetValue(target, out OperationRecord record))
            {
                return CancelOutcome.Unknown;
            }

            if (record.Entry.IsTerminal)
            {
                return CancelOutcome.TooLate;
            }

            records[target] = record.WithEntry(new OperationLedgerEntry(
                record.Handle,
                record.InputHash,
                Outcome.Cancelled,
                DiagnosticCode.Cancelled,
                CompositionRevision.Zero,
                AssemblyEpoch.Zero,
                null));
            Record(cancellationOperation, ContentHash.Empty);
            return CancelOutcome.Cancelled;
        }

        /// <summary>Returns true when the operation is newly admitted; false for a duplicate or a conflict.</summary>
        private bool Record(OperationId operation, ContentHash inputHash)
        {
            if (records.TryGetValue(operation, out OperationRecord existing))
            {
                if (existing.InputHash == inputHash)
                {
                    DuplicateCount++;
                    return false;
                }

                // Conflicting reuse is reported and the original ledger row is left exactly as it was (P-050).
                ConflictCount++;
                LastConflictCode = DiagnosticCode.IdempotencyConflict;
                return false;
            }

            AdmissionSequence assigned = nextSequence;
            nextSequence.TryIncrement(out AdmissionSequence incremented);
            nextSequence = incremented;
            OperationLedgerEntry entry = new OperationLedgerEntry(
                new OperationStatusHandle(operation, CompositionRevision.Zero),
                inputHash,
                defaultOutcome,
                DiagnosticCode.None,
                CompositionRevision.Zero,
                AssemblyEpoch.Zero,
                null);
            records.Add(operation, new OperationRecord(inputHash, assigned, entry));
            submissionOrder.Add(operation);
            Trim();
            return true;
        }

        private void Trim()
        {
            while (submissionOrder.Count > retainedResultLimit)
            {
                OperationId oldest = submissionOrder[0];
                submissionOrder.RemoveAt(0);
                records.Remove(oldest);
                expired.Add(oldest);
            }
        }

        private static ContentHash HashOf(FrozenPayload payload)
        {
            byte[] copy = new byte[payload.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = payload.Bytes[i];
            }

            return ContentHash.Compute(copy);
        }

        private readonly struct OperationRecord
        {
            public readonly ContentHash InputHash;

            /// <summary>Host-assigned admitted sequence; canonical replay compares this order (P-037).</summary>
            public readonly AdmissionSequence Sequence;

            public readonly OperationLedgerEntry Entry;

            public OperationRecord(ContentHash inputHash, AdmissionSequence sequence, OperationLedgerEntry entry)
            {
                InputHash = inputHash;
                Sequence = sequence;
                Entry = entry;
            }

            public OperationStatusHandle Handle => Entry.Handle;

            public OperationRecord WithEntry(OperationLedgerEntry entry) => new OperationRecord(InputHash, Sequence, entry);
        }
    }
}
