// Hollowmere P1.3 EditMode - the player/npc/interaction tools: round trips through Undo, refusals by GP code, and the
// authorable metadata (mirror attributes on every definition, tool and validator; catalog contributors discovered).
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
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
using GameCore.Rules.Gameplay.Interaction;
using Hollowmere.GameplayAuthoring;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Hollowmere.P1_3.EditMode.Tests
{
    [TestFixture]
    public sealed class P13ToolTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null)
                {
                    Object.DestroyImmediate(created[i]);
                }
            }

            created.Clear();
        }

        [Test]
        public void PlayerTools_TuneMovementAndSetCamera_RoundTripThroughUndo_AndRefuseOutOfRange()
        {
            PlayerDefinition player = Make<PlayerDefinition>();
            Undo.IncrementCurrentGroup();
            PlayerTools.TuneMovement(player, 3f, 6f, 1.2f, 800, 100, 90);
            Assert.That(player.WalkSpeed, Is.EqualTo(3f));
            Assert.That(player.RunSpeed, Is.EqualTo(6f));
            Assert.That(player.ToTuning().StaminaMax, Is.EqualTo(800));
            Assert.That(player.ToTuning().WalkMillimetresPerSecond, Is.EqualTo(3000));
            Undo.PerformUndo();
            Assert.That(player.WalkSpeed, Is.EqualTo(2.5f), "undo restores the tuning");

            Undo.IncrementCurrentGroup();
            PlayerTools.SetCamera(player, 7f, 2f, -20f, 60f);
            Assert.That(player.CameraDistance, Is.EqualTo(7f));
            Assert.That(player.CameraPitchMax, Is.EqualTo(60f));
            Undo.PerformUndo();
            Assert.That(player.CameraDistance, Is.EqualTo(5f));

            Refuses(PlayerNpcInteractionCodes.PlayerTuningOutOfRange, () => PlayerTools.TuneMovement(player, 3f, 2f));
            Refuses(PlayerNpcInteractionCodes.PlayerCameraOutOfRange, () => PlayerTools.SetCamera(player, 0.5f));
            Refuses(PlayerNpcInteractionCodes.PlayerMissingDefinition, () => PlayerTools.SetCamera(null!, 5f));
        }

        [Test]
        public void NpcTools_SetBehaviourPatrolAndSchedule_RoundTrip_AndRefuseMalformedInput()
        {
            NpcDefinition npc = Make<NpcDefinition>();
            BehaviourDefinition behaviour = Make<BehaviourDefinition>();
            NpcTools.SetBehaviour(npc, behaviour);
            Assert.That(npc.Behaviour, Is.SameAs(behaviour));

            NpcTools.SetPatrol(npc, new[] { new Vector3(1f, 0f, 2f), new Vector3(3f, 0f, 4f) }, 2f);
            Assert.That(behaviour.Kind, Is.EqualTo(NpcBehaviourKind.Patrol));
            Assert.That(behaviour.Route()[1].X, Is.EqualTo(3000));
            Assert.That(behaviour.Route()[1].Z, Is.EqualTo(4000));
            Assert.That(npc.ToProfile().WaitMilliseconds, Is.EqualTo(2000));

            ScheduleDefinition schedule = Make<ScheduleDefinition>();
            schedule.Configure(100f, 0f, new[]
            {
                new SchedulePhaseEntry { name = "day", startSeconds = 0f, behaviour = NpcBehaviourKind.Patrol },
                new SchedulePhaseEntry { name = "night", startSeconds = 50f, behaviour = NpcBehaviourKind.Idle },
            });
            Undo.IncrementCurrentGroup();
            NpcTools.SetSchedule(npc, schedule);
            Assert.That(npc.Schedule, Is.SameAs(schedule));
            Undo.PerformUndo();
            Assert.That(npc.Schedule, Is.Null, "undo removes the schedule again");

            ScheduleDefinition broken = Make<ScheduleDefinition>();
            broken.Configure(100f, 0f, new[] { new SchedulePhaseEntry { startSeconds = 50f }, new SchedulePhaseEntry { startSeconds = 30f } });
            Refuses(PlayerNpcInteractionCodes.NpcScheduleMalformed, () => NpcTools.SetSchedule(npc, broken));
            Refuses(PlayerNpcInteractionCodes.NpcPatrolEmpty, () => NpcTools.SetPatrol(npc, Array.Empty<Vector3>()));
            BehaviourDefinition empty = Make<BehaviourDefinition>();
            empty.Configure(NpcBehaviourKind.Patrol, null, 1f, string.Empty);
            Refuses(PlayerNpcInteractionCodes.NpcPatrolEmpty, () => NpcTools.SetBehaviour(npc, empty));
            Assert.That(NpcValidator.Validate(npc), Is.Not.Empty, "an NPC without an entity definition is reported");
        }

        [Test]
        public void InteractionTools_SetStatesAndLinkCondition_RoundTrip_AndRefuseIllegalStates()
        {
            InteractableDefinition door = Make<InteractableDefinition>();
            door.Configure(null, InteractableKind.Door, 3f, 0.5f, 0);
            InteractionTools.SetStates(door, "locked", new[] { "locked=Barred", "open=Close it" });
            Assert.That(door.InitialState, Is.EqualTo(InteractableStates.Locked));
            Assert.That(door.PromptFor(InteractableStates.Locked), Is.EqualTo("Barred"));
            Assert.That(door.PromptFor(InteractableStates.Closed), Is.EqualTo("Open"), "a state without a prompt falls back by kind");

            Undo.IncrementCurrentGroup();
            InteractionTools.LinkCondition(door, "narrative.fact.test", "logic.action.ring");
            Assert.That(door.ConditionRef, Is.EqualTo("narrative.fact.test"));
            Assert.That(door.ActionRef, Is.EqualTo("logic.action.ring"));
            Undo.PerformUndo();
            Assert.That(door.ConditionRef, Is.Empty);

            Refuses(PlayerNpcInteractionCodes.InteractableIllegalState, () => InteractionTools.SetStates(door, "on"));
            Refuses(PlayerNpcInteractionCodes.InteractableIllegalState, () => InteractionTools.SetStates(door, "closed", new[] { "used=Nope" }));
            Refuses(PlayerNpcInteractionCodes.InteractableMissingPrompt, () => InteractionTools.SetStates(door, "closed", new[] { "open=" }));
            IReadOnlyList<GameplayDiagnostic> diagnostics = InteractionValidator.Validate(door);
            Assert.That(Codes(diagnostics), Does.Contain(PlayerNpcInteractionCodes.InteractableLockWithoutCondition), "locked without a condition is reported");
        }

        [Test]
        public void PlacementTools_AddAnNpcADoorAnExaminable_AndSetTheSpawn_InsideTheRegionOnly()
        {
            // The Single-mode scene change unloads unused assets (ignoring script references), so the definitions are
            // loaded after it.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            NpcDefinition maren = AssetDatabase.LoadAssetAtPath<NpcDefinition>(HollowmereGameplayAuthoring.NpcRoot + "/Definitions/Maren.asset");
            InteractableDefinition gate = AssetDatabase.LoadAssetAtPath<InteractableDefinition>(HollowmereGameplayAuthoring.InteractableRoot + "/Definitions/CausewayGate.asset");
            InteractableDefinition well = AssetDatabase.LoadAssetAtPath<InteractableDefinition>(HollowmereGameplayAuthoring.InteractableRoot + "/Definitions/VillageWell.asset");
            if (maren == null || gate == null || well == null)
            {
                Assert.Ignore("the P1.3 content is not authored yet (run P13ContentTests first)");
            }

            RegionDefinition regionDefinition = Make<RegionDefinition>();
            regionDefinition.EnsureAuthoringId();
            var regionObject = new GameObject("Region Test");
            AuthoredRegion region = regionObject.AddComponent<AuthoredRegion>();
            region.Bounds.Configure(new Vector3(0f, 10f, 0f), new Vector3(20f, 30f, 20f));
            region.Configure(regionDefinition, null);

            AuthoredEntity npc = NpcTools.AddAtIn(scene, maren, new Vector3(2f, 0f, 3f), 90f, "Test Maren");
            Assert.That(npc.Definition, Is.SameAs(maren.Entity));
            Assert.That(AuthoringIds.IsValid(npc.AuthoringId), Is.True);
            Assert.That(npc.transform.position, Is.EqualTo(new Vector3(2f, 0f, 3f)));

            AuthoredEntity door = InteractionTools.AddIn(scene, gate, new Vector3(-3f, 0f, 1f), 0f, "Test Gate", true);
            Assert.That(door.Definition, Is.SameAs(gate.Entity));
            AuthoredEntity examinable = InteractionTools.AddIn(scene, well, new Vector3(0f, 0f, -4f), 0f, "Test Well", false);
            Assert.That(examinable.Definition, Is.SameAs(well.Entity));

            PlayerTools.SetSpawn(npc, new Vector3(4f, 0f, 4f), 45f);
            Assert.That(npc.transform.position, Is.EqualTo(new Vector3(4f, 0f, 4f)));

            Refuses(PlayerNpcInteractionCodes.NpcNotPlaced, () => NpcTools.AddAtIn(scene, maren, new Vector3(50f, 0f, 0f), 0f, "Outside"));
            Refuses(PlayerNpcInteractionCodes.InteractableIllegalState, () => InteractionTools.AddIn(scene, well, Vector3.zero, 0f, "Wrong", true));
            Refuses(PlayerNpcInteractionCodes.InteractableNotPlaced, () => InteractionTools.AddIn(scene, gate, new Vector3(0f, 0f, 40f), 0f, "Outside", true));
            Refuses(PlayerNpcInteractionCodes.PlayerSpawnOutsideRegion, () => PlayerTools.SetSpawn(npc, new Vector3(0f, 0f, 30f)));
            Assert.That(CountEntities(scene), Is.EqualTo(3), "refused placements leave nothing behind");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void EveryDefinition_CarriesTheMirrorAuthoringAttributes()
        {
            Type[] definitions =
            {
                typeof(PlayerDefinition), typeof(InputProfile), typeof(NpcDefinition), typeof(BehaviourDefinition), typeof(ScheduleDefinition),
                typeof(NpcRoster), typeof(InteractableDefinition), typeof(TriggerDefinition), typeof(InteractionRoster),
            };
            var typeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Type type in definitions)
            {
                AuthorableAttribute? authorable = type.GetCustomAttribute<AuthorableAttribute>();
                Assert.That(authorable, Is.Not.Null, type.Name + " is authorable");
                Assert.That(authorable!.Doc, Is.Not.Null.And.Not.Empty, type.Name + " documents itself");
                Assert.That(authorable.Scope, Is.EqualTo(AuthorScope.Definition), type.Name);
                Assert.That(typeIds.Add(authorable.ObjectTypeId), Is.True, "type ids are distinct: " + authorable.ObjectTypeId);
                Assert.That(typeof(IDefinitionAsset).IsAssignableFrom(type), Is.True, type.Name + " is a definition asset");
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    if (field.GetCustomAttribute<SerializeField>() == null || field.Name == "authoringId" || field.Name == "contentStamp")
                    {
                        continue;
                    }

                    bool described = field.GetCustomAttribute<AuthorFieldAttribute>() != null || field.GetCustomAttribute<AuthorRefAttribute>() != null;
                    Assert.That(described, Is.True, type.Name + "." + field.Name + " carries AuthorField or AuthorRef");
                }
            }
        }

        [Test]
        public void EveryTool_IsAnAuthorOperation_WithArgumentsAndAValidator()
        {
            var expected = new[]
            {
                "player.setSpawn", "player.tuneMovement", "player.setCamera",
                "npc.addAt", "npc.setPatrol", "npc.setSchedule", "npc.setBehaviour",
                "interaction.addDoor", "interaction.addExaminable", "interaction.addTrigger", "interaction.setStates", "interaction.linkCondition",
            };
            var found = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
            foreach (Type tools in new[] { typeof(PlayerTools), typeof(NpcTools), typeof(InteractionTools) })
            {
                foreach (MethodInfo method in tools.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    AuthorOperationAttribute? operation = method.GetCustomAttribute<AuthorOperationAttribute>();
                    if (operation == null)
                    {
                        continue;
                    }

                    found.Add(operation.ToolId, method);
                    Assert.That(operation.Doc, Is.Not.Null.And.Not.Empty, operation.ToolId);
                    Assert.That(operation.Validator, Is.Not.Null, operation.ToolId + " names a validator");
                    Assert.That(operation.Validator!.GetCustomAttribute<AuthorValidatorAttribute>(), Is.Not.Null, operation.ToolId + " validator is declared");
                    ParameterInfo[] parameters = method.GetParameters();
                    for (int p = 1; p < parameters.Length; p++)
                    {
                        Assert.That(parameters[p].GetCustomAttribute<AuthorArgAttribute>(), Is.Not.Null, operation.ToolId + " argument " + parameters[p].Name);
                    }
                }
            }

            Assert.That(found.Keys, Is.EquivalentTo(expected));
        }

        [Test]
        public void CatalogContributors_OfTheThreePackages_AreDiscovered()
        {
            IReadOnlyList<GameplayCatalogContribution> contributions = CatalogContributionDiscovery.Discover();
            var packages = new List<string>();
            for (int i = 0; i < contributions.Count; i++)
            {
                packages.Add(contributions[i].PackageName);
            }

            Assert.That(packages, Is.SupersetOf(new[] { "com.gamecore.gameplay.interaction", "com.gamecore.gameplay.npc", "com.gamecore.gameplay.player" }));
            var sorted = new List<string>(packages);
            sorted.Sort(StringComparer.Ordinal);
            Assert.That(packages, Is.EqualTo(sorted), "contributions are in package order");
        }

        private T Make<T>()
            where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            created.Add(instance);
            return instance;
        }

        private static void Refuses(string code, Action action)
        {
            ArgumentException? refused = Assert.Throws<ArgumentException>(() => action());
            Assert.That(refused!.Message, Does.StartWith(code));
        }

        private static List<string> Codes(IReadOnlyList<GameplayDiagnostic> diagnostics)
        {
            var codes = new List<string>();
            for (int i = 0; i < diagnostics.Count; i++)
            {
                codes.Add(diagnostics[i].Code);
            }

            return codes;
        }

        private static int CountEntities(Scene scene)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                count += root.GetComponentsInChildren<AuthoredEntity>(true).Length;
            }

            return count;
        }
    }
}
