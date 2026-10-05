// GameCore.Gameplay.Logic - reusable rules authoring: ConditionSetDefinition, ActionSetDefinition, RuleDefinition, and
// the content set / content manifest pair the bake learns narrative definitions from (P1.4, catalog row 9).
//
// Conditions and actions name what they are about through ScriptableObject references (a fact, an item, a quest, a
// region, a graph, another set) with [AuthorRef] categories, or through an entity authoring id for placed scene
// entities, which assets cannot reference. The bake turns every reference into the int32 key the slots carry; nothing
// is resolved by string at runtime except through the baked tables.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Logic;
using UnityEngine;

namespace GameCore.Gameplay.Logic
{
    /// <summary>One condition: what is read, how it compares, and against what.</summary>
    [Serializable]
    public sealed class ConditionEntry
    {
        [AuthorField(Doc = "What the condition reads.")]
        public ConditionKind kind = ConditionKind.Fact;

        [AuthorRef(Category = "narrative.subject", Required = false,
            Doc = "The fact, item, quest, region, graph, rule or condition set the condition is about.")]
        public ScriptableObject? subject;

        [AuthorRef(Category = "inventory.inventory", Required = false, Doc = "Inventory for item/currency conditions (empty = the actor's).")]
        public ScriptableObject? inventory;

        [AuthorField(Type = "authoringId", Doc = "Entity authoring id for region/slot conditions (empty = the actor or subject).")]
        public string entityId = string.Empty;

        [AuthorField(Doc = "Objective index (objectiveDone), node index (nodeVisited) or slot ref (slot).")]
        public int index;

        [AuthorField(Doc = "Comparison operator.")]
        public CompareOp op = CompareOp.GreaterOrEqual;

        [AuthorField(Doc = "Value compared against (ignored for region conditions, which compare with the subject region).")]
        public int value = 1;

        public static ConditionEntry Of(ConditionKind kind, ScriptableObject? subject, CompareOp op, int value) =>
            new ConditionEntry { kind = kind, subject = subject, op = op, value = value };
    }

    /// <summary>One action.</summary>
    [Serializable]
    public sealed class ActionEntry
    {
        [AuthorField(Doc = "What the action does.")]
        public ActionKind kind = ActionKind.SetFact;

        [AuthorRef(Category = "narrative.subject", Required = false,
            Doc = "The fact, item, quest, graph, region, world item or action set the action is about.")]
        public ScriptableObject? target;

        [AuthorRef(Category = "inventory.inventory", Required = false, Doc = "Inventory for grant/consume (empty = the actor's).")]
        public ScriptableObject? inventory;

        [AuthorField(Type = "authoringId", Doc = "Entity authoring id for spawn/despawn/setInteractableState/startDialogue (empty = the subject).")]
        public string entityId = string.Empty;

        [AuthorField(Doc = "Fact value, item count, quest stage (-1 = next) or interactable state.")]
        public int value = 1;

        [AuthorField(Doc = "Cue id (playAudio) or message text (showMessage).")]
        public string text = string.Empty;

        public static ActionEntry Of(ActionKind kind, ScriptableObject? target, int value) =>
            new ActionEntry { kind = kind, target = target, value = value };

        public static ActionEntry Text(ActionKind kind, string text) => new ActionEntry { kind = kind, text = text ?? string.Empty };
    }

    /// <summary>One baked definition of a content manifest.</summary>
    [Serializable]
    public sealed class ContentEntry
    {
        public string kind = string.Empty;
        public string authoringId = string.Empty;
        public string name = string.Empty;
        public int key;
        public string contentStamp = string.Empty;
        public ScriptableObject? asset;
    }

    /// <summary>One baked fact of a content manifest.</summary>
    [Serializable]
    public sealed class FactEntry
    {
        public string name = string.Empty;
        public int key;
        public int initial;
        public bool persistent = true;
        public string authoringId = string.Empty;
    }

}
