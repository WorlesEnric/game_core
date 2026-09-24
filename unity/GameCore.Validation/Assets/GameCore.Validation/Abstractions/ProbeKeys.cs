#nullable enable
using System;
using GameCore.Contracts;

namespace GameCore.Validation.Probe
{
    /// <summary>
    /// Stable build-time registration keys for the GC-001 qualification player. Each literal key is the
    /// documented derivation <see cref="ProbeKey.FromStableName"/> applied to the stable name beside it; the
    /// catalog generator verifies every literal against that derivation before writing generated code.
    /// </summary>
    /// <remarks>
    /// The derivation itself is owned by the production contract assembly
    /// (<see cref="GameCore.Contracts.StableNameKeyDerivation"/>), and the generated catalog now derives the same
    /// keys with the same rule, so a literal here and the generated <see cref="FactoryKey"/> constant cannot
    /// drift apart silently.
    /// </remarks>
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

        /// <summary>Generated key version used by every registration in the probe catalog.</summary>
        public const uint KeyVersion = 1U;

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

        /// <summary>Production contract key of the fixture plugin registration, as a catalog lookup uses it (P-009).</summary>
        public static FactoryKey FixturePluginKey => new FactoryKey(FixturePlugin.ToId128(), KeyVersion);

        /// <summary>Production contract key of the closed generic handler registration.</summary>
        public static FactoryKey ClosedGenericHandlerKey => new FactoryKey(ClosedGenericHandler.ToId128(), KeyVersion);

        /// <summary>Production contract key of a registration the generated catalog must not contain.</summary>
        public static FactoryKey AbsentFixturePluginKey => new FactoryKey(AbsentFixturePlugin.ToId128(), KeyVersion);

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
