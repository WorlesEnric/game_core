// GameCore.Gameplay.Dialogue.Editor - dialogue authoring operations (P1.4, catalog row 6; Studio 03 s4/s5).
//
//   dialogue.addLine        append a line node (optionally continuing an existing node)
//   dialogue.addChoice      append a choice node with options (optionally continuing an existing node)
//   dialogue.linkCondition  set a branch node's condition or a choice option's availability condition
//   dialogue.setFact        create or update a fact (name, initial value, persistence) on a content set
//   dialogue.preview        walk a graph over the content's initial facts plus overrides and print every path
//   dialogue.generateVoice  ask the media generation gateway for a voice clip of a line (Studio 05 tier Agent; the
//                           shared ToolTier has no Agent member, so it is declared Mechanism and documented as Agent)
//
// Every tool validates before it changes anything, records Undo and refuses with an ArgumentException whose message
// starts with the GP-* code; a change that leaves the graph invalid is refused by re-validating the graph.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Logic.Editor;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Rules.Gameplay.Logic;
using UnityEditor;
using UnityEngine;

namespace GameCore.Gameplay.Dialogue.Editor
{
    /// <summary>The dialogue.* authoring operations.</summary>
    public static class DialogueTools
    {
        [AuthorOperation("dialogue.addLine", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(DialogueValidator), Requires = NarrativeKinds.Graph,
            Doc = "Appends a line node to a dialogue graph; with 'after' the new line continues that node.")]
        public static int AddLine(
            DialogueGraphDefinition graph,
            [AuthorArg(Doc = "The spoken line.")] string text,
            [AuthorArg(Required = false, Doc = "Speaker name (empty = the graph's speaker).")] string speaker = "",
            [AuthorArg(Required = false, Doc = "Node the line continues (-1 = none).")] int after = -1,
            [AuthorArg(Category = "audio.clip", Required = false, Doc = "Voice clip.")] AudioClip? voiceClip = null)
        {
            Require(graph);
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.GraphEmpty + ": a line needs text");
            }

            RequireNode(graph, after, true);
            Undo.RecordObject(graph, "dialogue.addLine");
            int index = graph.AddNode(new DialogueNodeEntry { kind = DialogueNodeKind.Line, speaker = speaker ?? string.Empty, text = text, voiceClip = voiceClip });
            if (after >= 0)
            {
                graph.Link(after, DialoguePort.Next, 0, index);
            }

