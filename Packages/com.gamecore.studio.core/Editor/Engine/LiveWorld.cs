// GameCore.Studio.Edit - Play-mode world edits through the unity.app bridge (docs/studio/03-authoring-contracts.md s7,
// SADR-011). A Live operation whose tool has a registered ILiveOpTranslator becomes one CompositionEditPayload
// submitted with the revision the change set was staged against. The kernel's StalePlan refusal maps to Conflict
// with data {expected, actual}; other refusals map to Refused. The executed OperationId is recorded in the journal
// (outcome.gameCoreOps and links.gameCoreOps) as "w:<session>/i:<issuer>/s:<sequence>".
#nullable enable
using System;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Unity.App;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Studio.Edit
{
    /// <summary>How one live submission ended.</summary>
    public enum LiveSubmitStatus
    {
        Executed,
        /// <summary>The world moved past the expected revision (kernel StalePlan).</summary>
        Stale,
        Refused,
        /// <summary>No running world (not in Play, or the application is stopped).</summary>
        NoWorld,
    }

    /// <summary>The result of one live submission.</summary>
    public sealed class LiveSubmitResult
    {
        public LiveSubmitResult(LiveSubmitStatus status, string? operationId, ulong expectedRevision, ulong actualRevision, string? detail)
        {
            Status = status;
            OperationId = operationId;
            ExpectedRevision = expectedRevision;
            ActualRevision = actualRevision;
            Detail = detail;
        }

        public LiveSubmitStatus Status { get; }

        /// <summary>The GameCore OperationId text of the executed (or refused) edit.</summary>
        public string? OperationId { get; }

        public ulong ExpectedRevision { get; }

        /// <summary>The committed revision after the submission (or at refusal).</summary>
        public ulong ActualRevision { get; }

        public string? Detail { get; }
    }

    /// <summary>The running world, as the edit engine sees it (a fake in tests).</summary>
    public interface ILiveWorldGateway
    {
        /// <summary>True when a world is running and accepts edits.</summary>
        bool IsAvailable { get; }

        /// <summary>The committed composition revision (the expected revision of the next edit).</summary>
        ulong CommittedRevision { get; }

        LiveSubmitResult Submit(CompositionEditPayload payload, ulong expectedRevision);
    }

    /// <summary>The default gateway over <see cref="GameApplication.Current"/>.</summary>
    public sealed class GameApplicationLiveGateway : ILiveWorldGateway
    {
        public bool IsAvailable
        {
            get
            {
                GameApplicationRoot? root = GameApplication.Current;
                return root != null && root.State != GameApplicationState.Stopped;
            }
        }

        public ulong CommittedRevision => GameApplication.Current?.Lane.Committed.Revision.Value ?? 0UL;

        public LiveSubmitResult Submit(CompositionEditPayload payload, ulong expectedRevision)
        {
            GameApplicationRoot? root = GameApplication.Current;
            if (root == null || root.State == GameApplicationState.Stopped)
            {
                return new LiveSubmitResult(LiveSubmitStatus.NoWorld, null, expectedRevision, 0UL, "No GameCore world is running.");
            }

            WorldAdmissionReport report = root.Submit(payload, new CompositionRevision(expectedRevision));
            ulong actual = root.Lane.Committed.Revision.Value;
            string operation = Format(report.Operation);
            if (report.Outcome == BridgeOutcome.Executed)
            {
                return new LiveSubmitResult(LiveSubmitStatus.Executed, operation, expectedRevision, actual, null);
            }

            DiagnosticCode code = report.Refusal?.Code ?? report.RefusalCode;
            string detail = report.Outcome.ToString() + ": " + DiagnosticCodeText(code) + (report.Refusal != null ? " (" + report.Refusal.Witness + ")" : string.Empty);
            return new LiveSubmitResult(code == DiagnosticCode.StalePlan ? LiveSubmitStatus.Stale : LiveSubmitStatus.Refused, operation, expectedRevision, actual, detail);
        }

        /// <summary>"w:&lt;session&gt;/i:&lt;issuer&gt;/s:&lt;sequence&gt;" (03 s6 gameCoreOps).</summary>
        public static string Format(OperationId operation)
        {
            return "w:" + operation.World.Session.ToString() + "/i:" + operation.IssuerId.ToString() + "/s:" + operation.IssuerSequence.ToString(CultureInfo.InvariantCulture);
        }

        private static string DiagnosticCodeText(DiagnosticCode code) => code.ToString();
    }
}
