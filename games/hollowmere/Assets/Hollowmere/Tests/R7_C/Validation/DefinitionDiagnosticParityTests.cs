// W-PLUG-10: one invalid asset through the actual context inspector, validator console and
// Etos candidate-import/staging path. Only the remote companion/worker is fake.
#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Threading;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Testing;
using GameCore.Studio.Hollowmere.P2_2;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using GameCore.Studio.Views;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using AuthorScope = GameCore.Studio.Model.AuthorScope;
using EtosRequestBuilder = GameCore.Studio.Etos.AgentRequestBuilder;

namespace Hollowmere.R7_C.Validation
{
    public sealed class DefinitionDiagnosticParityTests
    {
        private GatewayHarness? _gateway;
        private StudioUiContext? _context;
        private string? _assetPath;

        [TearDown]
        public void TearDown()
        {
            _context?.Dispose();
            _context = null;
            _gateway?.Dispose();
            _gateway = null;
            if (_assetPath != null) AssetDatabase.DeleteAsset(_assetPath);
            _assetPath = null;
        }

        [UnityTest]
        [Category("R7_C")]
        public IEnumerator WPlug10_SameInvalidDefinition_HasCodeMessageAndLocationParityAcrossAllFronts()
        {
            // The identity is valid; a missing required view prefab is the sole definition error.
            var definition = ScriptableObject.CreateInstance<EntityDefinition>();
            definition.name = "R7C_MissingPrefab";
            definition.EnsureAuthoringId();
            definition.Configure(null, GameplayUnits.ScaleOne, true, true);
            _assetPath = "Assets/Hollowmere/Tests/R7_C/Validation/Temporary_" + Guid.NewGuid().ToString("N") + ".asset";
            AssetDatabase.CreateAsset(definition, _assetPath);
            AssetDatabase.SaveAssets();

            _gateway = GatewayHarness.WithFake();
            GatewayHarness gateway = _gateway;
            gateway.Runtime.Index.Rebuild();
            AuthoringRef target = gateway.Runtime.Resolver.BuildRef(definition, AuthorScope.Definition, true)!;
            Assert.That(target, Is.Not.Null, "the same real asset must be resolvable by every front");
            Assert.That(gateway.Runtime.Index.FindNode(target), Is.Not.Null);

            // Front 1: build the real Studio context panel, including its generated inspector and
            // live diagnostic section. Read structured findings from that panel's production dispatcher;
            // do not manufacture a diagnostic from the expected text or the selected target.
            _context = new StudioUiContext(gateway.Runtime, () => gateway.Gateway,
                new SelectionModel(gateway.Runtime), new TaskLedger(new MemoryTaskRowStore()), false);
            _context.Selection.Set(new[] { target });
            var panel = new ContextPanelView(_context);
            string[] renderedInspector = panel.Query<Label>(className: "gcs-diagnostic").ToList()
                .Select(label => label.text).ToArray();
            Diagnostic[] inspector = new ValidatorDiagnostics().For(definition, gateway.Runtime)
                .Where(diagnostic => diagnostic.Code == GameplayDiagnosticCodes.DefinitionMissingPrefab).ToArray();

            // Front 2: invoke the real validator console over the runtime's real semantic index.
            // Filtering selects this asset, not unrelated findings from the rest of Hollowmere.
            Diagnostic[] validator = ValidatorConsole.Run(gateway.Runtime, IndexGraph.Build(gateway.Runtime.Index.Snapshot()))
                .Where(row => row.Code == GameplayDiagnosticCodes.DefinitionMissingPrefab
                    && row.Where?.AuthoringId == definition.AuthoringId)
                .Select(row => row.Diagnostic).ToArray();

            // Front 3: remote transport returns an agent candidate that edits the same invalid asset.
            // The legal scalar edit leaves the missing-prefab defect in place: candidate acceptance
            // must not silently bypass definition validation. No local validator is injected here.
            gateway.Fake!.Worker = body =>
            {
                var operation = new Operation("op1", BuiltInToolIdsExt.Set, target,
                    new JObject { ["field"] = "defaultScaleMilli", ["value"] = GameplayUnits.ScaleOne + 1 });
                var candidate = new ChangeSet((string)body["changeSetId"]!, ChangeSet.SchemaId,
                    new Intent("Adjust the scale of the selected entity definition.", IntentOrigin.Agent),
                    new[] { operation });
                return new FakeCandidate((JObject)StudioJson.ToToken(candidate));
            };
            CandidateImport? imported = null;
            gateway.Gateway.CandidateStaged += result => imported = result;
            gateway.Gateway.Start();
            var request = EtosRequestBuilder.ForObjects(gateway.Runtime, new UnityEngine.Object[] { definition },
                "Adjust the selected definition scale by one milliunit.");
            var submitted = gateway.Gateway.SubmitAsync(request, CancellationToken.None);
            yield return gateway.Await(submitted, 30, "the parity candidate submission");
            string requestId = submitted.GetAwaiter().GetResult();
            yield return gateway.Until(() => imported != null, 30, "the actual candidate import and validation");
            CandidateImport result = imported!;
            Diagnostic[] agent = result.Diagnostics
                .Where(diagnostic => diagnostic.Code == GameplayDiagnosticCodes.DefinitionMissingPrefab).ToArray();

            // Retain all three observations before asserting parity, so a failure in one front does
            // not prevent the gateway from running and conceal the other missing product seam.
            TestContext.WriteLine("W-PLUG-10 asset=" + _assetPath + " subject=" + definition.AuthoringId);
            TestContext.WriteLine("inspector rendered=" + new JArray(renderedInspector));
            TestContext.WriteLine("inspector diagnostics=" + StudioJson.ToToken(inspector));
            TestContext.WriteLine("validator diagnostics=" + StudioJson.ToToken(validator));
            TestContext.WriteLine("agent request=" + requestId + " accepted=" + result.Ok
                + " diagnostics=" + StudioJson.ToToken(result.Diagnostics));

            {
                Assert.That(panel.Q(className: AuthoringInspectorBuilder.RootClass), Is.Not.Null,
                    "this is the actual generated inspector, not a test-only validator label");
                Assert.That(result.RequestId, Is.EqualTo(requestId));
                Assert.That(result.Staged, Is.Not.Null, "the fake companion must reach real candidate staging");
                Assert.That(validator, Has.Length.EqualTo(1), "the validator console must identify this missing prefab");
                Assert.That(inspector, Has.Length.EqualTo(1), "the inspector must report the same invalid definition");
                Assert.That(agent, Has.Length.EqualTo(1), "agent candidate validation must report the same invalid definition");
                Assert.That(result.Ok, Is.False, "a candidate retaining this invalid definition must be refused");
                Assert.That(definition.DefaultScaleMilli, Is.EqualTo(GameplayUnits.ScaleOne),
                    "staging is not permission to apply the candidate");
                Assert.That(definition.Prefab, Is.Null, "all fronts observed the same missing-prefab defect");
                if (validator.Length == 1)
                {
                    Diagnostic expected = validator[0];
                    Assert.That(expected.Where?.Ref?.AuthoringId, Is.EqualTo(definition.AuthoringId));
                    Assert.That(renderedInspector, Does.Contain(expected.Code + ": " + expected.Message),
                        "the actual inspector surface must display the validator's code and message");
                    foreach (Diagnostic observed in inspector.Concat(agent))
                    {
                        Assert.That(observed.Code, Is.EqualTo(expected.Code));
                        Assert.That(observed.Message, Is.EqualTo(expected.Message));
                        Assert.That(observed.Where?.Ref?.IdentityKey, Is.EqualTo(expected.Where?.Ref?.IdentityKey),
                            "preserve the diagnostic location itself; do not infer it from selection or operation target");
                        Assert.That(observed.Data?["subject"]?.Value<string>(), Is.EqualTo(definition.AuthoringId),
                            "the gameplay validator's canonical subject must survive diagnostic conversion");
                    }
                }
            }
        }
    }
}
