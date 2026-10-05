// Hollowmere.P1_7b.EditMode.Tests - P2.3 follow-ups (its PACKET "Left open" 3 and 4):
//   * the pure tools (dialogue.preview, quest.simulate, logic.explain, logic.test, interaction.explain, logic.whyNot,
//     quest.inspectRuntime) are [AuthorOperation(ReadOnly = true)]: ToolRegistry.Invoke runs them directly, they return
//     their output and leave their target clean;
//   * world.connectRegions, world.addPortal and world.setSpawnPoint bind every parameter through the engine (target =
//     first [Authorable] parameter, the rest [AuthorArg]) and apply as journaled change sets.
#nullable enable
using System;
using System.Reflection;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Quest;
using GameCore.Gameplay.World;
using GameCore.Gameplay.World.Editor;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GpArg = GameCore.Gameplay.Contracts.AuthorArgAttribute;
using GpAuthorable = GameCore.Gameplay.Contracts.AuthorableAttribute;
using GpOperation = GameCore.Gameplay.Contracts.AuthorOperationAttribute;
using StudioScope = GameCore.Studio.Model.AuthorScope;

namespace Hollowmere.P1_7b.EditMode.Tests
{
    public sealed class ReadOnlyAndWorldBindingTests
    {
        private HardeningTestBed? _bed;

        private HardeningTestBed Bed => _bed!;

        [SetUp]
        public void SetUp()
        {
            _bed = new HardeningTestBed("bind");
            _bed.Runtime.Index.Rebuild();
        }

        [TearDown]
        public void TearDown()
        {
            _bed?.Dispose();
            _bed = null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TestCase("dialogue.preview", "Maren", typeof(DialogueGraphDefinition), "{\"facts\":\"bell_rung=1\"}")]
        [TestCase("quest.simulate", "DrownedBell", typeof(QuestDefinition), "{\"path\":\"start; fail\"}")]
        [TestCase("quest.inspectRuntime", "DrownedBell", typeof(QuestDefinition), "{\"state\":\"\"}")]
        [TestCase("logic.explain", "BellKeepsGateOpen", typeof(RuleDefinition), "{}")]
        [TestCase("logic.test", "BellKeepsGateOpen", typeof(RuleDefinition), "{\"state\":\"fact.bell_rung=1\"}")]
        [TestCase("interaction.explain", "CausewayGate", typeof(InteractableDefinition), "{\"state\":\"\"}")]
        public void PureTool_IsInvokableThroughToolRegistryInvoke_AndLeavesItsTargetClean(string toolId, string asset, Type type, string args)
        {
            ScriptableObject target = Load(asset, type);
            AssertInvokes(toolId, target, JObject.Parse(args));
        }

        [Test]
        public void LogicWhyNot_IsInvokableThroughToolRegistryInvoke_AndLeavesItsTargetClean()
        {
            InteractableDefinition gate = HardeningTestBed.Load<InteractableDefinition>("CausewayGate");
            GameplayContentSet content = HardeningTestBed.Load<GameplayContentSet>("HollowmereContent");
            JToken output = AssertInvokes("logic.whyNot", content, new JObject { ["subject"] = Bed.RefToken(gate), ["state"] = string.Empty });
            Assert.That(output.ToString(), Does.Contain("gate_open"));
        }

        [Test]
        public void MutatingTool_IsStillRefusedByToolRegistryInvoke()
        {
            QuestDefinition quest = HardeningTestBed.Load<QuestDefinition>("DrownedBell");
            OperationResult result = Bed.Runtime.Registry.Invoke("quest.setBranch", Bed.Ref(quest, StudioScope.Definition), new JObject { ["objective"] = 0, ["branch"] = 0 });
            Assert.That(result.Status, Is.Not.EqualTo(OutcomeStatus.Applied), "a mutating tool goes through a change set");
        }

        [TestCase("world.connectRegions")]
        [TestCase("world.addPortal")]
        [TestCase("world.setSpawnPoint")]
        public void WorldTool_BindsEveryParameterThroughTheEngine(string toolId)
        {
            MethodInfo? method = null;
            foreach (MethodInfo candidate in typeof(WorldTools).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                GpOperation? operation = candidate.GetCustomAttribute<GpOperation>();
                if (operation != null && operation.ToolId == toolId)
                {
                    Assert.That(method, Is.Null, toolId + " is declared once");
                    method = candidate;
                }
            }

            Assert.That(method, Is.Not.Null, toolId);
            bool targetSeen = false;
            foreach (ParameterInfo parameter in method!.GetParameters())
            {
                if (parameter.GetCustomAttribute<GpArg>() != null || parameter.HasDefaultValue)
                {
                    continue;
                }

                bool authorable = parameter.ParameterType.GetCustomAttribute<GpAuthorable>() != null;
                Assert.That(!targetSeen && authorable, Is.True, toolId + " parameter '" + parameter.Name + "' is neither the target nor an [AuthorArg]");
                targetSeen = true;
            }
        }

