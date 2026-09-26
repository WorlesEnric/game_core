// GameCore.Contracts - production shared contract type (GC-003). Unity-free: BCL subset only, no
// UnityEngine/Unity.* reference, no runtime reflection and no second ECS facade (01 s1, P-058).
// Normative sources: docs/game-core/00-core-protocols.md and docs/game-core/05-contracts-and-data-model.md.
// The public surface of this assembly is API-compatible with the frozen W0 reference seam
// (tests/GameCore.ReferenceSeams); additions are reviewed in artifacts/gc-003/HANDOFF.md.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>One structured diagnostic. Codes are the literals required by P-052 (00 s7).</summary>
    public sealed class Diagnostic
    {
        public Diagnostic(
            DiagnosticCode code,
            OperationPhase phase,
            OperationId operation,
            ContentHash planHash,
            IReadOnlyList<Id128>? involvedIds,
            IReadOnlyList<FactoryKey>? involvedKeys,
            long count,
            long budgetLimit,
            RetryClassification retry,
            string summary)
        {
            Code = code;
            Phase = phase;
            Operation = operation;
            PlanHash = planHash;
            InvolvedIds = ContractCollections.Freeze(involvedIds);
            InvolvedKeys = ContractCollections.Freeze(involvedKeys);
            Count = count;
            BudgetLimit = budgetLimit;
            Retry = retry;
            Summary = summary ?? string.Empty;
            CodeText = DiagnosticCodeText.Of(code);
        }

        public DiagnosticCode Code { get; }

        public OperationPhase Phase { get; }

        /// <summary>Stable textual code; equal to the normative literal for <see cref="Code"/>.</summary>
        public string CodeText { get; }

        public OperationId Operation { get; }

        public ContentHash PlanHash { get; }

        public IReadOnlyList<Id128> InvolvedIds { get; }

        public IReadOnlyList<FactoryKey> InvolvedKeys { get; }

        public long Count { get; }

        public long BudgetLimit { get; }

        public RetryClassification Retry { get; }

        public string Summary { get; }

        public static Diagnostic Create(
            DiagnosticCode code,
            OperationPhase phase,
            OperationId operation,
            string summary) =>
            new Diagnostic(code, phase, operation, ContentHash.Empty, null, null, 0L, 0L, RetryClassification.NotRetryable, summary);
    }

    /// <summary>Maps <see cref="DiagnosticCode"/> to the exact literal used by the normative text.</summary>
    public static class DiagnosticCodeText
    {
        public static string Of(DiagnosticCode code)
        {
            switch (code)
            {
                case DiagnosticCode.None: return "None";
                case DiagnosticCode.StaleHandle: return "StaleHandle";
                case DiagnosticCode.StalePlan: return "StalePlan";
                case DiagnosticCode.MissingDependency: return "MissingDependency";
                case DiagnosticCode.ServiceConflict: return "ServiceConflict";
                case DiagnosticCode.CapabilityConflict: return "CapabilityConflict";
                case DiagnosticCode.AmbiguousOrder: return "AmbiguousOrder";
                case DiagnosticCode.Cycle: return "Cycle";
                case DiagnosticCode.Ineligible: return "Ineligible";
                case DiagnosticCode.UnsupportedVersion: return "UnsupportedVersion";
                case DiagnosticCode.OwnershipConflict: return "OwnershipConflict";
                case DiagnosticCode.BudgetExceeded: return "BudgetExceeded";
                case DiagnosticCode.MigrationRequired: return "MigrationRequired";
                case DiagnosticCode.ResourceUnavailable: return "ResourceUnavailable";
                case DiagnosticCode.Cancelled: return "Cancelled";
                case DiagnosticCode.TooLate: return "TooLate";
                case DiagnosticCode.IdempotencyConflict: return "IdempotencyConflict";
                case DiagnosticCode.ResultExpired: return "ResultExpired";
                case DiagnosticCode.ApplyFault: return "ApplyFault";
                case DiagnosticCode.SnapshotBackpressure: return "SnapshotBackpressure";
                case DiagnosticCode.TeardownBlocked: return "TeardownBlocked";
                case DiagnosticCode.CursorExpired: return "CursorExpired";
                default: throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown diagnostic code.");
            }
        }

        public static bool TryParse(string? text, out DiagnosticCode code)
        {
            code = DiagnosticCode.None;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (DiagnosticCode candidate in Values)
            {
                if (string.Equals(Of(candidate), text, StringComparison.Ordinal))
                {
                    code = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Every code required by the protocol, in P-052's order, excluding <see cref="DiagnosticCode.None"/>.
        /// <see cref="DiagnosticCode.SnapshotBackpressure"/> follows the P-052 list because P-007 requires it.
        /// </summary>
        public static IReadOnlyList<DiagnosticCode> Values { get; } = Array.AsReadOnly(new[]
        {
            DiagnosticCode.StaleHandle,
            DiagnosticCode.StalePlan,
            DiagnosticCode.MissingDependency,
            DiagnosticCode.ServiceConflict,
            DiagnosticCode.CapabilityConflict,
            DiagnosticCode.AmbiguousOrder,
            DiagnosticCode.Cycle,
            DiagnosticCode.Ineligible,
            DiagnosticCode.UnsupportedVersion,
            DiagnosticCode.OwnershipConflict,
            DiagnosticCode.BudgetExceeded,
            DiagnosticCode.MigrationRequired,
            DiagnosticCode.ResourceUnavailable,
            DiagnosticCode.Cancelled,
            DiagnosticCode.TooLate,
            DiagnosticCode.IdempotencyConflict,
            DiagnosticCode.ResultExpired,
            DiagnosticCode.ApplyFault,
            DiagnosticCode.TeardownBlocked,
            DiagnosticCode.CursorExpired,
            DiagnosticCode.SnapshotBackpressure,
        });
    }
}
