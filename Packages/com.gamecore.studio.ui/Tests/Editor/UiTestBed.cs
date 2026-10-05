// GameCore.Studio.UI.Tests - a disposable Studio runtime (temporary asset folder, saved temporary scene, temporary state
// root) with the P1.6 fixture types and tools as the only sources, plus a UI context over it with the test gateway and an
// in-memory task store. Nothing touches the project's own journal or settings.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SelectionMode = GameCore.Studio.Model.SelectionMode;

namespace GameCore.Studio.UI.Tests
{
    internal sealed class UiTestBed : IDisposable
    {
        public const string RootFolder = "Assets/GameCoreStudioUiTests";

        public static readonly Type[] FixtureTypes =
        {
            typeof(FixtureItemDefinition), typeof(FixtureNpcDefinition), typeof(FixtureAuthoredEntity), typeof(FixtureDuckEntity), typeof(FixtureRegion),
        };

        private readonly List<StudioRuntime> _runtimes = new List<StudioRuntime>();
        private readonly List<StudioUiContext> _contexts = new List<StudioUiContext>();

        public UiTestBed()
        {
            string leaf = Guid.NewGuid().ToString("N").Substring(0, 10);
            if (!AssetDatabase.IsValidFolder(RootFolder))
            {
                AssetDatabase.CreateFolder("Assets", "GameCoreStudioUiTests");
            }

            AssetDatabase.CreateFolder(RootFolder, leaf);
            Folder = RootFolder + "/" + leaf;
            StateRoot = Path.Combine(Path.GetTempPath(), "gcstudio-p21-" + leaf);
            Directory.CreateDirectory(StateRoot);
            Log = new MemoryStudioLog();
            Gateway = new TestAgentGateway();
            Store = new MemoryTaskRowStore();
            Scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(Scene, Folder + "/Test.unity");
            Runtime = CreateRuntime(false);
            Context = CreateContext(Runtime, Store);
        }

        public string Folder { get; }

        public string StateRoot { get; }

        public MemoryStudioLog Log { get; }

        public TestAgentGateway Gateway { get; }

        public MemoryTaskRowStore Store { get; private set; }

        public Scene Scene { get; }

        public StudioRuntime Runtime { get; private set; }

        public StudioUiContext Context { get; private set; }

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

        public StudioRuntime CreateRuntime(bool loadIndexCache)
        {
            StudioRuntime runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(ProjectRoot, StateRoot, "p21-test"),
                Log = Log,
                TypeSource = () => FixtureTypes,
                ToolMethodSource = FixtureMethods,
                SearchFolders = new[] { Folder },
                LoadIndexCache = loadIndexCache,
            });
            runtime.Services.AgentGateway = Gateway;
            _runtimes.Add(runtime);
            return runtime;
        }

        public StudioUiContext CreateContext(StudioRuntime runtime, ITaskRowStore store)
        {
            StudioUiContext context = new StudioUiContext(runtime, () => Gateway, new SelectionModel(runtime), new TaskLedger(store), false);
            _contexts.Add(context);
            return context;
        }

        /// <summary>
        /// Simulates a domain reload: the task store round-trips through EditorJsonUtility (as a ScriptableSingleton's
        /// serialized state would), the runtime and the UI context are rebuilt over the same durable state.
        /// </summary>
        public StudioUiContext Reload()
        {
            string json = EditorJsonUtility.ToJson(Store);
            MemoryTaskRowStore restored = new MemoryTaskRowStore();
            EditorJsonUtility.FromJsonOverwrite(json, restored);
            Context.Dispose();
            Runtime.Dispose();
            Store = restored;
            Runtime = CreateRuntime(true);
            Context = CreateContext(Runtime, Store);
            return Context;
        }

        public static void MintId(UnityEngine.Object target)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty("authoringId").stringValue = Guid.NewGuid().ToString("N");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public FixtureNpcDefinition CreateNpc(string name, string greeting = "Hello")
        {
            FixtureNpcDefinition npc = ScriptableObject.CreateInstance<FixtureNpcDefinition>();
            npc.greeting = greeting;
            MintId(npc);
            AssetDatabase.CreateAsset(npc, Folder + "/" + name + ".asset");
            return npc;
        }

        public FixtureAuthoredEntity SpawnEntity(string name, Vector3 position, FixtureNpcDefinition? npc = null)
        {
            GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = name;
            gameObject.transform.position = position;
            FixtureAuthoredEntity entity = gameObject.AddComponent<FixtureAuthoredEntity>();
            entity.definition = npc;
            MintId(entity);
            return entity;
        }

        public void SaveScene()
        {
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }

        public AuthoringRef Ref(UnityEngine.Object target)
        {
            return Runtime.Resolver.BuildRef(target, null, true) ?? throw new InvalidOperationException("No ref for " + target.name);
        }

        public string CatalogRevision()
        {
            ToolCatalog catalog = Runtime.Registry.Catalog;
            return catalog.Revision ?? catalog.ComputeRevision();
        }

        /// <summary>A camera at (0, 3, -10) looking at the origin with a 800x600 viewport.</summary>
        public static Camera CreateCamera(Rect viewport)
        {
            GameObject cameraObject = new GameObject("UiPickCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            Vector3 eye = new Vector3(0f, 3f, -10f);
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Vector3.zero - eye));
            camera.fieldOfView = 60f;
            camera.aspect = viewport.width / viewport.height;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            return camera;
        }

        public static void Timing(string name, double milliseconds)
        {
            Debug.Log("[P2.1] " + name + ": " + milliseconds.ToString("0.00", CultureInfo.InvariantCulture) + " ms");
        }

        public void Dispose()
        {
            foreach (StudioUiContext context in _contexts)
            {
                context.Dispose();
            }

            foreach (StudioRuntime runtime in _runtimes)
            {
                runtime.Dispose();
            }

            Selection.objects = Array.Empty<UnityEngine.Object>();
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
