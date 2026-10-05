// GameCore.Studio.Authoring - interfaces other packets implement and the Studio tools delegate to
// (docs/studio/03-authoring-contracts.md s5, 02 boundaries C/D, 06 P2.2/P2.4/P3).
//   IAgentGateway  - asset.generate and mechanism.propose open etos work through the companion (P2.2, boundary D).
//   IExplainSource - inspect.explain answers "why did/didn't this fire" from gameplay traces (gameplay.logic et al.).
//   IBuildLane     - project.build / project.launch (the build lane of P3/P4).
// Until an implementation is registered with the Studio runtime, the tools answer NotConfigured.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Authoring
{
    /// <summary>Status of a request handed to another boundary.</summary>
    public enum ServiceRequestStatus
    {
        /// <summary>The request was accepted (work runs asynchronously; results arrive later as candidates).</summary>
        Accepted,
        /// <summary>The request completed synchronously.</summary>
        Completed,
        /// <summary>No implementation is configured (diagnostic NotConfigured).</summary>
        NotConfigured,
        /// <summary>The service refused the request (diagnostic carries the code).</summary>
        Refused,
        /// <summary>The service is blocked on an external prerequisite (diagnostic Blocked).</summary>
        Blocked,
    }

    /// <summary>Result of an agent, explain or build request.</summary>
    public sealed class ServiceResult
    {
        public ServiceResult(ServiceRequestStatus status, JObject? output = null, Diagnostic? diagnostic = null, string? taskId = null, ArtifactRef? artifact = null)
        {
            Status = status;
            Output = output;
            Diagnostic = diagnostic;
            TaskId = taskId;
            Artifact = artifact;
        }

        public ServiceRequestStatus Status { get; }

        /// <summary>Service-specific payload (an explanation, build report, task summary).</summary>
        public JObject? Output { get; }

        public Diagnostic? Diagnostic { get; }

        /// <summary>etos task id when the request opened a task.</summary>
        public string? TaskId { get; }

        /// <summary>The produced artifact when it is already known.</summary>
        public ArtifactRef? Artifact { get; }

        public bool Succeeded => Status == ServiceRequestStatus.Accepted || Status == ServiceRequestStatus.Completed;

        public static ServiceResult NotConfigured(string service, string hint)
        {
            return new ServiceResult(
                ServiceRequestStatus.NotConfigured,
                diagnostic: new Diagnostic(DiagnosticCodes.NotConfigured, "No " + service + " is configured.", hint));
        }
    }

    /// <summary>A request to generate an asset through an etos task (03 s5 <c>asset.generate</c>).</summary>
    public sealed class AgentAssetRequest
    {
        public AgentAssetRequest(string changeSetId, string opId, string kind, string prompt, AuthoringRef? target, JObject? options)
        {
            ChangeSetId = changeSetId ?? throw new ArgumentNullException(nameof(changeSetId));
            OpId = opId ?? throw new ArgumentNullException(nameof(opId));
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            Target = target;
            Options = options;
        }

        public string ChangeSetId { get; }

        public string OpId { get; }

        /// <summary>What to generate: <c>image</c>, <c>texture</c>, <c>voice</c>, <c>mesh</c>, ...</summary>
        public string Kind { get; }

        public string Prompt { get; }

        public AuthoringRef? Target { get; }

        public JObject? Options { get; }
    }

    /// <summary>A request for a new mechanism package (03 s5/s8 <c>mechanism.propose</c>); never applied directly.</summary>
    public sealed class AgentMechanismRequest
    {
        public AgentMechanismRequest(string changeSetId, string opId, string description, IReadOnlyList<AuthoringRef> context, JObject? options)
        {
            ChangeSetId = changeSetId ?? throw new ArgumentNullException(nameof(changeSetId));
            OpId = opId ?? throw new ArgumentNullException(nameof(opId));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Options = options;
        }

        public string ChangeSetId { get; }

        public string OpId { get; }

        public string Description { get; }

        public IReadOnlyList<AuthoringRef> Context { get; }

        public JObject? Options { get; }
    }

    /// <summary>The Unity side of boundary D for tool calls (implemented by com.gamecore.studio.etos, P2.2).</summary>
    public interface IAgentGateway
    {
        /// <summary>True when the companion is paired and reachable.</summary>
        bool IsConfigured { get; }

        /// <summary>Opens an asset generation task; the artifact arrives later through a candidate change set.</summary>
        ServiceResult GenerateAsset(AgentAssetRequest request);

        /// <summary>Opens a mechanic task; the staged package arrives later through the staging lane.</summary>
        ServiceResult ProposeMechanism(AgentMechanismRequest request);
    }

    /// <summary>Explains why a rule, condition or interaction did or did not fire (03 s5 <c>inspect.explain</c>).</summary>
    public interface IExplainSource
    {
        /// <summary>Stable id of the source (e.g. <c>logic.rules</c>).</summary>
        string Id { get; }

        /// <summary>True when this source can explain <paramref name="target"/>.</summary>
        bool CanExplain(AuthoringRef target, JObject? args);

        ServiceResult Explain(AuthoringRef target, JObject? args);
    }

    /// <summary>The build lane (03 s5 <c>project.build</c>, <c>project.launch</c>; filled by P3/P4).</summary>
    public interface IBuildLane
    {
        bool IsConfigured { get; }

        /// <summary>Builds a player; <paramref name="options"/> carries target, profile and output path.</summary>
        ServiceResult Build(JObject? options);

        /// <summary>Launches the last (or a named) build.</summary>
        ServiceResult Launch(JObject? options);
    }
}
