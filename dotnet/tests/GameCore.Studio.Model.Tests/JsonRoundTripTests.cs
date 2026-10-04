// Round trips of the 03 sample documents: each parses into its model type, re-serializes equal modulo key order, and
// conforms to the emitted schema (as does the re-serialized form). Plus the strict-read rules.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Model.Tests
{
    public sealed class JsonRoundTripTests
    {
        private static IEnumerable<TestCaseData> SampleCases()
        {
            yield return new TestCaseData("authoring-ref.json", typeof(AuthoringRef));
            yield return new TestCaseData("authoring-ref-location.json", typeof(AuthoringRef));
            yield return new TestCaseData("selection-snapshot.json", typeof(SelectionSnapshot));
            yield return new TestCaseData("selection-snapshot-play.json", typeof(SelectionSnapshot));
            yield return new TestCaseData("semantic-index.json", typeof(SemanticIndex));
            yield return new TestCaseData("tool-catalog.json", typeof(ToolCatalog));
            yield return new TestCaseData("change-set.json", typeof(ChangeSet));
            yield return new TestCaseData("diagnostic.json", typeof(Diagnostic));
            yield return new TestCaseData("diagnostic-op.json", typeof(Diagnostic));
        }

        [TestCaseSource(nameof(SampleCases))]
        public void SampleRoundTripsAndMatchesSchema(string sample, Type type)
        {
            string text = Samples.Read(sample);
            JToken original = StudioJson.ParseToken(text);
            object model = JsonConvert.DeserializeObject(text, type, StudioJson.CreateSettings())!;
            Assert.That(model, Is.InstanceOf(type));

            string written = StudioJson.Serialize(model);
            JToken rewritten = StudioJson.ParseToken(written);
            Assert.That(JsonEquivalence.Difference(original, rewritten), Is.Null, "round trip of " + sample);

            MiniSchemaValidator schema = MiniSchemaValidator.For(type);
            Assert.That(schema.Validate(original), Is.Empty, "sample conforms to its schema");
            Assert.That(schema.Validate(rewritten), Is.Empty, "re-serialized form conforms to its schema");

            object again = JsonConvert.DeserializeObject(written, type, StudioJson.CreateSettings())!;
            Assert.That(StudioJson.Serialize(again), Is.EqualTo(written), "second round trip is byte-identical");
        }

        [Test]
        public void SerializedTextUsesLineFeedsAndCamelCaseAndOmitsNulls()
        {
            AuthoringRef target = new AuthoringRef(AuthoringKind.Asset, assetGuid: "9a8b7c6d5e4f30211203f4e5d6c7b8a9", path: "Assets/A.png");
            string text = StudioJson.Serialize(target);
            Assert.That(text, Does.Not.Contain("\r"));
            Assert.That(text, Does.Contain("\"assetGuid\""));
            Assert.That(text, Does.Not.Contain("authoringId"));
            Assert.That(text, Does.Not.Contain("null"));
            Assert.That(StudioJson.Serialize(target, indented: false),
                Is.EqualTo("{\"kind\":\"Asset\",\"assetGuid\":\"9a8b7c6d5e4f30211203f4e5d6c7b8a9\",\"path\":\"Assets/A.png\"}"));
        }

        [Test]
        public void DefaultJsonConvertProducesTheSameKeys()
        {
            ChangeSet changeSet = Samples.ChangeSet();
            JToken viaDefault = StudioJson.ParseToken(JsonConvert.SerializeObject(changeSet, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
            Assert.That(JsonEquivalence.Difference(StudioJson.ToToken(changeSet), viaDefault), Is.Null);
        }

        [Test]
        public void EnumSpellingsFollowTheContract()
        {
            JObject index = (JObject)StudioJson.ToToken(Samples.Index());
            Assert.That((string?)index["edges"]![2]!["kind"], Is.EqualTo("bindsUi"));
            JObject changeSet = (JObject)StudioJson.ToToken(Samples.ChangeSet());
            Assert.That((string?)changeSet["intent"]!["origin"], Is.EqualTo("agent"));
            Assert.That((string?)changeSet["operations"]![0]!["preconditions"], Is.EqualTo("stamp"));
            Assert.That((string?)changeSet["validation"]![0]!["status"], Is.EqualTo("pending"));
            Assert.That((string?)changeSet["policy"], Is.EqualTo("AllOrNothing"));
            Assert.That((string?)changeSet["state"], Is.EqualTo("Applied"));
        }

        [Test]
        public void MissingRequiredMemberIsRefused()
        {
            Assert.Throws<JsonSerializationException>(() => StudioJson.Deserialize<AuthoringRef>("{\"authoringId\":\"x\"}"));
            Assert.Throws<JsonSerializationException>(() => StudioJson.Deserialize<ChangeSet>(
                "{\"id\":\"cs_01K6Q0ACC07E5S3HTP4YZEASPW\",\"schema\":\"gamecore.studio.changeset/1\",\"operations\":[]}"));
        }

        [Test]
        public void UnknownMemberIsRefused()
        {
            Assert.Throws<JsonSerializationException>(() => StudioJson.Deserialize<AuthoringRef>("{\"kind\":\"Entity\",\"authoringId\":\"x\",\"colour\":1}"));
        }

        [Test]
        public void IntegerEnumValueIsRefused()
        {
            Assert.Throws<JsonSerializationException>(() => StudioJson.Deserialize<AuthoringRef>("{\"kind\":0,\"authoringId\":\"x\"}"));
            Assert.Throws<JsonSerializationException>(() => StudioJson.Deserialize<AuthoringRef>("{\"kind\":\"entity\",\"authoringId\":\"x\"}"));
            Assert.Throws<JsonSerializationException>(() => StudioJson.Deserialize<AuthoringRef>("{\"kind\":\"Entity\",\"authoringId\":\"x\",\"scope\":\"Instance, Prefab\"}"));
            Assert.Throws<JsonSerializationException>(() => StudioJson.Deserialize<IndexEdge>(
                "{\"from\":{\"kind\":\"Entity\",\"path\":\"a\"},\"to\":{\"kind\":\"Entity\",\"path\":\"b\"},\"kind\":\"BindsUi\"}"));
        }

        [Test]
        public void TrailingContentIsRefused()
        {
            Assert.Throws<JsonReaderException>(() => StudioJson.Deserialize<AuthoringRef>("{\"kind\":\"Entity\",\"authoringId\":\"x\"} {}"));
        }

        [Test]
        public void FreeFormPayloadKeepsDateLikeStringsVerbatim()
        {
            string json = "{\"opId\":\"op1\",\"tool\":\"t\",\"args\":{\"when\":\"2026-10-04T08:20:00.000Z\",\"n\":1.50}}";
            Operation operation = StudioJson.Deserialize<Operation>(json);
            Assert.That(operation.Args!["when"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That((string?)operation.Args!["when"], Is.EqualTo("2026-10-04T08:20:00.000Z"));
            Assert.That(StudioJson.Serialize(operation, indented: false), Does.Contain("\"when\":\"2026-10-04T08:20:00.000Z\""));
        }

        [Test]
        public void ModelObjectsAreDefensiveCopies()
        {
            JObject args = new JObject { ["count"] = 1 };
            List<string> dependsOn = new List<string> { "op0" };
            Operation operation = new Operation("op1", "t", args: args, dependsOn: dependsOn);
            args["count"] = 2;
            dependsOn.Add("opX");
            Assert.That((int)operation.Args!["count"]!, Is.EqualTo(1));
            Assert.That(operation.DependsOn, Is.EqualTo(new[] { "op0" }));
        }

        [Test]
        public void WithCopiesLeaveTheOriginalUntouched()
        {
            ChangeSet original = Samples.ChangeSet();
            ChangeSet rejected = original.WithState(ChangeSetState.Rejected).WithOutcomes(null);
            Assert.That(original.State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(original.Outcomes, Has.Count.EqualTo(2));
            Assert.That(rejected.State, Is.EqualTo(ChangeSetState.Rejected));
            Assert.That(rejected.Outcomes, Is.Null);
            Assert.That(rejected.Id, Is.EqualTo(original.Id));
            Assert.That(new ChangeSet("cs_x", ChangeSet.SchemaId, original.Intent, original.Operations).EffectivePolicy, Is.EqualTo(ApplyPolicy.AllOrNothing));
            Assert.That(new ChangeSet("cs_x", ChangeSet.SchemaId, original.Intent, original.Operations).EffectiveState, Is.EqualTo(ChangeSetState.Requested));
        }

        [Test]
        public void DiagnosticWhereIsTheContractUnion()
        {
            Diagnostic atRef = StudioJson.Deserialize<Diagnostic>(Samples.Read("diagnostic.json"));
            Assert.That(atRef.Where!.Ref, Is.Not.Null);
            Assert.That(atRef.Where.OpId, Is.Null);
            Assert.That(atRef.Where.Ref!.AuthoringId, Is.EqualTo("7f1c2a9e-4b3d-4e8f-9a1b-2c3d4e5f6a7b"));

            Diagnostic atOp = StudioJson.Deserialize<Diagnostic>(Samples.Read("diagnostic-op.json"));
            Assert.That(atOp.Where!.OpId, Is.EqualTo("op1"));
            Assert.That(atOp.Where.Ref, Is.Null);

            Diagnostic bare = new Diagnostic(DiagnosticCodes.Blocked, "no provider");
            Assert.That(StudioJson.Serialize(bare, indented: false), Is.EqualTo("{\"code\":\"Blocked\",\"message\":\"no provider\"}"));
            Assert.Throws<JsonSerializationException>(() => StudioJson.Deserialize<Diagnostic>("{\"code\":\"X\",\"message\":\"m\",\"where\":3}"));
        }

        [Test]
        public void DiagnosticCodeRegistryIsComplete()
        {
            string[] expected =
            {
                "StaleTarget", "Conflict", "UnknownTool", "InvalidArgs", "MissingPrerequisite", "ScopeNotAllowed",
                "ValidationFailed", "Refused", "CandidateInvalid", "StaleContext", "StageFailed", "LedgerConflict",
                "NotConfigured", "OutcomeUnknown", "Blocked",
            };
            Assert.That(DiagnosticCodes.All, Is.EqualTo(expected));
            Assert.That(DiagnosticCodes.All, Is.Unique);
            foreach (string code in expected)
            {
                Assert.That(DiagnosticCodes.IsRegistered(code), Is.True, code);
            }

            Assert.That(DiagnosticCodes.IsRegistered("staletarget"), Is.False);
            Assert.That(DiagnosticCodes.IsRegistered(null), Is.False);
            Assert.That(DiagnosticCodes.ToJson().Count, Is.EqualTo(expected.Length));
        }

        [Test]
        public void AuthoringRefShapeRules()
        {
            Assert.That(StudioJson.Deserialize<AuthoringRef>(Samples.Read("authoring-ref.json")).ShapeProblems(), Is.Empty);
            Assert.That(StudioJson.Deserialize<AuthoringRef>(Samples.Read("authoring-ref-location.json")).ShapeProblems(), Is.Empty);
            Assert.That(new AuthoringRef(AuthoringKind.Location).ShapeProblems(), Has.Some.Contains("needs 'location'"));
            Assert.That(new AuthoringRef(AuthoringKind.Asset, authoringId: "a", assetGuid: "g").ShapeProblems(), Has.Some.Contains("Asset ref has no 'authoringId'"));
            Assert.That(new AuthoringRef(AuthoringKind.Entity).ShapeProblems(), Has.Some.Contains("needs one of"));
            Assert.That(new AuthoringRef(AuthoringKind.Entity, authoringId: "a", stamp: "sha256:XYZ").ShapeProblems(), Has.Some.Contains("'stamp'"));
            Assert.That(new AuthoringRef(AuthoringKind.Entity, authoringId: "a", definition: "npc").ShapeProblems(), Has.Some.Contains("'definition'"));
            Assert.That(new AuthoringRef(AuthoringKind.Entity, authoringId: "a", scope: AuthorScope.Instance | AuthorScope.Prefab).ShapeProblems(), Has.Some.Contains("'scope'"));
            Assert.That(
                new AuthoringRef(AuthoringKind.Location, location: new LocationRef("marsh", new[] { 1.0, 2.0 })).ShapeProblems(),
                Has.Some.Contains("3 components"));
        }

        [Test]
        public void SameTargetIgnoresStampAndScopeButNotIdentity()
        {
            AuthoringRef ferryman = StudioJson.Deserialize<AuthoringRef>(Samples.Read("authoring-ref.json"));
            Assert.That(ferryman.SameTarget(ferryman.WithStamp(ContentStamp.OfUtf8("other")).WithScope(AuthorScope.Prefab)), Is.True);
            Assert.That(ferryman.SameTarget(new AuthoringRef(AuthoringKind.Entity, global: ferryman.Global)), Is.True);
            Assert.That(ferryman.SameTarget(new AuthoringRef(AuthoringKind.Entity, authoringId: "other", global: ferryman.Global)), Is.False);
            Assert.That(ferryman.SameTarget(new AuthoringRef(AuthoringKind.Definition, authoringId: ferryman.AuthoringId)), Is.False);
            Assert.That(ferryman, Is.EqualTo(StudioJson.Deserialize<AuthoringRef>(Samples.Read("authoring-ref.json"))));
            Assert.That(ferryman, Is.Not.EqualTo(ferryman.WithStamp(null)));
        }
    }
}
