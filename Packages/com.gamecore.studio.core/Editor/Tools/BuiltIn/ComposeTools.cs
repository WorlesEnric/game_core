// GameCore.Studio.Edit - Compose-tier built-ins (docs/studio/03-authoring-contracts.md s5): create, duplicate, delete,
// replace, move, place, addComponent, removeComponent, and the internal history.restoreObject.
//
// Identity (03 s1): create/place/duplicate/addComponent make new objects, so they mint fresh authoring ids (a redo
// reuses the ids recorded by the first apply). replace keeps the replaced entity's id, so references by id survive.
// Scene objects are created and destroyed through Undo; their journal inverse restores a deleted hierarchy from a
// retained prefab blob (ObjectBlob), which survives editor restarts. References from other objects to a deleted scene
// object are restored by Unity Undo within the session, and by authoring id otherwise.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Edit
{
    /// <summary>Captures a scene hierarchy as a retained prefab blob and restores it (journal inverse of delete/replace).</summary>
    internal static class ObjectBlob
    {
        public const string TempFolder = "Assets/GameCoreStudioTemp";

        /// <summary>The digest of a prefab blob of an unlinked copy of <paramref name="source"/>.</summary>
        public static string? Capture(EditContext context, GameObject source, out string? problem)
        {
            string? digest = null;
            string? failure = null;
            context.OutsideAssetEditing(() =>
            {
                ToolSupport.EnsureFolder(TempFolder);
                string path = TempFolder + "/" + Guid.NewGuid().ToString("N") + ".prefab";
                GameObject copy = UnityEngine.Object.Instantiate(source);
                copy.name = source.name;
                try
                {
                    PrefabUtility.SaveAsPrefabAsset(copy, path, out bool saved);
                    if (!saved)
                    {
                        failure = "could not save a restore copy of " + source.name;
                        return;
                    }

                    digest = context.Artifacts.Put(File.ReadAllBytes(context.Runtime.Paths.Absolute(path)), null);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(copy);
                    AssetDatabase.DeleteAsset(path);
                    DeleteTempFolderIfEmpty();
                }
            });
            problem = failure;
            return digest;
        }

        /// <summary>Instantiates a blob as an unpacked hierarchy (not yet registered with Undo).</summary>
        public static GameObject? Restore(EditContext context, string digest, out string? problem)
        {
            problem = null;
            byte[] bytes;
            try
            {
                bytes = context.Artifacts.Read(digest);
            }
            catch (ArtifactStoreException error)
            {
                problem = error.Message;
                return null;
            }

            GameObject? instance = null;
            string? failure = null;
            context.OutsideAssetEditing(() =>
            {
                ToolSupport.EnsureFolder(TempFolder);
                string path = TempFolder + "/" + Guid.NewGuid().ToString("N") + ".prefab";
                try
                {
                    File.WriteAllBytes(context.Runtime.Paths.Absolute(path), bytes);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    GameObject? prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        failure = "the restore blob did not import as a prefab";
                        return;
                    }

                    instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                    if (instance != null)
                    {
                        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    }
                }
                finally
                {
                    AssetDatabase.DeleteAsset(path);
                    DeleteTempFolderIfEmpty();
                }
            });
            problem = failure ?? (instance == null ? "the restore blob could not be instantiated" : null);
            return instance;
        }

        private static void DeleteTempFolderIfEmpty()
        {
            if (AssetDatabase.IsValidFolder(TempFolder) && AssetDatabase.FindAssets(string.Empty, new[] { TempFolder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }
    }

    /// <summary>Scene-object helpers shared by the compose tools.</summary>
    internal static class SceneTools
    {
        public static GameObject? GameObjectOf(UnityEngine.Object? target)
        {
            return target as GameObject ?? (target as Component)?.gameObject;
        }

        public static bool IsSceneObject(UnityEngine.Object? target)
        {
            GameObject? gameObject = GameObjectOf(target);
            return gameObject != null && !EditorUtility.IsPersistent(gameObject);
        }

        /// <summary>A stamp-free ref of a GameObject's logical owner (its authored component, else the GameObject).</summary>
        public static AuthoringRef? OwnerRef(EditContext context, GameObject? gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            MonoBehaviour? owner = context.Identity.FindAuthoredComponent(gameObject);
            return ToolSupport.RefOf(context, owner != null ? (UnityEngine.Object)owner : gameObject);
        }

        /// <summary>The ref of the object a <c>parent</c> argument names, resolved to a Transform.</summary>
        public static bool TryParent(EditContext context, string argName, out Transform? parent, out string? problem)
        {
            parent = null;
            problem = null;
            JToken? raw = context.Arg(argName);
            if (raw == null)
            {
                return true;
            }

            UnityEngine.Object? found = context.Codec.Refs.ReadRef(raw, typeof(GameObject), out problem);
            GameObject? gameObject = GameObjectOf(found);
            if (gameObject == null)
            {
                problem ??= "the parent does not resolve to a scene object";
                return false;
            }

            parent = gameObject.transform;
            return true;
        }

        public static JObject PlacementArgs(Transform transform)
        {
            return new JObject
            {
                ["localPosition"] = Vector(transform.localPosition),
                ["localRotation"] = new JArray(ValueCodec.Widen(transform.localRotation.x), ValueCodec.Widen(transform.localRotation.y), ValueCodec.Widen(transform.localRotation.z), ValueCodec.Widen(transform.localRotation.w)),
                ["localScale"] = Vector(transform.localScale),
                ["siblingIndex"] = transform.GetSiblingIndex(),
            };
        }

        public static JArray Vector(Vector3 value) => new JArray(ValueCodec.Widen(value.x), ValueCodec.Widen(value.y), ValueCodec.Widen(value.z));

        /// <summary>Restore-object arguments for a hierarchy that is about to be deleted.</summary>
        public static JObject RestoreArgs(EditContext context, GameObject gameObject, string blob)
        {
            JObject args = PlacementArgs(gameObject.transform);
            args["blob"] = new JObject { ["artifact"] = ContentStamp.Prefix + blob };
            args["name"] = gameObject.name;
            if (!string.IsNullOrEmpty(gameObject.scene.path))
            {
                args["scene"] = gameObject.scene.path;
            }

            AuthoringRef? parent = gameObject.transform.parent == null ? null : ToolSupport.RefOf(context, gameObject.transform.parent.gameObject);
            if (parent != null)
            {
                args["parent"] = StudioJson.ToToken(parent);
            }

            return args;
        }
    }

    /// <summary><c>create</c>: a new authored object (a ScriptableObject asset, or a GameObject with the component).</summary>
    internal sealed class CreateTool : BuiltInTool
    {
        public CreateTool()
            : base(Entry_(
                BuiltInToolIdsExt.Create,
                ToolTier.Compose,
                RuntimeApply.Rebuild,
                false,
                "Create a new authored object of an [Authorable] type: a definition asset (ScriptableObject types, at 'path') or a scene object carrying the component (MonoBehaviour types, under 'parent' or in the active scene). A fresh authoring id is minted. Creating a dialogue graph automatically enrolls it in the active region world's narrative content set.",
                null,
                Arg("type", ValueTypes.String, true, "The [Authorable] type id (or CLR type name)."),
                Arg("name", ValueTypes.String, false, "Object name."),
                Arg("path", ValueTypes.String, false, "Definitions: the .asset path or a folder under Assets/."),
                Arg("parent", ValueTypes.Ref, false, "Scene objects: the parent object."),
                Arg("position", ValueTypes.Vector3, false, "Scene objects: world position."),
                Arg("fields", ValueTypes.Object, false, "Initial authorable field values."),
                Arg("authoringId", ValueTypes.String, false, "Restore a specific authoring id (journal undo/redo).")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            AuthoringTypeInfo? info = Resolve(context, result);
            if (info == null)
            {
                return result;
            }

            ToolSupport.CheckFields(context, info, context.Arg("fields") as JObject, result);
            if (info.IsScriptableObject)
            {
                string? path = context.StringArg("path");
                if (path == null || !ToolSupport.IsSafeAssetPath(path))
                {
                    result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "Creating a definition needs 'path' (an .asset path or folder under Assets/)."));
                }
            }
            else if (!info.IsComponent)
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "Type '" + info.TypeId + "' is neither a ScriptableObject nor a component."));
            }
            else if (!SceneTools.TryParent(context, "parent", out _, out string? problem))
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'parent': " + problem + "."));
            }

            result.Preview = new JObject { ["type"] = info.TypeId, ["name"] = context.StringArg("name") ?? info.TypeId };
            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            ToolStageResult problems = new ToolStageResult();
            AuthoringTypeInfo? info = Resolve(context, problems);
            if (info == null)
            {
                return OperationResult.Refused(problems.Diagnostics[0].Code, problems.Diagnostics[0].Message);
            }

            string id = context.ReplayOrArg("authoringId") ?? AuthoringIdentity.NewAuthoringId();
            string name = context.StringArg("name") ?? info.Type.Name;
            if (info.IsScriptableObject)
            {
                string path = context.ReplayOrArg("assetPath") ?? PathFor(context.StringArg("path")!, name);
                ScriptableObject createdAsset = ScriptableObject.CreateInstance(info.Type);
                createdAsset.name = Path.GetFileNameWithoutExtension(path);
                ToolSupport.MintAuthoringId(createdAsset, info, id, false);
                string? fieldProblem = ToolSupport.ApplyFields(context, createdAsset, info, context.Arg("fields") as JObject, false);
                if (fieldProblem != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdAsset);
                    return OperationResult.Failed(DiagnosticCodes.InvalidArgs, fieldProblem);
                }

                context.PrepareInverse(new[] { ToolSupport.InverseOp(BuiltInToolIdsExt.DeleteAsset, null, new JObject { ["path"] = path }) }, true);
                context.OutsideAssetEditing(() =>
                {
                    string? folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
                    if (!string.IsNullOrEmpty(folder))
                    {
                        ToolSupport.EnsureFolder(folder!);
                    }

                    AssetDatabase.CreateAsset(createdAsset, path);
                    AssetDatabase.SaveAssetIfDirty(createdAsset);
                });
                AuthoringRef? reference = ToolSupport.RefOf(context, createdAsset);
                return OperationResult.Applied(new JObject { ["ref"] = ToolSupport.RefJson(reference), ["path"] = path })
                    .WithAssetLevelInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.DeleteAsset, null, new JObject { ["path"] = path }))
                    .WithReplay(new JObject { ["authoringId"] = id, ["assetPath"] = path })
                    .Touch(createdAsset);
            }

            if (!SceneTools.TryParent(context, "parent", out Transform? parent, out string? problem))
            {
                return OperationResult.Failed(DiagnosticCodes.InvalidArgs, "'parent': " + problem);
            }

            GameObject gameObject = new GameObject(name);
            if (parent != null)
            {
                SceneManager.MoveGameObjectToScene(gameObject, parent.gameObject.scene);
                gameObject.transform.SetParent(parent, false);
            }

            if (context.TryArg("position", out Vector3 position, out _))
            {
                gameObject.transform.position = position;
            }

            Component component = gameObject.AddComponent(info.Type);
            ToolSupport.MintAuthoringId(component, info, id, false);
            string? componentProblem = ToolSupport.ApplyFields(context, component, info, context.Arg("fields") as JObject, false);
            if (componentProblem != null)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                return OperationResult.Failed(DiagnosticCodes.InvalidArgs, componentProblem);
            }

            context.RegisterCreated(gameObject);
            AuthoringRef? created = ToolSupport.RefOf(context, component);
            OperationResult result = OperationResult.Applied(new JObject { ["ref"] = ToolSupport.RefJson(created) })
                .WithReplay(new JObject { ["authoringId"] = id })
                .Touch(component);
            if (created != null)
            {
                result.WithInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.Delete, created, null));
            }

            return result;
        }

        private static AuthoringTypeInfo? Resolve(EditContext context, ToolStageResult problems)
        {
            string? typeName = context.StringArg("type");
            Type? type = typeName == null ? null : context.Types.ResolveType(typeName);
            AuthoringTypeInfo? info = type == null ? null : context.Identity.Describe(type);
            if (info == null || type!.IsAbstract)
            {
                problems.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'" + (typeName ?? string.Empty) + "' is not a concrete [Authorable] type.", "Use an object type id listed in the tool catalog."));
                return null;
            }

            return info;
        }

        private static string PathFor(string path, string name)
        {
            string candidate = path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ? path : path.TrimEnd('/') + "/" + name + ".asset";
            return AssetDatabase.GenerateUniqueAssetPath(candidate);
        }
    }

    /// <summary><c>duplicate</c>: copy a definition asset or a scene object; the copy gets fresh authoring ids.</summary>
    internal sealed class DuplicateTool : BuiltInTool
    {
        public DuplicateTool()
            : base(Entry_(
                BuiltInToolIdsExt.Duplicate,
                ToolTier.Compose,
                RuntimeApply.Rebuild,
                true,
                "Duplicate a definition asset or a scene object. The copy is a new object with fresh authoring ids.",
                AuthoredKinds,
                Arg("name", ValueTypes.String, false, "Name of the copy."),
                Arg("offset", ValueTypes.Vector3, false, "Scene objects: offset from the original's position."),
                Arg("path", ValueTypes.String, false, "Assets: destination path (default: next to the original).")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            UnityEngine.Object? target = context.Target;
            if (target == null)
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'duplicate' needs a target."));
            }
            else if (EditorUtility.IsPersistent(target) && !(target is ScriptableObject))
            {
                result.Add(context.Problem(DiagnosticCodes.ScopeNotAllowed, "Only definition assets and scene objects can be duplicated."));
            }

            string? path = context.StringArg("path");
            if (path != null && !ToolSupport.IsSafeAssetPath(path))
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'path' must be a path under Assets/."));
            }

            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            UnityEngine.Object target = context.Target!;
            if (target is ScriptableObject asset && EditorUtility.IsPersistent(asset))
            {
                string source = AssetDatabase.GetAssetPath(asset);
                string name = context.StringArg("name") ?? asset.name + " Copy";
                string destination = context.ReplayOrArg("assetPath")
                    ?? AssetDatabase.GenerateUniqueAssetPath(context.StringArg("path") ?? (Path.GetDirectoryName(source)!.Replace('\\', '/') + "/" + name + ".asset"));
                context.PrepareInverse(new[] { ToolSupport.InverseOp(BuiltInToolIdsExt.DeleteAsset, null, new JObject { ["path"] = destination }) }, true);
                bool copied = false;
                context.OutsideAssetEditing(() => copied = AssetDatabase.CopyAsset(source, destination));
                ScriptableObject? copy = copied ? AssetDatabase.LoadAssetAtPath<ScriptableObject>(destination) : null;
                if (copy == null)
                {
                    return OperationResult.Failed(DiagnosticCodes.Refused, "AssetDatabase could not copy " + source + " to " + destination + ".");
                }

                AuthoringTypeInfo? info = context.Identity.Describe(copy);
                string id = context.ReplayOrArg("authoringId") ?? AuthoringIdentity.NewAuthoringId();
                if (info != null)
                {
                    ToolSupport.MintAuthoringId(copy, info, id, false);
                    context.OutsideAssetEditing(() => AssetDatabase.SaveAssetIfDirty(copy));
                }

                return OperationResult.Applied(new JObject { ["ref"] = ToolSupport.RefJson(ToolSupport.RefOf(context, copy)), ["path"] = destination })
                    .WithAssetLevelInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.DeleteAsset, null, new JObject { ["path"] = destination }))
                    .WithReplay(new JObject { ["authoringId"] = id, ["assetPath"] = destination })
                    .Touch(copy);
            }

            GameObject original = SceneTools.GameObjectOf(target)!;
            GameObject duplicate;
            if (PrefabUtility.IsOutermostPrefabInstanceRoot(original))
            {
                GameObject? prefab = PrefabUtility.GetCorrespondingObjectFromSource(original);
                duplicate = (prefab == null ? null : PrefabUtility.InstantiatePrefab(prefab, original.transform.parent) as GameObject)
                    ?? UnityEngine.Object.Instantiate(original, original.transform.parent);
                PropertyModification[]? modifications = PrefabUtility.GetPropertyModifications(original);
                if (prefab != null && modifications != null)
                {
                    PrefabUtility.SetPropertyModifications(duplicate, modifications);
                }
            }
            else
            {
                duplicate = UnityEngine.Object.Instantiate(original, original.transform.parent);
            }

            if (original.transform.parent == null && duplicate.scene != original.scene)
            {
                SceneManager.MoveGameObjectToScene(duplicate, original.scene);
            }

            duplicate.name = context.StringArg("name") ?? original.name;
            duplicate.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
            duplicate.transform.localScale = original.transform.localScale;
            duplicate.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
            if (context.TryArg("offset", out Vector3 offset, out _))
            {
                duplicate.transform.position += offset;
            }

            context.RegisterCreated(duplicate);
            JArray ids = ToolSupport.MintHierarchyIds(context, duplicate, context.Replay?["ids"] as JArray);
            AuthoringRef? created = SceneTools.OwnerRef(context, duplicate);
            OperationResult result = OperationResult.Applied(new JObject { ["ref"] = ToolSupport.RefJson(created) })
                .WithReplay(new JObject { ["ids"] = ids })
                .Touch(duplicate);
            if (created != null)
            {
                result.WithInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.Delete, created, null));
            }

            return result;
        }
    }

    /// <summary><c>delete</c>: delete a scene object (its whole GameObject) or a definition asset; the stage lists the impact.</summary>
    internal sealed class DeleteTool : BuiltInTool
    {
        public DeleteTool()
            : base(Entry_(
                BuiltInToolIdsExt.Delete,
                ToolTier.Compose,
                RuntimeApply.Rebuild,
                true,
                "Delete a scene object (its GameObject and children) or an asset. Staging lists everything that references the target (query.impact).",
                null,
                Arg("componentOnly", ValueTypes.Bool, false, "Scene objects: remove only the authored component, not the GameObject.")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            UnityEngine.Object? target = context.Target;
            if (target == null)
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'delete' needs a target."));
                return result;
            }

            GameObject? gameObject = SceneTools.GameObjectOf(target);
            if (EditorUtility.IsPersistent(target))
            {
                if (!AssetDatabase.IsMainAsset(target) && gameObject == null)
                {
                    result.Add(context.Problem(DiagnosticCodes.ScopeNotAllowed, "Only whole assets can be deleted."));
                }
                else if (gameObject != null && gameObject.transform.parent != null)
                {
                    result.Add(context.Problem(DiagnosticCodes.ScopeNotAllowed, "Objects inside a prefab asset are deleted in the prefab (open it, or edit at Prefab scope)."));
                }
            }
            else if (gameObject != null && PrefabUtility.IsPartOfPrefabInstance(gameObject) && !PrefabUtility.IsOutermostPrefabInstanceRoot(gameObject) && !context.BoolArg("componentOnly"))
            {
                result.Add(context.Problem(DiagnosticCodes.ScopeNotAllowed, "A child of a prefab instance cannot be deleted from the scene.", "Edit the prefab (scope Prefab) or unpack the instance."));
            }

            AuthoringRef? reference = context.Operation.Target;
            if (reference != null)
            {
                ImpactReport impact = context.Index.ImpactOf(reference);
                JArray items = new JArray();
                foreach (ImpactItem item in impact.Items)
                {
                    JObject entry = new JObject { ["ref"] = StudioJson.ToToken(item.Ref), ["relation"] = item.Relation, ["depth"] = item.Depth };
                    if (item.Field != null)
                    {
                        entry["field"] = item.Field;
                    }

                    items.Add(entry);
                }

                result.Preview = new JObject { ["impact"] = items };
            }

            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            UnityEngine.Object target = context.Target!;
            if (EditorUtility.IsPersistent(target))
            {
                string path = AssetDatabase.GetAssetPath(target);
                ToolSupport.RetainAssetFiles(context, path, out string fileSha, out string? metaSha);
                context.PrepareInverse(AssetImporting.InverseOf(new ImportOutcome(path, fileSha, false, true, fileSha, metaSha)), true);
                bool deleted = false;
                context.OutsideAssetEditing(() => deleted = AssetDatabase.DeleteAsset(path));
                if (!deleted)
                {
                    return OperationResult.Failed(DiagnosticCodes.Refused, "AssetDatabase refused to delete " + path + ".");
                }

                JObject args = new JObject { ["path"] = path, ["artifact"] = ArtifactArg(fileSha) };
                if (metaSha != null)
                {
                    args["meta"] = ArtifactArg(metaSha);
                }

                return OperationResult.Applied(new JObject { ["path"] = path })
                    .WithAssetLevelInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.RestoreAsset, null, args));
            }

            if (context.BoolArg("componentOnly") && target is Component component)
            {
                return RemoveComponentTool.Remove(context, component);
            }

            GameObject gameObject = SceneTools.GameObjectOf(target)!;
            string? blob = ObjectBlob.Capture(context, gameObject, out string? problem);
            if (blob == null)
            {
                return OperationResult.Failed(DiagnosticCodes.Refused, problem ?? "could not retain a restore copy");
            }

            JObject restore = SceneTools.RestoreArgs(context, gameObject, blob);
            context.PrepareInverse(new[] { ToolSupport.InverseOp(BuiltInToolIdsExt.RestoreObject, null, restore) });
            Undo.DestroyObjectImmediate(gameObject);
            return OperationResult.Applied().WithInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.RestoreObject, null, restore));
        }
    }

    /// <summary>Internal: restores a deleted hierarchy from its retained blob.</summary>
    internal sealed class RestoreObjectTool : BuiltInTool
    {
        public RestoreObjectTool()
            : base(Entry_(
                BuiltInToolIdsExt.RestoreObject,
                ToolTier.Compose,
                RuntimeApply.Rebuild,
                false,
                "Internal journal inverse: restore a deleted scene hierarchy from its retained blob.",
                null,
                Arg("blob", ValueTypes.Artifact, true, "The retained prefab blob."),
                Arg("name", ValueTypes.String, false, "Object name."),
                Arg("scene", ValueTypes.String, false, "Scene path."),
                Arg("parent", ValueTypes.Ref, false, "Parent object."),
                Arg("localPosition", ValueTypes.Vector3, false, "Local position."),
                Arg("localRotation", ValueTypes.Quaternion, false, "Local rotation."),
                Arg("localScale", ValueTypes.Vector3, false, "Local scale."),
                Arg("siblingIndex", ValueTypes.Int, false, "Sibling index.")))
        {
        }

        public override bool Internal => true;

        public override OperationResult Apply(EditContext context)
        {
            string? digest = ArtifactDigest(context.Arg("blob"));
            if (digest == null)
            {
                return OperationResult.Refused(DiagnosticCodes.InvalidArgs, "history.restoreObject needs 'blob'.");
            }

            GameObject? restored = ObjectBlob.Restore(context, digest, out string? problem);
            if (restored == null)
            {
                return OperationResult.Failed(DiagnosticCodes.Refused, problem ?? "restore failed");
            }

            string? scenePath = context.StringArg("scene");
            if (scenePath != null)
            {
                Scene scene = SceneManager.GetSceneByPath(scenePath);
                if (scene.IsValid() && scene.isLoaded && restored.scene != scene)
                {
                    SceneManager.MoveGameObjectToScene(restored, scene);
                }
            }

            if (SceneTools.TryParent(context, "parent", out Transform? parent, out _) && parent != null)
            {
                restored.transform.SetParent(parent, false);
            }

            restored.name = context.StringArg("name") ?? restored.name;
            if (context.TryArg("localPosition", out Vector3 position, out _))
            {
                restored.transform.localPosition = position;
            }

            if (context.TryArg("localRotation", out Quaternion rotation, out _))
            {
                restored.transform.localRotation = rotation;
            }

            if (context.TryArg("localScale", out Vector3 scale, out _))
            {
                restored.transform.localScale = scale;
            }

            if (context.TryArg("siblingIndex", out int sibling, out _))
            {
                restored.transform.SetSiblingIndex(sibling);
            }

            context.RegisterCreated(restored);
            AuthoringRef? reference = SceneTools.OwnerRef(context, restored);
            OperationResult result = OperationResult.Applied(new JObject { ["ref"] = ToolSupport.RefJson(reference) }).Touch(restored);
            if (reference != null)
            {
                result.WithInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.Delete, reference, null));
            }

            return result;
        }
    }

    /// <summary><c>replace</c>: swap a scene object for an instance of a prefab at the same place; the authoring id is kept.</summary>
    internal sealed class ReplaceTool : BuiltInTool
    {
        public ReplaceTool()
            : base(Entry_(
                BuiltInToolIdsExt.Replace,
                ToolTier.Compose,
                RuntimeApply.Rebuild,
                true,
                "Replace a scene object with an instance of a prefab at the same parent, sibling index and placement. The replaced entity's authoring id moves to the new instance.",
                SceneKinds,
                Arg("with", ValueTypes.Ref, true, "The prefab asset to instantiate.")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            if (!SceneTools.IsSceneObject(context.Target))
            {
                result.Add(context.Problem(DiagnosticCodes.ScopeNotAllowed, "'replace' works on scene objects."));
            }

            JToken? with = context.Arg("with");
            GameObject? prefab = with == null ? null : context.Codec.Refs.ReadRef(with, typeof(GameObject), out _) as GameObject;
            if (prefab == null || !EditorUtility.IsPersistent(prefab))
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'with' must reference a prefab asset."));
            }

            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            GameObject? original = SceneTools.GameObjectOf(context.Target);
            JToken? with = context.Arg("with");
            GameObject? prefab = with == null ? null : context.Codec.Refs.ReadRef(with, typeof(GameObject), out _) as GameObject;
            if (original == null || prefab == null)
            {
                return OperationResult.Refused(DiagnosticCodes.InvalidArgs, "'replace' needs a scene target and a prefab.");
            }

            string? blob = ObjectBlob.Capture(context, original, out string? problem);
            if (blob == null)
            {
                return OperationResult.Failed(DiagnosticCodes.Refused, problem ?? "could not retain a restore copy");
            }

            JObject restore = SceneTools.RestoreArgs(context, original, blob);
            GameObject? instance = PrefabUtility.InstantiatePrefab(prefab, original.transform.parent) as GameObject;
            if (instance == null)
            {
                return OperationResult.Failed(DiagnosticCodes.Refused, "could not instantiate " + prefab.name);
            }

            if (original.transform.parent == null && instance.scene != original.scene)
            {
                SceneManager.MoveGameObjectToScene(instance, original.scene);
            }

            instance.name = original.name;
            instance.transform.localPosition = original.transform.localPosition;
            instance.transform.localRotation = original.transform.localRotation;
            instance.transform.localScale = original.transform.localScale;
            instance.transform.SetSiblingIndex(original.transform.GetSiblingIndex());
            MonoBehaviour? oldOwner = context.Identity.FindAuthoredComponent(original);
            MonoBehaviour? newOwner = context.Identity.FindAuthoredComponent(instance);
            string? keptId = oldOwner == null ? null : context.Identity.GetAuthoringId(oldOwner);
            if (keptId != null && newOwner != null)
            {
                AuthoringTypeInfo? info = context.Identity.Describe(newOwner);
                if (info != null)
                {
                    ToolSupport.MintAuthoringId(newOwner, info, keptId, false);
                }
            }

            context.RegisterCreated(instance);
            Undo.DestroyObjectImmediate(original);
            AuthoringRef? created = SceneTools.OwnerRef(context, instance);
            OperationResult result = OperationResult.Applied(new JObject { ["ref"] = ToolSupport.RefJson(created) }).Touch(instance);
            if (created != null)
            {
                result.WithInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.Delete, created, null));
            }

            return result.WithInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.RestoreObject, null, restore));
        }
    }

    /// <summary><c>move</c>: set the world position/rotation, local scale, parent or sibling index of a scene object.</summary>
    internal sealed class MoveTool : BuiltInTool, IReplannableTool
    {
        public MoveTool()
            : base(Entry_(
                BuiltInToolIdsExt.Move,
                ToolTier.Compose,
                RuntimeApply.Rebuild,
                true,
                "Move a scene object: world position, world rotation (quaternion), local scale, parent and sibling index (any subset). Gizmo drags and typed values produce the same operation.",
                SceneKinds,
                Arg("position", ValueTypes.Vector3, false, "World position (metres)."),
                Arg("rotation", ValueTypes.Quaternion, false, "World rotation [x, y, z, w]."),
                Arg("scale", ValueTypes.Vector3, false, "Local scale."),
                Arg("parent", ValueTypes.Ref, false, "New parent (keeps the world position unless 'position' is given)."),
                Arg("unparent", ValueTypes.Bool, false, "Move to the scene root."),
                Arg("siblingIndex", ValueTypes.Int, false, "Sibling index under the parent.")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            GameObject? gameObject = SceneTools.GameObjectOf(context.Target);
            if (gameObject == null || EditorUtility.IsPersistent(gameObject))
            {
                result.Add(context.Problem(DiagnosticCodes.ScopeNotAllowed, "'move' works on scene objects."));
                return result;
            }

            bool any = false;
            foreach (string name in new[] { "position", "rotation", "scale", "parent", "unparent", "siblingIndex" })
            {
                any |= context.Arg(name) != null;
            }

            if (!any)
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'move' needs at least one of position, rotation, scale, parent, unparent, siblingIndex."));
            }

            if (!SceneTools.TryParent(context, "parent", out _, out string? problem))
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'parent': " + problem + "."));
            }

            JObject preview = new JObject { ["before"] = SceneTools.Vector(gameObject.transform.position) };
            if (context.TryArg("position", out Vector3 position, out _))
            {
                preview["after"] = SceneTools.Vector(position);
                GameObject? ghost = context.Staging.Ghost(gameObject, position, context.TryArg("rotation", out Quaternion rotation, out _) ? rotation : gameObject.transform.rotation);
                if (ghost != null)
                {
                    result.AddPreviewObject(ghost);
                }
            }

            result.Preview = preview;
            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            GameObject? gameObject = SceneTools.GameObjectOf(context.Target);
            if (gameObject == null)
            {
                return OperationResult.Refused(DiagnosticCodes.InvalidArgs, "'move' needs a scene target.");
            }

            Transform transform = gameObject.transform;
            AuthoringRef? preparedRef = SceneTools.OwnerRef(context, gameObject);
            Quaternion beforeRotation = transform.rotation;
            JObject preparedPose = new JObject
            {
                ["position"] = SceneTools.Vector(transform.position),
                ["rotation"] = new JArray(ValueCodec.Widen(beforeRotation.x), ValueCodec.Widen(beforeRotation.y), ValueCodec.Widen(beforeRotation.z), ValueCodec.Widen(beforeRotation.w)),
                ["scale"] = SceneTools.Vector(transform.localScale),
                ["siblingIndex"] = transform.GetSiblingIndex(),
            };
            AuthoringRef? preparedParent = SceneTools.OwnerRef(context, transform.parent == null ? null : transform.parent.gameObject);
            if (preparedParent == null) preparedPose["unparent"] = true;
            else preparedPose["parent"] = StudioJson.ToToken(preparedParent);
            if (preparedRef != null) context.PrepareInverse(new[] { ToolSupport.InverseOp(BuiltInToolIdsExt.Move, preparedRef, preparedPose) });
            JObject inverse = new JObject();
            if (context.Arg("parent") != null || context.BoolArg("unparent"))
            {
                Transform? parent = null;
                if (!context.BoolArg("unparent") && !SceneTools.TryParent(context, "parent", out parent, out string? problem))
                {
                    return OperationResult.Failed(DiagnosticCodes.InvalidArgs, "'parent': " + problem);
                }

                AuthoringRef? previous = transform.parent == null ? null : ToolSupport.RefOf(context, transform.parent.gameObject);
                if (previous != null)
                {
                    inverse["parent"] = StudioJson.ToToken(previous);
                }
                else
                {
                    inverse["unparent"] = true;
                }

                inverse["position"] = SceneTools.Vector(transform.position);
                inverse["siblingIndex"] = transform.GetSiblingIndex();
                Undo.SetTransformParent(transform, parent, context.UndoLabel);
            }

            context.RecordUndo(transform);
            if (context.TryArg("position", out Vector3 position, out _))
            {
                inverse["position"] = SceneTools.Vector(transform.position);
                transform.position = position;
            }

            if (context.TryArg("rotation", out Quaternion rotation, out _))
            {
                Quaternion before = transform.rotation;
                inverse["rotation"] = new JArray(ValueCodec.Widen(before.x), ValueCodec.Widen(before.y), ValueCodec.Widen(before.z), ValueCodec.Widen(before.w));
                transform.rotation = rotation;
            }

            if (context.TryArg("scale", out Vector3 scale, out _))
            {
                inverse["scale"] = SceneTools.Vector(transform.localScale);
                transform.localScale = scale;
            }

            if (context.TryArg("siblingIndex", out int sibling, out _))
            {
                inverse["siblingIndex"] = transform.GetSiblingIndex();
                transform.SetSiblingIndex(sibling);
            }

            EditorUtility.SetDirty(transform);
            AuthoringRef? reference = SceneTools.OwnerRef(context, gameObject);
            OperationResult result = OperationResult.Applied().Touch(context.Target);
            if (reference != null)
            {
                result.WithInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.Move, reference, inverse));
            }

            return result;
        }

        public Operation? Replan(EditContext context, string? currentStamp, out Diagnostic? problem)
        {
            problem = null;
            Operation operation = context.Operation;
            return new Operation(operation.OpId, operation.Tool, operation.Target?.WithStamp(currentStamp), operation.Args, operation.DependsOn, operation.Preconditions, operation.ApplyRequirement);
        }
    }

    /// <summary><c>place</c>: instantiate a prefab at a sampled location (or position); fresh authoring ids.</summary>
    internal sealed class PlaceTool : BuiltInTool
    {
        public PlaceTool()
            : base(Entry_(
                BuiltInToolIdsExt.Place,
                ToolTier.Compose,
                RuntimeApply.Rebuild,
                false,
                "Place an instance of a prefab at a location (a Location ref from PointAt) or a position. Authored components of the instance get fresh authoring ids.",
                null,
                Arg("prefab", ValueTypes.Ref, true, "The prefab asset."),
                Arg("location", ValueTypes.Ref, false, "A Location ref (region + position)."),
                Arg("position", ValueTypes.Vector3, false, "World position (when no location is given)."),
                Arg("rotation", ValueTypes.Quaternion, false, "World rotation."),
                Arg("parent", ValueTypes.Ref, false, "Parent object."),
                Arg("name", ValueTypes.String, false, "Instance name.")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            JToken? prefabArg = context.Arg("prefab");
            GameObject? prefab = prefabArg == null ? null : context.Codec.Refs.ReadRef(prefabArg, typeof(GameObject), out _) as GameObject;
            if (prefab == null || !EditorUtility.IsPersistent(prefab))
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'prefab' must reference a prefab asset."));
            }

            if (!TryPosition(context, out Vector3 position, out string? problem))
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, problem ?? "'place' needs a location or a position."));
            }
            else if (prefab != null)
            {
                GameObject? ghost = context.Staging.Ghost(prefab, position, context.TryArg("rotation", out Quaternion rotation, out _) ? rotation : prefab.transform.rotation);
                if (ghost != null)
                {
                    result.AddPreviewObject(ghost);
                }

                result.Preview = new JObject { ["position"] = SceneTools.Vector(position), ["prefab"] = prefab.name };
            }

            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            JToken? prefabArg = context.Arg("prefab");
            GameObject? prefab = prefabArg == null ? null : context.Codec.Refs.ReadRef(prefabArg, typeof(GameObject), out _) as GameObject;
            if (prefab == null || !TryPosition(context, out Vector3 position, out string? problem))
            {
                return OperationResult.Refused(DiagnosticCodes.InvalidArgs, "'place' needs a prefab and a location or position.");
            }

            if (!SceneTools.TryParent(context, "parent", out Transform? parent, out problem))
            {
                return OperationResult.Failed(DiagnosticCodes.InvalidArgs, "'parent': " + problem);
            }

            GameObject? instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
            {
                return OperationResult.Failed(DiagnosticCodes.Refused, "could not instantiate " + prefab.name);
            }

            instance.transform.position = position;
            if (context.TryArg("rotation", out Quaternion rotation, out _))
            {
                instance.transform.rotation = rotation;
            }

            if (context.StringArg("name") != null)
            {
                instance.name = context.StringArg("name")!;
            }

            context.RegisterCreated(instance);
            JArray ids = ToolSupport.MintHierarchyIds(context, instance, context.Replay?["ids"] as JArray);
            AuthoringRef? created = SceneTools.OwnerRef(context, instance);
            OperationResult result = OperationResult.Applied(new JObject { ["ref"] = ToolSupport.RefJson(created) })
                .WithReplay(new JObject { ["ids"] = ids })
                .Touch(instance);
            if (created != null)
            {
                result.WithInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.Delete, created, null));
            }

            return result;
        }

        private static bool TryPosition(EditContext context, out Vector3 position, out string? problem)
        {
            position = Vector3.zero;
            problem = null;
            JToken? location = context.Arg("location");
            if (location != null)
            {
                if (!context.Codec.TryToClr(location, typeof(AuthoringRef), out object? parsed, out problem) || !(parsed is AuthoringRef reference)
                    || reference.Kind != AuthoringKind.Location || reference.Location == null || reference.Location.Position.Count != 3)
                {
                    problem ??= "'location' must be a Location ref";
                    return false;
                }

                position = new Vector3((float)reference.Location.Position[0], (float)reference.Location.Position[1], (float)reference.Location.Position[2]);
                return true;
            }

            return context.TryArg("position", out position, out problem);
        }
    }

    /// <summary><c>addComponent</c>: add a component (an [Authorable] type or any component type) to a scene object.</summary>
    internal sealed class AddComponentTool : BuiltInTool
    {
        public AddComponentTool()
            : base(Entry_(
                BuiltInToolIdsExt.AddComponent,
                ToolTier.Compose,
                RuntimeApply.Rebuild,
                true,
                "Add a component to a scene object. An [Authorable] component gets a fresh authoring id and optional initial field values.",
                SceneKinds,
                Arg("type", ValueTypes.String, true, "[Authorable] type id or component type full name."),
                Arg("fields", ValueTypes.Object, false, "Initial authorable field values."),
                Arg("authoringId", ValueTypes.String, false, "Restore a specific authoring id (journal undo/redo).")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            if (!SceneTools.IsSceneObject(context.Target))
            {
                result.Add(context.Problem(DiagnosticCodes.ScopeNotAllowed, "'addComponent' works on scene objects."));
            }

            Type? type = ResolveType(context);
            if (type == null)
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'" + (context.StringArg("type") ?? string.Empty) + "' is not a component type."));
            }
            else
            {
                AuthoringTypeInfo? info = context.Identity.Describe(type);
                if (info != null)
                {
                    ToolSupport.CheckFields(context, info, context.Arg("fields") as JObject, result);
                }
            }

            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            GameObject? gameObject = SceneTools.GameObjectOf(context.Target);
            Type? type = ResolveType(context);
            if (gameObject == null || type == null)
            {
                return OperationResult.Refused(DiagnosticCodes.InvalidArgs, "'addComponent' needs a scene target and a component type.");
            }

            Component? component = Undo.AddComponent(gameObject, type);
            if (component == null)
            {
                return OperationResult.Failed(DiagnosticCodes.Refused, "Unity refused to add " + type.Name + " to " + gameObject.name + ".");
            }

            AuthoringTypeInfo? info = context.Identity.Describe(type);
            JObject replay = new JObject();
            if (info != null)
            {
                string id = context.ReplayOrArg("authoringId") ?? AuthoringIdentity.NewAuthoringId();
                ToolSupport.MintAuthoringId(component, info, id, true);
                replay["authoringId"] = id;
                string? problem = ToolSupport.ApplyFields(context, component, info, context.Arg("fields") as JObject, true);
                if (problem != null)
                {
                    return OperationResult.Failed(DiagnosticCodes.InvalidArgs, problem);
                }
            }

            AuthoringRef? created = ToolSupport.RefOf(context, component);
            OperationResult result = OperationResult.Applied(new JObject { ["ref"] = ToolSupport.RefJson(created) }).WithReplay(replay).Touch(component);
            if (created != null)
            {
                result.WithInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.RemoveComponent, created, null));
            }

            return result;
        }

        private static Type? ResolveType(EditContext context)
        {
            string? name = context.StringArg("type");
            Type? type = name == null ? null : context.Types.ResolveType(name);
            return type != null && typeof(Component).IsAssignableFrom(type) && !type.IsAbstract ? type : null;
        }
    }

    /// <summary><c>removeComponent</c>: remove a component; the inverse re-adds it with its fields and authoring id.</summary>
    internal sealed class RemoveComponentTool : BuiltInTool
    {
        public RemoveComponentTool()
            : base(Entry_(
                BuiltInToolIdsExt.RemoveComponent,
                ToolTier.Compose,
                RuntimeApply.Rebuild,
                true,
                "Remove a component from a scene object (the target component, or the component of 'type' on the target object).",
                SceneKinds,
                Arg("type", ValueTypes.String, false, "Component type (when the target is the GameObject).")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            if (Component(context) == null)
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "No removable component found on the target."));
            }

            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            Component? component = Component(context);
            if (component == null)
            {
                return OperationResult.Refused(DiagnosticCodes.InvalidArgs, "No removable component found on the target.");
            }

            return Remove(context, component);
        }

        internal static OperationResult Remove(EditContext context, Component component)
        {
            GameObject gameObject = component.gameObject;
            AuthoringTypeInfo? info = context.Identity.Describe(component);
            JObject args = new JObject { ["type"] = info != null ? info.TypeId : component.GetType().FullName };
            if (info != null)
            {
                args["fields"] = ToolSupport.CaptureMembers(context, component, info);
                string? id = info.ReadId(component);
                if (id != null)
                {
                    args["authoringId"] = id;
                }
            }

            AuthoringRef? owner = ToolSupport.RefOf(context, gameObject);
            if (owner != null) context.PrepareInverse(new[] { ToolSupport.InverseOp(BuiltInToolIdsExt.AddComponent, owner, args) });
            Undo.DestroyObjectImmediate(component);
            OperationResult result = OperationResult.Applied().Touch(gameObject);
            if (owner != null)
            {
                result.WithInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.AddComponent, owner, args));
            }

            return result;
        }

        private static Component? Component(EditContext context)
        {
            UnityEngine.Object? target = context.Target;
            string? typeName = context.StringArg("type");
            if (typeName != null)
            {
                GameObject? gameObject = SceneTools.GameObjectOf(target);
                Type? type = context.Types.ResolveType(typeName);
                Component? found = gameObject == null || type == null ? null : gameObject.GetComponent(type);
                return found is Transform ? null : found;
            }

            return target is Component component && !(component is Transform) && SceneTools.IsSceneObject(component) ? component : null;
        }
    }
}
