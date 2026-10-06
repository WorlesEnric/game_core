#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hollowmere.R6_B
{
    public sealed class DialogueCandidateTests
    {
        public static string Repo
        {
            get
            {
                for (DirectoryInfo? dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
                    if (File.Exists(Path.Combine(dir.FullName, "studio/etos/models.toml.tmpl"))) return dir.FullName;
                throw new InvalidOperationException("Run from the project repository");
            }
        }

        public static bool IsRule(Diagnostic d, string code) => d.Code == DiagnosticCodes.ValidationFailed && d.Message.StartsWith("[" + code + "]", StringComparison.Ordinal);

        public static JObject Witness() => JObject.Parse(File.ReadAllText(Path.Combine(Repo,
            "studio/etos/agent/workers/tests/fixtures/request6-original.json")));

        private static IReadOnlyList<Diagnostic> Check(JObject candidate, bool complete = true)
        {
            ChangeSet cs = StudioJson.Deserialize<ChangeSet>(candidate.ToString());
            JObject graph = JObject.Parse(File.ReadAllText(Path.Combine(Repo,
                "studio/etos/agent/workers/tests/fixtures/request6-odd-before.json")));
            var fields = graph.Properties().ToDictionary(p => p.Name, p => new IndexField("object", p.Value));
            if (!complete) fields.Remove("edges");
            var node = new IndexNode(cs.Operations[1].Target!, "dialogue.graph", fields: fields);
            var index = new SemanticIndex(1, "r6-b", new[] { node });
            // Other model rules have their own catalog tests. This seam must also work on bounded indexes.
            var validator = new ChangeSetValidator(new ToolCatalog(Array.Empty<ObjectTypeEntry>(), Array.Empty<ToolEntry>()), index,
                new ChangeSetValidationOptions { Mode = ValidationMode.Candidate, CheckStamps = false, IndexIsSlice = true });
            return validator.Validate(cs);
        }

        [Test]
        public void R6_Request6_RetainedFerrymanCandidateRefusesWithExactUnreachableWitness()
        {
            Diagnostic d = Check(Witness()).Single(d => IsRule(d, "GP-DLG-005"));
            Assert.That(d.Where!.OpId, Is.EqualTo("op3"));
            Assert.That(d.Data!["unreachable"]!.Values<int>(), Is.EqualTo(Enumerable.Range(0, 8)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void R6_Request6_EntryWithExplicitRelinkPreservesEveryNode(bool multipleFields)
        {
            JObject cs = Witness();
            JObject graph = JObject.Parse(File.ReadAllText(Path.Combine(Repo, "studio/etos/agent/workers/tests/fixtures/request6-odd-before.json")));
            JArray edges = (JArray)graph["edges"]!;
            edges.Add(new JObject { ["from"] = 8, ["port"] = "Next", ["option"] = 0, ["to"] = 0 });
            JObject entry = (JObject)cs["operations"]![2]!;
            if (multipleFields) entry["args"] = new JObject { ["fields"] = new JObject { ["entry"] = 8, ["edges"] = edges } };
            else ((JArray)cs["operations"]!).Add(new JObject
            {
                ["opId"] = "relink", ["tool"] = "set", ["target"] = entry["target"]!.DeepClone(),
                ["args"] = new JObject { ["field"] = "edges", ["value"] = edges }, ["dependsOn"] = new JArray("op3"),
            });
            Assert.That(Check(cs).Any(d => d.Message.StartsWith("[GP-DLG-", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void R6_Request6_DuplicatePortCannotInventAReachablePath()
        {
            JObject cs = Witness();
            JObject graph = JObject.Parse(File.ReadAllText(Path.Combine(Repo, "studio/etos/agent/workers/tests/fixtures/request6-odd-before.json")));
            JArray edges = (JArray)graph["edges"]!;
            edges.Add(new JObject { ["from"] = 8, ["port"] = "Next", ["option"] = 0, ["to"] = -1 });
            edges.Add(new JObject { ["from"] = 8, ["port"] = "Next", ["option"] = 0, ["to"] = 0 });
            cs["operations"]![2]!["args"] = new JObject { ["fields"] = new JObject { ["entry"] = 8, ["edges"] = edges } };
            Assert.That(Check(cs).Single(d => IsRule(d, "GP-DLG-005")).Data!["unreachable"]!.Count(), Is.EqualTo(8));
        }

        [Test]
        public void R6_Request6_IncompleteGraphCannotApproveEntryChange()
        {
            Assert.That(Check(Witness(), false).Any(d => d.Code == DiagnosticCodes.StaleContext), Is.True);
        }

        [Test]
        public void R6_Request6_FieldsEntryCannotBypassReachability()
        {
            JObject cs = Witness();
            cs["operations"]![2]!["args"] = new JObject { ["fields"] = new JObject { ["entry"] = 8 } };
            Assert.That(Check(cs).Single(d => IsRule(d, "GP-DLG-005")).Data!["unreachable"]!.Count(), Is.EqualTo(8));
        }

        [Test]
        public void R6_Request6_OutOfRangeEntryRefusesBeforeBake()
        {
            JObject cs = Witness();
            cs["operations"]![2]!["args"]!["value"] = 99;
            Assert.That(Check(cs).Any(d => IsRule(d, "GP-DLG-002")), Is.True);
        }
    }
}
