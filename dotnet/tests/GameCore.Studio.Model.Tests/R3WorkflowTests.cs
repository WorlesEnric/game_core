#nullable enable
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Model.Tests
{
    public sealed class R3WorkflowTests
    {
        private static ChangeSet Retained(string run) => StudioJson.Deserialize<ChangeSet>(File.ReadAllText(Path.Combine(
            RepositoryRoot.Find(), "artifacts/studio/workflows/P3.2/runs", run, "robe/candidate.json")));

        private static ToolCatalog Catalog(params AuthorScope[] scopes) => new ToolCatalog(
            new[] { new ObjectTypeEntry("entity.instance", RuntimeApply.Rebuild, Array.Empty<FieldSpec>(), scopes: scopes) },
            new[] { new ToolEntry("entity.applyOverride", ToolTier.Configure, RuntimeApply.Rebuild,
                targetRequired: true, targetType: "entity.instance", scopes: scopes, args: new[] {
                    new ArgSpec("field", ValueTypes.String, true), new ArgSpec("value", ValueTypes.String, false) }) });

        [Test]
        public void D4_RetainedEightDigitInstanceTintFailsCandidateValidation()
        {
            ChangeSet candidate = Retained("robe-20261005T055032Z");
            var diagnostics = new ChangeSetValidator(Catalog(AuthorScope.Instance), options: new ChangeSetValidationOptions
                { Mode = ValidationMode.Candidate }).Validate(candidate);
            Assert.That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidArgs && (string?)d.Data?["contract"] == "GP-ENT-004" && d.Message.Contains("#rrggbb") && d.Where!.OpId == "op1"), Is.True);
        }


        [TestCase("#228B22", true)]
        [TestCase("", true)]
        [TestCase("#228B22FF", false)]
        [TestCase("#ZZZZZZ", false)]
        [TestCase("green", false)]
        public void D4_InstanceTintContractDoesNotChangeGeneralColorSupport(string tint, bool valid)
        {
            JObject json = (JObject)StudioJson.ToToken(Retained("robe-20261005T055032Z"));
            json["operations"]![0]!["args"]!["value"] = tint;
            var diagnostics = new ChangeSetValidator(Catalog(AuthorScope.Instance)).Validate(StudioJson.Deserialize<ChangeSet>(json.ToString()));
            Assert.That(diagnostics.Count == 0, Is.EqualTo(valid));
            var colors = new ToolCatalog(Array.Empty<ObjectTypeEntry>(), new[] { new ToolEntry("color.test", ToolTier.Configure,
                RuntimeApply.Live, false, new[] { new ArgSpec("color", ValueTypes.Color, true) }) });
            var change = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("color", IntentOrigin.Manual),
                new[] { new Operation("op1", "color.test", args: new JObject { ["color"] = "#228B22FF" }) });
            Assert.That(new ChangeSetValidator(colors).Validate(change), Is.Empty);
        }

        [Test]
        public void D5_InferenceIsRecordedAndDoesNotMutateTheCandidate()
        {
            ChangeSet original = Retained("robe2-20261005T070105Z");
            var validator = new ChangeSetValidator(Catalog(AuthorScope.Instance));
            ChangeSet normalized = validator.NormalizeScopes(original);
            Assert.That(normalized.Operations[0].Target!.Scope, Is.EqualTo(AuthorScope.Instance));
            Assert.That(original.Operations[0].Target!.Scope, Is.Null);
            Assert.That((bool)validator.Inferences.Single().Data!["inferred"]!, Is.True);
            Assert.That((string?)validator.Inferences.Single().Data!["scope"], Is.EqualTo("Instance"));
            Assert.That(validator.Validate(original), Is.Empty);
            Assert.That(validator.Inferences.Count, Is.EqualTo(1));
        }

        [Test]
        public void D5_IntersectToolWithActualIndexedTypeAndRefuseEmptyIntersection()
        {
            ChangeSet original = Retained("robe2-20261005T070105Z");
            ToolEntry tool = new ToolEntry("entity.applyOverride", ToolTier.Configure, RuntimeApply.Rebuild, true,
                new[] { new ArgSpec("field", ValueTypes.String, true), new ArgSpec("value", ValueTypes.String, false) },
                scopes: new[] { AuthorScope.Instance, AuthorScope.Definition });
            ToolCatalog catalog = new ToolCatalog(new[] { new ObjectTypeEntry("entity.instance", RuntimeApply.Rebuild,
                Array.Empty<FieldSpec>(), scopes: new[] { AuthorScope.Instance, AuthorScope.Prefab }) }, new[] { tool });
            SemanticIndex index = new SemanticIndex(1, "test", new[] { new IndexNode(original.Operations[0].Target!, "entity.instance") });
            var validator = new ChangeSetValidator(catalog, index);
            Assert.That(validator.NormalizeScopes(original).Operations[0].Target!.Scope, Is.EqualTo(AuthorScope.Instance));
            ToolCatalog empty = new ToolCatalog(new[] { new ObjectTypeEntry("entity.instance", RuntimeApply.Rebuild,
                Array.Empty<FieldSpec>(), scopes: new[] { AuthorScope.Scope }) }, new[] { tool });
            Assert.That(new ChangeSetValidator(empty, index).Validate(original).Any(d => d.Code == DiagnosticCodes.ScopeNotAllowed), Is.True);
        }

        [Test]
        public void D5_RetainedMissingScopeIsAcceptedOnlyForOneAllowedScope()
        {
            ChangeSet candidate = Retained("robe2-20261005T070105Z");
            Assert.That(new ChangeSetValidator(Catalog(AuthorScope.Instance)).Validate(candidate), Is.Empty);
            Assert.That(new ChangeSetValidator(Catalog(AuthorScope.Instance, AuthorScope.Definition)).Validate(candidate)
                .Any(d => d.Code == DiagnosticCodes.ScopeNotAllowed), Is.True);
        }
    }
}
