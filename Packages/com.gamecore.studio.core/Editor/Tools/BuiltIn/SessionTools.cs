// GameCore.Studio.Edit - direct built-ins (docs/studio/03-authoring-contracts.md s5): preview.stage, preview.compare,
// history.undo, history.redo, project.save, project.reload, project.build, project.launch. They run through
// ToolRegistry.Invoke, never inside a change set (undo/redo operate on the journal itself; previews and project
// lifecycle are not edits). project.build/launch go to the build lane, NotConfigured until P3/P4 provides one.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace GameCore.Studio.Edit
{
    /// <summary>Base of the direct tools.</summary>
    internal abstract class DirectTool : BuiltInTool, IDirectTool
    {
        protected DirectTool(ToolEntry entry)
            : base(entry)
        {
        }

        protected static JArray DiagnosticsJson(IEnumerable<Diagnostic> diagnostics)
        {
            JArray rows = new JArray();
            foreach (Diagnostic diagnostic in diagnostics)
            {
                rows.Add(StudioJson.ToToken(diagnostic));
            }

            return rows;
        }

        protected static OperationResult FromHistory(HistoryResult result)
        {
            JObject output = new JObject { ["ok"] = result.Ok, ["diagnostics"] = DiagnosticsJson(result.Diagnostics) };
            if (result.ChangeSetId != null)
            {
                output["changeSetId"] = result.ChangeSetId;
            }

            if (result.State.HasValue)
            {
                output["state"] = result.State.Value.ToString();
            }

            if (result.Ok)
            {
                return OperationResult.Applied(output);
            }

            string code = result.Diagnostics.Count > 0 ? result.Diagnostics[0].Code : DiagnosticCodes.Refused;
            string detail = result.Diagnostics.Count > 0 ? result.Diagnostics[0].Message : "refused";
            return OperationResult.Refused(code, detail).WithOutput(output);
        }
    }

    /// <summary><c>preview.stage</c>: stage a change set (dry runs and ghosts) without applying it.</summary>
    internal sealed class PreviewStageTool : DirectTool
    {
        public PreviewStageTool()
            : base(Entry_(
                BuiltInToolIds.PreviewStage,
                ToolTier.Configure,
                RuntimeApply.Live,
                false,
                "Stage a change set without applying it: prechecks, tool dry runs and preview ghosts. Returns the staged id, the findings and each operation's preview.",
                null,
                Arg("changeSet", ValueTypes.Object, true, "The change set (gamecore.studio.changeset/1)."),
                Arg("candidate", ValueTypes.Bool, false, "Validate in candidate mode (agent change sets)."),
                Arg("toolCatalogRevision", ValueTypes.String, false, "The catalog revision the candidate was planned against."),
                Arg("indexIsSlice", ValueTypes.Bool, false, "The planner saw a bounded index slice.")))
        {
        }

        public override OperationResult Apply(EditContext context)
        {
            JObject? raw = context.Arg("changeSet") as JObject;
            if (raw == null)
            {
                return OperationResult.Refused(DiagnosticCodes.InvalidArgs, "preview.stage needs 'changeSet'.");
            }

            ChangeSet changeSet;
            try
            {
                changeSet = StudioJson.Deserialize<ChangeSet>(raw.ToString(Formatting.None));
            }
            catch (JsonException error)
            {
                return OperationResult.Refused(DiagnosticCodes.CandidateInvalid, "The change set does not parse: " + error.Message);
            }

            StageOptions options = new StageOptions
            {
                Mode = context.BoolArg("candidate") ? ValidationMode.Candidate : ValidationMode.Journal,
                ToolCatalogRevision = context.StringArg("toolCatalogRevision"),
                IndexIsSlice = context.BoolArg("indexIsSlice"),
            };
            StagedChangeSet staged = context.Runtime.Engine.Stage(changeSet, options);
            context.Runtime.Engine.RememberPreview(staged);
            return OperationResult.Applied(Describe(context, staged));
        }

        internal static JObject Describe(EditContext context, StagedChangeSet staged)
        {
            JArray operations = new JArray();
            foreach (StagedOperation operation in staged.Operations)
            {
                JObject row = new JObject
                {
                    ["opId"] = operation.OpId,
                    ["tool"] = operation.Operation.Tool,
                    ["blocked"] = operation.Blocked,
                    ["deferred"] = operation.Deferred,
                    ["live"] = operation.Live,
                    ["diagnostics"] = DiagnosticsJson(operation.Diagnostics),
                    ["ghosts"] = operation.StageResult?.PreviewObjects.Count ?? 0,
                };
                if (operation.Preview != null)
                {
                    row["preview"] = operation.Preview.DeepClone();
                }

                if (operation.CurrentStamp != null)
                {
                    row["stamp"] = operation.CurrentStamp;
                }

                operations.Add(row);
            }

            return new JObject
            {
                ["stagedId"] = staged.Id,
                ["ok"] = staged.Ok,
                ["indexRevision"] = staged.IndexRevision,
                ["toolCatalogRevision"] = staged.CatalogRevision,
                ["diagnostics"] = DiagnosticsJson(staged.Diagnostics),
                ["operations"] = operations,
            };
        }
    }

    /// <summary><c>preview.compare</c>: before/after of a staged change set, and whether its targets changed since staging.</summary>
    internal sealed class PreviewCompareTool : DirectTool
    {
        public PreviewCompareTool()
            : base(Entry_(
                BuiltInToolIds.PreviewCompare,
                ToolTier.Configure,
                RuntimeApply.Live,
                false,
                "Compare a staged change set with the project now (per operation: preview before/after, and whether the target changed since staging), or two staged change sets with each other.",
                null,
                Arg("stagedId", ValueTypes.String, true, "A change-set id staged by preview.stage."),
                Arg("other", ValueTypes.String, false, "A second staged id to compare against.")))
        {
        }

        public override bool ReadOnly => true;

        public override OperationResult Apply(EditContext context)
        {
            string? id = context.StringArg("stagedId");
            if (id == null || !context.Runtime.Engine.Previews.TryGetValue(id, out StagedChangeSet? staged))
            {
                return OperationResult.Refused(DiagnosticCodes.StaleContext, "No staged change set '" + (id ?? string.Empty) + "'; stage it with preview.stage first.");
            }

            string? otherId = context.StringArg("other");
            if (otherId != null)
            {
                if (!context.Runtime.Engine.Previews.TryGetValue(otherId, out StagedChangeSet? other))
                {
                    return OperationResult.Refused(DiagnosticCodes.StaleContext, "No staged change set '" + otherId + "'.");
                }

                return OperationResult.Applied(CompareStaged(staged, other));
            }

            JArray rows = new JArray();
            foreach (StagedOperation operation in staged.Operations)
            {
                JObject row = new JObject { ["opId"] = operation.OpId, ["tool"] = operation.Operation.Tool };
                if (operation.Preview != null)
                {
                    row["preview"] = operation.Preview.DeepClone();
                }

                if (operation.Target != null)
                {
                    string? now = context.Resolver.ComputeStamp(operation.Target);
                    row["changedSinceStage"] = operation.CurrentStamp != null && now != null && !string.Equals(now, operation.CurrentStamp, StringComparison.Ordinal);
                    if (now != null)
                    {
                        row["stampNow"] = now;
                    }
                }

                rows.Add(row);
            }

            return OperationResult.Applied(new JObject
            {
                ["stagedId"] = staged.Id,
                ["indexRevisionAtStage"] = staged.IndexRevision,
                ["indexRevisionNow"] = context.Index.Revision,
                ["operations"] = rows,
            });
        }

        private static JObject CompareStaged(StagedChangeSet left, StagedChangeSet right)
        {
            JArray differences = new JArray();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (StagedOperation operation in left.Operations)
            {
                seen.Add(operation.OpId);
                StagedOperation? match = right.Find(operation.OpId);
                if (match == null)
                {
                    differences.Add(new JObject { ["opId"] = operation.OpId, ["only"] = left.Id });
                }
                else if (!string.Equals(operation.Operation.Tool, match.Operation.Tool, StringComparison.Ordinal) || !JToken.DeepEquals(operation.Preview, match.Preview))
                {
                    JObject row = new JObject { ["opId"] = operation.OpId };
                    if (operation.Preview != null)
                    {
                        row["left"] = operation.Preview.DeepClone();
                    }

                    if (match.Preview != null)
                    {
                        row["right"] = match.Preview.DeepClone();
                    }

                    differences.Add(row);
                }
            }

            foreach (StagedOperation operation in right.Operations)
            {
                if (!seen.Contains(operation.OpId))
                {
                    differences.Add(new JObject { ["opId"] = operation.OpId, ["only"] = right.Id });
                }
            }

            return new JObject { ["left"] = left.Id, ["right"] = right.Id, ["differences"] = differences };
        }
    }

    /// <summary><c>history.undo</c>: journal undo of the latest (or a named) applied change set.</summary>
    internal sealed class HistoryUndoTool : DirectTool
    {
        public HistoryUndoTool()
            : base(Entry_(
                BuiltInToolIds.HistoryUndo,
                ToolTier.Configure,
                RuntimeApply.Rebuild,
                false,
                "Undo the latest applied change set (or 'changeSetId') through its journaled inverse operations. Refused with Conflict when a touched object changed since, unless 'force'.",
                null,
                Arg("changeSetId", ValueTypes.String, false, "The change set to undo (default: the latest applied)."),
                Arg("force", ValueTypes.Bool, false, "Undo even when touched objects changed since.")))
        {
        }

        public override OperationResult Apply(EditContext context)
        {
            return FromHistory(context.Runtime.History.Undo(context.StringArg("changeSetId"), context.BoolArg("force")));
        }
    }

    /// <summary><c>history.redo</c>: re-apply the latest (or a named) undone change set with its retained artifacts.</summary>
    internal sealed class HistoryRedoTool : DirectTool
    {
        public HistoryRedoTool()
            : base(Entry_(
                BuiltInToolIds.HistoryRedo,
                ToolTier.Configure,
                RuntimeApply.Rebuild,
                false,
                "Redo the latest undone change set (or 'changeSetId'). Retained artifacts are reused; nothing is regenerated.",
                null,
                Arg("changeSetId", ValueTypes.String, false, "The change set to redo (default: the latest undone).")))
        {
        }

        public override OperationResult Apply(EditContext context)
        {
            return FromHistory(context.Runtime.History.Redo(context.StringArg("changeSetId")));
        }
    }

    /// <summary><c>project.save</c>: save dirty assets and open scenes.</summary>
    internal sealed class ProjectSaveTool : DirectTool
    {
        public ProjectSaveTool()
            : base(Entry_(
                BuiltInToolIds.ProjectSave,
                ToolTier.Configure,
                RuntimeApply.Rebuild,
                false,
                "Save dirty assets and every open scene (the journal is independent of saving: an entry can be Applied and unsaved).",
                null,
                Arg("scenes", ValueTypes.Bool, false, "Also save open scenes (default true).")))
        {
        }

        public override OperationResult Apply(EditContext context)
        {
            if (context.IsPlayMode)
            {
                return OperationResult.Refused(DiagnosticCodes.Refused, "Scenes cannot be saved in Play mode.");
            }

            context.OutsideAssetEditing(AssetDatabase.SaveAssets);
            bool scenes = context.BoolArg("scenes", true);
            bool saved = !scenes || EditorSceneManager.SaveOpenScenes();
            context.Index.SaveCache();
            return saved
                ? OperationResult.Applied(new JObject { ["assets"] = true, ["scenes"] = scenes })
                : OperationResult.Failed(DiagnosticCodes.Refused, "Unity could not save every open scene (an untitled scene needs a path).");
        }
    }

    /// <summary><c>project.reload</c>: refresh the AssetDatabase and rebuild types, tools and the index.</summary>
    internal sealed class ProjectReloadTool : DirectTool
    {
        public ProjectReloadTool()
            : base(Entry_(
                BuiltInToolIds.ProjectReload,
                ToolTier.Configure,
                RuntimeApply.Rebuild,
                false,
                "Refresh the AssetDatabase, forget cached types and tools, and rebuild the semantic index. Returns the new index and catalog revisions.",
                null))
        {
        }

        public override OperationResult Apply(EditContext context)
        {
            context.OutsideAssetEditing(() => AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport));
            context.Runtime.InvalidateCode();
            context.Index.Rebuild();
            return OperationResult.Applied(new JObject
            {
                ["indexRevision"] = context.Index.Revision,
                ["toolCatalogRevision"] = context.Runtime.Registry.Catalog.Revision,
            });
        }
    }

    /// <summary><c>project.build</c>: build a player through the build lane.</summary>
    internal sealed class ProjectBuildTool : DirectTool
    {
        public ProjectBuildTool()
            : base(Entry_(
                BuiltInToolIds.ProjectBuild,
                ToolTier.Configure,
                RuntimeApply.Build,
                false,
                "Build a player through the build lane. NotConfigured until a build lane is registered.",
                null,
                Arg("options", ValueTypes.Object, false, "Target, profile and output path.")))
        {
        }

        public override OperationResult Apply(EditContext context)
        {
            IBuildLane? lane = context.Services.BuildLane;
            if (lane == null || !lane.IsConfigured)
            {
                return ServiceTools.FromService(ServiceResult.NotConfigured("project.build", "No build lane is registered (P3/P4)."));
            }

            return ServiceTools.FromService(lane.Build(context.Arg("options") as JObject));
        }
    }

    /// <summary><c>project.launch</c>: launch the last (or a named) build.</summary>
    internal sealed class ProjectLaunchTool : DirectTool
    {
        public ProjectLaunchTool()
            : base(Entry_(
                BuiltInToolIds.ProjectLaunch,
                ToolTier.Configure,
                RuntimeApply.Build,
                false,
                "Launch the last (or a named) build through the build lane. NotConfigured until a build lane is registered.",
                null,
                Arg("options", ValueTypes.Object, false, "Which build and launch arguments.")))
        {
        }

        public override OperationResult Apply(EditContext context)
        {
            IBuildLane? lane = context.Services.BuildLane;
            if (lane == null || !lane.IsConfigured)
            {
                return ServiceTools.FromService(ServiceResult.NotConfigured("project.launch", "No build lane is registered (P3/P4)."));
            }

            return ServiceTools.FromService(lane.Launch(context.Arg("options") as JObject));
        }
    }
}
