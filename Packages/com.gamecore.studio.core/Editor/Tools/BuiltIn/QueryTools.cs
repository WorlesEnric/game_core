// GameCore.Studio.Edit - read-only built-ins (docs/studio/03-authoring-contracts.md s5): inspect.describe,
// inspect.explain, query.references, query.impact. They change nothing, need no journal entry and may be invoked
// directly (ToolRegistry.Invoke) or inside a change set (their output lands in the outcome detail).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    /// <summary>Base of read-only tools: no staging checks beyond a resolvable target, output in the result.</summary>
    internal abstract class ReadOnlyTool : BuiltInTool
    {
        protected ReadOnlyTool(ToolEntry entry)
            : base(entry)
        {
        }

        public override bool ReadOnly => true;

        public override ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            if (Entry.TargetRequired && context.Operation.Target == null)
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "'" + Entry.Id + "' needs a target."));
            }

            return result;
        }

        protected static OperationResult Output(JToken output) => OperationResult.Applied(output).WithDetail(ToolSupport.Compact(output, 4000));
    }

    /// <summary><c>inspect.describe</c>: what a thing is, its authorable values, references and capabilities.</summary>
    internal sealed class InspectDescribeTool : ReadOnlyTool
    {
        public InspectDescribeTool()
            : base(Entry_(
                BuiltInToolIds.InspectDescribe,
                ToolTier.Configure,
                RuntimeApply.Live,
                true,
                "Describe an authored thing: its type, name, current authorable values, outgoing references, capabilities and incoming references.",
                null,
                Arg("includeReferrers", ValueTypes.Bool, false, "Also list who references it (default true).")))
        {
        }

        public override OperationResult Apply(EditContext context)
        {
            AuthoringRef target = context.Operation.Target!;
            JObject output = new JObject();
            IndexNode? node = context.Index.FindNode(target);
            if (node != null)
            {
                output["node"] = StudioJson.ToToken(node);
            }

            UnityEngine.Object? live = context.Target;
            if (live != null)
            {
                AuthoringRef? current = context.Resolver.BuildRef(live, null, true);
                if (current != null)
                {
                    output["ref"] = StudioJson.ToToken(current);
                }

                output["name"] = live.name;
                output["unityType"] = live.GetType().FullName;
                AuthoringTypeInfo? info = context.Identity.Describe(live);
                if (info != null)
                {
                    output["type"] = info.TypeId;
                    output["values"] = ToolSupport.CaptureMembers(context, live, info);
                    IReadOnlyList<string>? capabilities = context.Identity.GetCapabilities(live);
                    if (capabilities != null && capabilities.Count > 0)
                    {
                        output["capabilities"] = new JArray(capabilities);
                    }
                }
                else if (live is GameObject gameObject)
                {
                    JArray components = new JArray();
                    foreach (Component component in gameObject.GetComponents<Component>())
                    {
                        if (component != null)
                        {
                            components.Add(component.GetType().FullName);
                        }
                    }

                    output["components"] = components;
                }
            }
            else if (node == null)
            {
                return OperationResult.Refused(DiagnosticCodes.StaleTarget, "The target does not resolve and is not in the index.");
            }

            if (context.BoolArg("includeReferrers", true))
            {
                JArray referrers = new JArray();
                foreach (IndexReference reference in context.Index.ReferencesTo(target))
                {
                    referrers.Add(new JObject { ["from"] = StudioJson.ToToken(reference.From), ["field"] = reference.Field, ["type"] = reference.FromType });
                }

                output["referencedBy"] = referrers;
            }

            return Output(output);
        }
    }

    /// <summary><c>inspect.explain</c>: why a rule, condition or interaction did or did not fire (via an explain source).</summary>
    internal sealed class InspectExplainTool : ReadOnlyTool
    {
        public InspectExplainTool()
            : base(Entry_(
                BuiltInToolIds.InspectExplain,
                ToolTier.Configure,
                RuntimeApply.Live,
                true,
                "Explain why a rule, condition or interaction on the target did or did not fire, from the traces of a registered explain source. NotConfigured when no source can explain the target.",
                null,
                Arg("question", ValueTypes.String, false, "What to explain (free text or a rule id)."),
                Arg("source", ValueTypes.String, false, "A specific explain source id.")))
        {
        }

        public override OperationResult Apply(EditContext context)
        {
            AuthoringRef target = context.Operation.Target!;
            string? sourceId = context.StringArg("source");
            IExplainSource? source = null;
            if (sourceId != null)
            {
                foreach (IExplainSource candidate in context.Services.ExplainSources)
                {
                    if (string.Equals(candidate.Id, sourceId, StringComparison.Ordinal))
                    {
                        source = candidate;
                        break;
                    }
                }
            }
            else
            {
                source = context.Services.FindExplainSource(target, context.Operation.Args);
            }

            if (source == null)
            {
                return ServiceTools.FromService(ServiceResult.NotConfigured(
                    "inspect.explain",
                    sourceId == null ? "No explain source can explain this target; gameplay packages register IExplainSource." : "Explain source '" + sourceId + "' is not registered."));
            }

            OperationResult result = ServiceTools.FromService(source.Explain(target, context.Operation.Args));
            return result.Output != null ? result.WithDetail(ToolSupport.Compact(result.Output, 4000)) : result;
        }
    }

    /// <summary><c>query.references</c>: who references the target (index refs).</summary>
    internal sealed class QueryReferencesTool : ReadOnlyTool
    {
        public QueryReferencesTool()
            : base(Entry_(
                BuiltInToolIds.QueryReferences,
                ToolTier.Configure,
                RuntimeApply.Live,
                true,
                "List every authored thing that references the target, with the referencing field.",
                null))
        {
        }

        public override OperationResult Apply(EditContext context)
        {
            JArray rows = new JArray();
            foreach (IndexReference reference in context.Index.ReferencesTo(context.Operation.Target!))
            {
                rows.Add(new JObject { ["from"] = StudioJson.ToToken(reference.From), ["field"] = reference.Field, ["type"] = reference.FromType });
            }

            return Output(new JObject { ["revision"] = context.Index.Revision, ["references"] = rows });
        }
    }

    /// <summary><c>query.impact</c>: everything deleting or changing the target affects (transitive).</summary>
    internal sealed class QueryImpactTool : ReadOnlyTool
    {
        public QueryImpactTool()
            : base(Entry_(
                BuiltInToolIds.QueryImpact,
                ToolTier.Configure,
                RuntimeApply.Live,
                true,
                "Everything deleting the target affects: its referrers, its contents and, transitively, their referrers.",
                null,
                Arg("maxDepth", ValueTypes.Int, false, "Traversal depth (default 8).")))
        {
        }

        public override OperationResult Apply(EditContext context)
        {
            int depth = context.TryArg("maxDepth", out int requested, out _) && requested > 0 ? requested : 8;
            ImpactReport report = context.Index.ImpactOf(context.Operation.Target!, depth);
            JArray rows = new JArray();
            foreach (ImpactItem item in report.Items)
            {
                JObject row = new JObject { ["ref"] = StudioJson.ToToken(item.Ref), ["relation"] = item.Relation, ["depth"] = item.Depth, ["via"] = StudioJson.ToToken(item.Via) };
                if (item.Field != null)
                {
                    row["field"] = item.Field;
                }

                rows.Add(row);
            }

            return Output(new JObject { ["revision"] = context.Index.Revision, ["impact"] = rows });
        }
    }
}
