// GameCore.Studio.Edit - asset built-ins (docs/studio/03-authoring-contracts.md s5, s6; 02 s5).
//   asset.import       verified import: the bytes (a retained artifact)
//                      are written under Assets/, imported with importer settings, and re-hashed on disk
//   asset.generate     opens an etos task through IAgentGateway (P2.2); NotConfigured until a gateway is registered
//   mechanism.propose  opens a mechanic task through IAgentGateway; never applied directly (staging lane)
//   history.restoreAsset / history.deleteAsset  internal inverses: put files (and .meta, so the GUID survives) back
//                      from the artifact store, or delete them after retaining their bytes
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    /// <summary>What a verified import did (for its inverse).</summary>
    internal sealed class ImportOutcome
    {
        public ImportOutcome(string path, string sha, bool created, bool changed, string? previousSha, string? previousMetaSha)
        {
            Path = path;
            Sha = sha;
            Created = created;
            Changed = changed;
            PreviousSha = previousSha;
            PreviousMetaSha = previousMetaSha;
        }

        public string Path { get; }

        public string Sha { get; }

        public bool Created { get; }

        public bool Changed { get; }

        public string? PreviousSha { get; }

        public string? PreviousMetaSha { get; }
    }

    /// <summary>Verified artifact import shared by <c>asset.import</c>, <c>bind</c> and redo.</summary>
    internal static class AssetImporting
    {
        public static bool Import(EditContext context, string digest, string path, JObject? importer, out ImportOutcome? outcome, out string? problem)
        {
            outcome = null;
            problem = null;
            if (MediaImportPolicy.Validate(context.Runtime.Paths, path, importer) is string refusal)
            {
                problem = refusal;
                return false;
            }

            byte[] bytes;
            try
            {
                bytes = context.Artifacts.Read(digest);
            }
            catch (ArtifactStoreException error)
            {
                problem = error.Message;
                return false;
            }

            string full = context.Runtime.Paths.Absolute(path);
            bool existed = File.Exists(full);
            string? previousSha = null;
            string? previousMeta = null;
            if (existed)
            {
                ToolSupport.RetainAssetFiles(context, path, out string fileSha, out previousMeta);
                previousSha = fileSha;
            }

            bool changed = !existed || !string.Equals(previousSha, digest, StringComparison.Ordinal) || (importer != null && importer.HasValues);
            outcome = new ImportOutcome(path, digest, !existed, changed, previousSha, previousMeta);
            context.PrepareInverse(InverseOf(outcome), true);
            string? importProblem = null;
            context.OutsideAssetEditing(() =>
            {
                string? directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(directory))
                {
                    ToolSupport.EnsureFolder(directory!);
                }

                if (changed)
                {
                    File.WriteAllBytes(full, bytes);
                    context.Runtime.Engine.Options.FaultHook?.Invoke(EngineFaultPoint.AfterFileWrite, context.Operation.OpId);
                }

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                if (importer != null && importer.HasValues)
                {
                    importProblem = ApplyImporterSettings(path, importer);
                }
            });
            if (importProblem != null)
            {
                problem = importProblem;
                return false;
            }

            string onDisk = ContentStamp.Sha256Hex(File.ReadAllBytes(full));
            if (!string.Equals(onDisk, digest, StringComparison.Ordinal))
            {
                problem = "verification failed: " + path + " hashes to " + onDisk + ", expected " + digest;
                return false;
            }

            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
            {
                problem = "Unity did not import " + path;
                return false;
            }

            outcome = new ImportOutcome(path, digest, !existed, changed, previousSha, previousMeta);
            return true;
        }

        /// <summary>The inverse of an import: delete a created file, or restore the bytes it replaced.</summary>
        public static Operation[] InverseOf(ImportOutcome outcome)
        {
            if (outcome.Created)
            {
                return new[] { ToolSupport.InverseOp(BuiltInToolIdsExt.DeleteAsset, null, new JObject { ["path"] = outcome.Path }) };
            }

            if (!outcome.Changed || outcome.PreviousSha == null)
            {
                return Array.Empty<Operation>();
            }

            JObject args = new JObject { ["path"] = outcome.Path, ["artifact"] = new JObject { ["artifact"] = ContentStamp.Prefix + outcome.PreviousSha } };
            if (outcome.PreviousMetaSha != null)
            {
                args["meta"] = new JObject { ["artifact"] = ContentStamp.Prefix + outcome.PreviousMetaSha };
            }

            return new[] { ToolSupport.InverseOp(BuiltInToolIdsExt.RestoreAsset, null, args) };
        }

        /// <summary>Sets public writable importer properties by name (enums by name), then reimports.</summary>
        public static string? ApplyImporterSettings(string path, JObject settings)
        {
            AssetImporter? importer = AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                return "no importer for " + path;
            }

            List<JProperty> ordered = new List<JProperty>(settings.Properties());
            ordered.Sort((left, right) => left.Name == right.Name ? 0 : left.Name == "textureType" ? -1
                : right.Name == "textureType" ? 1 : string.CompareOrdinal(left.Name, right.Name));
            foreach (JProperty setting in ordered)
            {
                PropertyInfo? property = importer.GetType().GetProperty(setting.Name, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanWrite)
                {
                    return importer.GetType().Name + " has no writable property '" + setting.Name + "'";
                }

                object? value;
                try
                {
                    if (importer is TextureImporter && setting.Name == "spriteImportMode")
                    {
                        if ((string?)setting.Value != "Single") return "spriteImportMode must be Single";
                        value = (int)SpriteImportMode.Single;
                    }
                    else if (property.PropertyType.IsEnum)
                    {
                        value = Enum.Parse(property.PropertyType, setting.Value.Value<string>() ?? string.Empty);
                    }
                    else
                    {
                        value = setting.Value.ToObject(property.PropertyType);
                    }
                }
                catch (Exception error) when (error is ArgumentException || error is FormatException || error is InvalidCastException || error is Newtonsoft.Json.JsonException)
                {
                    return "importer setting '" + setting.Name + "': " + error.Message;
                }

                property.SetValue(importer, value);
            }

            importer.SaveAndReimport();
            return null;
        }
    }

    /// <summary><c>asset.import</c>: verified import of a file into Assets/.</summary>
    internal sealed class AssetImportTool : BuiltInTool
    {
        public AssetImportTool()
            : base(Entry_(
                BuiltInToolIds.AssetImport,
                ToolTier.Compose,
                RuntimeApply.Live,
                false,
                "Verified import: write a retained media artifact to 'path' under Assets/, import it with importer settings and re-check its sha256 on disk. The bytes are retained in Studio/Artifacts.",
                null,
                Arg("path", ValueTypes.String, true, "Destination asset path under Assets/."),
                Arg("artifact", ValueTypes.Artifact, true, "A retained artifact: { \"artifact\": \"sha256:...\" }."),
                Arg("importer", ValueTypes.Object, false, "Importer settings (public importer properties by name).")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            string? problem = MediaImportPolicy.Validate(context.Runtime.Paths, context.StringArg("path"), context.Arg("importer") as JObject);
            if (problem != null) result.Add(context.Problem(MediaImportPolicy.Code(problem), problem));
            if (context.Arg("source") != null || context.Arg("sha256") != null)
                result.Add(context.Problem(DiagnosticCodes.ArtifactSourceForbidden, "artifact_source_forbidden: sources must be retained artifact digests."));
            string? digest = ArtifactDigest(context.Arg("artifact"));
            if (digest == null) result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "A retained artifact digest is required."));
            else if (!context.Artifacts.Has(digest)) result.Add(context.Problem(DiagnosticCodes.StageFailed, "Artifact is not retained."));
            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            string? path = context.StringArg("path");
            string? digest = ArtifactDigest(context.Arg("artifact"));
            ToolStageResult checks = Stage(context);
            if (!checks.Ok) return OperationResult.Refused(checks.Diagnostics[0].Code, checks.Diagnostics[0].Message);
            if (path == null || digest == null) return OperationResult.Refused(DiagnosticCodes.InvalidArgs, "path and artifact are required.");

            if (!AssetImporting.Import(context, digest, path, context.Arg("importer") as JObject, out ImportOutcome? outcome, out string? problem))
            {
                return OperationResult.Failed(DiagnosticCodes.StageFailed, problem ?? "import failed")
                    .WithAssetLevelInverse(outcome == null ? Array.Empty<Operation>() : AssetImporting.InverseOf(outcome));
            }

            UnityEngine.Object? asset = AssetDatabase.LoadMainAssetAtPath(path);
            AuthoringRef? reference = asset == null ? null : ToolSupport.RefOf(context, asset);
            return OperationResult.Applied(new JObject
                {
                    ["path"] = path,
                    ["sha256"] = digest,
                    ["created"] = outcome!.Created,
                    ["ref"] = ToolSupport.RefJson(reference),
                })
                .WithAssetLevelInverse(AssetImporting.InverseOf(outcome))
                .Touch(asset);
        }
    }

    /// <summary><c>asset.generate</c>: delegates to <see cref="IAgentGateway"/> (P2.2); returns the task, never an asset.</summary>
    internal sealed class AssetGenerateTool : BuiltInTool
    {
        public AssetGenerateTool()
            : base(Entry_(
                BuiltInToolIds.AssetGenerate,
                ToolTier.Compose,
                RuntimeApply.Live,
                false,
                "Generate an asset (image, texture, voice line, mesh) through an etos task. Returns the task; the artifact arrives later in a candidate change set.",
                null,
                Arg("kind", ValueTypes.String, true, "What to generate: image, texture, portrait, voice, mesh, ..."),
                Arg("prompt", ValueTypes.String, true, "The generation prompt."),
                Arg("options", ValueTypes.Object, false, "Provider/op options (size, voice, format).")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            IAgentGateway? gateway = context.Services.AgentGateway;
            if (gateway == null || !gateway.IsConfigured)
            {
                result.Add(context.Problem(DiagnosticCodes.NotConfigured, "No agent gateway is configured, so assets cannot be generated.", "Pair the Studio companion (com.gamecore.studio.etos)."));
            }

            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            IAgentGateway? gateway = context.Services.AgentGateway;
            if (gateway == null || !gateway.IsConfigured)
            {
                return OperationResult.Refused(DiagnosticCodes.NotConfigured, "No agent gateway is configured.");
            }

            AgentAssetRequest request = new AgentAssetRequest(
                context.ChangeSet.Id,
                context.Operation.OpId,
                context.StringArg("kind") ?? string.Empty,
                context.StringArg("prompt") ?? string.Empty,
                context.Operation.Target,
                context.Arg("options") as JObject);
            return ServiceTools.FromService(gateway.GenerateAsset(request));
        }
    }

    /// <summary><c>mechanism.propose</c>: a staged package request through <see cref="IAgentGateway"/>; never applied directly.</summary>
    internal sealed class MechanismProposeTool : BuiltInTool
    {
        public MechanismProposeTool()
            : base(Entry_(
                BuiltInToolIds.MechanismPropose,
                ToolTier.Mechanism,
                RuntimeApply.Compile,
                false,
                "Propose a new mechanism (a UPM package with rules, runtime and tests). Produces a staged package through the staging lane; only admission can install it.",
                null,
                Arg("description", ValueTypes.String, true, "What the mechanism should do."),
                Arg("context", ValueTypes.Ref + ValueTypes.ArraySuffix, false, "Authored things the mechanism relates to."),
                Arg("options", ValueTypes.Object, false, "Worker options."), Arg("package", ValueTypes.Artifact, false, "The staged package archive (worker candidates; read by the staging lane)."), Arg("proposal", ValueTypes.Artifact, false, "The proposal.json artifact (worker candidates)."), Arg("stageInputs", ValueTypes.String + ValueTypes.ArraySuffix, false, "Project paths copied into the stage slot (worker candidates; P2.4).")))
        {
        }

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            IAgentGateway? gateway = context.Services.AgentGateway;
            if (gateway == null || !gateway.IsConfigured)
            {
                result.Add(context.Problem(DiagnosticCodes.NotConfigured, "No agent gateway is configured, so mechanisms cannot be proposed.", "Pair the Studio companion (com.gamecore.studio.etos)."));
            }

            return result;
        }

        public override OperationResult Apply(EditContext context)
        {
            IAgentGateway? gateway = context.Services.AgentGateway;
            if (gateway == null || !gateway.IsConfigured)
            {
                return OperationResult.Refused(DiagnosticCodes.NotConfigured, "No agent gateway is configured.");
            }

            List<AuthoringRef> refs = new List<AuthoringRef>();
            if (context.Arg("context") is JArray items)
            {
                foreach (JToken item in items)
                {
                    if (item is JObject && context.Codec.TryToClr(item, typeof(AuthoringRef), out object? parsed, out _) && parsed is AuthoringRef reference)
                    {
                        refs.Add(reference);
                    }
                }
            }

            AgentMechanismRequest request = new AgentMechanismRequest(context.ChangeSet.Id, context.Operation.OpId, context.StringArg("description") ?? string.Empty, refs, context.Arg("options") as JObject);
            return ServiceTools.FromService(gateway.ProposeMechanism(request));
        }
    }

    /// <summary>Internal: writes retained bytes (and .meta) back to an asset path and imports it.</summary>
    internal sealed class RestoreAssetTool : BuiltInTool
    {
        public RestoreAssetTool()
            : base(Entry_(
                BuiltInToolIdsExt.RestoreAsset,
                ToolTier.Compose,
                RuntimeApply.Live,
                false,
                "Internal journal inverse: restore an asset file (and its .meta) from retained artifacts.",
                null,
                Arg("path", ValueTypes.String, true, "Asset path."),
                Arg("artifact", ValueTypes.Artifact, true, "The file bytes."),
                Arg("meta", ValueTypes.Artifact, false, "The .meta bytes.")))
        {
        }

        public override bool Internal => true;

        public override OperationResult Apply(EditContext context)
        {
            string? path = context.StringArg("path");
            string? digest = ArtifactDigest(context.Arg("artifact"));
            string? meta = ArtifactDigest(context.Arg("meta"));
            if (path == null || digest == null || !ToolSupport.IsSafeAssetPath(path))
            {
                return OperationResult.Refused(DiagnosticCodes.InvalidArgs, "history.restoreAsset needs a safe path and an artifact.");
            }

            byte[] bytes;
            byte[]? metaBytes = null;
            try
            {
                bytes = context.Artifacts.Read(digest);
                if (meta != null)
                {
                    metaBytes = context.Artifacts.Read(meta);
                }
            }
            catch (ArtifactStoreException error)
            {
                return OperationResult.Failed(DiagnosticCodes.StageFailed, error.Message);
            }

            string full = context.Runtime.Paths.Absolute(path);
            bool existed = File.Exists(full);
            string? replacedSha = null;
            string? replacedMeta = null;
            if (existed)
            {
                ToolSupport.RetainAssetFiles(context, path, out string fileSha, out replacedMeta);
                replacedSha = fileSha;
            }

            context.PrepareInverse(AssetImporting.InverseOf(new ImportOutcome(path, digest, !existed, true, replacedSha, replacedMeta)), true);
            context.OutsideAssetEditing(() =>
            {
                string? directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(directory))
                {
                    ToolSupport.EnsureFolder(directory!);
                }

                if (metaBytes != null)
                {
                    File.WriteAllBytes(full + ".meta", metaBytes);
                }

                File.WriteAllBytes(full, bytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            });
            UnityEngine.Object? asset = AssetDatabase.LoadMainAssetAtPath(path);
            OperationResult result = OperationResult.Applied(new JObject { ["path"] = path }).Touch(asset);
            if (existed && replacedSha != null)
            {
                JObject args = new JObject { ["path"] = path, ["artifact"] = ArtifactArg(replacedSha) };
                if (replacedMeta != null)
                {
                    args["meta"] = ArtifactArg(replacedMeta);
                }

                return result.WithAssetLevelInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.RestoreAsset, null, args));
            }

            return result.WithAssetLevelInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.DeleteAsset, null, new JObject { ["path"] = path }));
        }
    }

    /// <summary>Internal: deletes an asset after retaining its bytes (inverse: restore).</summary>
    internal sealed class DeleteAssetTool : BuiltInTool
    {
        public DeleteAssetTool()
            : base(Entry_(
                BuiltInToolIdsExt.DeleteAsset,
                ToolTier.Compose,
                RuntimeApply.Live,
                false,
                "Internal journal inverse: delete an asset file after retaining its bytes and .meta.",
                null,
                Arg("path", ValueTypes.String, true, "Asset path.")))
        {
        }

        public override bool Internal => true;

        public override OperationResult Apply(EditContext context)
        {
            string? path = context.StringArg("path");
            if (path == null || !ToolSupport.IsSafeAssetPath(path))
            {
                return OperationResult.Refused(DiagnosticCodes.InvalidArgs, "history.deleteAsset needs a safe path.");
            }

            string full = context.Runtime.Paths.Absolute(path);
            if (!File.Exists(full))
            {
                return OperationResult.Applied(new JObject { ["path"] = path, ["deleted"] = false });
            }

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

            return OperationResult.Applied(new JObject { ["path"] = path, ["deleted"] = true })
                .WithAssetLevelInverse(ToolSupport.InverseOp(BuiltInToolIdsExt.RestoreAsset, null, args));
        }
    }

    /// <summary>Maps service results to outcomes.</summary>
    internal static class ServiceTools
    {
        public static OperationResult FromService(ServiceResult result)
        {
            JObject output = result.Output != null ? (JObject)result.Output.DeepClone() : new JObject();
            output["status"] = result.Status.ToString();
            if (result.TaskId != null)
            {
                output["taskId"] = result.TaskId;
            }

            if (result.Artifact != null)
            {
                output["artifact"] = StudioJson.ToToken(result.Artifact);
            }

            switch (result.Status)
            {
                case ServiceRequestStatus.Accepted:
                case ServiceRequestStatus.Completed:
                    return OperationResult.Applied(output);
                case ServiceRequestStatus.NotConfigured:
                    return OperationResult.Refused(DiagnosticCodes.NotConfigured, result.Diagnostic?.Message ?? "Not configured.").WithOutput(output);
                case ServiceRequestStatus.Blocked:
                    return OperationResult.Refused(DiagnosticCodes.Blocked, result.Diagnostic?.Message ?? "Blocked.").WithOutput(output);
                default:
                    return OperationResult.Refused(result.Diagnostic?.Code ?? DiagnosticCodes.Refused, result.Diagnostic?.Message ?? "Refused.").WithOutput(output);
            }
        }
    }
}
