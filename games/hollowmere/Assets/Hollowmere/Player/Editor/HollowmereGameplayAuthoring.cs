// Hollowmere - authors the P1.3 content (player, NPCs, interactables, NavMesh) through the gameplay tools, then re-bakes.
//
// Like the P1.1 world authoring, everything goes through the tools Studio drives (npc.addAt / setPatrol / setSchedule,
// interaction.addDoor / addExaminable / setStates / linkCondition, player.tuneMovement / setCamera), and the script is
// idempotent: assets and placements that exist are kept (GUIDs and authoring ids stay stable), only missing pieces are
// created, and the bake is re-run (byte-identical when nothing changed). Region scenes are only added to: a NavMesh
// surface (baked data under Npcs/NavMesh), NPC placements and interactables; P1.1's entities are untouched.
//
//   Assets/Hollowmere/Player/Player.prefab, InputProfile.asset, PlayerDefinition.asset
//                                       the player rig (CharacterController), input profile, player definition
//                                       (entity = P1.1's Traveller definition: the traveller is the player)
//   Assets/Hollowmere/Npcs/...          NpcCapsule.prefab (capsule + name bubble), one entity definition and NPC
//                                       definition per NPC (Maren, Odd, Pip, Hale, Belfry Echo), behaviours, Pip's
//                                       day/night schedule, NpcRoster.asset, NavMesh/<Region>.asset
//   Assets/Hollowmere/Interactables/... WellBucket / CausewayGate prefabs and entity definitions, the Village Well
//                                       (examinable), Causeway Gate (gate locked by narrative.fact.gate_open) and
//                                       Drowned Bell (switch, P1.1's Bell definition) interactables, InteractionRoster.asset
//   Boot.unity                          GameBoot configured with the player definition and both rosters
//   EditorBuildSettings                 Boot + the three region scenes
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Interaction.Editor;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Npc.Editor;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.Player.Editor;
using GameCore.Gameplay.World;
using GameCore.Gameplay.World.Editor;
using GameCore.Rules.Gameplay.Interaction;
using Hollowmere.Boot;
using Hollowmere.WorldAuthoring;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Hollowmere.GameplayAuthoring
{
    /// <summary>Creates (when missing) the P1.3 content and re-bakes the world.</summary>
    public static class HollowmereGameplayAuthoring
    {
        public const string PlayerRoot = "Assets/Hollowmere/Player";
        public const string NpcRoot = "Assets/Hollowmere/Npcs";
        public const string InteractableRoot = "Assets/Hollowmere/Interactables";
        public const string PlayerDefinitionPath = PlayerRoot + "/PlayerDefinition.asset";
        public const string InputProfilePath = PlayerRoot + "/InputProfile.asset";
        public const string PlayerRigPath = PlayerRoot + "/Player.prefab";
        public const string InputActionsPath = "Assets/Hollowmere/Input/Player.inputactions";
        public const string NpcRosterPath = NpcRoot + "/NpcRoster.asset";
        public const string InteractionRosterPath = InteractableRoot + "/InteractionRoster.asset";
        public const string NpcPrefabPath = NpcRoot + "/Prefabs/NpcCapsule.prefab";
        public const string TravellerDefinitionPath = HollowmereWorldAuthoring.Root + "/Definitions/Traveller.asset";
        public const string BellDefinitionPath = HollowmereWorldAuthoring.Root + "/Definitions/Bell.asset";
        public const string WellName = "Village Well";
        public const string GateName = "Causeway Gate";
        public const string BellName = "Drowned Bell";
        public const string GateCondition = "narrative.fact.gate_open";

        private static readonly Vector3 Village = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 Marsh = new Vector3(200f, 0f, 0f);
        private static readonly Vector3 Belfry = new Vector3(100f, 0f, 200f);

        private sealed class NpcPlan
        {
            public NpcPlan(string name, string scene, Vector3 position, float yaw, NpcBehaviourKind kind, Vector3[] patrol, float speed, string voice, string graph)
            {
                Name = name;
                Scene = scene;
                Position = position;
                Yaw = yaw;
                Kind = kind;
                Patrol = patrol;
                Speed = speed;
                Voice = voice;
                Graph = graph;
            }

            public string Name { get; }

            public string Scene { get; }

            public Vector3 Position { get; }

            public float Yaw { get; }

            public NpcBehaviourKind Kind { get; }

            public Vector3[] Patrol { get; }

            public float Speed { get; }

            public string Voice { get; }

            public string Graph { get; }

            public string AssetName => Name.Replace(" ", string.Empty);
        }

        private static readonly NpcPlan[] Npcs =
        {
            new NpcPlan("Maren", HollowmereWorldAuthoring.VillageScene, Village + new Vector3(-8f, 0f, -8f), 0f, NpcBehaviourKind.Patrol,
                new[] { Village + new Vector3(-8f, 0f, -12f), Village + new Vector3(-8f, 0f, -4f) }, 1.8f, "voice.maren", "dialogue.maren"),
            new NpcPlan("Odd", HollowmereWorldAuthoring.VillageScene, Village + new Vector3(-8f, 0f, 6f), 180f, NpcBehaviourKind.Idle,
                Array.Empty<Vector3>(), 1.6f, "voice.odd", "dialogue.odd"),
            new NpcPlan("Pip", HollowmereWorldAuthoring.VillageScene, Village + new Vector3(5f, 0f, 3f), 90f, NpcBehaviourKind.Patrol,
                new[] { Village + new Vector3(5f, 0f, 3f), Village + new Vector3(8f, 0f, 6f) }, 2.2f, "voice.pip", "dialogue.pip"),
            new NpcPlan("Hale", HollowmereWorldAuthoring.MarshScene, Marsh + new Vector3(-4f, 0f, 4f), 270f, NpcBehaviourKind.Idle,
                Array.Empty<Vector3>(), 1.5f, "voice.hale", "dialogue.hale"),
            new NpcPlan("Belfry Echo", HollowmereWorldAuthoring.BelfryScene, Belfry + new Vector3(0f, 0f, -9f), 0f, NpcBehaviourKind.Idle,
                Array.Empty<Vector3>(), 1.2f, "voice.echo", "dialogue.belfry_echo"),
        };

        /// <summary>Once this asset exists (P3.1 AuthorAll), the P1.3 content is no longer re-applied here.</summary>
        public const string P31SupersededMarker = "Assets/Hollowmere/Game/HollowmereDirector.asset";

        [MenuItem("Hollowmere/Author P1.3 Gameplay Content And Bake")]
        public static void AuthorMenu()
        {
            BakeResult result = AuthorAndBake();
            if (result.Succeeded)
            {
                Debug.Log("[Hollowmere] " + result);
            }
            else
            {
                Debug.LogError("[Hollowmere] " + result);
            }
        }

        /// <summary>Creates whatever P1.3 content is missing, then re-bakes the world (without importing generated C#).</summary>
        public static BakeResult AuthorAndBake()
        {
            WorldDefinition world = HollowmereWorldAuthoring.EnsureWorld();
            if (AssetDatabase.LoadMainAssetAtPath(P31SupersededMarker) != null)
            {
                // P3.1 owns the content now (Authoring/Editor, journaled change sets): only re-bake.
                return Entry.Bake(world, BakePaths.ConventionFor(HollowmereWorldAuthoring.WorldPath), false);
            }

            WorldTools.EnsureFolder(PlayerRoot);
            WorldTools.EnsureFolder(NpcRoot + "/Prefabs");
            WorldTools.EnsureFolder(NpcRoot + "/Definitions");
            WorldTools.EnsureFolder(NpcRoot + "/NavMesh");
            WorldTools.EnsureFolder(InteractableRoot + "/Prefabs");
            WorldTools.EnsureFolder(InteractableRoot + "/Definitions");

            EnsurePlayer();
            EnsureNpcAssets();
            EnsureInteractableAssets();
            AssetDatabase.SaveAssets();

            PlaceInRegion(HollowmereWorldAuthoring.VillageScene, Village);
            PlaceInRegion(HollowmereWorldAuthoring.MarshScene, Marsh);
            PlaceInRegion(HollowmereWorldAuthoring.BelfryScene, Belfry);

            BakeResult result = Entry.Bake(AssetDatabase.LoadAssetAtPath<WorldDefinition>(HollowmereWorldAuthoring.WorldPath) ?? world,
                BakePaths.ConventionFor(HollowmereWorldAuthoring.WorldPath), false);
            if (!result.Succeeded)
            {
                return result;
            }

            HollowmereWorldAuthoring.EnsureBootScene();
            ConfigureBoot();
            ConfigureBuildSettings();
            AssetDatabase.SaveAssets();
            return result;
        }

        // ---------------------------------------------------------------- player

        private static void EnsurePlayer()
        {
            GameObject rig = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRigPath) ?? CreateRig();
            InputProfile input = LoadOrCreate<InputProfile>(InputProfilePath, created =>
                created.Configure(AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath), InputProfile.DefaultMap, 0.15f));
            PlayerDefinition player = LoadOrCreate<PlayerDefinition>(PlayerDefinitionPath, created =>
            {
                created.SetEntity(AssetDatabase.LoadAssetAtPath<EntityDefinition>(TravellerDefinitionPath));
                created.SetInput(input);
                created.SetRig(rig);
            });
            if (player.Entity == null)
            {
                player.SetEntity(AssetDatabase.LoadAssetAtPath<EntityDefinition>(TravellerDefinitionPath));
            }

            PlayerTools.TuneMovement(player, 2.5f, 5.5f, 1f, 1000, 200, 150);
            PlayerTools.SetCamera(player, 5f, 1.6f, -30f, 70f);
            EditorUtility.SetDirty(player);
        }

        private static GameObject CreateRig()
        {
            var root = new GameObject("Player");
            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.skinWidth = 0.035f;
            controller.stepOffset = 0.35f;
            controller.slopeLimit = 45f;
            controller.center = new Vector3(0f, 0.935f, 0f);
            var cameraTarget = new GameObject("CameraTarget");
            cameraTarget.transform.SetParent(root.transform, false);
            cameraTarget.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PlayerRigPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        // ---------------------------------------------------------------- NPCs

        private static void EnsureNpcAssets()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefabPath) ?? CreateNpcPrefab();
            NpcRoster roster = LoadOrCreate<NpcRoster>(NpcRosterPath, created => created.Configure(20, 8));
            for (int i = 0; i < Npcs.Length; i++)
            {
                NpcPlan plan = Npcs[i];
                EntityDefinition entity = LoadOrCreate<EntityDefinition>(NpcRoot + "/Definitions/" + plan.AssetName + "Entity.asset",
                    created => created.Configure(prefab, 1000, true, true));
                NpcDefinition npc = LoadOrCreate<NpcDefinition>(NpcRoot + "/Definitions/" + plan.AssetName + ".asset",
                    created => created.Configure(entity, plan.Name, plan.Speed, plan.Voice, plan.Graph));
                BehaviourDefinition behaviour = LoadOrCreate<BehaviourDefinition>(NpcRoot + "/Definitions/" + plan.AssetName + "Behaviour.asset",
                    created => created.Configure(plan.Kind, plan.Patrol, 1.5f, string.Empty));
                NpcTools.SetBehaviour(npc, behaviour);
                if (plan.Kind == NpcBehaviourKind.Patrol)
                {
                    NpcTools.SetPatrol(npc, plan.Patrol, 1.5f);
                }

                if (plan.Name == "Pip")
                {
                    ScheduleDefinition schedule = LoadOrCreate<ScheduleDefinition>(NpcRoot + "/Definitions/PipDayNight.asset", created =>
                        created.Configure(120f, 0f, new[]
                        {
                            new SchedulePhaseEntry { name = "day", startSeconds = 0f, behaviour = NpcBehaviourKind.Patrol },
                            new SchedulePhaseEntry { name = "night", startSeconds = 60f, behaviour = NpcBehaviourKind.Idle, hasLocation = true, location = Village + new Vector3(10f, 0f, -2f) },
                        }));
                    NpcTools.SetSchedule(npc, schedule);
                }

                roster.Add(npc);
                EditorUtility.SetDirty(npc);
            }

            EditorUtility.SetDirty(roster);
        }

        private static GameObject CreateNpcPrefab()
        {
            var root = new GameObject("NpcCapsule");
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            Renderer renderer = body.GetComponent<Renderer>();
            if (renderer.sharedMaterial != null)
            {
                var material = new Material(renderer.sharedMaterial) { name = "NpcCapsule" };
                Color tint = new Color(0.45f, 0.6f, 0.75f);
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", tint);
                }

                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", tint);
                }

                AssetDatabase.CreateAsset(material, NpcRoot + "/Prefabs/NpcCapsule.mat");
                renderer.sharedMaterial = material;
            }

            var label = new GameObject("Bubble");
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            TextMesh text = label.AddComponent<TextMesh>();
            text.text = "NPC";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.08f;
            text.fontSize = 48;
            Font? font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                text.font = font;
                label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }

            NpcBubble bubble = root.AddComponent<NpcBubble>();
            bubble.Configure(text, 2.3f);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, NpcPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        // ---------------------------------------------------------------- interactables

        private static void EnsureInteractableAssets()
        {
            GameObject bucket = AssetDatabase.LoadAssetAtPath<GameObject>(InteractableRoot + "/Prefabs/WellBucket.prefab")
                ?? PrimitivePrefab("WellBucket", PrimitiveType.Cylinder, new Vector3(0.5f, 0.3f, 0.5f), 0.3f, new Color(0.45f, 0.35f, 0.25f));
            GameObject gate = AssetDatabase.LoadAssetAtPath<GameObject>(InteractableRoot + "/Prefabs/CausewayGate.prefab")
                ?? PrimitivePrefab("CausewayGate", PrimitiveType.Cube, new Vector3(3f, 2.2f, 0.25f), 1.1f, new Color(0.3f, 0.28f, 0.25f));
            EntityDefinition bucketEntity = LoadOrCreate<EntityDefinition>(InteractableRoot + "/Definitions/WellBucketEntity.asset",
                created => created.Configure(bucket, 1000, true, true));
            EntityDefinition gateEntity = LoadOrCreate<EntityDefinition>(InteractableRoot + "/Definitions/CausewayGateEntity.asset",
                created => created.Configure(gate, 1000, true, true));
            EntityDefinition bellEntity = AssetDatabase.LoadAssetAtPath<EntityDefinition>(BellDefinitionPath);

            InteractableDefinition well = LoadOrCreate<InteractableDefinition>(InteractableRoot + "/Definitions/VillageWell.asset",
                created => created.Configure(bucketEntity, InteractableKind.Examinable, 3f, 0.5f, 0));
            InteractionTools.SetStates(well, "idle", new[] { "idle=Examine the well" });

            InteractableDefinition causeway = LoadOrCreate<InteractableDefinition>(InteractableRoot + "/Definitions/CausewayGate.asset",
                created => created.Configure(gateEntity, InteractableKind.Gate, 3f, 0.5f, 0));
            InteractionTools.LinkCondition(causeway, GateCondition, string.Empty);
            InteractionTools.SetStates(causeway, "locked", new[] { "locked=The gate is barred", "closed=Open the gate", "open=Close the gate" });

            InteractableDefinition bell = LoadOrCreate<InteractableDefinition>(InteractableRoot + "/Definitions/DrownedBell.asset",
                created => created.Configure(bellEntity, InteractableKind.Switch, 3.5f, 1f, 0));
            InteractionTools.SetStates(bell, "off", new[] { "off=Ring the bell", "on=Silence the bell" });

            InteractionRoster roster = LoadOrCreate<InteractionRoster>(InteractionRosterPath, created => { });
            roster.Add(well);
            roster.Add(causeway);
            roster.Add(bell);
            EditorUtility.SetDirty(roster);
        }

        private static GameObject PrimitivePrefab(string name, PrimitiveType shape, Vector3 scale, float height, Color color)
        {
            var root = new GameObject(name);
            GameObject body = GameObject.CreatePrimitive(shape);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = scale;
            body.transform.localPosition = new Vector3(0f, height, 0f);
            Renderer renderer = body.GetComponent<Renderer>();
            if (renderer.sharedMaterial != null)
            {
                var material = new Material(renderer.sharedMaterial) { name = name };
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", color);
                }

                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", color);
                }

                AssetDatabase.CreateAsset(material, InteractableRoot + "/Prefabs/" + name + ".mat");
                renderer.sharedMaterial = material;
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, InteractableRoot + "/Prefabs/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        // ---------------------------------------------------------------- region scenes

        private static void PlaceInRegion(string scenePath, Vector3 center)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            bool changed = false;
            for (int i = 0; i < Npcs.Length; i++)
            {
                NpcPlan plan = Npcs[i];
                if (plan.Scene != scenePath)
                {
                    continue;
                }

                NpcDefinition npc = AssetDatabase.LoadAssetAtPath<NpcDefinition>(NpcRoot + "/Definitions/" + plan.AssetName + ".asset");
                if (FindPlaced(scene, npc.Entity) == null)
                {
                    NpcTools.AddAtIn(scene, npc, plan.Position, plan.Yaw, plan.Name);
                    changed = true;
                }
            }

            if (scenePath == HollowmereWorldAuthoring.VillageScene)
            {
                changed |= PlaceInteractable(scene, InteractableRoot + "/Definitions/VillageWell.asset", center + new Vector3(0f, 0f, -1.9f), 0f, WellName, false);
            }
            else if (scenePath == HollowmereWorldAuthoring.MarshScene)
            {
                changed |= PlaceInteractable(scene, InteractableRoot + "/Definitions/CausewayGate.asset", center + new Vector3(-30.5f, 0f, 2f), 0f, GateName, true);
            }

            changed |= EnsureNavMesh(scene, center);
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        private static bool PlaceInteractable(Scene scene, string definitionPath, Vector3 position, float yaw, string name, bool door)
        {
            InteractableDefinition definition = AssetDatabase.LoadAssetAtPath<InteractableDefinition>(definitionPath);
            if (FindPlaced(scene, definition.Entity) != null)
            {
                return false;
            }

            if (door)
            {
                InteractionTools.AddIn(scene, definition, position, yaw, name, true);
            }
            else
            {
                InteractionTools.AddIn(scene, definition, position, yaw, name, false);
            }

            return true;
        }

        private static AuthoredEntity? FindPlaced(Scene scene, EntityDefinition? definition)
        {
            if (definition == null)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                AuthoredEntity[] entities = roots[r].GetComponentsInChildren<AuthoredEntity>(true);
                for (int i = 0; i < entities.Length; i++)
                {
                    if (entities[i].Definition == definition)
                    {
                        return entities[i];
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Adds a NavMeshSurface over the region volume and bakes it once (entity proxies excluded: they are deactivated
        /// at runtime and their views move). The data is saved under Npcs/NavMesh; an existing bake is kept.
        /// </summary>
        private static bool EnsureNavMesh(Scene scene, Vector3 center)
        {
            NavMeshSurface? surface = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length && surface == null; i++)
            {
                surface = roots[i].GetComponentInChildren<NavMeshSurface>(true);
            }

            if (surface != null && surface.navMeshData != null)
            {
                return false;
            }

            if (surface == null)
            {
                var holder = new GameObject("NavMesh");
                SceneManager.MoveGameObjectToScene(holder, scene);
                holder.transform.position = center;
                surface = holder.AddComponent<NavMeshSurface>();
            }

            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3(0f, 10f, 0f);
            surface.size = new Vector3(80f, 30f, 80f);
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;

            var hidden = new List<GameObject>();
            for (int r = 0; r < roots.Length; r++)
            {
                AuthoredEntity[] proxies = roots[r].GetComponentsInChildren<AuthoredEntity>(false);
                for (int i = 0; i < proxies.Length; i++)
                {
                    hidden.Add(proxies[i].gameObject);
                    proxies[i].gameObject.SetActive(false);
                }
            }

            try
            {
                surface.BuildNavMesh();
            }
            finally
            {
                for (int i = 0; i < hidden.Count; i++)
                {
                    hidden[i].SetActive(true);
                }
            }

            NavMeshData data = surface.navMeshData;
            if (data != null)
            {
                data.name = scene.name + "NavMesh";
                AssetDatabase.CreateAsset(data, NpcRoot + "/NavMesh/" + scene.name + ".asset");
                surface.navMeshData = data;
            }

            EditorUtility.SetDirty(surface);
            return true;
        }

        // ---------------------------------------------------------------- boot and build settings

        private static void ConfigureBoot()
        {
            Scene scene = EditorSceneManager.OpenScene(HollowmereWorldAuthoring.BootScenePath, OpenSceneMode.Single);
            GameBoot? boot = null;
            Camera? camera = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                boot ??= roots[i].GetComponentInChildren<GameBoot>(true);
                camera ??= roots[i].GetComponentInChildren<Camera>(true);
            }

            if (boot == null)
            {
                throw new InvalidOperationException("Boot.unity has no GameBoot");
            }

            PlayerDefinition player = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(PlayerDefinitionPath);
            NpcRoster npcs = AssetDatabase.LoadAssetAtPath<NpcRoster>(NpcRosterPath);
            InteractionRoster interactions = AssetDatabase.LoadAssetAtPath<InteractionRoster>(InteractionRosterPath);
            if (boot.PlayerDefinition == player && boot.NpcRoster == npcs && boot.InteractionRoster == interactions)
            {
                return;
            }

            Undo.RecordObject(boot, "GameBoot.ConfigureGameplay");
            boot.ConfigureGameplay(player, npcs, interactions, camera);
            EditorUtility.SetDirty(boot);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureBuildSettings()
        {
            string[] paths =
            {
                HollowmereWorldAuthoring.BootScenePath,
                HollowmereWorldAuthoring.VillageScene,
                HollowmereWorldAuthoring.MarshScene,
                HollowmereWorldAuthoring.BelfryScene,
            };
            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            bool same = current.Length == paths.Length;
            for (int i = 0; same && i < paths.Length; i++)
            {
                same = current[i].path == paths[i] && current[i].enabled;
            }

            if (same)
            {
                return;
            }

            var scenes = new EditorBuildSettingsScene[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                scenes[i] = new EditorBuildSettingsScene(paths[i], true);
            }

            EditorBuildSettings.scenes = scenes;
        }

        // ---------------------------------------------------------------- helpers

        private static T LoadOrCreate<T>(string path, Action<T> configure)
            where T : ScriptableObject, IDefinitionAsset
        {
            T? existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            T created = ScriptableObject.CreateInstance<T>();
            EnsureId(created);
            configure(created);
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static void EnsureId(ScriptableObject asset)
        {
            switch (asset)
            {
                case PlayerDefinition player:
                    player.EnsureAuthoringId();
                    break;
                case InputProfile input:
                    input.EnsureAuthoringId();
                    break;
                case NpcDefinition npc:
                    npc.EnsureAuthoringId();
                    break;
                case BehaviourDefinition behaviour:
                    behaviour.EnsureAuthoringId();
                    break;
                case ScheduleDefinition schedule:
                    schedule.EnsureAuthoringId();
                    break;
                case NpcRoster roster:
                    roster.EnsureAuthoringId();
                    break;
                case InteractableDefinition interactable:
                    interactable.EnsureAuthoringId();
                    break;
                case TriggerDefinition trigger:
                    trigger.EnsureAuthoringId();
                    break;
                case InteractionRoster interactions:
                    interactions.EnsureAuthoringId();
                    break;
                case EntityDefinition entity:
                    entity.EnsureAuthoringId();
                    break;
            }
        }
    }
}
