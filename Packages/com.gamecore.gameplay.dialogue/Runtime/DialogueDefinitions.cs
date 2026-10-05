// GameCore.Gameplay.Dialogue - dialogue and narrative authoring (P1.4, catalog row 6).
//
//   FactDefinition           a named world fact (int32): the bake declares slot narrative.fact.<name> for it
//   DialogueGraphDefinition  nodes (line, choice, branch, action, end) and the edges between them
//
// Edges are explicit: an edge leaves a node through a port (next, else, or option n) and enters another node. A node
// with no outgoing next edge ends the conversation there. Speakers are named per node (or once per graph); a speaker
// can also be a placed entity by authoring id. Voice clips and portraits are presentation references only - the
// kernel never reads them.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Rules.Gameplay.Dialogue;
using UnityEngine;

namespace GameCore.Gameplay.Dialogue
{
    /// <summary>A named world fact.</summary>
    [Authorable(NarrativeKinds.Fact, DisplayName = "Fact", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A named int32 world fact (narrative.fact.<name>), set by dialogue, rules and quests and read by conditions.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Fact", fileName = "Fact")]
    public sealed class FactDefinition : NarrativeDefinitionAsset, IFactDefinition
    {
        [AuthorField(Doc = "Fact name: lowercase letters, digits and underscores (the slot is narrative.fact.<name>).")]
        [SerializeField] private string factName = string.Empty;

        [AuthorField(Doc = "Value before anything sets it.")]
        [SerializeField] private int initialValue;

        [AuthorField(Doc = "False = the fact returns to its initial value when a conversation ends (a conversation-local flag).")]
        [SerializeField] private bool persistent = true;

        public override string NarrativeKind => NarrativeKinds.Fact;

        public override string DefinitionName => factName.Length > 0 ? factName : name;

        public string FactName => factName.Length > 0 ? factName : name;

        public int InitialValue => initialValue;

        public bool Persistent => persistent;

        public void Configure(string fact, int initial, bool keep)
        {
            factName = fact ?? string.Empty;
            initialValue = initial;
            persistent = keep;
        }
    }

    /// <summary>The port an edge leaves its node through.</summary>
    public enum DialoguePort
    {
        /// <summary>A line's, action's or option-less node's continuation; a branch's "condition holds" path.</summary>
        Next = 0,

        /// <summary>A branch's "condition fails" path.</summary>
        Else = 1,

        /// <summary>Option <see cref="DialogueEdge.option"/> of a choice node.</summary>
        Option = 2,
    }

    /// <summary>One edge of a dialogue graph.</summary>
    [Serializable]
    public sealed class DialogueEdge
    {
        [AuthorField(Doc = "Source node index.")]
        public int from;

        [AuthorField(Doc = "Which port of the source node.")]
        public DialoguePort port = DialoguePort.Next;

        [AuthorField(Doc = "Option index for the Option port.")]
        public int option;

        [AuthorField(Doc = "Target node index (-1 = end the conversation).")]
        public int to = -1;
    }

    /// <summary>One option of a choice node.</summary>
    [Serializable]
    public sealed class DialogueOptionEntry
    {
        [AuthorField(Doc = "What the player says.")]
        public string text = string.Empty;

        [AuthorRef(Category = NarrativeKinds.ConditionSet, Required = false, Doc = "The option is available only while these conditions hold.")]
        public ConditionSetDefinition? condition;

        [AuthorField(Doc = "Hide the option while unavailable (instead of showing it disabled).")]
        public bool hideWhenUnavailable;
    }

    /// <summary>One node of a dialogue graph.</summary>
    [Serializable]
    public sealed class DialogueNodeEntry
    {
        [AuthorField(Doc = "Line, choice, branch, action or end.")]
        public DialogueNodeKind kind = DialogueNodeKind.Line;

        [AuthorField(Doc = "Speaker name (empty = the graph's speaker).")]
        public string speaker = string.Empty;

        [AuthorField(Type = "authoringId", Doc = "Speaker entity authoring id (optional; empty = the graph's speaker).")]
        public string speakerEntityId = string.Empty;

        [AuthorField(Doc = "The spoken line (line nodes) or the prompt (choice nodes).")]
        [TextArea(1, 6)]
        public string text = string.Empty;

        [AuthorRef(Category = "audio.clip", Required = false, Doc = "Voice clip of the line (presentation only).")]
        public AudioClip? voiceClip;

        [AuthorRef(Category = "texture.sprite", Required = false, Doc = "Speaker portrait (presentation only).")]
        public Sprite? portrait;

        [AuthorRef(Category = NarrativeKinds.ConditionSet, Required = false, Doc = "Branch condition (branch nodes).")]
        public ConditionSetDefinition? condition;

        [AuthorRef(Category = NarrativeKinds.ActionSet, Required = false, Doc = "Actions (action nodes): facts apply in the step, everything else through the outbox.")]
        public ActionSetDefinition? actions;

        [AuthorField(Doc = "Options (choice nodes), at most 8.")]
        public List<DialogueOptionEntry> options = new List<DialogueOptionEntry>();
    }

    /// <summary>A conversation graph.</summary>
    [Authorable(NarrativeKinds.Graph, DisplayName = "Dialogue Graph", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A conversation: line, choice, branch, action and end nodes joined by edges; conditions read facts and state.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Dialogue Graph", fileName = "DialogueGraph")]
    public sealed class DialogueGraphDefinition : NarrativeDefinitionAsset
    {
        [AuthorField(Doc = "Default speaker name of the graph's lines.")]
        [SerializeField] private string speaker = string.Empty;

        [AuthorField(Type = "authoringId", Doc = "Default speaker entity authoring id (optional).")]
        [SerializeField] private string speakerEntityId = string.Empty;

        [AuthorField(Min = 0, Doc = "Entry node index.")]
        [SerializeField] private int entry;

        [AuthorField(Doc = "The nodes; an edge names them by index.")]
        [SerializeField] private List<DialogueNodeEntry> nodes = new List<DialogueNodeEntry>();

        [AuthorField(Doc = "The edges.")]
        [SerializeField] private List<DialogueEdge> edges = new List<DialogueEdge>();

        public override string NarrativeKind => NarrativeKinds.Graph;

        public string Speaker => speaker;

        public string SpeakerEntityId => speakerEntityId;

        public int Entry => entry;

        public IReadOnlyList<DialogueNodeEntry> Nodes => nodes;

        public IReadOnlyList<DialogueEdge> Edges => edges;

        public void Configure(string defaultSpeaker, string defaultSpeakerEntityId, int entryNode)
        {
            speaker = defaultSpeaker ?? string.Empty;
            speakerEntityId = defaultSpeakerEntityId ?? string.Empty;
            entry = entryNode;
        }

        /// <summary>Appends a node and returns its index.</summary>
        public int AddNode(DialogueNodeEntry node)
        {
            nodes.Add(node ?? throw new ArgumentNullException(nameof(node)));
            return nodes.Count - 1;
        }

        /// <summary>Sets (replaces) the edge leaving <paramref name="from"/> through a port.</summary>
        public void Link(int from, DialoguePort port, int option, int to)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                DialogueEdge edge = edges[i];
                if (edge.from == from && edge.port == port && (port != DialoguePort.Option || edge.option == option))
                {
                    edge.to = to;
                    return;
                }
            }

            edges.Add(new DialogueEdge { from = from, port = port, option = port == DialoguePort.Option ? option : 0, to = to });
        }

        /// <summary>The target of the edge leaving a node through a port (-1 = none: the conversation ends).</summary>
        public int Target(int from, DialoguePort port, int option)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                DialogueEdge edge = edges[i];
                if (edge.from == from && edge.port == port && (port != DialoguePort.Option || edge.option == option))
                {
                    return edge.to;
                }
            }

            return -1;
        }

        public DialogueNodeEntry Node(int index) => nodes[index];
    }
}
