// GameCore.Studio.Edit.Tests - fakes for the services a Studio runtime calls out to: the running world (live edits),
// the etos agent gateway (call counting) and a live-op translator.
#nullable enable
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Studio.Authoring;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;

namespace GameCore.Studio.Edit.Tests
{
    /// <summary>A world that executes every edit unless told to answer StalePlan.</summary>
    internal sealed class FakeLiveGateway : ILiveWorldGateway
    {
        private ulong _sequence;

        public bool IsAvailable { get; set; } = true;

        public ulong CommittedRevision { get; set; } = 7;

        /// <summary>When true the next submission is refused as StalePlan (and the world moves on).</summary>
        public bool RefuseNextAsStale { get; set; }

        public List<ulong> ExpectedRevisions { get; } = new List<ulong>();

        public LiveSubmitResult Submit(CompositionEditPayload payload, ulong expectedRevision)
        {
            ExpectedRevisions.Add(expectedRevision);
            _sequence++;
            string operation = "w:00000000000000000000000000000001/i:00000000000000000000000000000002/s:" + _sequence;
            if (RefuseNextAsStale || expectedRevision != CommittedRevision)
            {
                RefuseNextAsStale = false;
                CommittedRevision++;
                return new LiveSubmitResult(LiveSubmitStatus.Stale, operation, expectedRevision, CommittedRevision, "StalePlan");
            }

            CommittedRevision++;
            return new LiveSubmitResult(LiveSubmitStatus.Executed, operation, expectedRevision, CommittedRevision, null);
        }
    }

    /// <summary>A configured gateway that records calls and accepts every request.</summary>
    internal sealed class CountingAgentGateway : IAgentGateway
    {
        public bool IsConfigured { get; set; } = true;

        public int Calls { get; private set; }

        public ServiceResult GenerateAsset(AgentAssetRequest request)
        {
            Calls++;
            return new ServiceResult(ServiceRequestStatus.Accepted, taskId: "t_fake" + Calls);
        }

        public ServiceResult ProposeMechanism(AgentMechanismRequest request)
        {
            Calls++;
            return new ServiceResult(ServiceRequestStatus.Accepted, taskId: "t_fake" + Calls);
        }
    }

    /// <summary>Translates <c>set</c> on a fixture entity into an (empty) composition edit.</summary>
    internal sealed class FixtureLiveTranslator : ILiveOpTranslator
    {
        public int Translations { get; private set; }

        public bool CanTranslate(EditContext context)
        {
            return context.Operation.Tool == BuiltInToolIdsExt.Set && context.Target is FixtureAuthoredEntity;
        }

        public CompositionEditPayload? Translate(EditContext context, out Diagnostic? problem)
        {
            problem = null;
            Translations++;
            return new CompositionEditPayload(
                default(CompositionEditSubject),
                default,
                default,
                false,
                null,
                null,
                null,
                null,
                default,
                default,
                default,
                default,
                null,
                0,
                null,
                default);
        }
    }
}
