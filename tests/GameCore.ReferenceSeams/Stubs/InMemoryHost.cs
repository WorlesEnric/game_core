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
    /// Duplicate submissions with the same input hash return the original result; a reused operation id
    /// with a different input hash is an <see cref="DiagnosticCode.IdempotencyConflict"/> (P-050).
    /// </summary>
    public sealed class InMemoryHost : ICompositionCommands, ICommandIngress, IOperationReader, IOperationControl
    {
        private readonly Dictionary<OperationId, OperationRecord> records = new Dictionary<OperationId, OperationRecord>();
        private readonly List<OperationId> submissionOrder = new List<OperationId>();
        private readonly Outcome defaultOutcome;

        public InMemoryHost(Outcome defaultOutcome)
        {
            this.defaultOutcome = defaultOutcome;
        }

        /// <summary>Every accepted operation id in submission order; the only ordering this stub exposes.</summary>
        public IReadOnlyList<OperationId> SubmissionOrder => submissionOrder;

        public int SubmissionCount => submissionOrder.Count;

        public int DuplicateCount { get; private set; }

        public int ConflictCount { get; private set; }

        public OperationId Submit(CompositionProposal proposal)
        {
            if (proposal == null)
            {
                throw new ArgumentNullException(nameof(proposal));
            }

            Record(proposal.Operation, HashOf(proposal.EditPayload));
            return proposal.Operation;
        }

        public OperationId Submit(CommandEnvelope command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            Record(command.RequestId, HashOf(command.Payload));
            return command.RequestId;
        }

        public OperationResult Read(OperationId operation)
        {
            if (!records.TryGetValue(operation, out OperationRecord record))
            {
                return new OperationResult(
                    operation,
                    Outcome.Rejected,
                    DiagnosticCodeText.Of(DiagnosticCode.ResultExpired),
                    null);
            }

            return record.Result;
        }

        public CancelOutcome Cancel(OperationId cancellationOperation, OperationId target)
        {
            if (!records.TryGetValue(target, out OperationRecord record))
            {
                return CancelOutcome.Unknown;
            }

            if (record.Result.IsTerminal)
            {
                return CancelOutcome.TooLate;
            }

            records[target] = new OperationRecord(record.InputHash, new OperationResult(
                target,
                Outcome.Cancelled,
                DiagnosticCodeText.Of(DiagnosticCode.Cancelled),
                null));
            records[cancellationOperation] = new OperationRecord(
                ContentHash.Empty,
                new OperationResult(
                    cancellationOperation,
                    Outcome.Published,
                    string.Empty,
                    null));
            submissionOrder.Add(cancellationOperation);
            return CancelOutcome.Cancelled;
        }

        private void Record(OperationId operation, ContentHash inputHash)
        {
            if (records.TryGetValue(operation, out OperationRecord existing))
            {
                if (existing.InputHash == inputHash)
                {
                    DuplicateCount++;
                    return;
                }

                ConflictCount++;
                records[operation] = new OperationRecord(inputHash, new OperationResult(
                    operation,
                    Outcome.Rejected,
                    DiagnosticCodeText.Of(DiagnosticCode.IdempotencyConflict),
                    null));
                return;
            }

            records.Add(operation, new OperationRecord(inputHash, new OperationResult(
                operation,
                defaultOutcome,
                string.Empty,
                null)));
            submissionOrder.Add(operation);
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
            public readonly OperationResult Result;

            public OperationRecord(ContentHash inputHash, OperationResult result)
            {
                InputHash = inputHash;
                Result = result;
            }
        }
    }
}
