// Hollowmere P1.3 EditMode - the P1.3 content: authoring, the bake with the three new plugins, Verify.
//
// AuthorAndBake creates the player, NPC and interactable content when it is missing (idempotent otherwise), bakes the
// NavMesh of each region once and re-bakes the world (whose catalog now carries the player, npc and interaction
// registrations), so the host runs it once on a fresh checkout and commits what it produced (PACKET.md, "Content").
#nullable enable
using System.Collections.Generic;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using Hollowmere.GameplayAuthoring;
using Hollowmere.WorldAuthoring;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollowmere.P1_3.EditMode.Tests
{
    [TestFixture]
    public sealed class P13ContentTests
    {
        // P3.1 added Bram the innkeeper (and moved Odd to the marsh jetty, Hale to the causeway gate).
        private static readonly string[] NpcNames = { "Maren", "Odd", "Pip", "Hale", "Belfry Echo", "Bram" };

        [Test, Order(0)]
        public void AuthorAndBake_AddsThePlayerNpcsAndInteractables()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            BakeResult result = HollowmereGameplayAuthoring.AuthorAndBake();
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Debug.Log("[P1.3] author+bake " + clock.ElapsedMilliseconds + " ms; regions=" + result.Regions + " definitions=" + result.Definitions
                + " entities=" + result.Entities + " fingerprint=" + result.CatalogFingerprint);

            RegionManifest manifest = RequireManifest();
            PlayerDefinition player = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(HollowmereGameplayAuthoring.PlayerDefinitionPath);
            Assert.That(player, Is.Not.Null);
            Assert.That(player.Entity, Is.Not.Null);
            Assert.That(player.Input, Is.Not.Null);
            Assert.That(player.Input!.Actions, Is.Not.Null, "the input profile points at Player.inputactions");
            Assert.That(player.Rig, Is.Not.Null);
            ManifestEntity? focus = manifest.FindEntity(manifest.FocusEntityId);
            Assert.That(focus, Is.Not.Null);
            Assert.That(focus!.definitionId, Is.EqualTo(player.Entity!.AuthoringId), "the focus traveller is the player");

            NpcRoster roster = AssetDatabase.LoadAssetAtPath<NpcRoster>(HollowmereGameplayAuthoring.NpcRosterPath);
            Assert.That(roster.Npcs.Count, Is.EqualTo(NpcNames.Length));
            for (int i = 0; i < NpcNames.Length; i++)
            {
                Assert.That(EntityNamed(manifest, NpcNames[i]), Is.Not.Null, NpcNames[i] + " is placed and baked");
            }

            int scheduled = 0;
            int patrolling = 0;
            for (int i = 0; i < roster.Npcs.Count; i++)
            {
                scheduled += roster.Npcs[i].Schedule != null ? 1 : 0;
                patrolling += roster.Npcs[i].Behaviour != null && roster.Npcs[i].Behaviour!.Kind == NpcBehaviourKind.Patrol ? 1 : 0;
            }

            Assert.That(scheduled, Is.GreaterThanOrEqualTo(1), "at least one NPC follows a day/night schedule");
            Assert.That(patrolling, Is.GreaterThanOrEqualTo(1), "at least one NPC patrols");

            InteractionRoster interactions = AssetDatabase.LoadAssetAtPath<InteractionRoster>(HollowmereGameplayAuthoring.InteractionRosterPath);
            Assert.That(interactions.Interactables.Count, Is.GreaterThanOrEqualTo(3), "P1.3's well, gate and bell, plus P3.1's pickups and story interactables");
            Assert.That(EntityNamed(manifest, HollowmereGameplayAuthoring.WellName), Is.Not.Null);
            Assert.That(EntityNamed(manifest, HollowmereGameplayAuthoring.GateName), Is.Not.Null);
            Assert.That(EntityNamed(manifest, HollowmereGameplayAuthoring.BellName), Is.Not.Null);
            InteractableDefinition? gate = null;
            for (int i = 0; i < interactions.Interactables.Count; i++)
            {
                if (interactions.Interactables[i].Kind == GameCore.Rules.Gameplay.Interaction.InteractableKind.Gate)
                {
                    gate = interactions.Interactables[i];
                }
            }

            Assert.That(gate, Is.Not.Null);
            Assert.That(gate!.ConditionRef, Is.EqualTo(HollowmereGameplayAuthoring.GateCondition));
            Assert.That(gate.InitialState, Is.EqualTo(GameCore.Rules.Gameplay.Interaction.InteractableStates.Locked));
        }

        [Test, Order(1)]
        public void Verify_PassesAfterTheContentBake()
        {
            WorldDefinition world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(HollowmereWorldAuthoring.WorldPath);
            Assert.That(world, Is.Not.Null);
            BakeResult verify = Entry.Verify(world, BakePaths.ConventionFor(HollowmereWorldAuthoring.WorldPath));
            Assert.That(verify.Succeeded, Is.True, verify.ToString());
        }

        [Test, Order(2)]
        public void EveryRegionScene_HasABakedNavMeshSurface_AndTheBootSceneIsConfigured()
        {
            string[] scenes = { HollowmereWorldAuthoring.VillageScene, HollowmereWorldAuthoring.MarshScene, HollowmereWorldAuthoring.BelfryScene };
            for (int s = 0; s < scenes.Length; s++)
            {
                Scene scene = EditorSceneManager.OpenScene(scenes[s], OpenSceneMode.Single);
                NavMeshSurface? surface = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    surface ??= root.GetComponentInChildren<NavMeshSurface>(true);
                }

                Assert.That(surface, Is.Not.Null, scenes[s]);
                Assert.That(surface!.navMeshData, Is.Not.Null, scenes[s] + " has baked NavMesh data");
                Assert.That(AssetDatabase.GetAssetPath(surface.navMeshData), Does.StartWith(HollowmereGameplayAuthoring.NpcRoot + "/NavMesh/"));
            }

            Scene boot = EditorSceneManager.OpenScene(HollowmereWorldAuthoring.BootScenePath, OpenSceneMode.Single);
            Hollowmere.Boot.GameBoot? gameBoot = null;
            foreach (GameObject root in boot.GetRootGameObjects())
            {
                gameBoot ??= root.GetComponentInChildren<Hollowmere.Boot.GameBoot>(true);
            }

            Assert.That(gameBoot, Is.Not.Null);
            Assert.That(gameBoot!.PlayerDefinition, Is.Not.Null);
            Assert.That(gameBoot.NpcRoster, Is.Not.Null);
            Assert.That(gameBoot.InteractionRoster, Is.Not.Null);

            var paths = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                paths.Add(scene.path);
            }

            Assert.That(paths, Is.EqualTo(new[]
            {
                HollowmereWorldAuthoring.BootScenePath, HollowmereWorldAuthoring.VillageScene, HollowmereWorldAuthoring.MarshScene, HollowmereWorldAuthoring.BelfryScene,
            }));
        }

        internal static RegionManifest RequireManifest()
        {
            RegionManifest? manifest = AssetDatabase.LoadAssetAtPath<RegionManifest>(HollowmereWorldAuthoring.ManifestPath);
            if (manifest == null)
            {
                Assert.Ignore("the Hollowmere world is not baked yet");
            }

            return manifest!;
        }

        internal static ManifestEntity? EntityNamed(RegionManifest manifest, string name)
        {
            for (int i = 0; i < manifest.Entities.Count; i++)
            {
                if (manifest.Entities[i].name == name)
                {
                    return manifest.Entities[i];
                }
            }

            return null;
        }
    }
}
