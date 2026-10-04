// Schema emission: the committed docs/studio/schemas files are byte-identical to a fresh emission, emission is
// deterministic with sorted keys, and the conformance checker used by the round-trip tests rejects bad instances.
#nullable enable
using System.IO;
using GameCore.Studio.Model;
using GameCore.Studio.Model.Schema;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Model.Tests
{
    public sealed class SchemaTests
    {
        [Test]
        public void CommittedSchemasAreCurrent()
        {
            string directory = Path.Combine(RepositoryRoot.Find(), "docs", "studio", "schemas");
            foreach (SchemaDocument document in StudioSchemaEmitter.EmitAll())
            {
                string path = Path.Combine(directory, document.FileName);
                Assert.That(File.Exists(path), Is.True, path + " is missing; run tools/studio/emit_studio_schemas.py");
                Assert.That(File.ReadAllText(path), Is.EqualTo(document.Json),
                    document.FileName + " is stale; run tools/studio/emit_studio_schemas.py");
            }

            Assert.That(Directory.GetFiles(directory, "*.schema.json"), Has.Length.EqualTo(StudioSchemaEmitter.Roots.Count));
        }

        [Test]
        public void EmissionIsDeterministicWithSortedKeys()
        {
            var first = StudioSchemaEmitter.EmitAll();
            var second = StudioSchemaEmitter.EmitAll();
            Assert.That(second, Has.Count.EqualTo(first.Count));
            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(second[i].Json, Is.EqualTo(first[i].Json));
                Assert.That(StudioSchemaEmitter.Write(JToken.Parse(first[i].Json)), Is.EqualTo(first[i].Json), "keys are already sorted");
                Assert.That(first[i].Json, Does.Not.Contain("\r"));
                Assert.That(first[i].Json.EndsWith("}\n"), Is.True);
                JObject schema = JObject.Parse(first[i].Json);
                Assert.That((string?)schema["$schema"], Is.EqualTo("https://json-schema.org/draft/2020-12/schema"));
                Assert.That((string?)schema["title"], Is.EqualTo(first[i].Root.Name));
            }
        }

        [Test]
        public void SchemasCarryTheContractFacts()
        {
            JObject changeSet = StudioSchemaEmitter.BuildSchema("change-set", typeof(ChangeSet));
            Assert.That((string?)changeSet["properties"]!["schema"]!["const"], Is.EqualTo(ChangeSet.SchemaId));
            Assert.That((string?)changeSet["properties"]!["id"]!["pattern"], Is.EqualTo(StudioPatterns.ChangeSetId));
            Assert.That(changeSet["required"]!.ToObject<string[]>(), Is.EqualTo(new[] { "id", "intent", "operations", "schema" }));
            Assert.That(changeSet["$defs"]!["EdgeKind"], Is.Null, "only reachable definitions are emitted");
            Assert.That(changeSet["$defs"]!["IntentOrigin"]!["enum"]!.ToObject<string[]>(), Is.EqualTo(new[] { "agent", "manual", "voice", "replay" }));
            Assert.That(changeSet["$defs"]!["Operation"]!["properties"]!["args"]!["type"]!.ToString(), Is.EqualTo("object"));

            JObject diagnostic = StudioSchemaEmitter.BuildSchema("diagnostic", typeof(Diagnostic));
            Assert.That(diagnostic["properties"]!["where"]!["oneOf"], Is.Not.Null);

            JObject index = StudioSchemaEmitter.BuildSchema("semantic-index", typeof(SemanticIndex));
            Assert.That(index["$defs"]!["EdgeKind"]!["enum"]!.ToObject<string[]>(),
                Is.EqualTo(new[] { "references", "contains", "spawns", "bindsUi", "triggers" }));
            Assert.That((int?)index["$defs"]!["LocationRef"]!["properties"]!["position"]!["minItems"], Is.EqualTo(3));
            Assert.That(index["$defs"]!["IndexNode"]!["properties"]!["fields"]!["additionalProperties"]!["$ref"]!.ToString(),
                Is.EqualTo("#/$defs/IndexField"));
        }

        [Test]
        public void ConformanceCheckerRejectsBadInstances()
        {
            MiniSchemaValidator schema = MiniSchemaValidator.For(typeof(ChangeSet));
            JObject good = Samples.Object("change-set.json");
            Assert.That(schema.Validate(good), Is.Empty);

            JObject missing = (JObject)good.DeepClone();
            missing.Remove("intent");
            Assert.That(schema.Validate(missing), Has.Some.Contains("missing required 'intent'"));

            JObject extra = (JObject)good.DeepClone();
            extra["colour"] = "red";
            Assert.That(schema.Validate(extra), Has.Some.Contains("unexpected property 'colour'"));

            JObject badEnum = (JObject)good.DeepClone();
            badEnum["state"] = "Done";
            Assert.That(schema.Validate(badEnum), Has.Some.Contains("not in enum"));

            JObject badPattern = (JObject)good.DeepClone();
            badPattern["id"] = "cs_01J";
            Assert.That(schema.Validate(badPattern), Has.Some.Contains("does not match"));

            JObject badConst = (JObject)good.DeepClone();
            badConst["schema"] = "gamecore.studio.changeset/2";
            Assert.That(schema.Validate(badConst), Has.Some.Contains("const"));

            JObject badType = (JObject)good.DeepClone();
            badType["artifacts"]![0]!["bytes"] = "48213";
            Assert.That(schema.Validate(badType), Has.Some.Contains("expected integer"));

            JObject badArity = (JObject)good.DeepClone();
            badArity["selection"]!["frame"]!["viewport"] = new JArray(1920);
            Assert.That(schema.Validate(badArity), Has.Some.Contains("fewer than 2 items"));

            MiniSchemaValidator diagnostic = MiniSchemaValidator.For(typeof(Diagnostic));
            Assert.That(diagnostic.Validate(JObject.Parse("{\"code\":\"X\",\"message\":\"m\",\"where\":3}")), Has.Some.Contains("oneOf"));
        }
    }
}
