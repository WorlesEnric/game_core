// GameCore.Unity.App — SADR-010 (studio): a boot either produces the game's world or a typed, named failure.
//
// There is no third outcome. An infrastructure-only world in place of the game's world is exactly the silent
// substitution SR-7.1 forbids ("start the player with a corrupted catalog: it exits with a named failure code, not an
// empty world"), so every refusal on the boot path becomes one `GameApplicationBootFailed` carrying a stable code,
// the protocol diagnostic code of the module that refused, the boot stage and the refusing module's own detail.
#nullable enable
using System;
using GameCore.Contracts;

namespace GameCore.Unity.App
{
    /// <summary>Stable, named reasons a game application refuses to boot (SADR-010).</summary>
    public enum GameApplicationBootCode
    {
        None = 0,

        /// <summary>The definition is structurally incomplete (no root scope, no issuer, no world definition).</summary>
        InvalidDefinition = 1,

        /// <summary>The definition declares no catalog fingerprint, so the world could only be created with an empty one.</summary>
        CatalogHashMissing = 2,

        /// <summary>The catalog's fingerprint differs from the declared one: a corrupted or substituted catalog.</summary>
        CatalogFingerprintMismatch = 3,

        /// <summary>A plugin declaration was refused by the catalog (unregistered factory, missing schema; P-009).</summary>
        PluginDeclarationRejected = 4,

        /// <summary>The ownership/schedule pipeline refused the declared stages (P-034, P-039, P-040).</summary>
        ScheduleRejected = 5,

        /// <summary>The application bootstrap fell back to an infrastructure-only world (FallbackCount > 0).</summary>
        BootstrapFallback = 6,

        /// <summary>The world host refused creation.</summary>
        WorldCreationFailed = 7,

        /// <summary>A configuration binding table is invalid (SADR-013).</summary>
        ConfigBindingInvalid = 8,

        /// <summary>A boot-script target seed was refused.</summary>
        TargetSeedRefused = 9,

        /// <summary>A boot-script composition edit was refused by the lane or the world.</summary>
        BootEditRefused = 10,

        /// <summary>The lane and the world do not publish the same assembly after boot (P-006).</summary>
        PublicationSeriesSplit = 11,

        /// <summary>A world is already booted by this application root path.</summary>
        AlreadyBooted = 12,

        /// <summary>An unexpected exception escaped a kernel module while composing the root.</summary>
        CompositionFault = 13,
    }

    /// <summary>One typed boot failure, raised to the caller and logged (SADR-010).</summary>
    public sealed class GameApplicationBootFailed
    {
        public GameApplicationBootFailed(GameApplicationBootCode code, DiagnosticCode diagnostic, string stage, string detail)
        {
            Code = code == GameApplicationBootCode.None ? GameApplicationBootCode.CompositionFault : code;
            Diagnostic = diagnostic == DiagnosticCode.None ? DiagnosticCode.ApplyFault : diagnostic;
            Stage = stage ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public GameApplicationBootCode Code { get; }

        /// <summary>The protocol code of the module that refused (P-028).</summary>
        public DiagnosticCode Diagnostic { get; }

        /// <summary>The boot stage that refused, e.g. "catalog", "schedule", "world", "boot-step:mount-a".</summary>
        public string Stage { get; }

        /// <summary>The refusing module's own detail.</summary>
        public string Detail { get; }

        public override string ToString() =>
            "GameApplicationBootFailed{code=" + Code.ToString() + ", diagnostic=" + DiagnosticCodeText.Of(Diagnostic)
            + ", stage=" + Stage + ", detail=" + Detail + "}";
    }

    /// <summary>The exception <see cref="GameApplication.Boot"/> throws; it carries the typed failure.</summary>
    public sealed class GameApplicationBootException : Exception
    {
        public GameApplicationBootException(GameApplicationBootFailed failure)
            : base((failure ?? throw new ArgumentNullException(nameof(failure))).ToString())
        {
            Failure = failure;
        }

        public GameApplicationBootFailed Failure { get; }
    }
}
