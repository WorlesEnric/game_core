// Hollowmere P3.1 EditMode - the authored reference game: AuthorAll is idempotent (a second run applies no change set,
// writes no journal entry and changes no file under Assets/Hollowmere), the bake verifies, and the authored content is
// bound to scripts (every MonoBehaviour / ScriptableObject reference of the Hollowmere assets resolves to a class, the
// boot scene carries HollowmereGame with its director, the director covers the three endings and the failure).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.World;
using GameCore.Studio.Edit;
using Hollowmere.Authoring;
using Hollowmere.Game;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Hollowmere.P3_1.EditMode.Tests
{
    public sealed class P31AuthoringTests
    {
        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath)!;

        /// <summary>sha256 of every file under Assets/Hollowmere (path -> digest).</summary>
        private static SortedDictionary<string, string> Snapshot()
        {
            var digests = new SortedDictionary<string, string>(StringComparer.Ordinal);
            string root = Path.Combine(ProjectRoot, "Assets", "Hollowmere");
            using (SHA256 sha = SHA256.Create())
            {
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(ProjectRoot.Length + 1).Replace('\\', '/');
                    byte[] hash = sha.ComputeHash(File.ReadAllBytes(file));
                    digests[relative] = BitConverter.ToString(hash).Replace("-", string.Empty);
                }
            }

            return digests;
        }

        [Test]
        public void AuthorAllIsIdempotent()
        {
            StudioRuntime runtime = StudioServices.Runtime;
            int journalBefore = runtime.Journal.List().Count;
            SortedDictionary<string, string> before = Snapshot();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            AuthorAllReport report = HollowmereAuthoring.AuthorAll(null, 0);

            SortedDictionary<string, string> after = Snapshot();
            int journalAfter = runtime.Journal.List().Count;
            var changed = new List<string>();
            foreach (KeyValuePair<string, string> pair in after)
            {
                if (!before.TryGetValue(pair.Key, out string? digest) || digest != pair.Value)
                {
                    changed.Add(pair.Key);
                }
            }

            foreach (string path in before.Keys)
            {
                if (!after.ContainsKey(path))
                {
                    changed.Add(path + " (deleted)");
                }
            }

            Debug.Log("[P3.1] AuthorAll second run: " + report + "; journal " + journalBefore + " -> " + journalAfter + "; files " + after.Count
                + ", changed " + changed.Count + "; " + watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms");
            Assert.That(report.Error, Is.Null, report.Error);
            Assert.That(report.Blocked, Is.Empty, "steps blocked: " + string.Join("; ", report.Blocked));
            Assert.That(report.Applied, Is.Empty, "the second run applied steps: " + string.Join(", ", report.Applied));
            Assert.That(report.Skipped.Count, Is.GreaterThan(40), "the journal holds the P3.1 steps");
            Assert.That(journalAfter, Is.EqualTo(journalBefore), "no new journal entries");
            Assert.That(changed, Is.Empty, "files changed by the second run: " + string.Join(", ", changed.Take(20)));
            Assert.That(report.BakeOk && report.VerifyOk, Is.True, report.Bake + " / " + report.Verify);
        }

        [Test]
        public void BakeVerifies()
        {
            WorldDefinition world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(HollowmerePaths.WorldPath);
            Assert.That(world, Is.Not.Null);
            BakeResult verify = Entry.Verify(world, BakePaths.ConventionFor(HollowmerePaths.WorldPath));
            Debug.Log("[P3.1] verify: " + verify);
            Assert.That(verify.Succeeded, Is.True, verify.ToString());
        }

        private static readonly Regex ScriptRef = new Regex(@"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32}), type: 3\}", RegexOptions.Compiled);

        [Test]
        public void ContentIsBoundToScripts()
        {
            string root = Path.Combine(ProjectRoot, "Assets", "Hollowmere");
            var unresolved = new List<string>();
            int references = 0;
            var guids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(file);
                if (extension != ".asset" && extension != ".prefab" && extension != ".unity")
                {
                    continue;
                }

                foreach (Match match in ScriptRef.Matches(File.ReadAllText(file)))
                {
                    references++;
                    string guid = match.Groups[1].Value;
                    if (!guids.Add(guid))
                    {
                        continue;
                    }

                    MonoScript? script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                    if (script == null || script.GetClass() == null)
                    {
                        unresolved.Add(guid + " in " + file.Substring(ProjectRoot.Length + 1));
                    }
                }
            }

            Debug.Log("[P3.1] script references: " + references + " (" + guids.Count + " scripts), unresolved " + unresolved.Count);
            Assert.That(unresolved, Is.Empty, string.Join("\n", unresolved));

            EditorSceneManager.OpenScene(HollowmerePaths.BootScene, OpenSceneMode.Single);
            HollowmereGame? game = UnityEngine.Object.FindAnyObjectByType<HollowmereGame>();
            Assert.That(game, Is.Not.Null, "Boot.unity carries HollowmereGame");
            Assert.That(game!.GetComponent<Hollowmere.Boot.GameBoot>(), Is.Not.Null, "next to GameBoot");
            HollowmereDirectorDefinition? director = AssetDatabase.LoadAssetAtPath<HollowmereDirectorDefinition>(HollowmerePaths.Director);
            Assert.That(director, Is.Not.Null);
            Assert.That(game.DirectorDefinition, Is.SameAs(director), "HollowmereGame points at the director asset");
            foreach (int branch in new[] { -1, 1, 2, 3 })
            {
                Assert.That(director!.EndingFor(branch), Is.Not.Null, "an ending for branch " + branch);
            }

            CollectionAssert.AreEqual(HollowmereAuthoring.BuildScenes, EditorBuildSettings.scenes.Select(scene => scene.path).ToArray(), "build settings: Boot + the three regions");
        }
    }
}
