// ChangeSetValidator: the 03 sample change set is valid against the sample catalog and index, and each rule fires on
// a minimal mutation of it with the documented code and location.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Model.Tests
{
    public sealed class ChangeSetValidatorTests
    {
        private static readonly string FerrymanStamp = ContentStamp.OfUtf8("ferryman");

        private static IReadOnlyList<Diagnostic> Validate(
            Action<JObject>? changeSet = null,
            Action<JObject>? catalog = null,
            Action<JObject>? index = null,
            ChangeSetValidationOptions? options = null,
            bool withIndex = true)
        {
            ChangeSetValidator validator = new ChangeSetValidator(
                Samples.Catalog(catalog),
                withIndex ? Samples.Index(index) : null,
                options);
            return validator.Validate(Samples.ChangeSet(changeSet));
        }

        private static Diagnostic Single(IReadOnlyList<Diagnostic> diagnostics, string code)
        {
            Assert.That(diagnostics.Count, Is.EqualTo(1), "exactly one diagnostic expected: " + string.Join(" | ", diagnostics));
            Assert.That(diagnostics[0].Code, Is.EqualTo(code), diagnostics[0].ToString());
            return diagnostics[0];
        }

        private static JObject Op(JObject changeSet, string opId) => Samples.Operation(changeSet, opId);

        private static void AddOperation(JObject changeSet, string json) => ((JArray)changeSet["operations"]!).Add(JObject.Parse(json));

        private static string FerrymanRef(JObject changeSet) => Op(changeSet, "op1")["target"]!.ToString();

        [Test]
        public void SampleChangeSetIsValid()
        {
            Assert.That(Validate().Select(d => d.ToString()), Is.Empty);
        }

        [Test]
        public void SampleChangeSetIsValidWithoutAnIndex()
        {
            Assert.That(Validate(withIndex: false).Select(d => d.ToString()), Is.Empty);
        }

        [Test]
        public void EnvelopeSchemaAndId()
        {
            Single(Validate(cs => cs["schema"] = "gamecore.studio.changeset/2"), DiagnosticCodes.CandidateInvalid);
            Diagnostic id = Single(Validate(cs => cs["id"] = "cs_01J"), DiagnosticCodes.CandidateInvalid);
            Assert.That(id.Message, Does.Contain("ULID"));
            Single(Validate(cs => cs["links"]!["parent"] = "t_1"), DiagnosticCodes.CandidateInvalid);
        }

        [Test]
        public void EmptyOperations()
        {
            IReadOnlyList<Diagnostic> diagnostics = Validate(cs => cs["operations"] = new JArray());
            Assert.That(diagnostics.Select(d => d.Message), Has.Some.Contains("no operations"));
            Assert.That(diagnostics.All(d => d.Code == DiagnosticCodes.CandidateInvalid), Is.True);
        }

        [Test]
        public void UnknownTool()
        {
            Diagnostic diagnostic = Single(Validate(cs => Op(cs, "op1")["tool"] = "inventory.grantEverything"), DiagnosticCodes.UnknownTool);
            Assert.That(diagnostic.Where!.OpId, Is.EqualTo("op1"));
        }

        [Test]
        public void ArgumentOfTheWrongType()
        {
            Diagnostic diagnostic = Single(Validate(cs => Op(cs, "op1")["args"]!["count"] = "one"), DiagnosticCodes.InvalidArgs);
            Assert.That(diagnostic.Message, Does.Contain("expected int"));
            Assert.That(diagnostic.Where!.OpId, Is.EqualTo("op1"));
            Single(Validate(cs => Op(cs, "op1")["args"]!["count"] = 1.5), DiagnosticCodes.InvalidArgs);
        }

        [Test]
        public void ArgumentOutOfRange()
        {
            Diagnostic above = Single(Validate(cs => Op(cs, "op1")["args"]!["count"] = 120), DiagnosticCodes.InvalidArgs);
            Assert.That(above.Message, Does.Contain("above the maximum 99"));
            Diagnostic below = Single(Validate(cs => Op(cs, "op1")["args"]!["count"] = 0), DiagnosticCodes.InvalidArgs);
            Assert.That(below.Message, Does.Contain("below the minimum 1"));
        }

        [Test]
        public void MissingRequiredArgument()
        {
            Diagnostic diagnostic = Single(Validate(cs => ((JObject)Op(cs, "op1")["args"]!).Remove("count")), DiagnosticCodes.InvalidArgs);
            Assert.That(diagnostic.Message, Does.Contain("requires argument 'count'"));
            Single(Validate(cs => Op(cs, "op1")["args"]!["count"] = JValue.CreateNull()), DiagnosticCodes.InvalidArgs);
        }

        [Test]
        public void UnknownArgument()
        {
            Diagnostic diagnostic = Single(Validate(cs => Op(cs, "op1")["args"]!["colour"] = "red"), DiagnosticCodes.InvalidArgs);
            Assert.That(diagnostic.Message, Does.Contain("no argument 'colour'"));
        }

        [Test]
        public void VectorArrayArity()
        {
            Diagnostic diagnostic = Single(
                Validate(cs => AddOperation(cs,
                    "{\"opId\":\"op3\",\"tool\":\"npc.setPatrol\",\"target\":" + FerrymanRef(cs) + ",\"args\":{\"points\":[[1,2],[1,2,3]]}}")),
                DiagnosticCodes.InvalidArgs);
            Assert.That(diagnostic.Message, Does.Contain("[0] expected [x, y, z]"));
            Assert.That(diagnostic.Where!.OpId, Is.EqualTo("op3"));
        }

        [Test]
        public void ReferenceCategoryMismatch()
        {
            Diagnostic diagnostic = Single(Validate(cs => Op(cs, "op1")["args"]!["item"] = "dialogue.ferryman-talk@5"), DiagnosticCodes.InvalidArgs);
            Assert.That(diagnostic.Message, Does.Contain("category 'item.definition'"));
            Assert.That(Validate(cs => Op(cs, "op1")["args"]!["item"] = "item.unknown@1").Select(d => d.ToString()), Is.Empty,
                "a reference outside the index slice cannot be judged");
        }

        [Test]
        public void MalformedArtifactArgument()
        {
            IReadOnlyList<Diagnostic> diagnostics = Validate(cs => Op(cs, "op2")["args"]!["voice"] = "sha256:" + ContentStamp.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("wav")));
            Assert.That(diagnostics.Select(d => d.Code), Is.EquivalentTo(new[] { DiagnosticCodes.InvalidArgs, DiagnosticCodes.CandidateInvalid }));
        }

        [Test]
        public void MissingTarget()
        {
            Diagnostic diagnostic = Single(Validate(cs => Op(cs, "op1").Remove("target")), DiagnosticCodes.InvalidArgs);
            Assert.That(diagnostic.Message, Does.Contain("needs a target of type 'npc.definition'"));
        }

        [Test]
        public void TargetKindNotAccepted()
        {
            Diagnostic diagnostic = Single(
                Validate(catalog: c => Samples.Tool(c, "inventory.grantStarting")["targetKinds"] = new JArray("Definition")),
                DiagnosticCodes.InvalidArgs);
            Assert.That(diagnostic.Message, Does.Contain("does not accept target kind Entity"));
        }

        [Test]
        public void TargetTypeMismatch()
        {
            Diagnostic diagnostic = Single(
                Validate(cs => Op(cs, "op1")["target"] = Op(cs, "op2")["target"]!.DeepClone()),
                DiagnosticCodes.InvalidArgs);
            Assert.That(diagnostic.Message, Does.Contain("needs a target of type 'npc.definition', but the target is 'dialogue.graph'"));
        }

        [Test]
        public void MalformedTargetShape()
        {
            IReadOnlyList<Diagnostic> diagnostics = Validate(cs => Op(cs, "op1")["target"] = JObject.Parse(
                "{\"kind\":\"Location\",\"authoringId\":\"x\",\"location\":{\"region\":\"marsh\",\"position\":[1,2,3]}}"));
            Assert.That(diagnostics.Select(d => d.Code),
                Is.EquivalentTo(new[] { DiagnosticCodes.CandidateInvalid, DiagnosticCodes.InvalidArgs, DiagnosticCodes.ScopeNotAllowed }));
            Assert.That(diagnostics.First(d => d.Code == DiagnosticCodes.CandidateInvalid).Where!.Ref, Is.Not.Null);
        }

        [Test]
        public void ScopeNotAllowedByTool()
        {
            Diagnostic diagnostic = Single(Validate(cs => Op(cs, "op2")["target"]!["scope"] = "Instance"), DiagnosticCodes.ScopeNotAllowed);
            Assert.That(diagnostic.Message, Does.Contain("allowed: Definition"));
            Assert.That(diagnostic.Where!.OpId, Is.EqualTo("op2"));
        }

        [Test]
        public void ScopeNotAllowedByObjectType()
        {
            Diagnostic diagnostic = Single(
                Validate(
                    cs => Op(cs, "op1")["target"]!["scope"] = "Prefab",
                    catalog: c => Samples.Tool(c, "inventory.grantStarting").Remove("scopes")),
                DiagnosticCodes.ScopeNotAllowed);
            Assert.That(diagnostic.Message, Does.Contain("Object type 'npc.definition'"));
        }

        [Test]
        public void MissingProjectPrerequisite()
        {
            Action<JObject> place = cs => AddOperation(cs,
                "{\"opId\":\"op3\",\"tool\":\"npc.place\",\"target\":" + FerrymanRef(cs) + ",\"args\":{\"position\":[1,0,2]}}");
            Assert.That(Validate(place).Select(d => d.ToString()), Is.Empty, "the index has a world.region");

            Diagnostic diagnostic = Single(
                Validate(place, index: i => ((JArray)i["nodes"]!).RemoveAt(3)),
                DiagnosticCodes.MissingPrerequisite);
            Assert.That(diagnostic.Message, Does.Contain("'world.region'"));
            Assert.That(diagnostic.Where!.OpId, Is.EqualTo("op3"));
        }

        [Test]
        public void MissingTargetPrerequisite()
        {
            Diagnostic diagnostic = Single(
                Validate(catalog: c => Samples.Tool(c, "dialogue.addNode")["prerequisites"]![0]!["requires"] = "dialogue.voiced"),
                DiagnosticCodes.MissingPrerequisite);
            Assert.That(diagnostic.Message, Does.Contain("target that provides 'dialogue.voiced'"));
        }

        [Test]
        public void DependsOnUnknownOperation()
        {
            Diagnostic diagnostic = Single(Validate(cs => Op(cs, "op2")["dependsOn"] = new JArray("op9")), DiagnosticCodes.CandidateInvalid);
            Assert.That(diagnostic.Message, Does.Contain("unknown operation 'op9'"));
            Assert.That(diagnostic.Where!.OpId, Is.EqualTo("op2"));
        }

        [Test]
        public void DependencyCycle()
        {
            Diagnostic cycle = Single(Validate(cs => Op(cs, "op1")["dependsOn"] = new JArray("op2")), DiagnosticCodes.CandidateInvalid);
            Assert.That(cycle.Message, Does.Contain("op1 -> op2 -> op1"));
            Assert.That(cycle.Where!.OpId, Is.EqualTo("op1"));

            Diagnostic self = Single(Validate(cs => Op(cs, "op1")["dependsOn"] = new JArray("op1")), DiagnosticCodes.CandidateInvalid);
            Assert.That(self.Message, Does.Contain("op1 -> op1"));
        }

        [Test]
        public void DuplicateOperationIds()
        {
            Diagnostic diagnostic = Single(
                Validate(cs =>
                {
                    Op(cs, "op2")["opId"] = "op1";
                    cs.Remove("outcomes");
                }),
                DiagnosticCodes.CandidateInvalid);
            Assert.That(diagnostic.Message, Does.Contain("used more than once"));
        }

        [Test]
        public void ArtifactThatNoOperationUses()
        {
            Diagnostic diagnostic = Single(Validate(cs => ((JObject)Op(cs, "op2")["args"]!).Remove("voice")), DiagnosticCodes.CandidateInvalid);
            Assert.That(diagnostic.Message, Does.Contain("ferryman_line_07.wav"));
            Assert.That(diagnostic.Message, Does.Contain("no operation uses it"));
        }

        [Test]
        public void ArtifactReferenceThatIsNotCarried()
        {
            Diagnostic diagnostic = Single(Validate(cs => cs.Remove("artifacts")), DiagnosticCodes.CandidateInvalid);
            Assert.That(diagnostic.Message, Does.Contain("does not carry"));
            Assert.That(diagnostic.Where!.OpId, Is.EqualTo("op2"));
        }

        [Test]
        public void DuplicateAndMalformedArtifacts()
        {
            Single(Validate(cs => ((JArray)cs["artifacts"]!).Add(cs["artifacts"]![0]!.DeepClone())), DiagnosticCodes.CandidateInvalid);
            IReadOnlyList<Diagnostic> malformed = Validate(cs => cs["artifacts"]![0]!["sha256"] = "ABC");
            Assert.That(malformed.Select(d => d.Code), Is.All.EqualTo(DiagnosticCodes.CandidateInvalid));
            Assert.That(malformed.Select(d => d.Message), Has.Some.Contains("invalid sha256"));
        }

        [Test]
        public void RequirementsBelowTheOperations()
        {
            IReadOnlyList<Diagnostic> diagnostics = Validate(cs => Op(cs, "op1")["applyRequirement"] = "Rebuild");
            Assert.That(diagnostics.Select(d => d.Code), Is.EqualTo(new[] { DiagnosticCodes.CandidateInvalid, DiagnosticCodes.CandidateInvalid }));
            Assert.That(diagnostics.Select(d => d.Message), Has.Some.Contains("max Live, but the operations need Rebuild"));
            Assert.That(diagnostics.Select(d => d.Message), Has.Some.Contains("'worldRebuild' is false"));

            Assert.That(
                Validate(cs =>
                {
                    Op(cs, "op1")["applyRequirement"] = "Rebuild";
                    cs["requirements"] = JObject.Parse("{\"max\":\"Rebuild\",\"worldRebuild\":true,\"compile\":false,\"build\":false}");
                }).Select(d => d.ToString()),
                Is.Empty);
        }

        [Test]
        public void RequirementsInternallyInconsistent()
        {
            Diagnostic diagnostic = Single(Validate(cs => cs["requirements"]!["compile"] = true), DiagnosticCodes.CandidateInvalid);
            Assert.That(diagnostic.Message, Does.Contain("'compile' is true but 'max' is below Compile"));
        }

        [Test]
        public void MissingRequirementsWhenOperationsNeedMoreThanLive()
        {
            Diagnostic diagnostic = Single(
                Validate(cs => cs.Remove("requirements"), catalog: c => Samples.Tool(c, "dialogue.addNode")["runtimeApply"] = "Compile"),
                DiagnosticCodes.CandidateInvalid);
            Assert.That(diagnostic.Message, Does.Contain("declares no requirements"));
        }

        [Test]
        public void ApplyRequirementWeakerThanTheTool()
        {
            IReadOnlyList<Diagnostic> diagnostics = Validate(catalog: c => Samples.Tool(c, "inventory.grantStarting")["runtimeApply"] = "Rebuild");
            Assert.That(diagnostics.Select(d => d.Message), Has.Some.Contains("declares applyRequirement Live, but tool 'inventory.grantStarting' needs Rebuild"));
            Assert.That(diagnostics.Select(d => d.Code), Is.All.EqualTo(DiagnosticCodes.CandidateInvalid));
            Assert.That(diagnostics, Has.Count.EqualTo(3), "plus max and worldRebuild under-declared");
        }

        [Test]
        public void ChangedTargetIsAConflictWithWitness()
        {
            string edited = ContentStamp.OfUtf8("ferryman-edited");
            IReadOnlyList<Diagnostic> diagnostics = Validate(index: i => i["nodes"]![0]!["ref"]!["stamp"] = edited);
            Assert.That(diagnostics.Select(d => d.Code), Is.EqualTo(new[] { DiagnosticCodes.Conflict, DiagnosticCodes.Conflict }),
                "the op target and the base version both changed");
            Diagnostic changed = diagnostics[0];
            Assert.That(changed.Message, Does.Contain("expected " + FerrymanStamp + ", actual " + edited));
            Assert.That(changed.Where!.Ref!.AuthoringId, Is.EqualTo("7f1c2a9e-4b3d-4e8f-9a1b-2c3d4e5f6a7b"));
            Assert.That((string?)changed.Data!["expected"], Is.EqualTo(FerrymanStamp));
            Assert.That((string?)changed.Data!["actual"], Is.EqualTo(edited));
            Assert.That((string?)diagnostics[1].Data!["actual"], Is.EqualTo(edited));

            Assert.That(
                Validate(index: i => i["nodes"]![0]!["ref"]!["stamp"] = edited, options: new ChangeSetValidationOptions { CheckStamps = false })
                    .Select(d => d.ToString()),
                Is.Empty);
        }

        [Test]
        public void StampPreconditionWithoutStamp()
        {
            Diagnostic diagnostic = Single(Validate(cs => ((JObject)Op(cs, "op1")["target"]!).Remove("stamp")), DiagnosticCodes.CandidateInvalid);
            Assert.That(diagnostic.Message, Does.Contain("carries no stamp"));
            Assert.That(
                Validate(cs =>
                {
                    ((JObject)Op(cs, "op1")["target"]!).Remove("stamp");
                    Op(cs, "op1")["preconditions"] = "none";
                }).Select(d => d.ToString()),
                Is.Empty);
        }

        [Test]
        public void TargetWithoutScopeUnderARestriction()
        {
            Assert.That(Validate(cs => ((JObject)Op(cs, "op2")["target"]!).Remove("scope")), Is.Empty,
                "D5: the definition-only tool/type intersection infers the missing scope");

            Diagnostic fromType = Single(
                Validate(
                    cs => ((JObject)Op(cs, "op1")["target"]!).Remove("scope"),
                    catalog: c => Samples.Tool(c, "inventory.grantStarting").Remove("scopes")),
                DiagnosticCodes.ScopeNotAllowed);
            Assert.That(fromType.Message, Does.Contain("only edits at Instance, Definition"), "the object type's restriction applies");

            Assert.That(
                Validate(
                    cs => ((JObject)Op(cs, "op1")["target"]!).Remove("scope"),
                    catalog: c =>
                    {
                        Samples.Tool(c, "inventory.grantStarting").Remove("scopes");
                        ((JObject)c["objectTypes"]![2]!).Remove("scopes");
                    }).Select(d => d.ToString()),
                Is.Empty,
                "no restriction, no scope needed");
        }

        [Test]
        public void CandidateModeRefusesLifecycleFields()
        {
            ChangeSetValidationOptions candidate = new ChangeSetValidationOptions { Mode = ValidationMode.Candidate };
            Action<JObject> asCandidate = cs =>
            {
                cs["state"] = "Candidate";
                cs.Remove("outcomes");
                ((JObject)cs["timestamps"]!).Remove("applied");
                ((JObject)cs["links"]!).Remove("gameCoreOps");
            };
            Assert.That(Validate(asCandidate, options: candidate).Select(d => d.ToString()), Is.Empty);
            Assert.That(Validate(cs => { asCandidate(cs); cs.Remove("state"); }, options: candidate).Select(d => d.ToString()), Is.Empty,
                "no state is allowed");

            Assert.That(Single(Validate(cs => { asCandidate(cs); cs["state"] = "Applied"; }, options: candidate), DiagnosticCodes.CandidateInvalid).Message,
                Does.Contain("state Applied"));
            Assert.That(Single(Validate(cs => { asCandidate(cs); cs["outcomes"] = new JArray(); }, options: candidate), DiagnosticCodes.CandidateInvalid).Message,
                Does.Contain("'outcomes'"));
            Assert.That(Single(Validate(cs => { asCandidate(cs); cs["timestamps"]!["applied"] = "2026-10-04T08:21:03.004Z"; }, options: candidate),
                DiagnosticCodes.CandidateInvalid).Message, Does.Contain("'timestamps.applied'"));
            Assert.That(Single(Validate(cs => { asCandidate(cs); cs["links"]!["gameCoreOps"] = new JArray(); }, options: candidate),
                DiagnosticCodes.CandidateInvalid).Message, Does.Contain("'links.gameCoreOps'"));

            IReadOnlyList<Diagnostic> journal = Validate(options: candidate);
            Assert.That(journal.Select(d => d.Code), Is.All.EqualTo(DiagnosticCodes.CandidateInvalid));
            Assert.That(journal, Has.Count.EqualTo(4), "the applied journal sample violates all four candidate rules");
            Assert.That(Validate().Select(d => d.ToString()), Is.Empty, "the same document is a valid journal entry");
        }

        [Test]
        public void OutcomeForAnUnknownOperation()
        {
            Diagnostic diagnostic = Single(Validate(cs => cs["outcomes"]![1]!["opId"] = "op7"), DiagnosticCodes.CandidateInvalid);
            Assert.That(diagnostic.Message, Does.Contain("unknown operation 'op7'"));
            Assert.That(diagnostic.Where, Is.Null);
        }

        [Test]
        public void IndexSliceSkipsAbsenceRules()
        {
            ChangeSetValidationOptions slice = new ChangeSetValidationOptions { IndexIsSlice = true };
            Assert.That(
                Validate(cs => Op(cs, "op1")["target"]!["authoringId"] = "00000000-0000-4000-8000-000000000000", options: slice)
                    .Select(d => d.ToString()),
                Is.Empty,
                "absence from a slice is not StaleTarget");

            Action<JObject> place = cs => AddOperation(cs,
                "{\"opId\":\"op3\",\"tool\":\"npc.place\",\"target\":" + FerrymanRef(cs) + ",\"args\":{\"position\":[1,0,2]}}");
            Assert.That(Validate(place, index: i => ((JArray)i["nodes"]!).RemoveAt(3), options: slice).Select(d => d.ToString()), Is.Empty,
                "absence from a slice is not MissingPrerequisite");
            Single(Validate(place, index: i => ((JArray)i["nodes"]!).RemoveAt(3)), DiagnosticCodes.MissingPrerequisite);

            Single(Validate(cs => Op(cs, "op1")["args"]!["count"] = 120, options: slice), DiagnosticCodes.InvalidArgs);
        }

        [Test]
        public void LongDependencyChainsDoNotRecurse()
        {
            const int Count = 20000;
            IReadOnlyList<Diagnostic> diagnostics = Validate(cs =>
            {
                JArray operations = (JArray)cs["operations"]!;
                operations.Clear();
                for (int i = 0; i < Count; i++)
                {
                    JObject operation = new JObject { ["opId"] = "c" + i, ["tool"] = "project.noop" };
                    if (i > 0)
                    {
                        operation["dependsOn"] = new JArray("c" + (i - 1));
                    }

                    operations.Add(operation);
                }

                operations[0]!["dependsOn"] = new JArray("c" + (Count - 1));
                cs.Remove("artifacts");
                cs.Remove("outcomes");
            });
            List<Diagnostic> cycles = diagnostics.Where(d => d.Code == DiagnosticCodes.CandidateInvalid).ToList();
            Assert.That(cycles, Has.Count.EqualTo(1), "one cycle through all operations, reported once");
            Assert.That(cycles[0].Where!.OpId, Is.EqualTo("c0"));
            Assert.That(cycles[0].Message, Does.StartWith("Operations depend on each other in a cycle: c0 -> c" + (Count - 1) + " -> "));
            Assert.That(diagnostics.Count(d => d.Code == DiagnosticCodes.UnknownTool), Is.EqualTo(Count));
        }

        [Test]
        public void CycleWithADownstreamTailIsReportedOnce()
        {
            Diagnostic cycle = Single(Validate(cs =>
            {
                Op(cs, "op1")["dependsOn"] = new JArray("op2");
                AddOperation(cs, "{\"opId\":\"op3\",\"tool\":\"npc.setPatrol\",\"target\":" + FerrymanRef(cs)
                    + ",\"args\":{\"points\":[[1,2,3]]},\"dependsOn\":[\"op2\"]}");
            }), DiagnosticCodes.CandidateInvalid);
            Assert.That(cycle.Message, Does.Contain("op1 -> op2 -> op1"));
        }

        [Test]
        public void TargetMissingFromTheIndex()
        {
            Diagnostic diagnostic = Single(
                Validate(cs => Op(cs, "op1")["target"]!["authoringId"] = "00000000-0000-4000-8000-000000000000"),
                DiagnosticCodes.StaleTarget);
            Assert.That(diagnostic.Message, Does.Contain("not in the semantic index (revision 1234)"));
            Assert.That(
                Validate(cs => Op(cs, "op1")["target"]!["authoringId"] = "00000000-0000-4000-8000-000000000000",
                    options: new ChangeSetValidationOptions { RequireTargetsInIndex = false }).Select(d => d.ToString()),
                Is.Empty);
        }

        [Test]
        public void BaseVersionConflict()
        {
            Diagnostic diagnostic = Single(
                Validate(cs => cs["baseVersions"]![0]!["stamp"] = ContentStamp.OfUtf8("ferryman-older")),
                DiagnosticCodes.Conflict);
            Assert.That(diagnostic.Where!.Ref, Is.Not.Null);
            Assert.That((string?)diagnostic.Data!["expected"], Is.EqualTo(ContentStamp.OfUtf8("ferryman-older")));
            Assert.That((string?)diagnostic.Data!["actual"], Is.EqualTo(FerrymanStamp));
            Single(Validate(cs => cs["baseVersions"]![0]!["stamp"] = "sha256:short"), DiagnosticCodes.CandidateInvalid);
        }

        [Test]
        public void DiagnosticsRoundTripAndConform()
        {
            IReadOnlyList<Diagnostic> diagnostics = Validate(cs =>
            {
                Op(cs, "op1")["tool"] = "nope";
                Op(cs, "op2")["target"]!["scope"] = "Instance";
            });
            MiniSchemaValidator schema = MiniSchemaValidator.For(typeof(Diagnostic));
            foreach (Diagnostic diagnostic in diagnostics)
            {
                Assert.That(DiagnosticCodes.IsRegistered(diagnostic.Code), Is.True);
                string text = StudioJson.Serialize(diagnostic);
                Assert.That(schema.Validate(StudioJson.ParseToken(text)), Is.Empty);
                Assert.That(StudioJson.Serialize(StudioJson.Deserialize<Diagnostic>(text)), Is.EqualTo(text));
            }
        }
    }
}
