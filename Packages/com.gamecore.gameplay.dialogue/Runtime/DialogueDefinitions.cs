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
        [AuthorField(Structural = true, Doc = "Line, choice, branch, action or end.")]
        public DialogueNodeKind kind = DialogueNodeKind.Line;

        [AuthorField(Doc = "Speaker name (empty = the graph's speaker).")]
        public string speaker = string.Empty;

        [AuthorRef(Category = AuthorRefCategories.EntityInstance, Required = false, Doc = "Speaker entity, by authoring id (optional; empty = the graph's speaker).")]
        public string speakerEntityId = string.Empty;

        [AuthorField(Doc = "The spoken line (line nodes) or the prompt (choice nodes).")]
        [TextArea(1, 6)]
        public string text = string.Empty;

        [AuthorRef(Category = AuthorRefCategories.AudioClip, Required = false, Doc = "Voice clip of the line (presentation only).")]
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

}
