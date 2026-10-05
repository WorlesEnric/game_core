// GameCore.Studio.Edit - helpers shared by the built-in and reflected tools: inverse operations, member snapshots,
// authoring-id minting and asset folders.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    public static class ToolSupport
    {
        /// <summary>Placeholder op id of inverse operations; the engine renumbers them when it runs an undo.</summary>
        public const string InverseOpId = "inverse";

        /// <summary>An inverse operation (no stamp preconditions: undo checks recorded post-apply stamps instead).</summary>
        public static Operation InverseOp(string tool, AuthoringRef? target, JObject? args)
        {
            return new Operation(InverseOpId, tool, target == null ? null : SemanticIndexService.EdgeRef(target), args, null, Preconditions.None);
        }

        /// <summary>A stamp-free ref of <paramref name="target"/> for inverse operations and outputs.</summary>
        public static AuthoringRef? RefOf(EditContext context, UnityEngine.Object? target)
        {
            return target == null ? null : context.Resolver.BuildRef(target, null, false);
        }

        /// <summary>The JSON of a ref (or null).</summary>
        public static JToken RefJson(AuthoringRef? reference)
        {
            return reference == null ? JValue.CreateNull() : StudioJson.ToToken(reference);
        }

        /// <summary>Values of every authorable member of <paramref name="target"/>.</summary>
        public static JObject CaptureMembers(EditContext context, UnityEngine.Object target, AuthoringTypeInfo info)
        {
            JObject values = new JObject();
            foreach (AuthorMemberInfo member in info.Members)
            {
                if (member.IsSerializedField || (member.Member is System.Reflection.PropertyInfo property && property.CanWrite))
                {
                    values[member.Name] = context.Codec.FromClr(member.GetValue(target));
                }
            }

            return values;
        }

        /// <summary>The members whose value differs between two snapshots, with their <paramref name="before"/> values.</summary>
        public static JObject ChangedBefore(JObject before, JObject after)
        {
            JObject changed = new JObject();
            foreach (JProperty property in before.Properties())
            {
                JToken? now = after[property.Name];
                if (now == null || !JToken.DeepEquals(now, property.Value))
                {
                    changed[property.Name] = property.Value.DeepClone();
                }
            }

            return changed;
        }

        /// <summary>The inverse <c>set</c> operation restoring <paramref name="fields"/>.</summary>
        public static Operation SetFieldsInverse(AuthoringRef target, JObject fields)
        {
            return InverseOp(BuiltInToolIdsExt.Set, target, new JObject { ["fields"] = fields });
        }

        /// <summary>Writes the authoring id of a new object through its serialized <c>authoringId</c> field.</summary>
        public static bool MintAuthoringId(UnityEngine.Object target, AuthoringTypeInfo info, string authoringId, bool withUndo)
        {
            if (info.IdField == null)
            {
                return false;
            }

            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty? property = serialized.FindProperty(info.IdField.Name);
            if (property == null || property.propertyType != SerializedPropertyType.String)
            {
                return false;
            }

            property.stringValue = authoringId;
            if (withUndo)
            {
                serialized.ApplyModifiedProperties();
            }
            else
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return true;
        }

        /// <summary>Mints fresh ids for every authored component under <paramref name="root"/> (a copy is a new object, 03 s1).</summary>
        public static JArray MintHierarchyIds(EditContext context, GameObject root, JArray? replayIds)
        {
            JArray minted = new JArray();
            int index = 0;
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                {
                    continue;
                }

                AuthoringTypeInfo? info = context.Identity.Describe(behaviour.GetType());
                if (info == null || info.IdField == null)
                {
                    continue;
                }

                string id = replayIds != null && index < replayIds.Count && replayIds[index].Type == JTokenType.String
                    ? replayIds[index].Value<string>()!
                    : AuthoringIdentity.NewAuthoringId();
                MintAuthoringId(behaviour, info, id, true);
                minted.Add(id);
                index++;
            }

            return minted;
        }

        /// <summary>Applies a <c>fields</c> object to an authorable object (used by create/addComponent/place).</summary>
        public static string? ApplyFields(EditContext context, UnityEngine.Object target, AuthoringTypeInfo info, JObject? fields, bool withUndo)
        {
            if (fields == null || !fields.HasValues)
            {
                return null;
            }

            SerializedObject serialized = new SerializedObject(target);
            foreach (JProperty property in fields.Properties())
            {
                AuthorMemberInfo? member = info.FindMember(property.Name);
                if (member == null)
                {
                    return "type '" + info.TypeId + "' has no authorable field '" + property.Name + "'";
                }

                if (!context.Codec.WriteMember(target, member, property.Value, serialized, out string? problem))
                {
                    return property.Name + ": " + problem;
                }
            }

            if (withUndo)
            {
                serialized.ApplyModifiedProperties();
            }
            else
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return null;
        }

        /// <summary>Checks a <c>fields</c> object against the type's field specs (dry run).</summary>
        public static void CheckFields(EditContext context, AuthoringTypeInfo info, JObject? fields, ToolStageResult result)
        {
            if (fields == null)
            {
                return;
            }

            foreach (JProperty property in fields.Properties())
            {
                AuthorMemberInfo? member = info.FindMember(property.Name);
                if (member == null)
                {
                    result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "Type '" + info.TypeId + "' has no authorable field '" + property.Name + "'."));
                    continue;
                }

                foreach (string problem in FieldValueChecker.Check(member.Spec, property.Value))
                {
                    result.Add(context.Problem(DiagnosticCodes.InvalidArgs, problem + "."));
                }
            }
        }

        /// <summary>True for a project-relative path under Assets/ without parent segments.</summary>
        public static bool IsSafeAssetPath(string? path)
        {
            if (string.IsNullOrEmpty(path) || !path!.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return false;
            }

            foreach (string segment in path.Split('/'))
            {
                if (segment == ".." || segment == "." || segment.Length == 0)
                {
                    return false;
                }
            }

            return path.IndexOf('\\') < 0;
        }

        /// <summary>Creates every missing folder of <paramref name="folder"/> (an Assets/... path).</summary>
        public static void EnsureFolder(string folder)
        {
            folder = folder.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            int slash = folder.LastIndexOf('/');
            string parent = slash < 0 ? "Assets" : folder.Substring(0, slash);
            string name = slash < 0 ? folder : folder.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>Compact JSON of a token, shortened for an outcome detail.</summary>
        public static string Compact(JToken? token, int max = 2000)
        {
            if (token == null)
            {
                return string.Empty;
            }

            string text = token.ToString(Formatting.None);
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }

        /// <summary>The bytes of a file and its .meta retained in the artifact store; returns the two digests.</summary>
        public static void RetainAssetFiles(EditContext context, string assetPath, out string fileSha, out string? metaSha)
        {
            string full = context.Runtime.Paths.Absolute(assetPath);
            fileSha = context.Artifacts.Put(File.ReadAllBytes(full), null);
            string meta = full + ".meta";
            metaSha = File.Exists(meta) ? context.Artifacts.Put(File.ReadAllBytes(meta), null) : null;
        }
    }

    /// <summary>Ids of the generic built-ins that BuiltInToolIds (the 03 s5 list) does not name.</summary>
    public static class BuiltInToolIdsExt
    {
        public const string Set = "set";
        public const string Assign = "assign";
        public const string Bind = "bind";
        public const string Create = "create";
        public const string Duplicate = "duplicate";
        public const string Delete = "delete";
        public const string Replace = "replace";
        public const string Move = "move";
        public const string Place = "place";
        public const string AddComponent = "addComponent";
        public const string RemoveComponent = "removeComponent";

        /// <summary>Internal journal tools (not exported): restore or delete files and scene objects on undo.</summary>
        public const string RestoreAsset = "history.restoreAsset";
        public const string DeleteAsset = "history.deleteAsset";
        public const string RestoreObject = "history.restoreObject";

        public static readonly IReadOnlyList<string> Generic = new[]
        {
            Set, Assign, Bind, Create, Duplicate, Delete, Replace, Move, Place, AddComponent, RemoveComponent,
        };
    }
}
