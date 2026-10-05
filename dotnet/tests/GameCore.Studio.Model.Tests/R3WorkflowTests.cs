#nullable enable
using System;
using System.IO;
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
            Assert.That(diagnostics.Any(d => d.Code == "GP-ENT-004" && d.Message.Contains("#rrggbb") && d.Where!.OpId == "op1"), Is.True);
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
