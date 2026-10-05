// GameCore.Gameplay.Dialogue - conversion of dialogue graphs to the pure models (P1.4).
//
// Facts are converted by the logic converter (every IFactDefinition); this converter owns DialogueGraphDefinition. A
// node's edges become its next / else / option targets; a graph is validated with DialogueRules.Validate and every
// problem is reported with its GP-DLG code, so the bake refuses a broken graph.
#nullable enable
using System.Collections.Generic;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Rules.Gameplay.Logic;
using UnityEngine;

namespace GameCore.Gameplay.Dialogue
{
    /// <summary>The dialogue package's converter.</summary>
    public sealed class DialogueContentConverter : INarrativeContentConverter
    {
        public bool CanConvert(ScriptableObject asset) => asset is DialogueGraphDefinition;

        public void Convert(ScriptableObject asset, NarrativeConversion conversion)
        {
            if (asset is DialogueGraphDefinition graph)
            {
                DialogueGraphModel? model = ToModel(graph, conversion);
                if (model != null)
                {
                    conversion.Models.AddGraph(model, graph.AuthoringId);
                    conversion.Models.Alias(graph.DefinitionName, model.Key);
                    if (graph.NpcGraphRef.Length > 0)
                    {
                        conversion.Models.Alias(graph.NpcGraphRef, model.Key);
                    }
                }
            }
        }

        /// <summary>The model of a graph (null when it cannot be converted); problems go to the conversion.</summary>
        public static DialogueGraphModel? ToModel(DialogueGraphDefinition graph, NarrativeConversion conversion)
        {
            int key = NarrativeRefs.KeyOf(graph);
            if (key == 0)
            {
                conversion.Problem(graph, NarrativeDiagnosticCodes.ContentKeyCollision + ": graph " + graph.name + " has no authoring id");
                return null;
            }

            if (graph.Nodes.Count == 0)
            {
                conversion.Problem(graph, NarrativeDiagnosticCodes.GraphEmpty + ": graph " + graph.name + " has no nodes");
                return null;
            }

            if (graph.Nodes.Count > DialogueRules.MaxNodes)
            {
                conversion.Problem(graph, NarrativeDiagnosticCodes.GraphTooManyNodes + ": graph " + graph.name + " has more than " + DialogueRules.MaxNodes + " nodes");
                return null;
            }

            for (int i = 0; i < graph.Edges.Count; i++)
            {
                DialogueEdge edge = graph.Edges[i];
                if (edge.from < 0 || edge.from >= graph.Nodes.Count || edge.to < -1 || edge.to >= graph.Nodes.Count)
                {
                    conversion.Problem(graph, NarrativeDiagnosticCodes.GraphDanglingEdge + ": edge " + edge.from + " -> " + edge.to + " leaves the graph");
                }
            }

            int graphSpeakerKey = SpeakerKey(graph.SpeakerEntityId, graph.Speaker);
            var nodes = new List<DialogueNodeModel>(graph.Nodes.Count);
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                DialogueNodeEntry entry = graph.Nodes[i];
                string speaker = entry.speaker.Length > 0 ? entry.speaker : graph.Speaker;
                int speakerKey = entry.speaker.Length > 0 || entry.speakerEntityId.Length > 0
                    ? SpeakerKey(entry.speakerEntityId, entry.speaker)
                    : graphSpeakerKey;
                var options = new List<DialogueOptionModel>();
                if (entry.kind == DialogueNodeKind.Choice)
                {
                    if (entry.options.Count > DialogueRules.MaxOptions)
                    {
                        conversion.Problem(graph, NarrativeDiagnosticCodes.GraphTooManyOptions + ": node " + i + " has more than " + DialogueRules.MaxOptions + " options");
                    }

                    for (int o = 0; o < entry.options.Count && o < DialogueRules.MaxOptions; o++)
                    {
                        DialogueOptionEntry option = entry.options[o];
                        options.Add(new DialogueOptionModel(
                            option.text,
                            conversion.ConditionSet(option.condition),
                            graph.Target(i, DialoguePort.Option, o),
                            option.hideWhenUnavailable));
                    }
                }

                ActionSetModel? actions = entry.kind == DialogueNodeKind.Action ? conversion.ActionSet(entry.actions) : null;
                ConditionSetModel? condition = entry.kind == DialogueNodeKind.Branch ? conversion.ConditionSet(entry.condition) : null;
                nodes.Add(new DialogueNodeModel(
                    i,
                    entry.kind,
                    speakerKey,
                    speaker,
                    entry.text,
                    entry.voiceClip != null ? entry.voiceClip.name : string.Empty,
                    entry.portrait != null ? entry.portrait.name : string.Empty,
                    condition,
                    actions,
                    graph.Target(i, DialoguePort.Next, 0),
                    graph.Target(i, DialoguePort.Else, 0),
                    options));
            }

            var model = new DialogueGraphModel(key, graph.DefinitionName, graph.Entry, nodes, graphSpeakerKey, graph.Speaker);
            List<string> problems = DialogueRules.Validate(model);
            for (int i = 0; i < problems.Count; i++)
            {
                conversion.Problem(graph, CodeOf(problems[i]) + ": " + problems[i]);
            }

            return model;
        }

        /// <summary>The GP-DLG code of a DialogueRules.Validate problem.</summary>
        public static string CodeOf(string problem)
        {
            if (problem.StartsWith("empty", System.StringComparison.Ordinal))
            {
                return NarrativeDiagnosticCodes.GraphEmpty;
            }

            if (problem.StartsWith("too many", System.StringComparison.Ordinal))
            {
                return NarrativeDiagnosticCodes.GraphTooManyNodes;
            }

            if (problem.StartsWith("bad entry", System.StringComparison.Ordinal))
            {
                return NarrativeDiagnosticCodes.GraphBadEntry;
            }

            if (problem.StartsWith("unreachable", System.StringComparison.Ordinal))
            {
                return NarrativeDiagnosticCodes.GraphUnreachableNode;
            }

            if (problem.StartsWith("dangling", System.StringComparison.Ordinal))
            {
                return NarrativeDiagnosticCodes.GraphDanglingEdge;
            }

            return NarrativeDiagnosticCodes.GraphTooManyOptions;
        }

        /// <summary>A speaker's key: the entity's stable key, else the speaker name key, else 0.</summary>
        public static int SpeakerKey(string entityId, string speaker)
        {
            int entity = NarrativeRefs.EntityKey(entityId);
            if (entity != 0)
            {
                return entity;
            }

            return string.IsNullOrEmpty(speaker) ? 0 : NarrativeKeys.SpeakerNameKey(speaker);
        }
    }
}
