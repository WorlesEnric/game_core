// GameCore.Studio.Edit - Configure-tier built-ins (docs/studio/03-authoring-contracts.md s5): set, assign, bind.
// Writes go through SerializedObject.ApplyModifiedProperties (Undo-recorded, prefab overrides kept, objects dirtied);
// [AuthorField] properties are set by reflection after Undo.RecordObject.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    /// <summary><c>set</c>: set or clear authorable fields (<c>{field, value}</c> or <c>{fields: {...}}</c>).</summary>
    internal sealed class SetTool : BuiltInTool, IReplannableTool
    {
        public SetTool()
            : base(Entry_(
                BuiltInToolIdsExt.Set,
                ToolTier.Configure,
                RuntimeApply.Live,
                true,
                "Set or clear authorable fields of an existing object. Give {field, value} or {fields: {name: value}}; each value has the type the object type declares for that field (catalog objectTypes).",
                AuthoredKinds,
                Arg("field", ValueTypes.String, false, "Field name (used with 'value')."),
                Arg("value", FieldValueChecker.FieldValueType, false, "New value of 'field' (its type is the field's catalog type; null clears)."),
                Arg("fields", ValueTypes.Object, false, "Several fields at once: { name: value }.")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            if (!Collect(context, result, out AuthoringTypeInfo? info, out List<KeyValuePair<AuthorMemberInfo, JToken>> assignments))
            {
                return result;
            }

            JObject preview = new JObject();
            foreach (KeyValuePair<AuthorMemberInfo, JToken> assignment in assignments)
            {
                AuthorMemberInfo member = assignment.Key;
                if (member.IsReference)
                {
                    if (assignment.Value.Type != JTokenType.Null && !context.Codec.TryToClr(assignment.Value, member.ValueType, out _, out string? problem)
                        && (context.Operation.DependsOn == null || context.Operation.DependsOn.Count == 0))
                    {
                        result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'" + member.Name + "': " + problem + "."));
                    }
                }
                else
                {
                    foreach (string problem in FieldValueChecker.Check(member.Spec, assignment.Value))
                    {
                        result.Add(context.Problem(DiagnosticCodes.InvalidArgs, problem + "."));
                    }
                }

                preview[member.Name] = new JObject
                {
                    ["before"] = context.Codec.ReadMember(context.Target!, member),
                    ["after"] = assignment.Value.DeepClone(),
                };
            }

            result.Preview = new JObject { ["fields"] = preview, ["type"] = info!.TypeId };
            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            ToolStageResult problems = new ToolStageResult();
            if (!Collect(context, problems, out AuthoringTypeInfo? info, out List<KeyValuePair<AuthorMemberInfo, JToken>> assignments))
            {
                return OperationResult.Refused(problems.Diagnostics[0].Code, problems.Diagnostics[0].Message);
            }

            UnityEngine.Object target = context.Target!;
            JObject before = new JObject();
            SerializedObject serialized = new SerializedObject(target);
            bool recorded = false;
            foreach (KeyValuePair<AuthorMemberInfo, JToken> assignment in assignments)
            {
                AuthorMemberInfo member = assignment.Key;
                before[member.Name] = context.Codec.ReadMember(target, member, serialized);
                if (!member.IsSerializedField && !recorded)
                {
                    context.RecordUndo(target);
                    recorded = true;
                }

                if (!context.Codec.WriteMember(target, member, assignment.Value, serialized, out string? problem))
                {
                    return OperationResult.Failed(DiagnosticCodes.InvalidArgs, "'" + member.Name + "': " + problem);
                }
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            AuthoringRef? reference = ToolSupport.RefOf(context, target);
            OperationResult result = OperationResult.Applied();
            if (reference != null)
            {
                result.WithInverse(ToolSupport.SetFieldsInverse(reference, before));
            }

            return result.Touch(target);
        }

        public Operation? Replan(EditContext context, string? currentStamp, out Diagnostic? problem)
        {
            problem = null;
            Operation operation = context.Operation;
            return new Operation(operation.OpId, operation.Tool, operation.Target?.WithStamp(currentStamp), operation.Args, operation.DependsOn, operation.Preconditions, operation.ApplyRequirement);
        }

        private static bool Collect(EditContext context, ToolStageResult problems, out AuthoringTypeInfo? info, out List<KeyValuePair<AuthorMemberInfo, JToken>> assignments)
        {
            assignments = new List<KeyValuePair<AuthorMemberInfo, JToken>>();
            info = context.Target == null ? null : context.Identity.Describe(context.Target);
            if (info == null)
            {
                problems.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'set' needs an authorable target (a type with [Authorable])."));
                return false;
            }

            JObject args = context.Args;
            string? field = context.StringArg("field");
            if (field != null)
            {
                JToken value = args["value"] ?? JValue.CreateNull();
                AddAssignment(context, info, field, value, assignments, problems);
            }

            if (args["fields"] is JObject fields)
            {
                foreach (JProperty property in fields.Properties())
                {
                    AddAssignment(context, info, property.Name, property.Value, assignments, problems);
                }
            }

            if (assignments.Count == 0 && problems.Ok)
            {
                problems.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'set' needs {field, value} or {fields: {...}}."));
            }

            return problems.Ok;
        }

        private static void AddAssignment(EditContext context, AuthoringTypeInfo info, string name, JToken value, List<KeyValuePair<AuthorMemberInfo, JToken>> assignments, ToolStageResult problems)
        {
            AuthorMemberInfo? member = info.FindMember(name);
            if (member == null)
            {
                problems.Add(context.Problem(DiagnosticCodes.InvalidArgs, "Type '" + info.TypeId + "' has no authorable field '" + name + "'.", "Use a field listed for the type in the tool catalog."));
                return;
            }

            if (!member.IsSerializedField && !(member.Member is System.Reflection.PropertyInfo property && property.CanWrite))
            {
                problems.Add(context.Problem(DiagnosticCodes.InvalidArgs, "Field '" + name + "' of '" + info.TypeId + "' is read-only."));
                return;
            }

            assignments.Add(new KeyValuePair<AuthorMemberInfo, JToken>(member, value));
        }
    }

    /// <summary><c>assign</c>: assign a reference (another authored thing or an asset) to an [AuthorRef] field.</summary>
    internal sealed class AssignTool : BuiltInTool, IReplannableTool
    {
        public AssignTool()
            : base(Entry_(
                BuiltInToolIdsExt.Assign,
                ToolTier.Configure,
                RuntimeApply.Live,
                true,
                "Assign a reference ([AuthorRef] field) to another authored thing or an asset; null clears. For list fields, 'append' adds and 'index' replaces one element.",
                AuthoredKinds,
                Arg("field", ValueTypes.String, true, "The [AuthorRef] field."),
                Arg("value", ValueTypes.Ref, false, "The referenced thing: an AuthoringRef, an authoring id, name@revision or an asset path; null clears."),
                Arg("index", ValueTypes.Int, false, "List fields: the element to replace."),
                Arg("append", ValueTypes.Bool, false, "List fields: append instead of replacing the list.")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            AuthorMemberInfo? member = Member(context, result);
            if (member == null)
            {
                return result;
            }

            JToken? value = context.Arg("value");
            if (value == null)
            {
                if (member.Spec.Required && !member.IsCollection)
                {
                    result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "Reference '" + member.Name + "' is required and cannot be cleared."));
                }
            }
            else
            {
                UnityEngine.Object? referenced = context.Codec.Refs.ReadRef(value, member.ElementType, out string? problem);
                if (referenced == null)
                {
                    if (context.Operation.DependsOn == null || context.Operation.DependsOn.Count == 0)
                    {
                        result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'" + member.Name + "': " + problem + "."));
                    }
                }
                else if (!CategoryAccepts(context, member, referenced))
                {
                    result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'" + member.Name + "' accepts category '" + member.Spec.Category + "'; the referenced object does not provide it."));
                }
            }

            result.Preview = new JObject
            {
                ["field"] = member.Name,
                ["before"] = context.Codec.ReadMember(context.Target!, member),
                ["after"] = value?.DeepClone() ?? JValue.CreateNull(),
            };
            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            ToolStageResult problems = new ToolStageResult();
            AuthorMemberInfo? member = Member(context, problems);
            if (member == null)
            {
                return OperationResult.Refused(problems.Diagnostics[0].Code, problems.Diagnostics[0].Message);
            }

            UnityEngine.Object? referenced = null;
            JToken? value = context.Arg("value");
            if (value != null)
            {
                referenced = context.Codec.Refs.ReadRef(value, member.ElementType, out string? problem);
                if (referenced == null)
                {
                    return OperationResult.Failed(DiagnosticCodes.InvalidArgs, "'" + member.Name + "': " + problem);
                }
            }

            int? index = context.Arg("index") is JToken indexToken && ValueCodec.IsInteger(indexToken) ? indexToken.Value<int>() : (int?)null;
            return AssignReference(context, member, referenced, context.BoolArg("append"), index);
        }

        public Operation? Replan(EditContext context, string? currentStamp, out Diagnostic? problem)
        {
            problem = null;
            Operation operation = context.Operation;
            return new Operation(operation.OpId, operation.Tool, operation.Target?.WithStamp(currentStamp), operation.Args, operation.DependsOn, operation.Preconditions, operation.ApplyRequirement);
        }

        /// <summary>Writes a reference into an [AuthorRef] member (single, append or index) with an inverse <c>set</c>.</summary>
        internal static OperationResult AssignReference(EditContext context, AuthorMemberInfo member, UnityEngine.Object? referenced, bool append, int? index)
        {
            UnityEngine.Object target = context.Target!;
            JToken before = context.Codec.ReadMember(target, member);
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty? property = member.IsSerializedField ? serialized.FindProperty(member.Name) : null;
            if (property != null)
            {
                if (member.IsCollection)
                {
                    if (append)
                    {
                        property.arraySize++;
                        property.GetArrayElementAtIndex(property.arraySize - 1).objectReferenceValue = referenced;
                    }
                    else if (index.HasValue)
                    {
                        if (index.Value < 0 || index.Value >= property.arraySize)
                        {
                            return OperationResult.Failed(DiagnosticCodes.InvalidArgs, "Index " + index.Value + " is outside '" + member.Name + "' (" + property.arraySize + " elements).");
                        }

                        property.GetArrayElementAtIndex(index.Value).objectReferenceValue = referenced;
                    }
                    else
                    {
                        property.arraySize = referenced == null ? 0 : 1;
                        if (referenced != null)
                        {
                            property.GetArrayElementAtIndex(0).objectReferenceValue = referenced;
                        }
                    }
                }
                else
                {
                    property.objectReferenceValue = referenced;
                }

                serialized.ApplyModifiedProperties();
            }
            else
            {
                context.RecordUndo(target);
                object? newValue = referenced;
                if (member.IsCollection)
                {
                    List<object?> items = new List<object?>(AuthoringIdentity.Items(member.GetValue(target)));
                    if (append)
                    {
                        items.Add(referenced);
                    }
                    else if (index.HasValue && index.Value >= 0 && index.Value < items.Count)
                    {
                        items[index.Value] = referenced;
                    }
                    else
                    {
                        items.Clear();
                        if (referenced != null)
                        {
                            items.Add(referenced);
                        }
                    }

                    newValue = ToCollection(member, items);
                }

                if (!member.TrySetValue(target, newValue))
                {
                    return OperationResult.Failed(DiagnosticCodes.InvalidArgs, "'" + member.Name + "' is read-only.");
                }
            }

            EditorUtility.SetDirty(target);
            OperationResult result = OperationResult.Applied();
            AuthoringRef? reference = ToolSupport.RefOf(context, target);
            if (reference != null)
            {
                result.WithInverse(ToolSupport.SetFieldsInverse(reference, new JObject { [member.Name] = before }));
            }

            return result.Touch(target);
        }

        internal static bool CategoryAccepts(EditContext context, AuthorMemberInfo member, UnityEngine.Object referenced)
        {
            string? category = member.Spec.Category;
            if (category == null)
            {
                return true;
            }

            UnityEngine.Object candidate = referenced is GameObject gameObject ? (UnityEngine.Object?)context.Identity.FindAuthoredComponent(gameObject) ?? referenced : referenced;
            AuthoringTypeInfo? info = context.Identity.Describe(candidate);
            if (info == null)
            {
                return true;
            }

            if (string.Equals(info.TypeId, category, StringComparison.Ordinal))
            {
                return true;
            }

            IReadOnlyList<string>? capabilities = context.Identity.GetCapabilities(candidate);
            if (capabilities == null)
            {
                return false;
            }

            foreach (string capability in capabilities)
            {
                if (string.Equals(capability, category, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static object ToCollection(AuthorMemberInfo member, List<object?> items)
        {
            Type element = member.ElementType;
            if (member.ValueType.IsArray)
            {
                Array array = Array.CreateInstance(element, items.Count);
                for (int i = 0; i < items.Count; i++)
                {
                    array.SetValue(items[i], i);
                }

                return array;
            }

            IList list = (IList)Activator.CreateInstance(member.ValueType)!;
            foreach (object? item in items)
            {
                list.Add(item);
            }

            return list;
        }

        private static AuthorMemberInfo? Member(EditContext context, ToolStageResult problems)
        {
            AuthoringTypeInfo? info = context.Target == null ? null : context.Identity.Describe(context.Target);
            if (info == null)
            {
                problems.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'" + context.Operation.Tool + "' needs an authorable target (a type with [Authorable])."));
                return null;
            }

            string? field = context.StringArg("field");
            AuthorMemberInfo? member = field == null ? null : info.FindMember(field);
            if (member == null || !member.IsReference)
            {
                problems.Add(context.Problem(DiagnosticCodes.InvalidArgs, "Type '" + info.TypeId + "' has no [AuthorRef] field '" + (field ?? string.Empty) + "'."));
                return null;
            }

            return member;
        }

        internal static AuthorMemberInfo? ReferenceMember(EditContext context, ToolStageResult problems) => Member(context, problems);
    }

    /// <summary><c>bind</c>: import a retained artifact as an asset (verified) and assign it to an [AuthorRef] field.</summary>
    internal sealed class BindTool : BuiltInTool
    {
        public BindTool()
            : base(Entry_(
                BuiltInToolIdsExt.Bind,
                ToolTier.Configure,
                RuntimeApply.Live,
                true,
                "Import a retained artifact (verified by sha256) at 'path' and assign the imported asset to an [AuthorRef] field.",
                AuthoredKinds,
                Arg("field", ValueTypes.String, true, "The [AuthorRef] field receiving the asset."),
                Arg("artifact", ValueTypes.Artifact, true, "The retained artifact: { \"artifact\": \"sha256:...\" }."),
                Arg("path", ValueTypes.String, true, "Destination asset path under Assets/."),
                Arg("importer", ValueTypes.Object, false, "Importer settings (public importer properties by name, e.g. { \"textureType\": \"Sprite\" }).")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            AssignTool.ReferenceMember(context, result);
            string? digest = ArtifactDigest(context.Arg("artifact"));
            if (digest == null)
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'artifact' must be { \"artifact\": \"sha256:...\" }."));
            }
            else if (!context.Artifacts.Has(digest))
            {
                result.Add(context.Problem(DiagnosticCodes.StageFailed, "Artifact sha256:" + digest + " is not retained in Studio/Artifacts.", "Fetch the artifact through the companion first."));
            }

            if (!ToolSupport.IsSafeAssetPath(context.StringArg("path")))
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'path' must be a path under Assets/ without '..'."));
            }

            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            ToolStageResult problems = new ToolStageResult();
            AuthorMemberInfo? member = AssignTool.ReferenceMember(context, problems);
            string? digest = ArtifactDigest(context.Arg("artifact"));
            string? path = context.StringArg("path");
            if (member == null || digest == null || path == null)
            {
                return OperationResult.Refused(DiagnosticCodes.InvalidArgs, problems.Ok ? "'bind' needs field, artifact and path." : problems.Diagnostics[0].Message);
            }

            if (!AssetImporting.Import(context, digest, path, context.Arg("importer") as JObject, out ImportOutcome? outcome, out string? problem))
            {
                return OperationResult.Failed(DiagnosticCodes.StageFailed, problem ?? "import failed");
            }

            UnityEngine.Object? asset = AssetDatabase.LoadAssetAtPath(path, member.ElementType) ?? AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null || !member.ElementType.IsInstanceOfType(asset))
            {
                return OperationResult.Failed(DiagnosticCodes.InvalidArgs, "The imported asset at " + path + " is not a " + member.ElementType.Name + ".")
                    .WithAssetLevelInverse(AssetImporting.InverseOf(outcome!));
            }

            OperationResult assigned = AssignTool.AssignReference(context, member, asset, false, null);
            if (assigned.Status != OutcomeStatus.Applied)
            {
                return assigned.WithAssetLevelInverse(AssetImporting.InverseOf(outcome!));
            }

            OperationResult result = OperationResult.Applied(new JObject { ["path"] = path, ["sha256"] = digest, ["created"] = outcome!.Created });
            foreach (Operation inverse in assigned.Inverse)
            {
                result.WithInverse(inverse);
            }

            result.WithAssetLevelInverse(AssetImporting.InverseOf(outcome));
            result.Touch(context.Target);
            result.Touch(asset);
            return result;
        }
    }
}
