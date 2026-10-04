// GameCore.Rules.Gameplay.Logic - pure action sets (P1.4, catalog row 9).
//
// An action set is an ordered list of actions. Nested sets are expanded at boot (Flatten), so the kernel and the
// delivery only ever see flat lists whose ordinals are stable: action (set key, ordinal) names exactly one action,
// which is what the logic stage commits as one ActionDue event per action and what the outbox delivers once each.
#nullable enable
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Rules.Gameplay.Logic
{
    /// <summary>What an action does.</summary>
    public enum ActionKind
    {
        /// <summary>narrative.setFact: Key = fact key, Value = value.</summary>
        SetFact = 0,

        /// <summary>narrative.setFact with the current value plus Value (counter facts). Key = fact key.</summary>
        AddFact = 1,

        /// <summary>inv.grant: Key = item key, Value = count, Key2 = inventory key (0 = the actor's).</summary>
        Grant = 2,

        /// <summary>inv.consume: Key = item key, Value = count, Key2 = inventory key (0 = the actor's).</summary>
        Consume = 3,

        /// <summary>quest.start: Key = quest key.</summary>
        StartQuest = 4,

        /// <summary>quest.advance: Key = quest key, Value = stage (-1 = the next stage).</summary>
        AdvanceQuest = 5,

        /// <summary>quest.complete: Key = quest key.</summary>
        CompleteQuest = 6,

        /// <summary>quest.fail: Key = quest key.</summary>
        FailQuest = 7,

        /// <summary>entity.setVariant on an interactable: Ref = entity authoring id (empty = the subject), Value = state (variant).</summary>
        SetInteractableState = 8,

        /// <summary>A presentation cue through the feedback sink: Text = cue id.</summary>
        PlayAudio = 9,

        /// <summary>entity.spawn: Ref = entity authoring id (empty = the subject).</summary>
        Spawn = 10,

        /// <summary>entity.despawn: Ref = entity authoring id (empty = the subject).</summary>
        Despawn = 11,

        /// <summary>world.travel of the actor: Ref = region authoring id.</summary>
        Travel = 12,

        /// <summary>A message through the message sink: Text = message.</summary>
        ShowMessage = 13,

        /// <summary>dialogue.start: Key = graph key, Ref = speaker NPC authoring id (may be empty).</summary>
        StartDialogue = 14,

        /// <summary>Runs another action set (expanded by Flatten). Key = action set key.</summary>
        RunActionSet = 15,

        /// <summary>inv.pickup: Ref = world item authoring id (empty = the subject).</summary>
        Pickup = 16,
    }

    /// <summary>One action.</summary>
    public readonly struct ActionModel
    {
        public ActionModel(ActionKind kind, int key, int key2, int value, string reference, string text, string label)
        {
            Kind = kind;
            Key = key;
            Key2 = key2;
            Value = value;
            Ref = reference ?? string.Empty;
            Text = text ?? string.Empty;
            Label = label ?? string.Empty;
        }

        public ActionKind Kind { get; }

        public int Key { get; }

        public int Key2 { get; }

        public int Value { get; }

        /// <summary>Authoring id of an entity, region or world item the action is about (empty = the evaluation subject).</summary>
        public string Ref { get; }

        public string Text { get; }

        /// <summary>Human-readable name of what the action touches, e.g. <c>fact gate_open</c>.</summary>
        public string Label { get; }

        public static ActionModel SetFact(int factKey, int value, string label) =>
            new ActionModel(ActionKind.SetFact, factKey, 0, value, string.Empty, string.Empty, label);

        public static ActionModel Grant(int itemKey, int count, string label) =>
            new ActionModel(ActionKind.Grant, itemKey, 0, count, string.Empty, string.Empty, label);

        public string Describe()
        {
            string what = Label.Length > 0 ? Label : (Ref.Length > 0 ? Ref : Key.ToString(CultureInfo.InvariantCulture));
            string value = Value.ToString(CultureInfo.InvariantCulture);
            switch (Kind)
            {
                case ActionKind.SetFact: return "set " + what + " = " + value;
                case ActionKind.AddFact: return "add " + value + " to " + what;
                case ActionKind.Grant: return "grant " + value + " x " + what;
                case ActionKind.Consume: return "consume " + value + " x " + what;
                case ActionKind.StartQuest: return "start quest " + what;
                case ActionKind.AdvanceQuest: return "advance quest " + what + (Value < 0 ? string.Empty : " to stage " + value);
                case ActionKind.CompleteQuest: return "complete quest " + what;
                case ActionKind.FailQuest: return "fail quest " + what;
                case ActionKind.SetInteractableState: return "set state of " + (Ref.Length > 0 ? what : "subject") + " = " + value;
                case ActionKind.PlayAudio: return "play " + Text;
                case ActionKind.Spawn: return "spawn " + (Ref.Length > 0 ? what : "subject");
                case ActionKind.Despawn: return "despawn " + (Ref.Length > 0 ? what : "subject");
                case ActionKind.Travel: return "travel to " + what;
                case ActionKind.ShowMessage: return "show \"" + Text + "\"";
                case ActionKind.StartDialogue: return "start dialogue " + what;
                case ActionKind.RunActionSet: return "run " + what;
                case ActionKind.Pickup: return "pick up " + (Ref.Length > 0 ? what : "subject");
                default: return Kind.ToString();
            }
        }

        public override string ToString() => Describe();
    }

    /// <summary>A reusable action set (ActionSetDefinition baked to keys).</summary>
    public sealed class ActionSetModel
    {
        public ActionSetModel(int key, string name, IReadOnlyList<ActionModel> actions)
        {
            Key = key;
            Name = name ?? string.Empty;
            Actions = actions ?? System.Array.Empty<ActionModel>();
        }

        public int Key { get; }

        public string Name { get; }

        public IReadOnlyList<ActionModel> Actions { get; }

        /// <summary>True when every action is a fact action (setFact/addFact), or the set is empty.</summary>
        public bool OnlyFacts
        {
            get
            {
                for (int i = 0; i < Actions.Count; i++)
                {
                    if (!ActionRules.IsFactAction(Actions[i].Kind))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    /// <summary>Resolves action sets by key.</summary>
    public interface IActionSetLookup
    {
        bool TryGet(int key, out ActionSetModel? set);
    }

    /// <summary>A dictionary-backed action set lookup.</summary>
    public sealed class ActionSetTable : IActionSetLookup
    {
        private readonly Dictionary<int, ActionSetModel> sets = new Dictionary<int, ActionSetModel>();

        public int Count => sets.Count;

        public ActionSetTable Add(ActionSetModel set)
        {
            if (set != null)
            {
                sets[set.Key] = set;
            }

            return this;
        }

        public bool TryGet(int key, out ActionSetModel? set)
        {
            if (sets.TryGetValue(key, out ActionSetModel found))
            {
                set = found;
                return true;
            }

            set = null;
            return false;
        }
    }

    /// <summary>Pure action-set operations.</summary>
    public static class ActionRules
    {
        /// <summary>Deepest nesting of RunActionSet.</summary>
        public const int MaxDepth = 8;

        /// <summary>Most actions one flattened set may hold (one ActionDue event each, within one step's event budget).</summary>
        public const int MaxActions = 24;

        public static bool IsFactAction(ActionKind kind) => kind == ActionKind.SetFact || kind == ActionKind.AddFact;

        /// <summary>
        /// Expands nested RunActionSet actions depth-first, in order. A missing, cyclic or too-deep nested set is skipped
        /// and reported in <paramref name="problems"/>; the result never holds RunActionSet.
        /// </summary>
        public static ActionSetModel Flatten(ActionSetModel set, IActionSetLookup lookup, List<string>? problems)
        {
            var flat = new List<ActionModel>();
            var path = new HashSet<int>();
            Expand(set, lookup, path, 0, flat, problems);
            if (flat.Count > MaxActions)
            {
                if (problems != null)
                {
                    problems.Add(set.Name + " expands to " + flat.Count.ToString(CultureInfo.InvariantCulture)
                        + " actions; at most " + MaxActions.ToString(CultureInfo.InvariantCulture) + " run");
                }

                flat.RemoveRange(MaxActions, flat.Count - MaxActions);
            }

            return new ActionSetModel(set.Key, set.Name, flat);
        }

        private static void Expand(ActionSetModel set, IActionSetLookup lookup, HashSet<int> path, int depth, List<ActionModel> into, List<string>? problems)
        {
            if (!path.Add(set.Key))
            {
                if (problems != null)
                {
                    problems.Add("action set " + set.Name + " runs itself (cycle)");
                }

                return;
            }

            for (int i = 0; i < set.Actions.Count; i++)
            {
                ActionModel action = set.Actions[i];
                if (action.Kind != ActionKind.RunActionSet)
                {
                    into.Add(action);
                    continue;
                }

                if (depth + 1 > MaxDepth)
                {
                    if (problems != null)
                    {
                        problems.Add("action set " + set.Name + " nests deeper than " + MaxDepth.ToString(CultureInfo.InvariantCulture));
                    }

                    continue;
                }

                if (!lookup.TryGet(action.Key, out ActionSetModel? nested) || nested == null)
                {
                    if (problems != null)
                    {
                        problems.Add("action set " + set.Name + " runs a missing set (" + action.Describe() + ")");
                    }

                    continue;
                }

                Expand(nested, lookup, path, depth + 1, into, problems);
            }

            path.Remove(set.Key);
        }

        /// <summary>The value a setFact/addFact action writes, given the fact's current value.</summary>
        public static int FactValueAfter(ActionModel action, int current)
        {
            if (action.Kind == ActionKind.AddFact)
            {
                long sum = (long)current + action.Value;
                return sum > int.MaxValue ? int.MaxValue : (sum < int.MinValue ? int.MinValue : (int)sum);
            }

            return action.Value;
        }
    }
}
