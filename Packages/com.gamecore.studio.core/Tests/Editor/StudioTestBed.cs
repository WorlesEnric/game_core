// GameCore.Studio.Edit.Tests - a disposable Studio runtime over a temporary asset folder, a saved temporary scene and a
// temporary state root (journal, artifacts, index cache), with the fixture types and tools as the only type and tool
// sources so results do not depend on what else the host project contains.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Edit.Tests
{
    internal sealed class StudioTestBed : IDisposable
    {
        public const string RootFolder = "Assets/GameCoreStudioTests";

        public static readonly Type[] FixtureTypes =
        {
            typeof(FixtureItemDefinition), typeof(FixtureNpcDefinition), typeof(FixtureAuthoredEntity), typeof(FixtureDuckEntity), typeof(FixtureRegion), typeof(FixtureDialogueDefinition),
        };

        private readonly List<StudioRuntime> _runtimes = new List<StudioRuntime>();

        public StudioTestBed(bool openScene = true, EngineOptions? engine = null)
        {
            string leaf = Guid.NewGuid().ToString("N").Substring(0, 10);
            if (!AssetDatabase.IsValidFolder(RootFolder))
            {
                AssetDatabase.CreateFolder("Assets", "GameCoreStudioTests");
            }

            AssetDatabase.CreateFolder(RootFolder, leaf);
            Folder = RootFolder + "/" + leaf;
            StateRoot = Path.Combine(Path.GetTempPath(), "gcstudio-p16-" + leaf);
            Directory.CreateDirectory(StateRoot);
            Log = new MemoryStudioLog();
            Live = new FakeLiveGateway();
            Gateway = new CountingAgentGateway();
            if (openScene)
            {
                Scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(Scene, Folder + "/Test.unity");
            }

            Runtime = CreateRuntime(engine);
        }

        public string Folder { get; }

        public string StateRoot { get; }

        public MemoryStudioLog Log { get; }

        public FakeLiveGateway Live { get; }

        public CountingAgentGateway Gateway { get; }

        public Scene Scene { get; }

        public StudioRuntime Runtime { get; private set; }

        public static string ProjectRoot => Directory.GetParent(Application.dataPath)!.FullName;

        public static IEnumerable<MethodInfo> FixtureMethods()
        {
            foreach (MethodInfo method in typeof(FixtureTools).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (AuthoringMetadata.Operation(method) != null)
                {
                    yield return method;
                }
            }
        }

        /// <summary>A runtime over the same folders and state root (a second runtime simulates a domain reload).</summary>
        public StudioRuntime CreateRuntime(EngineOptions? engine = null, bool loadIndexCache = false)
        {
            StudioRuntime runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(ProjectRoot, StateRoot, "p16-test"),
                Log = Log,
                TypeSource = () => FixtureTypes,
                ToolMethodSource = FixtureMethods,
                SearchFolders = new[] { Folder },
                Live = Live,
                Engine = engine,
                LoadIndexCache = loadIndexCache,
            });
            runtime.Services.AgentGateway = Gateway;
            _runtimes.Add(runtime);
            return runtime;
        }

        /// <summary>Replaces <see cref="Runtime"/> with a fresh one over the same durable state (domain reload).</summary>
        public StudioRuntime Reload(EngineOptions? engine = null)
        {
            Runtime.Dispose();
            Runtime = CreateRuntime(engine, true);
            return Runtime;
        }

        public static void MintId(UnityEngine.Object target)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty("authoringId").stringValue = Guid.NewGuid().ToString("N");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public FixtureItemDefinition CreateItem(string name, float weight = 1f)
        {
            FixtureItemDefinition item = ScriptableObject.CreateInstance<FixtureItemDefinition>();
            item.weight = weight;
            item.displayName = name;
            MintId(item);
            AssetDatabase.CreateAsset(item, Folder + "/" + name + ".asset");
            return item;
        }

        public FixtureNpcDefinition CreateNpc(string name, string greeting = "Hello", FixtureItemDefinition? item = null)
        {
            FixtureNpcDefinition npc = ScriptableObject.CreateInstance<FixtureNpcDefinition>();
            npc.greeting = greeting;
            npc.startingItem = item;
            MintId(npc);
            AssetDatabase.CreateAsset(npc, Folder + "/" + name + ".asset");
            return npc;
        }

        public FixtureAuthoredEntity SpawnEntity(string name, Vector3 position, FixtureNpcDefinition? npc = null, Transform? parent = null, bool collider = true)
        {
            GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = name;
            if (!collider)
            {
                UnityEngine.Object.DestroyImmediate(gameObject.GetComponent<BoxCollider>());
            }

            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }

            gameObject.transform.position = position;
            FixtureAuthoredEntity entity = gameObject.AddComponent<FixtureAuthoredEntity>();
            entity.definition = npc;
            MintId(entity);
            return entity;
        }

        public FixtureRegion SpawnRegion(string name)
        {
            GameObject gameObject = new GameObject(name);
            FixtureRegion region = gameObject.AddComponent<FixtureRegion>();
            region.regionName = name.ToLowerInvariant();
            MintId(region);
            return region;
        }

        public void SaveScene()
        {
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }

        public AuthoringRef Ref(UnityEngine.Object target, bool stamp = true)
        {
            AuthoringRef? reference = Runtime.Resolver.BuildRef(target, null, stamp);
            if (reference == null)
            {
                throw new InvalidOperationException("No ref for " + target.name);
            }

            return reference;
        }

        public static Operation Op(string opId, string tool, AuthoringRef? target, JObject? args = null, params string[] dependsOn)
        {
            return new Operation(opId, tool, target, args, dependsOn.Length == 0 ? null : dependsOn);
        }

        public static Operation Set(string opId, AuthoringRef target, string field, JToken value)
        {
            return new Operation(opId, BuiltInToolIdsExt.Set, target, new JObject { ["field"] = field, ["value"] = value });
        }

        public static ChangeSet NewChangeSet(string intent, ApplyPolicy? policy, params Operation[] operations)
        {
            return new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent(intent, IntentOrigin.Manual), operations, policy: policy);
        }

        /// <summary>Logs a timing line the run summary collects ("[P1.6] name: 12.3 ms").</summary>
        public static void Timing(string name, Stopwatch watch)
        {
            UnityEngine.Debug.Log("[P1.6] " + name + ": " + watch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms");
        }

        public void Dispose()
        {
            foreach (StudioRuntime runtime in _runtimes)
            {
                runtime.Dispose();
            }

            Undo.ClearAll();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
            if (AssetDatabase.IsValidFolder(RootFolder) && AssetDatabase.FindAssets(string.Empty, new[] { RootFolder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(RootFolder);
            }

            try
            {
                Directory.Delete(StateRoot, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
