#nullable enable
using System;
using System.IO;
using System.Linq;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class R10CatalogFreshnessTests
    {
        [Test]
        public void R10_A_W_MECH_01_StaleDescriptionExportsCurrentWorldWithoutRebake()
        {
            WithWorld((project, packages, baseline) =>
            {
                string descriptionPath = Path.Combine(project, (string)baseline["sourcePath"]!);
                byte[] before = File.ReadAllBytes(descriptionPath);
                try
                {
                    JObject stale = JObject.Parse((string)baseline["description"]!);
                    JProperty implementation = stale.Descendants().OfType<JProperty>().First(p => p.Name == "implementationId");
                    implementation.Value = "11111111111111111111111111111111";
                    File.WriteAllText(descriptionPath, stale.ToString(Formatting.Indented));
                    byte[] committedStale = File.ReadAllBytes(descriptionPath);
                    StageWorldSnapshot.Publish(project, packages, "r10-source", Array.Empty<string>());
                    JObject exported = JObject.Parse(File.ReadAllText(Path.Combine(project, StageWorldSnapshot.RelativePath)));
                    Assert.That((string?)exported["description"], Is.EqualTo((string?)baseline["description"]));
                    Assert.That((string?)exported["sha256"], Is.EqualTo((string?)baseline["sha256"]));
                    Assert.That(File.ReadAllBytes(descriptionPath), Is.EqualTo(committedStale), "export must not rewrite the committed bake");
                }
                finally { File.WriteAllBytes(descriptionPath, before); }
            });
        }

        [Test]
        public void R10_A_W_MECH_01_StaleGeneratedRuntimeRefusesBeforeSnapshotPublication()
        {
            WithWorld((project, packages, baseline) =>
            {
                string world = (string)baseline["worldPath"]!;
                string generated = Path.Combine(project, Path.GetDirectoryName(world)!, "Generated", Path.GetFileNameWithoutExtension(world) + "Catalog.g.cs");
                byte[] before = File.ReadAllBytes(generated);
                try
                {
                    File.AppendAllText(generated, "\n// Stale generated runtime witness.\n");
                    byte[] stale = File.ReadAllBytes(generated);
                    InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() =>
                        StageWorldSnapshot.Publish(project, packages, "r10-source", Array.Empty<string>()));
                    Assert.That(error!.Message, Does.StartWith("bake_stale:").And.Contain("Generated").And.Contain("manifest.asset"));
                    Assert.That(File.ReadAllBytes(generated), Is.EqualTo(stale), "refusal must not silently rebake");
                }
                finally { File.WriteAllBytes(generated, before); }
            });
        }

        private static void WithWorld(Action<string, string, JObject> assertion)
        {
            if (AssetDatabase.FindAssets("t:WorldDefinition").Length != 1)
                Assert.Ignore("Requires the real single-world gameplay project (run in Hollowmere).");
            string project = Path.GetDirectoryName(Application.dataPath)!;
            string snapshot = Path.Combine(project, StageWorldSnapshot.RelativePath);
            byte[]? previous = File.Exists(snapshot) ? File.ReadAllBytes(snapshot) : null;
            string state = Path.Combine(Path.GetTempPath(), "r10-catalog-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (StudioRuntime runtime = StudioRuntime.Create(new StudioRuntimeOptions
                {
                    Paths = new StudioPaths(project, state), LoadIndexCache = false,
                }))
                {
                    string packages = Path.Combine(StageAdmission.Of(runtime).RepositoryRoot!, "Packages");
                    StageWorldSnapshot.Publish(project, packages, "r10-source", Array.Empty<string>());
                    JObject baseline = JObject.Parse(File.ReadAllText(snapshot));
                    assertion(project, packages, baseline);
                }
            }
            finally
            {
                if (previous == null) File.Delete(snapshot);
                else File.WriteAllBytes(snapshot, previous);
                if (Directory.Exists(state)) Directory.Delete(state, true);
            }
        }
    }
}
