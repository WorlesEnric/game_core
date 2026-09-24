#nullable enable
namespace GameCore.Validation.Probe
{
    /// <summary>
    /// Stable build-time registration keys for the GC-001 qualification player. Each literal key is the
    /// documented derivation <see cref="ProbeKey.FromStableName"/> applied to the stable name beside it; the
    /// catalog generator verifies every literal against that derivation before writing generated code.
    /// </summary>
    public static class ProbeKeys
    {
        /// <summary>Stable name of the linked-but-inactive fixture plugin.</summary>
        public const string FixturePluginStableName = "gamecore.validation.plugin.fixture";

        /// <summary>Stable name of the generated closed generic handler root.</summary>
        public const string ClosedGenericHandlerStableName = "gamecore.validation.handler.magnitude";

        /// <summary>
        /// Stable name of a plugin key that is deliberately absent from the generated catalog. The negative
        /// probe mode resolves it and requires an explicit negative result.
        /// </summary>
        public const string AbsentFixturePluginStableName = "gamecore.validation.plugin.absent";

        /// <summary>Key of the fixture plugin registration, derived from <see cref="FixturePluginStableName"/>.</summary>
        public static readonly ProbeKey FixturePlugin = new ProbeKey(0x0284B6EC6D41B5AAUL, 0xD17744CF859C74B6UL);

        /// <summary>Key of the closed generic handler registration, derived from <see cref="ClosedGenericHandlerStableName"/>.</summary>
        public static readonly ProbeKey ClosedGenericHandler = new ProbeKey(0x1C9A1F368FDAD8DDUL, 0x4E0CC4C0E9F7DA67UL);

        /// <summary>
        /// Key that the generated catalog must never contain. Used by the negative probe mode
        /// (<c>-probeMissingRegistration</c>) to prove that a missing registration is detected rather than
        /// silently constructed through reflection.
        /// </summary>
        public static readonly ProbeKey AbsentFixturePlugin = new ProbeKey(0x4771366F6C5F1EA9UL, 0xCDD9BC836D9C05AEUL);

        /// <summary>
        /// Build-time (generator) verification that every literal key above matches its documented derivation.
        /// This runs inside the Editor generator, not in the player.
        /// </summary>
        public static bool DerivationHolds()
        {
            return FixturePlugin.Equals(ProbeKey.FromStableName(FixturePluginStableName))
                && ClosedGenericHandler.Equals(ProbeKey.FromStableName(ClosedGenericHandlerStableName))
                && AbsentFixturePlugin.Equals(ProbeKey.FromStableName(AbsentFixturePluginStableName));
        }
    }
}
