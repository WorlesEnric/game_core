#nullable enable
using System;
using System.IO;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.R5_B
{
    public sealed class ImporterTests
    {
        [Test]
        public void Request8_OriginalCandidateStillRefusesAndPublishedEnumsMatchUnity()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            JObject receipt = JObject.Parse(File.ReadAllText(Path.Combine(repo, "studio/etos/agent/workers/tests/fixtures/request8-original.json")));
            JObject args = (JObject)receipt["candidate"]!["operations"]![0]!["args"]!;
            var paths = new StudioPaths(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), Path.Combine(Path.GetTempPath(), "r5-b-import"));
            string? refusal = MediaImportPolicy.Validate(paths, (string)args["path"]!, (JObject)args["importer"]!);
            Assert.That(refusal, Is.Not.Null);
            Assert.That(MediaImportPolicy.Code(refusal!), Is.EqualTo(DiagnosticCodes.MediaImporterInvalid));
            JObject contract = JObject.Parse(File.ReadAllText(Path.Combine(repo, "studio/etos/agent/workers/importer-contract.json")));
            foreach (JProperty property in ((JObject)contract["properties"]!).Properties())
            {
                foreach (JToken value in property.Value["enum"]!)
                {
                    var settings = new JObject { [property.Name] = value.DeepClone() };
                    if (property.Name == "spriteImportMode") settings["textureType"] = "Sprite";
                    Assert.That(MediaImportPolicy.Validate(paths, "Assets/R5_B_Enum.png", settings), Is.Null, property.Name + ":" + value);
                }
            }
        }
    }
}
