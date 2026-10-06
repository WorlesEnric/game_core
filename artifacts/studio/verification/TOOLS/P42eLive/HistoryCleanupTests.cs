#nullable enable
using System;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Entities;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace P42e.Live
{
    public sealed class HistoryCleanupTests
    {
        [Test]
        public void R2_38_P42e_NormalJournalUndoAfterNpcPlayReload()
        {
            string? id = Environment.GetEnvironmentVariable("GAMECORE_P42E_UNDO_ID");
            if (string.IsNullOrEmpty(id)) Assert.Ignore("Explicit retained packet change-set id required");
            EditorSceneManager.OpenScene("Assets/Hollowmere/Regions/ThornwickVillage.unity");
            var runtime = StudioUiSession.Context.Runtime;
            runtime.Index.Rebuild();
            int before = UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Length;
            var journal = runtime.Journal.Read(id!);
            Assert.That(journal?.EffectiveState, Is.EqualTo(ChangeSetState.Applied));
            var result = runtime.History.Undo(id!);
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveOpenScenes();
            int after = UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Length;
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!, "normal-undo.json"),
                new JObject { ["id"] = id, ["ok"] = result.Ok, ["beforeCount"] = before, ["afterCount"] = after,
                    ["state"] = result.State?.ToString(), ["diagnostics"] = new JArray(result.Diagnostics.Select(StudioJson.ToToken)) }.ToString());
            Assert.That(result.Ok, Is.True);
            Assert.That(result.State, Is.EqualTo(ChangeSetState.Undone));
            Assert.That(before, Is.EqualTo(21)); Assert.That(after, Is.EqualTo(20));
        }
    }
}
