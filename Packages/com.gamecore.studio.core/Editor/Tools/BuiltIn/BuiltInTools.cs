// GameCore.Studio.Edit - the built-in tool set (docs/studio/03-authoring-contracts.md s5).
//   Configure: set, assign, bind
//   Compose:   create, duplicate, delete, replace, move, place, addComponent, removeComponent
//   Always present: inspect.describe, inspect.explain, query.references, query.impact, preview.stage, preview.compare,
//   history.undo, history.redo, project.save, project.reload, project.build, project.launch, asset.import,
//   asset.generate, mechanism.propose
//   Internal (journal inverses, not exported): history.restoreAsset, history.deleteAsset, history.restoreObject
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    internal static class BuiltInTools
    {
        public static IReadOnlyList<IStudioTool> Create()
        {
            return new IStudioTool[]
            {
                new SetTool(),
                new AssignTool(),
                new BindTool(),
                new CreateTool(),
                new DuplicateTool(),
                new DeleteTool(),
                new ReplaceTool(),
                new MoveTool(),
                new PlaceTool(),
                new AddComponentTool(),
                new RemoveComponentTool(),
                new InspectDescribeTool(),
                new InspectExplainTool(),
                new QueryReferencesTool(),
                new QueryImpactTool(),
                new PreviewStageTool(),
                new PreviewCompareTool(),
                new HistoryUndoTool(),
                new HistoryRedoTool(),
                new ProjectSaveTool(),
                new ProjectReloadTool(),
                new ProjectBuildTool(),
                new ProjectLaunchTool(),
                new AssetImportTool(),
                new AssetGenerateTool(),
                new MechanismProposeTool(),
                new RestoreAssetTool(),
                new DeleteAssetTool(),
                new RestoreObjectTool(),
            };
        }
    }

    /// <summary>Base of the built-in tools: entry construction helpers and defaults.</summary>
    internal abstract class BuiltInTool : IStudioTool
    {
        protected BuiltInTool(ToolEntry entry)
        {
            Entry = entry;
        }

        public ToolEntry Entry { get; }

        public virtual bool Internal => false;

        public virtual bool ReadOnly => false;

        public virtual ToolStageResult Stage(EditContext context) => new ToolStageResult();

        public abstract OperationResult Apply(EditContext context);

        protected static ArgSpec Arg(string name, string type, bool required, string doc, string? category = null, IReadOnlyList<string>? enumValues = null)
        {
            return new ArgSpec(name, type, required, category: category, doc: doc, enumValues: enumValues);
        }

        protected static ToolEntry Entry_(string id, ToolTier tier, RuntimeApply apply, bool targetRequired, string doc, IReadOnlyList<AuthoringKind>? targetKinds, params ArgSpec[] args)
        {
            return new ToolEntry(id, tier, apply, targetRequired, args, doc, null, targetKinds);
        }

        protected static readonly IReadOnlyList<AuthoringKind> AuthoredKinds = new[] { AuthoringKind.Entity, AuthoringKind.Definition, AuthoringKind.Region, AuthoringKind.SceneObject };

        protected static readonly IReadOnlyList<AuthoringKind> SceneKinds = new[] { AuthoringKind.Entity, AuthoringKind.SceneObject, AuthoringKind.Region };

        /// <summary>The digest of an artifact argument (<c>{"artifact":"sha256:..."}</c> or <c>"sha256:..."</c>), or null.</summary>
        protected static string? ArtifactDigest(JToken? value)
        {
            JToken? reference = value;
            if (value is JObject wrapper)
            {
                reference = wrapper["artifact"];
            }

            string? text = reference != null && reference.Type == JTokenType.String ? reference.Value<string>() : null;
            if (text == null)
            {
                return null;
            }

            if (ContentStamp.IsValid(text))
            {
                return ContentStamp.DigestOf(text);
            }

            return ContentStamp.IsValidHex(text) ? text : null;
        }

        protected static JObject ArtifactArg(string digest) => new JObject { ["artifact"] = ContentStamp.Prefix + digest };
    }
}
