// GameCore.Studio.Views.Hollowmere.Tests - shared set-up: a Studio runtime over Hollowmere (temporary state root, so
// the tests' journal never mixes with the project's), ThornwickVillage open (so its placed entities and region marker
// are indexed), a view context with an in-memory selection and a gameplay bridge that is never playing, and a byte
// backup of every asset a test may change (restored in TearDown even when an undo fails).
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameCore.Studio.Views.Hollowmere.Tests
{
    public abstract class HollowmereViewsFixture
    {
        public const string Root = "Assets/Hollowmere";
        public const string TempFolder = "Assets/P2_3ViewsTemp";
        public const string Thornwick = Root + "/Regions/ThornwickVillage.unity";

        /// <summary>studio.core's retained-blob folder (delete/replace inverses); removed when a test created it.</summary>
        public const string BlobFolder = "Assets/GameCoreStudioTemp";

        protected static readonly string[] BackedUp =
        {
            Root + "/Dialogue/Graphs/Maren.asset",
            Root + "/Items/Lantern.asset",
            Root + "/Items/OldCoin.asset",
            Root + "/Items/GateKey.asset",
            Root + "/Quests/DrownedBell.asset",
            Root + "/World/Hollowmere.asset",
            Root + "/World/Regions/ThornwickVillage.asset",
            Root + "/World/Regions/BlackmereMarsh.asset",
            Root + "/World/Regions/DrownedBelfry.asset",
        };

        private readonly Dictionary<string, byte[]> _backup = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private string _stateRoot = string.Empty;
        private List<string> _portalsBefore = new List<string>();
        private bool _blobFolderExisted;

        protected StudioRuntime Runtime { get; private set; } = null!;

        protected StudioViewContext Context { get; private set; } = null!;

        protected ListSelectionBridge Selection { get; private set; } = null!;

        [SetUp]
        public void SetUpRuntime()
        {
            string project = Directory.GetParent(Application.dataPath)!.FullName;
            foreach (string asset in BackedUp)
            {
                _backup[asset] = File.ReadAllBytes(Path.Combine(project, asset));
            }

            _portalsBefore = new List<string>(Directory.GetFiles(Path.Combine(project, Root, "World", "Portals"), "*.asset"));
            _blobFolderExisted = AssetDatabase.IsValidFolder(BlobFolder);
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "P2_3ViewsTemp");
            }

            EditorSceneManager.OpenScene(Thornwick, OpenSceneMode.Single);
            _stateRoot = Path.Combine(Path.GetTempPath(), "gcstudio-p23-views-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_stateRoot);
            Runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(project, _stateRoot, "p23-views"),
                Log = new MemoryStudioLog(),
                SearchFolders = new[] { Root, TempFolder },
                IndexScope = AuthoringSourceScope.All,
                LoadIndexCache = false,
            });
            Selection = new ListSelectionBridge();
            Context = new StudioViewContext(Runtime, Selection, new ReflectionGameplayBridge(null, () => false), false);
        }

        [TearDown]
        public void TearDownRuntime()
        {
            Context?.Dispose();
            Runtime?.Dispose();

            // An edit whose undo failed can still be only in memory (dirty, not yet written); write it now so the
            // byte comparison below sees it and the restore replaces it, instead of a later save writing it back.
            AssetDatabase.SaveAssets();
            string project = Directory.GetParent(Application.dataPath)!.FullName;
            foreach (KeyValuePair<string, byte[]> asset in _backup)
            {
                string path = Path.Combine(project, asset.Key);
                if (!File.Exists(path) || !BytesEqual(File.ReadAllBytes(path), asset.Value))
                {
                    File.WriteAllBytes(path, asset.Value);
                    AssetDatabase.ImportAsset(asset.Key, ImportAssetOptions.ForceUpdate);
                }
            }

            foreach (string portal in Directory.GetFiles(Path.Combine(project, Root, "World", "Portals"), "*.asset"))
            {
                if (!_portalsBefore.Contains(portal))
                {
                    AssetDatabase.DeleteAsset(Root + "/World/Portals/" + Path.GetFileName(portal));
                }
            }

            AssetDatabase.DeleteAsset(TempFolder);
            if (!_blobFolderExisted)
            {
                AssetDatabase.DeleteAsset(BlobFolder);
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (Directory.Exists(_stateRoot))
            {
                Directory.Delete(_stateRoot, true);
            }
        }

        protected IndexGraph Graph() => Context.Graph();

        protected IndexNode NodeAt(string assetPath)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            Assert.That(asset, Is.Not.Null, assetPath);
            AuthoringRef? reference = Runtime.Resolver.BuildRef(asset, null, false);
            Assert.That(reference, Is.Not.Null, assetPath + " has no authoring ref");
            IndexNode? node = Graph().Node(reference!.IdentityKey);
            Assert.That(node, Is.Not.Null, assetPath + " is not in the index");
            return node!;
        }

        protected static void Log(string text) => UnityEngine.Debug.Log("[P2.3] " + text);

        /// <summary>
        /// Inconclusive (not passed, not failed) while studio.core rejects the gameplay packages'
        /// <c>[AuthorField(Type = "authoringId")]</c> (DialogueGraphDefinition.speakerEntityId and others): until then
        /// dialogue graphs and quests are neither indexed nor in the tool catalog. Reported in PACKET.md (left open).
        /// </summary>
        protected void RequireAuthoringIdValueType()
        {
            UnityEngine.Object graph = AssetDatabase.LoadMainAssetAtPath(Root + "/Dialogue/Graphs/Maren.asset");
            try
            {
                Runtime.Resolver.BuildRef(graph, null, false);
            }
            catch (InvalidOperationException error) when (error.Message.Contains("'authoringId'"))
            {
                Assert.Inconclusive("Blocked outside P2.3 (studio.core ValueTypes vs gameplay [AuthorField(Type = \"authoringId\")]): " + error.Message);
            }
        }

        /// <summary>
        /// Journal undo; Inconclusive when it fails with the known studio.core defect where the set tool's inverse
        /// records nested [Serializable] list elements holding object references as their type name.
        /// </summary>
        protected HistoryResult UndoOrInconclusive(string changeSetId)
        {
            HistoryResult undo = Context.Edits.Undo(changeSetId);
            string diagnostics = string.Join("; ", undo.Diagnostics);
            if (!undo.Ok && diagnostics.Contains("expected object, got string"))
            {
                Log("undo blocked by the set-inverse defect: " + diagnostics);
                Assert.Inconclusive("Blocked outside P2.3 (studio.core set inverse via ValueCodec.FromClr loses nested list elements): " + diagnostics);
            }

            return undo;
        }

        protected static T First<T>(IEnumerable<T> items, Func<T, bool> match)
            where T : class
        {
            foreach (T item in items)
            {
                if (match(item))
                {
                    return item;
                }
            }

            Assert.Fail("no matching " + typeof(T).Name);
            return null!;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
