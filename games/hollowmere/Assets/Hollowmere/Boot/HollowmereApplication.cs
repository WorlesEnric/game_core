// Hollowmere - the player's application registration (P3.1, P1.7a A11 / SADR-010).
//
// At RuntimeInitializeLoadType.SubsystemRegistration - before the Entities bootstrap and before any scene - the player
// loads HollowmereApplicationAssets from Resources, composes Hollowmere's application definition exactly as GameBoot
// does (the baked world plan with the player, NPC, interaction, UI and audio extensions, plus the logic, inventory,
// quest and dialogue modules' declarations) and hands it to GameApplication.Register. The one application bootstrap
// then creates THE game world with the real catalog fingerprint under the hard-failure policy (no infrastructure-only
// world beside it), and composes the root, left Ready. When Boot.unity's GameBoot starts it adopts that root
// (GameBoot.Adopted): it re-parents the UI/audio rig created here under itself, attaches the narrative layer with the
// same plan and module instances the definition was composed from, installs the sessions and starts the root.
//
// The composed plan, modules and rig live on one DontDestroyOnLoad object (this component), not in static fields; the
// object is consumed by the first GameBoot. New Game and Restart reload Boot.unity: GameBoot then boots its own root
// (NarrativeComposer.TryBoot) as before, since the registered root stopped with the first GameBoot.
//
// Editor Play Mode does not register: Play Mode tests and the Studio boot worlds themselves (NarrativeComposer.Boot,
// GameApplication.Boot), which a registered bootstrap root would refuse as AlreadyBooted. -noRegister opts a player out.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Unity.Adapters;
using GameCore.Unity.App;
using Hollowmere.Narrative;
using UnityEngine;

namespace Hollowmere.Boot
{
    /// <summary>Hollowmere's application definition and the instances it was composed from (one composition).</summary>
    public sealed class HollowmereComposition
    {
        internal HollowmereComposition(
            WorldBuildPlan plan,
            GameApplicationDefinition definition,
            HollowmereNarrativeModules modules,
            PlayerWorldExtension playerExtension,
            NpcWorldExtension npcExtension,
            InteractionWorldExtension interactionExtension)
        {
            Plan = plan;
            Definition = definition;
            Modules = modules;
            PlayerExtension = playerExtension;
            NpcExtension = npcExtension;
            InteractionExtension = interactionExtension;
        }

        public WorldBuildPlan Plan { get; }

        /// <summary>The base plan extended with the narrative modules' declarations (what the root is composed from).</summary>
        public GameApplicationDefinition Definition { get; }

        public HollowmereNarrativeModules Modules { get; }

        public PlayerWorldExtension PlayerExtension { get; }

        public NpcWorldExtension NpcExtension { get; }

        public InteractionWorldExtension InteractionExtension { get; }

        /// <summary>The world build options GameBoot uses (one place, so a registered and a booted world compose alike).</summary>
        public static WorldBuildOptions BuildOptions() =>
            new WorldBuildOptions { Name = "Hollowmere", MaxEventsPerStep = 64, MaxRetainedEvents = 1024 };

        /// <summary>
        /// Composes Hollowmere's definition without booting it: GameBoot's extensions and UI/audio rig (created under
        /// <paramref name="host"/>), then NarrativeComposer.Boot's planning half (catalog, plan, models, declarations).
        /// </summary>
        public static bool TryCompose(
            RegionManifest manifest,
            GameplayContentManifest content,
            PlayerDefinition? player,
            NpcRoster? npcs,
            InteractionRoster? interactions,
            GameObject host,
            out HollowmereComposition? composition,
            out string failure)
        {
            composition = null;
            failure = string.Empty;
            if (manifest == null || content == null || host == null)
            {
                failure = "the region manifest, the content manifest and a host object are required";
                return false;
            }

            if (!string.Equals(content.FormatId, GameplayContentManifest.Format, StringComparison.Ordinal)
                || !string.Equals(content.WorldId, manifest.WorldId, StringComparison.Ordinal))
            {
                failure = NarrativeDiagnosticCodes.ContentStale + ": the content manifest is not a bake of world " + manifest.WorldId;
                return false;
            }

            if (!GameplayCatalog.TryBuild(manifest.CatalogTypeName, out ICatalog? catalog, out ContentHash fingerprint, out string detail) || catalog == null)
            {
                failure = detail;
                return false;
            }

            try
            {
                var playerExtension = new PlayerWorldExtension(player);
                var npcExtension = new NpcWorldExtension(npcs);
                var interactionExtension = new InteractionWorldExtension(interactions);
                WorldBuildOptions build = BuildOptions();
                build.Extensions.Add(playerExtension);
                build.Extensions.Add(npcExtension);
                build.Extensions.Add(interactionExtension);
                UiAudioBootstrap.Configure(build, host);
                var modules = new HollowmereNarrativeModules();
                UiAudioBootstrap.AssignViews(UiAudioBootstrap.RigOf(host), modules);

                WorldBuildPlan plan = WorldBuilder.Build(manifest, catalog, fingerprint, build);
                var converters = new List<INarrativeContentConverter>();
                for (int i = 0; i < modules.All.Count; i++)
                {
                    if (modules.All[i].Converter != null)
                    {
                        converters.Add(modules.All[i].Converter!);
                    }
                }

                NarrativeModelSet models = NarrativeContent.Build(content, converters);
                var narrative = new NarrativeComposition(manifest, content, models, new NarrativeIndex(manifest, content, models));
                for (int i = 0; i < modules.All.Count; i++)
                {
                    modules.All[i].Declare(narrative);
                }

                composition = new HollowmereComposition(plan, narrative.Extend(plan.Definition), modules, playerExtension, npcExtension, interactionExtension);
                return true;
            }
            catch (Exception refused) when (refused is InvalidOperationException || refused is ArgumentException)
            {
                failure = refused.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// The registered composition, kept on a DontDestroyOnLoad object until the first GameBoot adopts the registered root
    /// (see the file header).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HollowmereApplication : MonoBehaviour
    {
        public const string OptOutArgument = "-noRegister";

        /// <summary>The composition the registered definition came from (null before Prepare or after a failure).</summary>
        public HollowmereComposition? Composition { get; private set; }

        public HollowmereApplicationAssets? Assets { get; private set; }

        /// <summary>Why the registration did not happen (empty when it did).</summary>
        public string Failure { get; private set; } = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterAtStartup()
        {
            if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), OptOutArgument) >= 0)
            {
                return;
            }

            var holder = new GameObject("[Hollowmere Application]");
            DontDestroyOnLoad(holder);
            HollowmereApplication application = holder.AddComponent<HollowmereApplication>();
            if (!application.Prepare(Resources.Load<HollowmereApplicationAssets>(HollowmereApplicationAssets.ResourcePath)))
            {
                Debug.LogWarning("[Hollowmere] no application registration (GameBoot boots its own root): " + application.Failure);
                Destroy(holder);
                return;
            }

            GameApplication.Register(application.Composition!.Definition, new GameApplicationBootOptions { StartImmediately = false });
            Debug.Log("[Hollowmere] registered the application definition at SubsystemRegistration (" + application.Composition.Definition.Plugins.Count + " plugins)");
        }

