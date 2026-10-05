#nullable enable
using System.IO;
using System.Linq;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.R4_C
{
    public sealed class RetainedCandidateTests
    {
        [Test]
        public void P42_LIVE_01_RetainedGenericMoveInfersIndexedInstance()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            JObject context = JObject.Parse(File.ReadAllText(Path.Combine(root, "studio/agent/tests/fixtures/r4_c/context.json")));
            JObject envelope = JObject.Parse(File.ReadAllText(Path.Combine(root,
                "artifacts/studio/verification/W-EDIT-07/live-catalog-candidate-mode-20261005T194931.456257Z/real-candidate.json")));
            ChangeSet candidate = StudioJson.Deserialize<ChangeSet>(envelope["changeSet"]!.ToString());
            var validator = new ChangeSetValidator(StudioJson.Deserialize<ToolCatalog>(context["catalog"]!.ToString()),
                StudioJson.Deserialize<SemanticIndex>(context["index"]!.ToString()));
            ChangeSet normalized = validator.NormalizeScopes(candidate);
            Assert.That(normalized.Operations[0].Target!.Scope, Is.EqualTo(AuthorScope.Instance));
            Assert.That(validator.Inferences.Any(d => d.Code == DiagnosticCodes.ScopeInferred), Is.True);
            Assert.That(validator.Validate(candidate).Any(d => d.Code == DiagnosticCodes.ScopeNotAllowed), Is.False);
            foreach (JToken node in context["index"]!["nodes"]!) ((JObject)node["ref"]!).Remove("scope");
            var missing = new ChangeSetValidator(StudioJson.Deserialize<ToolCatalog>(context["catalog"]!.ToString()),
                StudioJson.Deserialize<SemanticIndex>(context["index"]!.ToString()));
            Assert.That(missing.NormalizeScopes(candidate).Operations[0].Target!.Scope, Is.Null);
            Assert.That(missing.Validate(candidate).Any(d => d.Code == DiagnosticCodes.ScopeNotAllowed), Is.True);
        }
    }
}
