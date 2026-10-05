// Hollowmere - INTERIM restore re-attach (P3.1). Deleted when P1.7a's NarrativeComposer.Attach(..., seedSlots:false) and
// GameBoot's RootChanged re-attach land; HollowmereGame then plugs its codecs into that path instead.
//
// A restore (SaveService.Restore) boots a new root from the checkpoint. The UI runtime (UiRuntime.OnRootChanged, P1.5)
// shuts the old gameplay world down, attaches the world plan to the restored root (WorldBuilder.Attach, seedSlots
// false: the slots come from the checkpoint) and then calls the game's prepare callback - this file - before it raises
// WorldReplaced and shows the HUD. What the restored world still lacks is everything GameBoot added after the plan's
// own attach, re-done here in GameBoot's order:
//   1. the narrative half: a new NarrativeRuntime over the restored root with the old models and index (the content did
//      not change), every module re-attached with seedSlots false, a new NarrativeHost as a world input, a new
//      NarrativeWorld and the INarrativeWorldAware callbacks (dialogue runner, presenters, world-item binder...);
//      the old runtime's delivery is disposed;
//   2. streaming: the cancellation token, the views (the old world's views were cleared by its Shutdown);
//   3. the P1.3 sessions: player (the old one disposed), NPCs, interactions, focus sources;
//   4. the seams: HollowmereNarrative.Wire (P1.4) and UiAudioBootstrap.Wire (P1.5);
//   5. GameBoot's GameplayWorldBehaviour now holds the restored world;
//   6. scene residency repair: the restored residency slots say which region scenes should be loaded, but the scenes
//      that are loaded are the old world's. Regions committed Resident whose scene is not loaded are loaded (additive,
//      authored proxies deactivated); loaded region scenes the restored world does not hold Resident are unloaded.
//      The streamer only acts on committed transitions, so without this a save made in another region would restore
//      onto the wrong scenery. HollowmereDirector polls the started operations.
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.World;
using Hollowmere.Boot;
using Hollowmere.Narrative;
using UnityEngine;

namespace Hollowmere.Game
{
    /// <summary>Re-attaches GameBoot's narrative modules and P1.3 sessions to a restored world (interim, see file header).</summary>
    public static class InterimRestoreReattach
    {
        /// <summary>What the re-attach produced.</summary>
        public sealed class Result
        {
            public Result(NarrativeWorld narrative, PlayerSession? player, NpcSession? npcs, InteractionSession? interactions, IReadOnlyList<ISceneOperation> sceneOperations, int seams)
            {
                Narrative = narrative;
                Player = player;
                Npcs = npcs;
                Interactions = interactions;
                SceneOperations = sceneOperations;
                Seams = seams;
            }

            public NarrativeWorld Narrative { get; }

            public PlayerSession? Player { get; }

            public NpcSession? Npcs { get; }

            public InteractionSession? Interactions { get; }

            /// <summary>Region scene loads/unloads started by the residency repair (poll IsDone).</summary>
            public IReadOnlyList<ISceneOperation> SceneOperations { get; }

            /// <summary>How many presentation seams UiAudioBootstrap.Wire connected.</summary>
            public int Seams { get; }
        }

        public static Result Reattach(
            GameBoot boot,
            NarrativeWorld previous,
            HollowmereNarrativeModules modules,
            GameplayWorld restored,
            PlayerSession? previousPlayer,
            CancellationToken lifetime)
        {
            if (boot == null || previous == null || modules == null || restored == null)
            {
                throw new InvalidOperationException("restore re-attach needs the boot, the previous narrative world, the modules and the restored world");
            }

            // 1. narrative half (NarrativeComposer.Boot's attach, seedSlots false).
            NarrativeRuntime old = previous.Runtime;
            old.Delivery.Dispose();
            var runtime = new NarrativeRuntime(restored.Root, restored, old.Manifest, old.Content, old.Models, old.Index);
            for (int i = 0; i < modules.All.Count; i++)
            {
                modules.All[i].Attach(runtime, false);
            }

            var host = new NarrativeHost(runtime);
            restored.AddInput(host);
            var narrative = new NarrativeWorld(restored, runtime, host);
            for (int i = 0; i < modules.All.Count; i++)
            {
                if (modules.All[i] is INarrativeWorldAware aware)
                {
                    aware.OnWorld(narrative);
                }
            }

            // 2. streaming and views.
            restored.Streamer.PreloadNeighbours = previous.World.Streamer.PreloadNeighbours;
            restored.Streamer.Observe(lifetime);
            restored.CreateViews(boot.transform);

            // 3. P1.3 sessions.
            previousPlayer?.Dispose();
            PlayerSession? player = null;
            NpcSession? npcs = null;
            InteractionSession? interactions = null;
            if (boot.PlayerExtension != null && boot.PlayerExtension.Module != null)
            {
                Camera? orbit = previousPlayer != null && previousPlayer.Camera.IsActive ? Camera.main : null;
                player = PlayerSession.Install(restored, boot.PlayerExtension, orbit != null ? orbit : Camera.main);
            }

            if (boot.NpcExtension != null)
            {
                npcs = NpcSession.Install(restored, boot.NpcExtension);
            }

            if (boot.InteractionExtension != null && boot.PlayerExtension != null)
            {
                interactions = InteractionSession.Install(restored, boot.InteractionExtension, boot.PlayerExtension.Player, boot.PlayerExtension.PlayerKey);
            }

            if (player != null)
            {
                if (npcs != null)
                {
                    player.Focus.AddSource(npcs.Candidates);
                }

                if (interactions != null)
                {
                    player.Focus.AddSource(interactions.Candidates);
                }
            }

            // 4. seams.
            HollowmereNarrative.Wire(narrative, boot.InteractionExtension, interactions, npcs);
            int seams = UiAudioBootstrap.Wire(restored, player, interactions, narrative, modules);

            // 5. the world holder.
            GameplayWorldBehaviour? holder = boot.GetComponent<GameplayWorldBehaviour>();
            if (holder != null)
            {
                holder.World = restored;
            }

            // 6. scene residency repair.
            List<ISceneOperation> operations = RepairResidency(restored);
            return new Result(narrative, player, npcs, interactions, operations, seams);
        }

        /// <summary>Loads the scenes of regions committed Resident and unloads loaded region scenes that are not (see header).</summary>
        public static List<ISceneOperation> RepairResidency(GameplayWorld world)
        {
            var operations = new List<ISceneOperation>();
            if (BinderEnvironment.IsHeadless && !Application.isPlaying)
            {
                return operations;
            }

            var loader = new UnitySceneLoader();
            for (int i = 0; i < world.Worlds.Regions.Count; i++)
            {
                RegionRecord region = world.Worlds.Regions[i];
                if (string.IsNullOrEmpty(region.ScenePath))
                {
                    continue;
                }

                int committed = world.Slots.ReadOrDefault(region.Target, GameplaySlots.WorldOwner, GameplaySlots.Residency, Residency.Unloaded);
                bool loaded = loader.IsLoaded(region.ScenePath);
                if (committed == Residency.Resident && !loaded)
                {
                    operations.Add(loader.Load(region.ScenePath));
                }
                else if (committed != Residency.Resident && committed != Residency.Loading && loaded)
                {
                    operations.Add(loader.Unload(region.ScenePath));
                }
            }

            return operations;
        }
    }
}
