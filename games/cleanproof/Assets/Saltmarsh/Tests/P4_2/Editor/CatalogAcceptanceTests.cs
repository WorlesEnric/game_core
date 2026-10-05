#nullable enable
using System;
using System.IO;
using System.Linq;
using GameCore.Studio.Edit;
using NUnit.Framework;
using UnityEngine;

namespace Saltmarsh.P4_2
{
    public sealed class CatalogAcceptanceTests
    {
        [Test]
        public void R2_39_W_TOOL_01_ExportOnlyInstalledProductionTools()
        {
            using StudioRuntime runtime = StudioRuntime.Create();
            string[] ids = runtime.Registry.Catalog.Tools.Select(t => t.Id).ToArray();
            Assert.That(ids.Length, Is.GreaterThan(0));
            foreach (string expected in new[]
            {
                "world.connectRegions", "entity.place", "player.tuneMovement", "npc.setDialogue",
                "interaction.setStates", "dialogue.addLine", "quest.addStage", "inventory.grantStarting",
                "logic.test", "ui.bind", "audio.assignClip", "save.testRoundTrip", "saltmarsh.author",
            }) Assert.That(ids, Does.Contain(expected), "installed game/plugin tool " + expected);
            Assert.That(ids, Does.Not.Contain("mechanism.admit"));
            Assert.That(ids, Does.Not.Contain("mechanism.remove"));
            Assert.That(ids.Any(id => id.StartsWith("fixture.", StringComparison.Ordinal) || id.StartsWith("hollowmere.", StringComparison.Ordinal)), Is.False);
            Assert.That(AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name!.StartsWith("Hollowmere", StringComparison.Ordinal)), Is.False);
            string project = Directory.GetParent(Application.dataPath)!.FullName;
            string repo = Path.GetFullPath(Path.Combine(project, "..", ".."));
            string output = Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE") ??
                Path.Combine(repo, "artifacts", "studio", "verification", "W-TOOL-01", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ"));
            Directory.CreateDirectory(output);
            runtime.Registry.Export(Path.Combine(output, "tool-catalog.json"));
            File.Copy(Path.Combine(project, "Packages", "manifest.json"), Path.Combine(output, "installed-packages.json"), true);
            Debug.Log("[P4.2] W-TOOL-01 exported " + ids.Length + " production tools; no fixture, foreign-game, or internal admission tools.");
        }
    }
}