        [Test]
        public void WorldTools_ConnectAddPortalAndSetSpawnPoint_ApplyThroughTheEngine()
        {
            RegionDefinition a = Bed.Create<RegionDefinition>("P17bRegionA", r => r.SetBounds(new Vector3(0f, 5f, 0f), new Vector3(60f, 20f, 60f)));
            RegionDefinition b = Bed.Create<RegionDefinition>("P17bRegionB", r => r.SetBounds(new Vector3(0f, 5f, 0f), new Vector3(60f, 20f, 60f)));
            WorldDefinition world = Bed.Create<WorldDefinition>("P17bWorld", w =>
            {
                w.AddRegion(a);
                w.AddRegion(b);
                w.SetStartRegion(a);
            });
            Bed.Runtime.Index.Rebuild();

            Bed.Apply("world.connectRegions", world, new JObject { ["regionA"] = Bed.RefToken(a), ["regionB"] = Bed.RefToken(b) });
            Assert.That(world.Portals.Count, Is.EqualTo(1));
            PortalDefinition portal = world.Portals[0];
            Assert.That(portal.Other(a), Is.SameAs(b));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var markerObject = new GameObject("Region A");
            SceneManager.MoveGameObjectToScene(markerObject, scene);
            AuthoredRegion marker = markerObject.AddComponent<AuthoredRegion>();
            marker.Bounds.Configure(new Vector3(0f, 5f, 0f), new Vector3(60f, 20f, 60f));
            marker.Configure(a, null);
            Bed.Runtime.Index.Rebuild();

            // No region argument: the portal's region whose scene is open (A) receives the end.
            Bed.Apply("world.addPortal", portal, new JObject { ["position"] = new JArray(20, 0, 0), ["yaw"] = -90 });
            RegionPortal? end = FindEnd(scene, portal);
            Assert.That(end, Is.Not.Null, "the portal end is in region A's scene");

            Bed.Apply("world.setSpawnPoint", a, new JObject { ["position"] = new JArray(0, 0, -5), ["yaw"] = 0 });
            Assert.That(marker.SpawnPoint.name, Is.EqualTo("SpawnPoint"), "an open region gets its scene SpawnPoint");
            Assert.That(a.TryGetSpawnPoint(RegionDefinition.DefaultSpawnPoint, out RegionSpawnPoint _), Is.True);

            Bed.Apply("world.setSpawnPoint", b, new JObject { ["position"] = new JArray(3, 0, 4), ["yaw"] = 90, ["name"] = "dock" });
            Assert.That(b.TryGetSpawnPoint("dock", out RegionSpawnPoint dock), Is.True, "a closed region records the named point on its definition");
            Assert.That(dock.yaw, Is.EqualTo(90f));
        }

        private JToken AssertInvokes(string toolId, ScriptableObject target, JObject args)
        {
            EditorUtility.ClearDirty(target);
            OperationResult result = Bed.Runtime.Registry.Invoke(toolId, Bed.Ref(target, StudioScope.Definition), args);
            Assert.That(result.Status, Is.EqualTo(OutcomeStatus.Applied), toolId + ": " + result.Code + " " + result.Detail);
            Assert.That(result.Output, Is.Not.Null, toolId + " returns its output");
            Assert.That(result.Inverse, Is.Empty, toolId + " needs no inverse");
            Assert.That(EditorUtility.IsDirty(target), Is.False, toolId + " leaves its target clean");
            TestContext.WriteLine(toolId + " -> " + result.Output);
            return result.Output!;
        }

        private static ScriptableObject Load(string name, Type type)
        {
            MethodInfo load = typeof(HardeningTestBed).GetMethod(nameof(HardeningTestBed.Load))!.MakeGenericMethod(type);
            return (ScriptableObject)load.Invoke(null, new object[] { name })!;
        }

        private static RegionPortal? FindEnd(Scene scene, PortalDefinition portal)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (RegionPortal end in root.GetComponentsInChildren<RegionPortal>(true))
                {
                    if (end.Portal == portal)
                    {
                        return end;
                    }
                }
            }

            return null;
        }
    }
}
