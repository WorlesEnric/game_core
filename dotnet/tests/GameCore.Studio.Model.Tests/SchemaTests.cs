// Schema emission: the committed docs/studio/schemas files are byte-identical to a fresh emission, emission is
// deterministic with sorted keys, and the conformance checker used by the round-trip tests rejects bad instances.
#nullable enable
using System;
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

            Assert.That((long?)changeSet["$defs"]!["ArtifactRef"]!["properties"]!["bytes"]!["minimum"], Is.EqualTo(0));

            JObject diagnostic = StudioSchemaEmitter.BuildSchema("diagnostic", typeof(Diagnostic));
            Assert.That(diagnostic["properties"]!["where"]!["oneOf"], Is.Not.Null);
            Assert.That((string?)diagnostic["properties"]!["data"]!["type"], Is.EqualTo("object"));
            Assert.That(diagnostic["required"]!.ToObject<string[]>(), Is.EqualTo(new[] { "code", "message" }));

            JObject catalog = StudioSchemaEmitter.BuildSchema("tool-catalog", typeof(ToolCatalog));
            Assert.That((string?)catalog["properties"]!["revision"]!["pattern"], Is.EqualTo(StudioPatterns.Sha256Hex));

            JObject selection = StudioSchemaEmitter.BuildSchema("selection-snapshot", typeof(SelectionSnapshot));
            Assert.That((string?)selection["properties"]!["worldSession"]!["type"], Is.EqualTo("string"), "optional members stay plain types (never null)");

            JObject index = StudioSchemaEmitter.BuildSchema("semantic-index", typeof(SemanticIndex));
            Assert.That(index["$defs"]!["EdgeKind"]!["enum"]!.ToObject<string[]>(),
                Is.EqualTo(new[] { "references", "contains", "spawns", "bindsUi", "triggers" }));
            Assert.That((int?)index["$defs"]!["LocationRef"]!["properties"]!["position"]!["minItems"], Is.EqualTo(3));
            Assert.That(index["$defs"]!["IndexNode"]!["properties"]!["fields"]!["additionalProperties"]!["$ref"]!.ToString(),
                Is.EqualTo("#/$defs/IndexField"));
        }

        [Test]
        public void DefsNameCollisionIsRefused()
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => StudioSchemaEmitter.BuildSchema("collision", typeof(Collision.Root)))!;
            Assert.That(error.Message, Does.Contain("$defs name collision: 'Item'"));
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

            JObject negative = (JObject)good.DeepClone();
            negative["artifacts"]![0]!["bytes"] = -1;
            Assert.That(schema.Validate(negative), Has.Some.Contains("below the minimum"));

            JObject badArity = (JObject)good.DeepClone();
            badArity["selection"]!["frame"]!["viewport"] = new JArray(1920);
            Assert.That(schema.Validate(badArity), Has.Some.Contains("fewer than 2 items"));

            MiniSchemaValidator diagnostic = MiniSchemaValidator.For(typeof(Diagnostic));
            Assert.That(diagnostic.Validate(JObject.Parse("{\"code\":\"X\",\"message\":\"m\",\"where\":3}")), Has.Some.Contains("oneOf"));
        }
    }
}

namespace GameCore.Studio.Model.Tests.Collision
{
    [Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptIn)]
    public sealed class Root
    {
        [Newtonsoft.Json.JsonProperty("a")]
        public A.Item? First { get; set; }

        [Newtonsoft.Json.JsonProperty("b")]
        public B.Item? Second { get; set; }
    }
}

namespace GameCore.Studio.Model.Tests.Collision.A
{
    [Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptIn)]
    public sealed class Item
    {
        [Newtonsoft.Json.JsonProperty("x")]
        public int X { get; set; }
    }
}

namespace GameCore.Studio.Model.Tests.Collision.B
{
    [Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptIn)]
    public sealed class Item
    {
        [Newtonsoft.Json.JsonProperty("y")]
        public string? Y { get; set; }
    }
}