        /// <summary>Composes the definition from <paramref name="assets"/> with the UI/audio rig under this object.</summary>
        public bool Prepare(HollowmereApplicationAssets? assets)
        {
            Assets = assets;
            if (assets == null || assets.Manifest == null || assets.Content == null)
            {
                Failure = "no HollowmereApplicationAssets with a region and a content manifest at Resources/" + HollowmereApplicationAssets.ResourcePath;
                return false;
            }

            if (!HollowmereComposition.TryCompose(assets.Manifest, assets.Content, assets.Player, assets.Npcs, assets.Interactions, gameObject, out HollowmereComposition? composed, out string failure)
                || composed == null)
            {
                Failure = failure;
                return false;
            }

            Composition = composed;
            return true;
        }

        /// <summary>
        /// The registered root GameBoot may adopt: the live, not yet started root composed from this composition's
        /// definition, booted for the same assets GameBoot holds; else null.
        /// </summary>
        public GameApplicationRoot? AdoptableRoot(RegionManifest? manifest, GameplayContentManifest? content, PlayerDefinition? player, NpcRoster? npcs, InteractionRoster? interactions, out string reason)
        {
            GameApplicationRoot? root = GameApplication.Current;
            reason = string.Empty;
            if (Composition == null || Assets == null)
            {
                reason = "the registration has no composition (" + Failure + ")";
            }
            else if (root == null)
            {
                reason = "the application bootstrap composed no root (bootstraps " + GameCoreApplicationBootstrap.BootstrapCount
                    + ", last code " + GameCoreApplicationBootstrap.LastCode + ", last failed " + GameCoreApplicationBootstrap.LastBootFailed
                    + ", detail '" + GameCoreApplicationBootstrap.LastDetail + "', boot failure " + (GameApplication.LastFailure?.ToString() ?? "none") + ")";
            }
            else if (!ReferenceEquals(root.Definition, Composition.Definition))
            {
                reason = "the live root " + root + " was not composed from the registered definition";
            }
            else if (root.State != GameApplicationState.Ready)
            {
                reason = "the registered root is " + root.State + ", not Ready";
            }
            else if (!Assets.Matches(manifest, content, player, npcs, interactions))
            {
                reason = "GameBoot holds other assets than Resources/" + HollowmereApplicationAssets.ResourcePath;
            }

            return reason.Length == 0 ? root : null;
        }

        /// <summary>Hands the rig to <paramref name="boot"/> and retires this object (the composition is consumed).</summary>
        public void HandOver(Transform boot)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                transform.GetChild(i).SetParent(boot, false);
            }

            Composition = null;
            Destroy(gameObject);
        }

        /// <summary>Retires an unadopted registration: stops its root (if it still runs) and drops the rig.</summary>
        public void Discard(string reason)
        {
            GameApplicationRoot? root = GameApplication.Current;
            if (Composition != null && root != null && ReferenceEquals(root.Definition, Composition.Definition) && root.State != GameApplicationState.Stopped)
            {
                root.Stop("Hollowmere registration not adopted: " + reason);
            }

            Debug.LogWarning("[Hollowmere] the registered application root was not adopted: " + reason);
            Composition = null;
            Destroy(gameObject);
        }
    }
}
