// GameCore.Studio.UI - resolves the agent gateway the UI talks to: the runtime's registered gateway when it implements
// IStudioAgentGateway (P2.2 registers it as StudioServiceRegistry.AgentGateway), else the not-configured gateway.
#nullable enable
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;

namespace GameCore.Studio.UI
{
    /// <summary>Gateway resolution.</summary>
    public static class StudioAgentGateways
    {
        public static IStudioAgentGateway Resolve(StudioRuntime runtime)
        {
            return runtime.Services.AgentGateway as IStudioAgentGateway ?? NullStudioAgentGateway.Instance;
        }
    }
}
