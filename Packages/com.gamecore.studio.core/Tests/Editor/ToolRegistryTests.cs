// GameCore.Studio.Edit.Tests - tool registry: built-ins plus [AuthorOperation] discovery, catalog export with a minted
// revision, and the candidate catalog-revision check (StaleContext) (docs/studio/03-authoring-contracts.md s4, s5, s9).
#nullable enable
using System.Collections.Generic;
using System.IO;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class ToolRegistryTests
    {
        private StudioTestBed _bed = null!;

        [SetUp]
        public void SetUp() => _bed = new StudioTestBed(false);

        [TearDown]
        public void TearDown() => _bed.Dispose();

        [Test]
        public void Export_ContainsBuiltInsFixtureToolsAndObjectTypesWithARevision()
        {
            string path = _bed.Runtime.Registry.Export();
            Assert.That(path, Is.EqualTo(_bed.Runtime.Paths.CatalogPath));
            ToolCatalog catalog = StudioJson.Deserialize<ToolCatalog>(File.ReadAllText(path));

            Assert.That(catalog.Revision, Is.Not.Null);
            Assert.That(catalog.HasValidRevision(), Is.True);
            Assert.That(catalog.Revision, Is.EqualTo(_bed.Runtime.Registry.Catalog.Revision));

            List<string> ids = new List<string>();
            foreach (ToolEntry tool in catalog.Tools)
            {
                ids.Add(tool.Id);
            }

            foreach (string id in BuiltInToolIds.All)
            {
                Assert.That(ids, Does.Contain(id));
            }

            foreach (string id in BuiltInToolIdsExt.Generic)
            {
                Assert.That(ids, Does.Contain(id));
            }

            Assert.That(ids, Does.Contain("fixture.setGreeting"));
            Assert.That(ids, Does.Contain("fixture.fail"));
            Assert.That(ids, Does.Not.Contain(BuiltInToolIdsExt.RestoreAsset), "internal journal tools are not exported");
            Assert.That(ids, Is.Ordered.Using(System.StringComparer.Ordinal));

            ToolEntry? greeting = catalog.FindTool("fixture.setGreeting");
            Assert.That(greeting!.TargetType, Is.EqualTo("fixture.npc"));
            Assert.That(greeting.FindArg("greeting")?.Type, Is.EqualTo(ValueTypes.String));

            ObjectTypeEntry? item = catalog.FindObjectType("fixture.item");
            Assert.That(item, Is.Not.Null);
            bool hasWeight = false;
            foreach (FieldSpec field in item!.Fields)
            {
                hasWeight |= field.Name == "weight" && field.Unit == "kg";
            }

            Assert.That(hasWeight, Is.True);
            Assert.That(_bed.Runtime.Registry.Problems, Is.Empty);
        }

        [Test]
        public void Invoke_RunsReadOnlyToolsAndRefusesMutatingOnes()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern");
            _bed.Runtime.Index.Rebuild();
            OperationResult described = _bed.Runtime.Registry.Invoke(BuiltInToolIds.InspectDescribe, _bed.Ref(lantern), null);
            Assert.That(described.Status, Is.EqualTo(OutcomeStatus.Applied));
            Assert.That((string?)((JObject)described.Output!)["type"], Is.EqualTo("fixture.item"));

            OperationResult refused = _bed.Runtime.Registry.Invoke(BuiltInToolIdsExt.Set, _bed.Ref(lantern), new JObject { ["field"] = "weight", ["value"] = 3 });
            Assert.That(refused.Status, Is.EqualTo(OutcomeStatus.Refused));

            OperationResult explain = _bed.Runtime.Registry.Invoke(BuiltInToolIds.InspectExplain, _bed.Ref(lantern), null);
            Assert.That(explain.Code, Is.EqualTo(DiagnosticCodes.NotConfigured));
            OperationResult build = _bed.Runtime.Registry.Invoke(BuiltInToolIds.ProjectBuild, null, null);
            Assert.That(build.Code, Is.EqualTo(DiagnosticCodes.NotConfigured));
        }

        [Test]
        public void Candidate_WithAnotherCatalogRevision_IsStaleContext()
        {
            FixtureNpcDefinition npc = _bed.CreateNpc("Ferryman");
            _bed.Runtime.Index.Rebuild();
            Operation operation = StudioTestBed.Op("op1", "fixture.setGreeting", _bed.Ref(npc), new JObject { ["greeting"] = "Ahoy" });
            ChangeSet candidate = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("greet", IntentOrigin.Agent), new[] { operation });
            string revision = _bed.Runtime.Registry.Catalog.Revision!;

            StagedChangeSet stale = _bed.Runtime.Engine.Stage(candidate, new StageOptions { Mode = ValidationMode.Candidate, ToolCatalogRevision = new string('0', 64), JournalCandidate = false });
            Assert.That(stale.Diagnostics, Has.Some.Matches<Diagnostic>(d => d.Code == DiagnosticCodes.StaleContext && (string?)d.Data?["expected"] == revision));
            _bed.Runtime.Engine.Discard(stale);

            StagedChangeSet missing = _bed.Runtime.Engine.Stage(candidate, new StageOptions { Mode = ValidationMode.Candidate, JournalCandidate = false });
            Assert.That(missing.Diagnostics, Has.Some.Matches<Diagnostic>(d => d.Code == DiagnosticCodes.StaleContext));
            _bed.Runtime.Engine.Discard(missing);

            StagedChangeSet current = _bed.Runtime.Engine.Stage(candidate, new StageOptions { Mode = ValidationMode.Candidate, ToolCatalogRevision = revision });
            Assert.That(current.Ok, Is.True, string.Join("; ", current.AllDiagnostics));
            Assert.That(_bed.Runtime.Journal.Read(candidate.Id)?.State, Is.EqualTo(ChangeSetState.Candidate), "a valid candidate is journaled so a domain reload can re-stage it");
            ApplyReport report = _bed.Runtime.Engine.Apply(current);
            Assert.That(report.State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(npc.greeting, Is.EqualTo("Ahoy"));
        }
    }
}
