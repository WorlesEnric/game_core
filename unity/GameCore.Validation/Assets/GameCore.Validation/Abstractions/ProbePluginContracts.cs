#nullable enable
using System;

namespace GameCore.Validation.Probe
{
    /// <summary>
    /// Fixture plugin surface for the qualification player. A plugin is created only through a generated
    /// catalog factory; nothing resolves plugin types by reflection.
    /// </summary>
    public interface IProbePlugin
    {
        ProbeKey PluginKey { get; }

        string PluginName { get; }

        /// <summary>
        /// Executes the supplied generated closed generic handler. The plugin receives the handler instead of
        /// resolving the generated catalog itself, so the fixture assembly never depends on generated code.
        /// </summary>
        int ExecuteClosedHandler(IProbeHandler<ProbeAmount, int> handler, ProbeAmount input);
    }

    /// <summary>
    /// Generated catalog factory entry. <see cref="CreatedInstanceCount"/> lets the probe prove that a plugin
    /// linked into the player was not instantiated at startup and was mounted exactly once, late, by ID.
    /// </summary>
    public interface IProbePluginFactory
    {
        ProbeKey PluginKey { get; }

        string StableName { get; }

        int CreatedInstanceCount { get; }

        IProbePlugin Create();
    }

    /// <summary>One generated plugin registration: an explicit key plus a direct constructor reference.</summary>
    public sealed class ProbePluginRegistration
    {
        public ProbePluginRegistration(ProbeKey key, string stableName, IProbePluginFactory factory)
        {
            Key = key;
            StableName = stableName ?? throw new ArgumentNullException(nameof(stableName));
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public ProbeKey Key { get; }

        public string StableName { get; }

        public IProbePluginFactory Factory { get; }
    }

    /// <summary>One generated handler registration: an explicit key plus a direct closed generic reference.</summary>
    public sealed class ProbeHandlerRegistration
    {
        public ProbeHandlerRegistration(ProbeKey key, string stableName, IProbeHandler<ProbeAmount, int> handler)
        {
            Key = key;
            StableName = stableName ?? throw new ArgumentNullException(nameof(stableName));
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public ProbeKey Key { get; }

        public string StableName { get; }

        public IProbeHandler<ProbeAmount, int> Handler { get; }
    }
}
