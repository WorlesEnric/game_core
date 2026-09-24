#nullable enable
using System;
using System.Runtime.InteropServices;
using GameCore.Validation.Generated;
using Unity.Burst;
using UnityEngine;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// Environment facts recorded with every probe result
    /// (<c>docs/game-core/00-core-protocols.md</c> P-060). Anything a player cannot query at runtime is
    /// reported as a declared value together with the build-script setting that enforces it.
    /// </summary>
    public static class ProbeEnvironment
    {
        /// <summary>Editor and package baseline this probe is written against.</summary>
        public const string DeclaredUnityVersion = "6000.0.75f1";

        /// <summary>Selected build host/target for this qualification profile.</summary>
        public const string DeclaredTarget = "StandaloneLinux64";

        /// <summary>Release qualification stripping level required by the baseline.</summary>
        public const string ManagedStrippingLevel = "High";

        /// <summary>Where the stripping value comes from, so the artifact does not overstate runtime evidence.</summary>
        public const string ManagedStrippingLevelSource =
            "declared: GameCore.Validation.Editor.BuildProbe sets and re-reads "
            + "PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.High); "
            + "a player cannot query the stripping level at runtime";

        /// <summary>Runtime Unity version reported by the player.</summary>
        public static string UnityVersion => Application.unityVersion;

        /// <summary>Runtime platform the player was launched on.</summary>
        public static string Platform => Application.platform.ToString();

        /// <summary>Process architecture reported by the runtime.</summary>
        public static string Architecture => RuntimeInformation.ProcessArchitecture.ToString();

        /// <summary>Processor description reported by the engine; corroborates the architecture field.</summary>
        public static string ProcessorType
        {
            get
            {
                string processorType = SystemInfo.processorType;
                return string.IsNullOrEmpty(processorType) ? "unknown" : processorType;
            }
        }

        /// <summary>Compile-time scripting backend of the running player.</summary>
#if ENABLE_IL2CPP
        public static string ScriptingBackend => "IL2CPP";
#else
        public static string ScriptingBackend => "Mono";
#endif

        /// <summary>True when the player was produced with the IL2CPP scripting backend.</summary>
#if ENABLE_IL2CPP
        public static bool IsIl2Cpp => true;
#else
        public static bool IsIl2Cpp => false;
#endif

        /// <summary>True for a 64-bit player process.</summary>
        public static bool Is64BitProcess => IntPtr.Size == 8;

        /// <summary>
        /// Burst enablement observed inside this player. In a build this is only true when Burst AOT compilation
        /// ran and generated the player's Burst code (Burst 1.8.28 <c>BurstCompiler.IsEnabled</c>), so the Burst
        /// probe fails when a build silently disabled Burst.
        /// </summary>
        public static bool BurstCompilerEnabled => BurstCompiler.IsEnabled;

        /// <summary>
        /// How the fixture plugin is guaranteed to be linked into the player without being instantiated at
        /// startup. A player cannot inspect the linker configuration at runtime, so this is a declared value
        /// beside the observable evidence (the factory exists and reports zero instances before the late mount).
        /// </summary>
        public const string FixturePluginPreservation =
            "declared: Assets/link.xml preserves the GameCore.Validation.Fixture assembly (preserve=\"all\"), "
            + "and the generated catalog holds a direct FixturePluginFactory reference, so the plugin is linked "
            + "into the player while nothing instantiates it at startup";

        /// <summary>Generated catalog file name reported by the generated catalog itself.</summary>
        public static string CatalogGeneratedFile => ProbeCatalog.GeneratedFileName;

        /// <summary>Hash of the generated catalog file prefix, embedded by the content compiler.</summary>
        public static string CatalogFileHash => ProbeCatalog.CatalogFileHash;

        /// <summary>Canonical fingerprint of the generated registrations (P-028, P-053).</summary>
        public static string CatalogFingerprint => ProbeCatalog.CatalogFingerprint;

        /// <summary>Description format the generated catalog was compiled from.</summary>
        public static string CatalogDescriptionFormat => ProbeCatalog.DescriptionFormat;

        /// <summary>Protocol version the generated catalog declares (P-055).</summary>
        public static string CatalogProtocolVersion => ProbeCatalog.ProtocolVersion;

        /// <summary>Declared protocol features this build supports, in canonical identity order (P-055).</summary>
        public static int CatalogSupportedFeatureCount => ProbeCatalog.SupportedFeatureIds.Length;

        /// <summary>Hash algorithm of <see cref="CatalogFileHash"/>.</summary>
        public static string CatalogFileHashAlgorithm => ProbeCatalog.HashAlgorithm;

        /// <summary>Exact scope of <see cref="CatalogFileHash"/>, so the verification command is unambiguous.</summary>
        public static string CatalogFileHashScope => ProbeCatalog.CatalogFileHashScope;
    }
}
