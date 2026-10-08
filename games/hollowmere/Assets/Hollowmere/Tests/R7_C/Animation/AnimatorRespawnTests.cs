// W-PLUG-02: real controller evaluation through GameplayWorld's registered presentation binders.
// Run in a graphics-enabled Editor. No headless bypass, synthetic slot reader or direct parameter writes.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Unity.Adapters;
using GameCore.Unity.App;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollowmere.R7_C.Animation.Tests
{
    public sealed class AnimatorRespawnTests
    {
        private const string ManifestPath = "Assets/Hollowmere/World/Hollowmere.manifest.asset";
        private const string Parameter = "CommittedVariant";
        private const string ScaleParameter = "CommittedScale";
        private readonly List<Object> owned = new List<Object>();
        private GameplayWorld? world;
        private string assetFolder = string.Empty;
        private bool pumpWasEnabled;
        private long frame = 700000;

        [SetUp]
        public void SetUp()
        {
            pumpWasEnabled = GameCoreApplicationPump.IsEnabled;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.Ignore("W-PLUG-02 requires a graphics-enabled Editor (omit -nographics); run artifacts/studio/verification/TOOLS/rows-p42l.py native.");
            }
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (world != null)
                {
                    try
                    {
                        world.Shutdown();
                    }
                    finally
                    {
                        world.Root.Stop("W-PLUG-02 teardown");
                        world = null;
                    }
                }
            }
            finally
            {
                GameCoreApplicationPump.IsEnabled = pumpWasEnabled;
                for (int i = owned.Count - 1; i >= 0; i--)
                {
                    if (owned[i] != null) Object.DestroyImmediate(owned[i]);
                }
                owned.Clear();
                if (assetFolder.Length != 0)
                {
                    Assert.That(AssetDatabase.DeleteAsset(assetFolder), Is.True, "remove temporary controller, clips and prefabs");
                    assetFolder = string.Empty;
                }
            }
        }

        [Test]
        public void W_PLUG_02_AnimatorEvaluatesCommittedVariant_AndRespawnRetainsOverrides()
        {
            Assert.That(BinderEnvironment.IsHeadless, Is.False,
                "W-PLUG-02 requires a graphics-enabled Editor: omit -nographics; inactive binders are not acceptance evidence.");
            Assert.That(GameApplication.Current == null || GameApplication.Current.State == GameApplicationState.Stopped,
                Is.True, "this test owns its isolated application root");
            RegionManifest source = AssetDatabase.LoadAssetAtPath<RegionManifest>(ManifestPath);
            Assert.That(source, Is.Not.Null, "the baked Hollowmere manifest is required");
            RegionManifest manifest = Own(Object.Instantiate(source));
            ManifestEntity entity = FindEntity(manifest, "Market Crate");
            Assert.That(entity.regionId, Is.EqualTo(manifest.StartRegionId), "the test target must be in the resident start region");
            ManifestDefinition entry = manifest.FindDefinition(entity.definitionId)!;
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.definition, Is.Not.Null);

            CreateAssetFolder();
            AnimatorController controller = CreateController();
            GameObject basePrefab = CreatePrefab(controller, "DefaultAppearance");
            GameObject overridePrefab = CreatePrefab(controller, "OverrideAppearance");
            EntityDefinition definition = Own(Object.Instantiate(entry.definition!));
            definition.Configure(basePrefab, 1000, true, true);
            VariantDefinition unused = Own(ScriptableObject.CreateInstance<VariantDefinition>());
            unused.Configure(null, Color.white);
            VariantDefinition variant = Own(ScriptableObject.CreateInstance<VariantDefinition>());
            variant.Configure(overridePrefab, Color.white);
            definition.SetVariants(new[] { unused, variant });
            definition.SetAnimatorBindings(new[]
            {
                new AnimatorSlotBinding("variant", Parameter),
                new AnimatorSlotBinding("scaleMilli", ScaleParameter),
            });
            // Only the in-memory manifest/definition clones change; the baked assets and recipe identity stay intact.
            entry.definition = definition;
            entry.variantCount = definition.VariantCount;
            entity.variant = 2;
            entity.scaleMilli = 1200;
            entity.alive = true;
            entity.visible = true;
            TargetId target = AuthoringIds.TargetIdFor(entity.authoringId);

            GameCoreApplicationPump.IsEnabled = true;
            GameCoreThreading.CaptureMainThread();
            world = GameplayBoot.Boot(manifest, new GameApplicationBootOptions
            {
                InstallPlayerLoop = false,
                AssignDefaultWorld = false,
                PumpAssertions = false,
                FrameClock = () => frame,
            }, null, false);
            world.UseSceneLoader(new ImmediateSceneLoader());
            GameObject root = Own(new GameObject("W-PLUG-02 presentation"));
            PrefabViewBinder views = world.CreateViews(root.transform);
            Assert.That(views.IsActive, Is.True);
            Assert.That(world.Root.Start().Outcome, Is.EqualTo(Outcome.Published));
            PumpUntil(() => views.TryGetView(target, out _), "initial committed view");
            GameObject initial = View(views, target);
            AssertAppearance(initial, true);
            AssertAnimation(initial, controller, 2, "Override", 0.75f);

            // A live change must actually drive the controller back to the default state, not merely copy a binding map.
            Assert.That(world.Commands.SetVariant(target, 0).Admitted, Is.True);
            PumpUntil(() => Slot(target, GameplaySlots.Variant) == 0, "default variant commit");
            GameObject defaultView = View(views, target);
            AssertAppearance(defaultView, false);
            AssertAnimation(defaultView, controller, 0, "Default", 0.25f);
            Assert.That(world.Commands.SetVariant(target, 2).Admitted, Is.True);
            PumpUntil(() => Slot(target, GameplaySlots.Variant) == 2, "override variant commit");
            GameObject beforeDespawn = View(views, target);
            AssertAppearance(beforeDespawn, true);
            Animator previousAnimator = AssertAnimation(beforeDespawn, controller, 2, "Override", 0.75f);

            Assert.That(world.Commands.Despawn(target).Admitted, Is.True);
            PumpUntil(() => Slot(target, GameplaySlots.Alive) == 0 && !views.TryGetView(target, out _), "despawn removes presentation");
            Assert.That(beforeDespawn == null, Is.True, "the old presentation was destroyed, not just hidden");
            Assert.That(previousAnimator == null, Is.True, "a stale Animator cannot satisfy the respawn assertion");
            Assert.That(world.Commands.Spawn(target).Admitted, Is.True);
            PumpUntil(() => Slot(target, GameplaySlots.Alive) == 1 && views.TryGetView(target, out _), "respawn recreates presentation");
            GameObject respawned = View(views, target);
            Assert.That(ReferenceEquals(respawned, beforeDespawn), Is.False);
            Assert.That(Slot(target, GameplaySlots.Variant), Is.EqualTo(2));
            Assert.That(Slot(target, GameplaySlots.ScaleMilli), Is.EqualTo(1200));
            AssertAppearance(respawned, true);
            AssertAnimation(respawned, controller, 2, "Override", 0.75f);
            Debug.Log("[W-PLUG-02] PASS: committed variant drives real controller state and animated pose; despawn destroys Animator; respawn restores variant prefab, 1.2 scale and binding.");
        }

        private AnimatorController CreateController()
        {
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(assetFolder + "/Binding.controller");
            controller.AddParameter(Parameter, AnimatorControllerParameterType.Int);
            controller.AddParameter(ScaleParameter, AnimatorControllerParameterType.Int);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState normal = machine.AddState("Default");
            AnimatorState overridden = machine.AddState("Override");
            normal.motion = CreateClip("Default", 0.25f);
            overridden.motion = CreateClip("Override", 0.75f);
            machine.defaultState = normal;
            Transition(normal, overridden, 2);
            Transition(overridden, normal, 0);
            AssetDatabase.SaveAssetIfDirty(controller);
            return controller;
        }

        private AnimationClip CreateClip(string name, float x)
        {
            var clip = new AnimationClip { name = name };
            AssetDatabase.CreateAsset(clip, assetFolder + "/" + name + ".anim");
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Probe", typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Constant(0f, 1f, x));
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }

        private static void Transition(AnimatorState from, AnimatorState to, int variant)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.AddCondition(AnimatorConditionMode.Equals, variant, Parameter);
        }

        private GameObject CreatePrefab(AnimatorController controller, string marker)
        {
            var source = new GameObject(marker);
            try
            {
                new GameObject(marker).transform.SetParent(source.transform, false);
                var rig = new GameObject("Rig");
                rig.transform.SetParent(source.transform, false);
                new GameObject("Probe").transform.SetParent(rig.transform, false);
                Animator animator = rig.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                return PrefabUtility.SaveAsPrefabAsset(source, assetFolder + "/" + marker + ".prefab");
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        private static Animator AssertAnimation(GameObject view, RuntimeAnimatorController controller, int variant, string state, float x)
        {
            Animator animator = view.GetComponentInChildren<Animator>();
            Assert.That(animator, Is.Not.Null, "production must instantiate the authored Animator");
            Assert.That(animator.runtimeAnimatorController, Is.SameAs(controller));
            Assert.That(animator.GetInteger(Parameter), Is.EqualTo(variant), "the production AnimatorBinder consumes committed slots");
            Assert.That(animator.GetInteger(ScaleParameter), Is.EqualTo(1200));
            // EditMode does not automatically advance Animator time. Evaluate its real state machine, never Play/SetInteger.
            for (int i = 0; i < 8; i++) animator.Update(1f / 30f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer." + state), Is.True,
                "the committed integer must select the actual controller state: " + state);
            Transform probe = animator.transform.Find("Probe");
            Assert.That(probe.localPosition.x, Is.EqualTo(x).Within(0.001f), "the selected clip must animate the visible hierarchy");
            return animator;
        }

        private static void AssertAppearance(GameObject view, bool overridden)
        {
            Assert.That(view.transform.Find(overridden ? "OverrideAppearance" : "DefaultAppearance"), Is.Not.Null,
                "the committed variant must select the actual replacement prefab");
            Assert.That(view.transform.localScale, Is.EqualTo(Vector3.one * 1.2f), "the scale override must reach the presented transform");
        }

        private void CreateAssetFolder()
        {
            string name = "R7CAnimation_" + Guid.NewGuid().ToString("N");
            string guid = AssetDatabase.CreateFolder("Assets", name);
            Assert.That(guid, Is.Not.Empty);
            assetFolder = AssetDatabase.GUIDToAssetPath(guid);
        }

        private T Own<T>(T value) where T : Object
        {
            owned.Add(value);
            return value;
        }

        private static ManifestEntity FindEntity(RegionManifest manifest, string name)
        {
            foreach (ManifestEntity entity in manifest.Entities)
            {
                if (entity.name == name) return entity;
            }
            throw new InvalidOperationException("No baked entity named " + name);
        }

        private int Slot(TargetId target, SlotId slot) => world!.Slots.ReadOrDefault(target, GameplaySlots.EntityOwner, slot, int.MinValue);

        private void PumpUntil(Func<bool> condition, string stage)
        {
            for (int i = 0; i < 180; i++)
            {
                frame++;
                GameCoreApplicationPump.PumpFrame();
                if (condition()) return;
            }
            Assert.Fail("W-PLUG-02 did not reach " + stage + " within 180 sanctioned frames");
        }

        private static GameObject View(PrefabViewBinder views, TargetId target)
        {
            Assert.That(views.TryGetView(target, out GameObject? view), Is.True);
            return view!;
        }
    }
}
