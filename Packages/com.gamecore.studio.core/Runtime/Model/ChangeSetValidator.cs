// GameCore.Studio.Model - static validation of a change set against a tool catalog and a semantic index
// (docs/studio/03-authoring-contracts.md s1, s5, s6, s7, s9). This is the Unity-free part of Precheck: the companion
// runs it on every agent candidate (CandidateInvalid path, 02 s5) and the edit engine runs it before staging. The
// Unity-side precheck (live stamps, residency, destroyed objects) stays with the edit engine.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Model
{
    /// <summary>Switches for checks whose inputs may legitimately be incomplete.</summary>
    public sealed class ChangeSetValidationOptions
    {
        /// <summary>Compare operation target stamps and base-version stamps with the index (default true).</summary>
        public bool CheckStamps { get; set; } = true;

        /// <summary>Report an operation target missing from the index as <c>StaleTarget</c> (default true).</summary>
        public bool RequireTargetsInIndex { get; set; } = true;
    }

    /// <summary>
    /// Validates a <see cref="ChangeSet"/> against a <see cref="ToolCatalog"/> and an optional <see cref="SemanticIndex"/>.
    /// Diagnostics are returned in a deterministic order: envelope, operation ids and dependencies, then per operation
    /// (tool, target, scope, prerequisites, arguments, runtime requirement), then artifacts, requirements and base
    /// versions. Without an index the index-dependent checks are skipped.
    /// </summary>
    public sealed class ChangeSetValidator
    {
        private static readonly Regex ColorText = new Regex("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.CultureInvariant);

        private readonly ToolCatalog _catalog;

        private readonly SemanticIndex? _index;

        private readonly ChangeSetValidationOptions _options;

        public ChangeSetValidator(ToolCatalog catalog, SemanticIndex? index = null, ChangeSetValidationOptions? options = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _index = index;
            _options = options ?? new ChangeSetValidationOptions();
        }

        /// <summary>All findings; an empty list means the change set may proceed to staging.</summary>
        public IReadOnlyList<Diagnostic> Validate(ChangeSet changeSet)
        {
            if (changeSet == null)
            {
                throw new ArgumentNullException(nameof(changeSet));
            }

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            CheckEnvelope(changeSet, diagnostics);
            Dictionary<string, Operation> operations = CheckOperationIds(changeSet, diagnostics);
            CheckDependencies(changeSet, operations, diagnostics);

            List<RuntimeApply> perOperation = new List<RuntimeApply>();
            foreach (Operation operation in changeSet.Operations)
            {
                ToolEntry? tool = _catalog.FindTool(operation.Tool);
                if (tool == null)
                {
                    diagnostics.Add(Diagnostic.AtOperation(
                        DiagnosticCodes.UnknownTool,
                        operation.OpId,
                        "Tool '" + operation.Tool + "' is not in the tool catalog.",
                        "Use a tool id listed in the catalog the request was planned with."));
                    continue;
                }

                IndexNode? node = CheckTarget(operation, tool, diagnostics);
                CheckPrerequisites(operation, tool, node, diagnostics);
                CheckArguments(operation, tool, diagnostics);
                perOperation.Add(CheckApplyRequirement(operation, tool, diagnostics));
            }

            CheckArtifacts(changeSet, diagnostics);
            CheckRequirements(changeSet, perOperation, diagnostics);
            CheckBaseVersions(changeSet, diagnostics);
            return diagnostics;
        }

        private static void CheckEnvelope(ChangeSet changeSet, List<Diagnostic> diagnostics)
        {
            if (!string.Equals(changeSet.Schema, ChangeSet.SchemaId, StringComparison.Ordinal))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.CandidateInvalid,
                    "Schema is '" + changeSet.Schema + "', expected '" + ChangeSet.SchemaId + "'."));
            }

            if (!IdDerivation.IsChangeSetId(changeSet.Id))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.CandidateInvalid,
                    "Change-set id '" + changeSet.Id + "' is not 'cs_' plus a 26-character ULID.",
                    "Keep the id minted with the request (IdDerivation.NewChangeSetId)."));
            }

            string? parent = changeSet.Links == null ? null : changeSet.Links.Parent;
            if (parent != null && !IdDerivation.IsChangeSetId(parent))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.CandidateInvalid,
                    "Parent change-set id '" + parent + "' is not a change-set id."));
            }

            if (changeSet.Operations.Count == 0)
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.CandidateInvalid, "The change set has no operations."));
            }
        }

        private static Dictionary<string, Operation> CheckOperationIds(ChangeSet changeSet, List<Diagnostic> diagnostics)
        {
            Dictionary<string, Operation> operations = new Dictionary<string, Operation>(StringComparer.Ordinal);
            foreach (Operation operation in changeSet.Operations)
            {
                if (operations.ContainsKey(operation.OpId))
                {
                    diagnostics.Add(Diagnostic.AtOperation(
                        DiagnosticCodes.CandidateInvalid,
                        operation.OpId,
                        "Operation id '" + operation.OpId + "' is used more than once."));
                    continue;
                }

                operations.Add(operation.OpId, operation);
            }

            return operations;
        }

        private static void CheckDependencies(ChangeSet changeSet, Dictionary<string, Operation> operations, List<Diagnostic> diagnostics)
        {
            foreach (Operation operation in changeSet.Operations)
            {
                if (operation.DependsOn == null)
                {
                    continue;
                }

                foreach (string dependency in operation.DependsOn)
                {
                    if (!operations.ContainsKey(dependency))
                    {
                        diagnostics.Add(Diagnostic.AtOperation(
                            DiagnosticCodes.CandidateInvalid,
                            operation.OpId,
                            "Operation '" + operation.OpId + "' depends on unknown operation '" + dependency + "'."));
                    }
                }
            }

            // Cycle detection over the unique, known operations; each cycle is reported once, at its first member in
            // document order.
            Dictionary<string, int> color = new Dictionary<string, int>(StringComparer.Ordinal);
            HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (Operation operation in changeSet.Operations)
            {
                List<string> path = new List<string>();
                FindCycle(operation.OpId, operations, color, path, reported, diagnostics);
            }
        }

        private static void FindCycle(
            string opId,
            Dictionary<string, Operation> operations,
            Dictionary<string, int> color,
            List<string> path,
            HashSet<string> reported,
            List<Diagnostic> diagnostics)
        {
            if (color.TryGetValue(opId, out int state))
            {
                if (state == 1)
                {
                    int start = path.IndexOf(opId);
                    List<string> cycle = path.GetRange(start, path.Count - start);
                    cycle.Add(opId);
                    string first = cycle[0];
                    if (reported.Add(first))
                    {
                        diagnostics.Add(Diagnostic.AtOperation(
                            DiagnosticCodes.CandidateInvalid,
                            first,
                            "Operations depend on each other in a cycle: " + string.Join(" -> ", cycle) + "."));
                    }
                }

                return;
            }

            if (!operations.TryGetValue(opId, out Operation? operation))
            {
                return;
            }

            color[opId] = 1;
            path.Add(opId);
            if (operation.DependsOn != null)
            {
                foreach (string dependency in operation.DependsOn)
                {
                    FindCycle(dependency, operations, color, path, reported, diagnostics);
                }
            }

            path.RemoveAt(path.Count - 1);
            color[opId] = 2;
        }

        private IndexNode? CheckTarget(Operation operation, ToolEntry tool, List<Diagnostic> diagnostics)
        {
            AuthoringRef? target = operation.Target;
            if (target == null)
            {
                if (tool.TargetRequired)
                {
                    diagnostics.Add(Diagnostic.AtOperation(
                        DiagnosticCodes.InvalidArgs,
                        operation.OpId,
                        "Tool '" + tool.Id + "' needs a target" + (tool.TargetType == null ? "." : " of type '" + tool.TargetType + "'.")));
                }

                return null;
            }

            foreach (string problem in target.ShapeProblems())
            {
                diagnostics.Add(Diagnostic.AtRef(DiagnosticCodes.CandidateInvalid, target, "Malformed target of '" + operation.OpId + "': " + problem + "."));
            }

            if (tool.TargetKinds != null && !Contains(tool.TargetKinds, target.Kind))
            {
                diagnostics.Add(Diagnostic.AtOperation(
                    DiagnosticCodes.InvalidArgs,
                    operation.OpId,
                    "Tool '" + tool.Id + "' does not accept target kind " + target.Kind.ToString() + " (accepts " + JoinKinds(tool.TargetKinds) + ")."));
            }

            if (target.Scope.HasValue)
            {
                AuthorScope scope = target.Scope.Value;
                if (tool.Scopes != null && !Contains(tool.Scopes, scope))
                {
                    diagnostics.Add(Diagnostic.AtOperation(
                        DiagnosticCodes.ScopeNotAllowed,
                        operation.OpId,
                        "Tool '" + tool.Id + "' cannot edit at scope " + scope.ToString() + " (allowed: " + JoinScopes(tool.Scopes) + ").",
                        "Choose an allowed scope for the target, or a tool that edits at this scope."));
                }
                else
                {
                    ObjectTypeEntry? type = tool.TargetType == null ? null : _catalog.FindObjectType(tool.TargetType);
                    if (type != null && type.Scopes != null && !Contains(type.Scopes, scope))
                    {
                        diagnostics.Add(Diagnostic.AtOperation(
                            DiagnosticCodes.ScopeNotAllowed,
                            operation.OpId,
                            "Object type '" + type.TypeId + "' cannot be edited at scope " + scope.ToString() + " (allowed: " + JoinScopes(type.Scopes) + ")."));
                    }
                }
            }

            bool stampChecked = operation.EffectivePreconditions == Preconditions.Stamp && target.Kind != AuthoringKind.Location;
            if (_options.CheckStamps && stampChecked && target.Stamp == null)
            {
                diagnostics.Add(Diagnostic.AtOperation(
                    DiagnosticCodes.CandidateInvalid,
                    operation.OpId,
                    "Operation '" + operation.OpId + "' uses stamp preconditions but its target carries no stamp.",
                    "Copy the target ref (with its stamp) from the index slice, or declare preconditions \"none\"."));
            }

            if (_index == null || target.Kind == AuthoringKind.Location)
            {
                return null;
            }

            IndexNode? node = _index.FindNode(target);
            if (node == null)
            {
                if (_options.RequireTargetsInIndex)
                {
                    diagnostics.Add(Diagnostic.AtRef(
                        DiagnosticCodes.StaleTarget,
                        target,
                        "Target of '" + operation.OpId + "' is not in the semantic index (revision "
                        + _index.Revision.ToString(CultureInfo.InvariantCulture) + ").",
                        "The object may have been deleted or is outside the planned slice; re-plan against the current index."));
                }

                return null;
            }

            if (tool.TargetType != null && !string.Equals(node.Type, tool.TargetType, StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic.AtOperation(
                    DiagnosticCodes.InvalidArgs,
                    operation.OpId,
                    "Tool '" + tool.Id + "' needs a target of type '" + tool.TargetType + "', but the target is '" + node.Type + "'."));
            }

            if (_options.CheckStamps && stampChecked && target.Stamp != null && node.Ref.Stamp != null
                && !string.Equals(target.Stamp, node.Ref.Stamp, StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic.AtRef(
                    DiagnosticCodes.StaleTarget,
                    target,
                    "Target of '" + operation.OpId + "' changed since planning (expected " + target.Stamp + ", actual " + node.Ref.Stamp + ").",
                    "Rebase: re-plan the operation against the current stamp, or skip it."));
            }

            return node;
        }

        private void CheckPrerequisites(Operation operation, ToolEntry tool, IndexNode? node, List<Diagnostic> diagnostics)
        {
            if (tool.Prerequisites == null || _index == null)
            {
                return;
            }

            foreach (Prerequisite prerequisite in tool.Prerequisites)
            {
                if (prerequisite.On == PrerequisiteSubject.Target)
                {
                    if (node != null && !node.Provides(prerequisite.Requires))
                    {
                        diagnostics.Add(Diagnostic.AtOperation(
                            DiagnosticCodes.MissingPrerequisite,
                            operation.OpId,
                            "Tool '" + tool.Id + "' needs a target that provides '" + prerequisite.Requires + "'.",
                            prerequisite.Doc));
                    }

                    continue;
                }

                bool found = false;
                foreach (IndexNode candidate in _index.Nodes)
                {
                    if (candidate.Provides(prerequisite.Requires))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    diagnostics.Add(Diagnostic.AtOperation(
                        DiagnosticCodes.MissingPrerequisite,
                        operation.OpId,
                        "Tool '" + tool.Id + "' needs '" + prerequisite.Requires + "' in the project, and the index has none.",
                        prerequisite.Doc ?? "Create the prerequisite first (for example with a Compose tool), then retry."));
                }
            }
        }

        private void CheckArguments(Operation operation, ToolEntry tool, List<Diagnostic> diagnostics)
        {
            JObject args = operation.Args ?? new JObject();
            foreach (JProperty property in args.Properties())
            {
                ArgSpec? spec = tool.FindArg(property.Name);
                if (spec == null)
                {
                    diagnostics.Add(Diagnostic.AtOperation(
                        DiagnosticCodes.InvalidArgs,
                        operation.OpId,
                        "Tool '" + tool.Id + "' has no argument '" + property.Name + "'."));
                    continue;
                }

                if (property.Value.Type == JTokenType.Null)
                {
                    continue;
                }

                List<string> problems = new List<string>();
                CheckValue(spec, spec.Type, property.Value, problems);
                foreach (string problem in problems)
                {
                    diagnostics.Add(Diagnostic.AtOperation(
                        DiagnosticCodes.InvalidArgs,
                        operation.OpId,
                        "Argument '" + spec.Name + "' of '" + tool.Id + "': " + problem + "."));
                }
            }

            foreach (ArgSpec spec in tool.Args)
            {
                if (!spec.Required)
                {
                    continue;
                }

                JToken? value = args[spec.Name];
                if (value == null || value.Type == JTokenType.Null)
                {
                    diagnostics.Add(Diagnostic.AtOperation(
                        DiagnosticCodes.InvalidArgs,
                        operation.OpId,
                        "Tool '" + tool.Id + "' requires argument '" + spec.Name + "' (" + spec.Type + ")."));
                }
            }
        }

        private void CheckValue(ValueSpec spec, string type, JToken value, List<string> problems)
        {
            if (ValueTypes.IsArray(type))
            {
                if (!(value is JArray array))
                {
                    problems.Add("expected " + type + ", got " + Describe(value));
                    return;
                }

                string element = ValueTypes.ElementOf(type);
                for (int i = 0; i < array.Count; i++)
                {
                    List<string> inner = new List<string>();
                    CheckValue(spec, element, array[i], inner);
                    foreach (string problem in inner)
                    {
                        problems.Add("[" + i.ToString(CultureInfo.InvariantCulture) + "] " + problem);
                    }
                }

                return;
            }

            switch (type)
            {
                case ValueTypes.Bool:
                    Expect(value.Type == JTokenType.Boolean, type, value, problems);
                    return;
                case ValueTypes.Int:
                    if (!IsInteger(value))
                    {
                        problems.Add("expected int, got " + Describe(value));
                        return;
                    }

                    CheckRange(spec, value.Value<double>(), problems);
                    return;
                case ValueTypes.Float:
                    if (!IsNumber(value))
                    {
                        problems.Add("expected float, got " + Describe(value));
                        return;
                    }

                    CheckRange(spec, value.Value<double>(), problems);
                    return;
                case ValueTypes.String:
                    Expect(value.Type == JTokenType.String, type, value, problems);
                    return;
                case ValueTypes.Enum:
                    if (value.Type != JTokenType.String)
                    {
                        problems.Add("expected enum name, got " + Describe(value));
                        return;
                    }

                    if (spec.EnumValues != null && !Contains(spec.EnumValues, value.Value<string>() ?? string.Empty))
                    {
                        problems.Add("'" + value.Value<string>() + "' is not one of " + string.Join(", ", spec.EnumValues));
                    }

                    return;
                case ValueTypes.Vector2:
                    Expect(IsNumberArray(value, 2, 2), "[x, y]", value, problems);
                    return;
                case ValueTypes.Vector3:
                    Expect(IsNumberArray(value, 3, 3), "[x, y, z]", value, problems);
                    return;
                case ValueTypes.Vector4:
                case ValueTypes.Quaternion:
                    Expect(IsNumberArray(value, 4, 4), "[x, y, z, w]", value, problems);
                    return;
                case ValueTypes.Color:
                    Expect(
                        IsNumberArray(value, 3, 4) || (value.Type == JTokenType.String && ColorText.IsMatch(value.Value<string>() ?? string.Empty)),
                        "[r, g, b(, a)] or #rrggbb(aa)",
                        value,
                        problems);
                    return;
                case ValueTypes.Ref:
                    CheckReference(spec, value, problems);
                    return;
                case ValueTypes.Artifact:
                    Expect(ArtifactReferenceOf(value) != null, "{ \"artifact\": \"sha256:...\" }", value, problems);
                    return;
                case ValueTypes.Object:
                    Expect(value.Type == JTokenType.Object, "object", value, problems);
                    return;
                default:
                    // An unknown type name is a catalog defect, not a change-set defect; any value is accepted.
                    return;
            }
        }

        private void CheckReference(ValueSpec spec, JToken value, List<string> problems)
        {
            IndexNode? node = null;
            if (value.Type == JTokenType.String)
            {
                string text = value.Value<string>() ?? string.Empty;
                if (text.Length == 0)
                {
                    problems.Add("a reference cannot be empty");
                    return;
                }

                if (_index != null)
                {
                    node = text.IndexOf('@') >= 0 ? _index.FindByDefinition(text) : FindByAuthoringId(_index, text);
                }
            }
            else if (value.Type == JTokenType.Object)
            {
                AuthoringRef? reference;
                try
                {
                    reference = value.ToObject<AuthoringRef>(JsonSerializer.Create(StudioJson.CreateSettings()));
                }
                catch (JsonException error)
                {
                    problems.Add("not an AuthoringRef: " + error.Message);
                    return;
                }

                if (reference == null)
                {
                    problems.Add("not an AuthoringRef");
                    return;
                }

                foreach (string problem in reference.ShapeProblems())
                {
                    problems.Add(problem);
                }

                node = _index?.FindNode(reference);
            }
            else
            {
                problems.Add("expected a reference (AuthoringRef, authoring id or name@revision), got " + Describe(value));
                return;
            }

            // A reference outside the (bounded) index slice cannot be judged here; one inside it must match the category.
            if (node != null && spec.Category != null && !node.Provides(spec.Category))
            {
                problems.Add("references '" + node.Type + "', which does not provide category '" + spec.Category + "'");
            }
        }

        private static IndexNode? FindByAuthoringId(SemanticIndex index, string authoringId)
        {
            foreach (IndexNode node in index.Nodes)
            {
                if (string.Equals(node.Ref.AuthoringId, authoringId, StringComparison.Ordinal))
                {
                    return node;
                }
            }

            return null;
        }

        private static RuntimeApply CheckApplyRequirement(Operation operation, ToolEntry tool, List<Diagnostic> diagnostics)
        {
            if (operation.ApplyRequirement.HasValue && operation.ApplyRequirement.Value < tool.RuntimeApply)
            {
                diagnostics.Add(Diagnostic.AtOperation(
                    DiagnosticCodes.CandidateInvalid,
                    operation.OpId,
                    "Operation '" + operation.OpId + "' declares applyRequirement " + operation.ApplyRequirement.Value.ToString()
                    + ", but tool '" + tool.Id + "' needs " + tool.RuntimeApply.ToString() + "."));
                return tool.RuntimeApply;
            }

            return operation.ApplyRequirement ?? tool.RuntimeApply;
        }

        private static void CheckArtifacts(ChangeSet changeSet, List<Diagnostic> diagnostics)
        {
            Dictionary<string, ArtifactRef> carried = new Dictionary<string, ArtifactRef>(StringComparer.Ordinal);
            if (changeSet.Artifacts != null)
            {
                foreach (ArtifactRef artifact in changeSet.Artifacts)
                {
                    if (!ContentStamp.IsValidHex(artifact.Sha256))
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.CandidateInvalid,
                            "Artifact '" + (artifact.Name ?? artifact.Sha256) + "' has an invalid sha256 (64 lowercase hex digits expected)."));
                        continue;
                    }

                    if (carried.ContainsKey(artifact.Sha256))
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.CandidateInvalid,
                            "Artifact sha256:" + artifact.Sha256 + " is listed more than once."));
                        continue;
                    }

                    if (artifact.Bytes < 0)
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.CandidateInvalid,
                            "Artifact sha256:" + artifact.Sha256 + " has a negative size."));
                    }

                    carried.Add(artifact.Sha256, artifact);
                }
            }

            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            foreach (Operation operation in changeSet.Operations)
            {
                if (operation.Args == null)
                {
                    continue;
                }

                List<string> references = new List<string>();
                CollectArtifactReferences(operation.Args, references);
                foreach (string reference in references)
                {
                    if (!ContentStamp.IsValid(reference))
                    {
                        diagnostics.Add(Diagnostic.AtOperation(
                            DiagnosticCodes.CandidateInvalid,
                            operation.OpId,
                            "Artifact reference '" + reference + "' is not 'sha256:' plus 64 lowercase hex digits."));
                        continue;
                    }

                    string digest = ContentStamp.DigestOf(reference);
                    used.Add(digest);
                    if (!carried.ContainsKey(digest))
                    {
                        diagnostics.Add(Diagnostic.AtOperation(
                            DiagnosticCodes.CandidateInvalid,
                            operation.OpId,
                            "Operation '" + operation.OpId + "' uses artifact " + reference + ", which the change set does not carry.",
                            "List every referenced artifact under 'artifacts' with its digest, size and media type."));
                    }
                }
            }

            foreach (KeyValuePair<string, ArtifactRef> artifact in carried)
            {
                if (!used.Contains(artifact.Key))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.CandidateInvalid,
                        "Artifact sha256:" + artifact.Key + (artifact.Value.Name == null ? string.Empty : " (" + artifact.Value.Name + ")")
                        + " is carried but no operation uses it.",
                        "Remove the artifact or reference it from an operation argument as { \"artifact\": \"sha256:...\" }."));
                }
            }
        }

        private static void CollectArtifactReferences(JToken token, List<string> into)
        {
            string? reference = ArtifactReferenceOf(token);
            if (reference != null)
            {
                into.Add(reference);
            }

            if (token is JContainer container)
            {
                foreach (JToken child in container.Children())
                {
                    CollectArtifactReferences(child is JProperty property ? property.Value : child, into);
                }
            }
        }

        /// <summary>The <c>artifact</c> string of an <c>{ "artifact": "..." }</c> object, or null.</summary>
        private static string? ArtifactReferenceOf(JToken token)
        {
            if (token is JObject value && value["artifact"] is JValue artifact && artifact.Type == JTokenType.String)
            {
                return artifact.Value<string>();
            }

            return null;
        }

        private static void CheckRequirements(ChangeSet changeSet, List<RuntimeApply> perOperation, List<Diagnostic> diagnostics)
        {
            Requirements implied = Requirements.FromOperations(perOperation);
            Requirements? declared = changeSet.Requirements;
            if (declared == null)
            {
                if (implied.Max > RuntimeApply.Live)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.CandidateInvalid,
                        "The operations need " + implied.Max.ToString() + " but the change set declares no requirements."));
                }

                return;
            }

            if (declared.Max < implied.Max)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.CandidateInvalid,
                    "Requirements declare max " + declared.Max.ToString() + ", but the operations need " + implied.Max.ToString() + "."));
            }

            AddFlagProblem(implied.WorldRebuild && !declared.WorldRebuild, "an operation needs a world rebuild but 'worldRebuild' is false", diagnostics);
            AddFlagProblem(implied.Compile && !declared.Compile, "an operation needs a compile but 'compile' is false", diagnostics);
            AddFlagProblem(implied.Build && !declared.Build, "an operation needs a player build but 'build' is false", diagnostics);
            AddFlagProblem(declared.WorldRebuild && declared.Max < RuntimeApply.Rebuild, "'worldRebuild' is true but 'max' is below Rebuild", diagnostics);
            AddFlagProblem(declared.Compile && declared.Max < RuntimeApply.Compile, "'compile' is true but 'max' is below Compile", diagnostics);
            AddFlagProblem(declared.Build && declared.Max < RuntimeApply.Build, "'build' is true but 'max' is below Build", diagnostics);
        }

        private static void AddFlagProblem(bool condition, string message, List<Diagnostic> diagnostics)
        {
            if (condition)
            {
                diagnostics.Add(new Diagnostic(DiagnosticCodes.CandidateInvalid, "Inconsistent requirements: " + message + "."));
            }
        }

        private void CheckBaseVersions(ChangeSet changeSet, List<Diagnostic> diagnostics)
        {
            if (changeSet.BaseVersions == null)
            {
                return;
            }

            foreach (BaseVersion version in changeSet.BaseVersions)
            {
                if (!ContentStamp.IsValid(version.Stamp))
                {
                    diagnostics.Add(Diagnostic.AtRef(
                        DiagnosticCodes.CandidateInvalid,
                        version.Ref,
                        "Base version stamp '" + version.Stamp + "' is not 'sha256:' plus 64 lowercase hex digits."));
                    continue;
                }

                if (_index == null || !_options.CheckStamps)
                {
                    continue;
                }

                IndexNode? node = _index.FindNode(version.Ref);
                if (node != null && node.Ref.Stamp != null && !string.Equals(node.Ref.Stamp, version.Stamp, StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic.AtRef(
                        DiagnosticCodes.Conflict,
                        version.Ref,
                        "A read dependency changed since planning (expected " + version.Stamp + ", actual " + node.Ref.Stamp + ").",
                        "Rebase the change set against the current index, or skip the affected operations."));
                }
            }
        }

        private static void CheckRange(ValueSpec spec, double number, List<string> problems)
        {
            if (spec.Min.HasValue && number < spec.Min.Value)
            {
                problems.Add(Format(number) + " is below the minimum " + Format(spec.Min.Value) + Unit(spec));
            }

            if (spec.Max.HasValue && number > spec.Max.Value)
            {
                problems.Add(Format(number) + " is above the maximum " + Format(spec.Max.Value) + Unit(spec));
            }
        }

        private static string Unit(ValueSpec spec) => spec.Unit == null ? string.Empty : " " + spec.Unit;

        private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static void Expect(bool condition, string expected, JToken value, List<string> problems)
        {
            if (!condition)
            {
                problems.Add("expected " + expected + ", got " + Describe(value));
            }
        }

        private static bool IsNumber(JToken value)
        {
            if (value.Type == JTokenType.Integer)
            {
                return true;
            }

            if (value.Type != JTokenType.Float)
            {
                return false;
            }

            double number = value.Value<double>();
            return !double.IsNaN(number) && !double.IsInfinity(number);
        }

        private static bool IsInteger(JToken value)
        {
            if (value.Type == JTokenType.Integer)
            {
                return true;
            }

            if (value.Type != JTokenType.Float)
            {
                return false;
            }

            double number = value.Value<double>();
            return !double.IsNaN(number) && !double.IsInfinity(number) && Math.Floor(number) == number;
        }

        private static bool IsNumberArray(JToken value, int minCount, int maxCount)
        {
            if (!(value is JArray array) || array.Count < minCount || array.Count > maxCount)
            {
                return false;
            }

            foreach (JToken item in array)
            {
                if (!IsNumber(item))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Describe(JToken value)
        {
            switch (value.Type)
            {
                case JTokenType.Object:
                    return "an object";
                case JTokenType.Array:
                    return "an array of " + ((JArray)value).Count.ToString(CultureInfo.InvariantCulture);
                case JTokenType.String:
                    return "string \"" + value.Value<string>() + "\"";
                case JTokenType.Integer:
                case JTokenType.Float:
                    return "number " + value.ToString(Formatting.None);
                case JTokenType.Boolean:
                    return "boolean " + value.ToString(Formatting.None);
                default:
                    return value.Type.ToString();
            }
        }

        private static bool Contains<T>(IReadOnlyList<T> items, T value)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < items.Count; i++)
            {
                if (comparer.Equals(items[i], value))
                {
                    return true;
                }
            }

            return false;
        }

        private static string JoinKinds(IReadOnlyList<AuthoringKind> kinds)
        {
            string[] names = new string[kinds.Count];
            for (int i = 0; i < kinds.Count; i++)
            {
                names[i] = kinds[i].ToString();
            }

            return string.Join(", ", names);
        }

        private static string JoinScopes(IReadOnlyList<AuthorScope> scopes)
        {
            string[] names = new string[scopes.Count];
            for (int i = 0; i < scopes.Count; i++)
            {
                names[i] = scopes[i].ToString();
            }

            return string.Join(", ", names);
        }
    }
}
