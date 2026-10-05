// GameCore.Gameplay.Dialogue - DialogueGraphDefinition (its own file: Unity resolves a ScriptableObject script by file name).
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
