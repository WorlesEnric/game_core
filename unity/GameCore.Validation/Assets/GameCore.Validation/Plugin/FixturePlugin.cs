#nullable enable
using System;
using GameCore.Validation.Probe;

namespace GameCore.Validation.Fixture
{
    /// <summary>
    /// Linked-but-inactive fixture plugin. The player build contains this assembly (the generated catalog
    /// references <see cref="FixturePluginFactory"/> directly and <c>Assets/link.xml</c> preserves it), but
    /// nothing instantiates it at startup. The probe mounts it late through the generated catalog key.
    /// </summary>
    public sealed class FixturePlugin : IProbePlugin
    {
        public FixturePlugin()
        {
        }

        public ProbeKey PluginKey => ProbeKeys.FixturePlugin;

        public string PluginName => "gamecore.validation.fixture-plugin";

        public int ExecuteClosedHandler(IProbeHandler<ProbeAmount, int> handler, ProbeAmount input)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (!handler.HandlerKey.Equals(ProbeKeys.ClosedGenericHandlerKey))
            {
                throw new InvalidOperationException(
                    "closed generic handler key mismatch: expected " + ProbeKeys.ClosedGenericHandlerKey
                    + " but the generated catalog supplied " + handler.HandlerKey);
            }

            return handler.Handle(input);
        }
    }

    /// <summary>
    /// Direct constructor reference for the linked inactive fixture plugin. The generated catalog embeds
    /// exactly this factory, which is why the plugin survives managed stripping without being mounted.
    /// </summary>
    public sealed class FixturePluginFactory : IProbePluginFactory
    {
        private int createdInstanceCount;

        public ProbeKey PluginKey => ProbeKeys.FixturePlugin;

        public string StableName => ProbeKeys.FixturePluginStableName;

        public int CreatedInstanceCount => createdInstanceCount;

        public IProbePlugin Create()
        {
            createdInstanceCount++;
            return new FixturePlugin();
        }
    }
}