            EditorUtility.SetDirty(graph);
            return index;
        }

        [AuthorOperation("dialogue.addChoice", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(DialogueValidator), Requires = NarrativeKinds.Graph,
            Doc = "Appends a choice node with up to 8 options; targets name the node each option leads to (-1 = end).")]
        public static int AddChoice(
            DialogueGraphDefinition graph,
            [AuthorArg(Doc = "Option texts.")] List<string> options,
            [AuthorArg(Required = false, Doc = "Node each option leads to (-1 = end); missing entries end.")] List<int>? targets = null,
            [AuthorArg(Required = false, Doc = "Node the choice continues (-1 = none).")] int after = -1,
            [AuthorArg(Required = false, Doc = "Prompt shown with the options.")] string prompt = "")
        {
            Require(graph);
            if (options == null || options.Count == 0 || options.Count > DialogueRules.MaxOptions)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.GraphTooManyOptions + ": a choice has 1.." + DialogueRules.MaxOptions + " options");
            }

            RequireNode(graph, after, true);
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    RequireNode(graph, targets[i], true);
                }
            }

            Undo.RecordObject(graph, "dialogue.addChoice");
            var node = new DialogueNodeEntry { kind = DialogueNodeKind.Choice, text = prompt ?? string.Empty };
            for (int i = 0; i < options.Count; i++)
            {
                node.options.Add(new DialogueOptionEntry { text = options[i] ?? string.Empty });
            }

            int index = graph.AddNode(node);
            for (int i = 0; i < options.Count; i++)
            {
                int to = targets != null && i < targets.Count ? targets[i] : -1;
                graph.Link(index, DialoguePort.Option, i, to);
            }

            if (after >= 0)
            {
                graph.Link(after, DialoguePort.Next, 0, index);
            }

            EditorUtility.SetDirty(graph);
            return index;
        }

        [AuthorOperation("dialogue.linkCondition", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(DialogueValidator), Requires = NarrativeKinds.Graph,
            Doc = "Sets the condition of a branch node, or the availability condition of one option of a choice node.")]
        public static void LinkCondition(
            DialogueGraphDefinition graph,
            [AuthorArg(Doc = "Node index.")] int node,
            [AuthorArg(Category = NarrativeKinds.ConditionSet, Required = false, Doc = "The condition (empty = none).")] ConditionSetDefinition? condition,
            [AuthorArg(Required = false, Doc = "Option index of a choice node (-1 = the node itself, a branch).")] int option = -1)
        {
            Require(graph);
            RequireNode(graph, node, false);
            DialogueNodeEntry entry = graph.Node(node);
            if (option >= 0)
            {
                if (entry.kind != DialogueNodeKind.Choice || option >= entry.options.Count)
                {
                    throw new ArgumentException(NarrativeDiagnosticCodes.ChoiceUnavailable + ": node " + node + " has no option " + option);
                }

                Undo.RecordObject(graph, "dialogue.linkCondition");
                entry.options[option].condition = condition;
            }
            else
            {
                if (entry.kind != DialogueNodeKind.Branch)
                {
                    throw new ArgumentException(NarrativeDiagnosticCodes.GraphBadEntry + ": node " + node + " is a " + entry.kind + ", not a branch");
                }

                Undo.RecordObject(graph, "dialogue.linkCondition");
                entry.condition = condition;
            }

            EditorUtility.SetDirty(graph);
        }

        [AuthorOperation("dialogue.setFact", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(DialogueValidator), Requires = NarrativeKinds.ContentSet,
            Doc = "Creates (or updates) a fact on a content set: name, initial value and whether it persists past a conversation.")]
        public static FactDefinition SetFact(
            GameplayContentSet contentSet,
            [AuthorArg(Doc = "Fact name (lowercase letters, digits, underscores).")] string factName,
            [AuthorArg(Required = false, Doc = "Initial value.")] int initial = 0,
            [AuthorArg(Required = false, Doc = "False = reset when a conversation ends.")] bool persistent = true)
        {
            if (!NarrativeKeys.IsValidFactName(factName))
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.FactInvalidName + ": '" + factName + "' is not a fact name");
            }

            if (contentSet == null)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.ContentSetMissingWorld + ": a content set is required");
            }

            for (int i = 0; i < contentSet.Definitions.Count; i++)
            {
                if (contentSet.Definitions[i] is FactDefinition existing && existing.FactName == factName)
                {
                    Undo.RecordObject(existing, "dialogue.setFact");
                    existing.Configure(factName, initial, persistent);
                    EditorUtility.SetDirty(existing);
                    return existing;
                }
            }

            FactDefinition fact = NarrativeAuthoring.CreateAsset<FactDefinition>(contentSet, factName, "Facts", string.Empty, "dialogue.setFact");
            fact.Configure(factName, initial, persistent);
            EditorUtility.SetDirty(fact);
            return fact;
        }

        [AuthorOperation("dialogue.preview", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live,
            Validator = typeof(DialogueValidator), Requires = NarrativeKinds.Graph,
            Doc = "Walks the graph over the initial facts plus overrides ('bell_rung=1; odd_paid=1') and prints every reachable path.")]
        public static string Preview(
            DialogueGraphDefinition graph,
            [AuthorArg(Required = false, Doc = "Fact overrides: name=value separated by ';'.")] string facts = "")
        {
            Require(graph);
            NarrativeModelSet models = NarrativeAuthoring.Models(string.Empty, graph);
            if (!models.TryResolve(graph.AuthoringId, out int key) || !models.TryGetGraph(key, out DialogueGraphModel? model) || model == null)
            {
                throw new ArgumentException(models.Problems.Count > 0 ? models.Problems[0] : NarrativeDiagnosticCodes.GraphUnknown + ": the graph did not convert");
            }

            StateSnapshot state = NarrativeAuthoring.ParseState(string.Empty, models, out RuleState _);
            var overrides = new Dictionary<int, int>();
            string[] terms = (facts ?? string.Empty).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < terms.Length; i++)
            {
                string[] pair = terms[i].Split('=');
                string name = pair[0].Trim();
                if (name.StartsWith("narrative.fact.", StringComparison.Ordinal))
                {
                    name = name.Substring("narrative.fact.".Length);
                }

                if (pair.Length != 2 || !int.TryParse(pair[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    throw new ArgumentException(NarrativeDiagnosticCodes.ConditionInvalid + ": '" + terms[i] + "' is not fact=value");
                }

                if (!models.TryGetFactByName(name, out FactModel? fact) || fact == null)
                {
                    throw new ArgumentException(NarrativeDiagnosticCodes.FactUnknown + ": the graph references no fact '" + name + "'");
                }

                overrides[fact.Key] = value;
            }

            return DialoguePreview.Preview(model, state, models, overrides);
        }

        [AuthorOperation("dialogue.generateVoice", Tier = ToolTier.Mechanism, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(DialogueValidator), Requires = NarrativeKinds.Graph,
            Doc = "Studio tier Agent: requests a generated voice clip for a line through the media generation gateway (NotConfigured until P3 wires a provider).")]
        public static MediaGenerationResult GenerateVoice(
            DialogueGraphDefinition graph,
            [AuthorArg(Doc = "Line node index.")] int node,
            [AuthorArg(Required = false, Doc = "Voice name for the provider.")] string voice = "")
        {
            return GenerateVoice(graph, node, voice, new NotConfiguredMediaGateway());
        }

        /// <summary>The same request through an explicit gateway (P3 passes a configured one).</summary>
        public static MediaGenerationResult GenerateVoice(DialogueGraphDefinition graph, int node, string voice, IMediaGenerationGateway gateway)
        {
            Require(graph);
            RequireNode(graph, node, false);
            DialogueNodeEntry entry = graph.Node(node);
            if (entry.kind != DialogueNodeKind.Line || string.IsNullOrEmpty(entry.text))
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.GraphBadEntry + ": node " + node + " is not a line with text");
            }

            string speaker = entry.speaker.Length > 0 ? entry.speaker : graph.Speaker;
            return (gateway ?? new NotConfiguredMediaGateway()).RequestVoiceLine(new VoiceGenerationRequest(graph.AuthoringId, node, speaker, entry.text, voice ?? string.Empty));
        }

        private static void Require(DialogueGraphDefinition graph)
        {
            if (graph == null)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.GraphUnknown + ": a dialogue graph is required");
            }
        }

        private static void RequireNode(DialogueGraphDefinition graph, int node, bool allowNone)
        {
            if ((allowNone && node == -1) || (node >= 0 && node < graph.Nodes.Count))
            {
                return;
            }

            throw new ArgumentException(NarrativeDiagnosticCodes.GraphDanglingEdge + ": node " + node + " does not exist in " + graph.name);
        }
    }

    /// <summary>Validation of dialogue graphs and facts.</summary>
    [AuthorValidator("dialogue.validator", Codes = new[]
    {
        NarrativeDiagnosticCodes.GraphEmpty,
        NarrativeDiagnosticCodes.GraphBadEntry,
        NarrativeDiagnosticCodes.GraphDanglingEdge,
        NarrativeDiagnosticCodes.GraphTooManyOptions,
        NarrativeDiagnosticCodes.GraphUnreachableNode,
        NarrativeDiagnosticCodes.GraphTooManyNodes,
        NarrativeDiagnosticCodes.FactInvalidName,
        NarrativeDiagnosticCodes.FactDuplicate,
        NarrativeDiagnosticCodes.ConditionInvalid,
    })]
    public static class DialogueValidator
    {
        public static IReadOnlyList<GameplayDiagnostic> Validate(ScriptableObject definition) => LogicValidator.Validate(definition);
    }

    /// <summary>The dialogue plugin's catalog registrations (P1.3's catalog contribution seam).</summary>
    public sealed class DialogueCatalogContributor : IGameplayCatalogContributor
    {
        public GameplayCatalogContribution Contribution =>
            new GameplayCatalogContribution("com.gamecore.gameplay.dialogue", NarrativeCatalogNames.Dialogue.Schemas, NarrativeCatalogNames.Dialogue.Entries);
    }
}
