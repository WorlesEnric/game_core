// GameCore.Rules.Gameplay.Logic - pure rules: trigger matching and the fire/skip decision (P1.4, catalog row 9).
//
// A rule is "when <trigger event> (matching a filter), if <conditions>, run <actions>", with optional once, cooldown and
// a fire limit. The trigger is matched host-side against committed events; the decision is made in the logic stage over
// the rule's own slots (logic.fired, logic.cooldownMs, logic.counter) and the committed gameplay state, so a rule fires
// at most once per evaluation, deterministically, and its state survives save and restore with the other slots.
#nullable enable
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Rules.Gameplay.Logic
{
    /// <summary>The event kinds a rule can be triggered by.</summary>
    public enum TriggerKind
    {
        /// <summary>Never triggered by an event (only by logic.test or an explicit logic.evaluate).</summary>
        Manual = 0,

        /// <summary>FactSet. Filter key = fact key, filter value = the new value.</summary>
        FactSet = 1,

        /// <summary>ItemGranted. Filter key = item key, filter value = count granted.</summary>
        ItemGranted = 2,

        /// <summary>ItemConsumed. Filter key = item key.</summary>
        ItemConsumed = 3,

        /// <summary>TradeDone. Filter key = vendor key, filter value = item key.</summary>
        TradeDone = 4,

        /// <summary>QuestStarted. Filter key = quest key.</summary>
        QuestStarted = 5,

        /// <summary>StageEntered. Filter key = quest key, filter value = stage.</summary>
        StageEntered = 6,

        /// <summary>QuestCompleted. Filter key = quest key, filter value = branch.</summary>
        QuestCompleted = 7,

        /// <summary>QuestFailed. Filter key = quest key.</summary>
        QuestFailed = 8,

        /// <summary>DialogueEnded. Filter key = graph key, filter value = last node.</summary>
        DialogueEnded = 9,

        /// <summary>ChoiceMade. Filter key = graph key, filter value = option index.</summary>
        ChoiceMade = 10,

        /// <summary>RegionEntered (world). Filter key = region key, filter value = traveller entity key.</summary>
        RegionEntered = 11,

        /// <summary>A successful interaction (P1.3's interaction.event.succeeded, or an action run). Filter key = subject key.</summary>
        Interacted = 12,

        /// <summary>RuleFired of another rule. Filter key = rule key.</summary>
        RuleFired = 13,

        /// <summary>ItemPickedUp. Filter key = world item key, filter value = item key.</summary>
        ItemPickedUp = 14,
    }

    /// <summary>The trigger of a rule: an event kind and an optional filter.</summary>
    public readonly struct TriggerModel
    {
        /// <summary>The filter value that matches every event value.</summary>
        public const int AnyValue = int.MinValue;

        public TriggerModel(TriggerKind kind, int filterKey, int filterValue, string label)
        {
            Kind = kind;
            FilterKey = filterKey;
            FilterValue = filterValue;
            Label = label ?? string.Empty;
        }

        public TriggerKind Kind { get; }

        /// <summary>0 matches every key.</summary>
        public int FilterKey { get; }

        /// <summary><see cref="AnyValue"/> matches every value.</summary>
        public int FilterValue { get; }

        public string Label { get; }

        public string Describe() =>
            Kind + (FilterKey != 0 ? " " + (Label.Length > 0 ? Label : FilterKey.ToString(CultureInfo.InvariantCulture)) : string.Empty)
            + (FilterValue != AnyValue ? " = " + FilterValue.ToString(CultureInfo.InvariantCulture) : string.Empty);
    }

    /// <summary>A rule (RuleDefinition baked to models).</summary>
    public sealed class RuleModel
    {
        public RuleModel(
            int key,
            string name,
            TriggerModel trigger,
            ConditionSetModel? conditions,
            ActionSetModel? actions,
            bool once,
            int cooldownMs,
            int maxFires,
            int priority)
        {
            Key = key;
            Name = name ?? string.Empty;
            Trigger = trigger;
            Conditions = conditions;
            Actions = actions;
            Once = once;
            CooldownMs = cooldownMs < 0 ? 0 : cooldownMs;
            MaxFires = maxFires < 0 ? 0 : maxFires;
            Priority = priority;
        }

        public int Key { get; }

        public string Name { get; }

        public TriggerModel Trigger { get; }

        /// <summary>Null = always holds.</summary>
        public ConditionSetModel? Conditions { get; }

        /// <summary>Flattened actions; null = fires without effect (still counts and commits RuleFired).</summary>
        public ActionSetModel? Actions { get; }

        public bool Once { get; }

        /// <summary>Domain milliseconds after a fire during which the rule skips.</summary>
        public int CooldownMs { get; }

        /// <summary>0 = unlimited.</summary>
        public int MaxFires { get; }

        /// <summary>Rules triggered by one event evaluate in ascending priority, then ascending key.</summary>
        public int Priority { get; }
    }

    /// <summary>The persistent state of one rule (its three slots).</summary>
    public readonly struct RuleState
    {
        public RuleState(int fired, int cooldownUntilMs, int counter)
        {
            Fired = fired;
            CooldownUntilMs = cooldownUntilMs;
            Counter = counter;
        }

        /// <summary>logic.fired: how many times the rule fired.</summary>
        public int Fired { get; }

        /// <summary>logic.cooldownMs: domain ms at which the rule is ready again (0 = ready).</summary>
        public int CooldownUntilMs { get; }

        /// <summary>logic.counter: how many times the rule was evaluated.</summary>
        public int Counter { get; }
    }

    /// <summary>Why a rule did not fire.</summary>
    public enum SkipReason
    {
        None = 0,
        Condition = 1,
        Cooldown = 2,
        Once = 3,
        MaxFires = 4,
    }

    /// <summary>The fire/skip decision of one evaluation and the rule's next state.</summary>
    public readonly struct RuleDecision
    {
        public RuleDecision(bool fire, SkipReason reason, RuleState next, ConditionResult? conditions)
        {
            Fire = fire;
            Reason = reason;
            Next = next;
            Conditions = conditions;
        }

        public bool Fire { get; }

        public SkipReason Reason { get; }

        public RuleState Next { get; }

        /// <summary>The condition evaluation (null when the rule was skipped before conditions were read).</summary>
        public ConditionResult? Conditions { get; }

        public static string ReasonName(SkipReason reason)
        {
            switch (reason)
            {
                case SkipReason.None: return "fired";
                case SkipReason.Condition: return "condition";
                case SkipReason.Cooldown: return "cooldown";
                case SkipReason.Once: return "once";
                case SkipReason.MaxFires: return "maxFires";
                default: return reason.ToString();
            }
        }
    }

    /// <summary>Pure rule operations.</summary>
    public static class RuleRules
    {
        /// <summary>True when an event of <paramref name="kind"/> with <paramref name="key"/>/<paramref name="value"/> matches the trigger.</summary>
        public static bool Matches(TriggerModel trigger, TriggerKind kind, int key, int value)
        {
            if (trigger.Kind == TriggerKind.Manual || trigger.Kind != kind)
            {
                return false;
            }

            if (trigger.FilterKey != 0 && trigger.FilterKey != key)
            {
                return false;
            }

            return trigger.FilterValue == TriggerModel.AnyValue || trigger.FilterValue == value;
        }

        /// <summary>
        /// Decides one evaluation: once/maxFires/cooldown gates first (no conditions read), then the conditions. Every
        /// evaluation counts; a fire increments logic.fired and starts the cooldown.
        /// </summary>
        public static RuleDecision Decide(
            RuleModel rule,
            RuleState state,
            int nowMs,
            IConditionState world,
            IConditionSetLookup? sets,
            in ConditionContext context)
        {
            int counter = state.Counter == int.MaxValue ? int.MaxValue : state.Counter + 1;
            if (rule.Once && state.Fired > 0)
            {
                return new RuleDecision(false, SkipReason.Once, new RuleState(state.Fired, state.CooldownUntilMs, counter), null);
            }

            if (rule.MaxFires > 0 && state.Fired >= rule.MaxFires)
            {
                return new RuleDecision(false, SkipReason.MaxFires, new RuleState(state.Fired, state.CooldownUntilMs, counter), null);
            }

            if (state.CooldownUntilMs > 0 && nowMs < state.CooldownUntilMs)
            {
                return new RuleDecision(false, SkipReason.Cooldown, new RuleState(state.Fired, state.CooldownUntilMs, counter), null);
            }

            ConditionResult conditions = ConditionRules.Evaluate(rule.Conditions, world, sets, context);
            if (!conditions.Passed)
            {
                return new RuleDecision(false, SkipReason.Condition, new RuleState(state.Fired, state.CooldownUntilMs, counter), conditions);
            }

            long until = rule.CooldownMs > 0 ? (long)nowMs + rule.CooldownMs : 0L;
            int cooldown = until > int.MaxValue ? int.MaxValue : (int)until;
            int fired = state.Fired == int.MaxValue ? int.MaxValue : state.Fired + 1;
            return new RuleDecision(true, SkipReason.None, new RuleState(fired, cooldown, counter), conditions);
        }

        /// <summary>The rules a trigger event selects, in evaluation order (priority, then key).</summary>
        public static List<RuleModel> Select(IEnumerable<RuleModel> rules, TriggerKind kind, int key, int value)
        {
            var selected = new List<RuleModel>();
            foreach (RuleModel rule in rules)
            {
                if (Matches(rule.Trigger, kind, key, value))
                {
                    selected.Add(rule);
                }
            }

            selected.Sort((l, r) => l.Priority != r.Priority ? l.Priority.CompareTo(r.Priority) : l.Key.CompareTo(r.Key));
            return selected;
        }
    }

    /// <summary>A bounded ring of the most recent items (the explain trace keeps 256 evaluations).</summary>
    public sealed class BoundedRing<T>
    {
        private readonly T[] items;
        private int next;

        public BoundedRing(int capacity)
        {
            items = new T[capacity < 1 ? 1 : capacity];
        }

        public int Capacity => items.Length;

        public int Count { get; private set; }

        /// <summary>Total items ever added (Count stops at Capacity).</summary>
        public long Added { get; private set; }

        public void Add(T item)
        {
            items[next] = item;
            next = (next + 1) % items.Length;
            if (Count < items.Length)
            {
                Count++;
            }

            Added++;
        }

        /// <summary>The item <paramref name="age"/> places back (0 = newest).</summary>
        public T At(int age)
        {
            if (age < 0 || age >= Count)
            {
                throw new System.ArgumentOutOfRangeException(nameof(age));
            }

            int index = next - 1 - age;
            if (index < 0)
            {
                index += items.Length;
            }

            return items[index];
        }

        /// <summary>Up to <paramref name="max"/> items, newest first.</summary>
        public List<T> Recent(int max)
        {
            int take = max < Count ? (max < 0 ? 0 : max) : Count;
            var recent = new List<T>(take);
            for (int i = 0; i < take; i++)
            {
                recent.Add(At(i));
            }

            return recent;
        }

        public void Clear()
        {
            System.Array.Clear(items, 0, items.Length);
            next = 0;
            Count = 0;
        }
    }

    /// <summary>
    /// The applied-request ring of an idempotent target: the last <c>Size</c> request ids, written round-robin. A
    /// command whose request id is in the ring was already applied and is acknowledged without a second mutation.
    /// </summary>
    public static class RequestRing
    {
        public const int Size = 8;

        /// <summary>True for a request id that idempotency applies to (0 = no request id, never deduplicated).</summary>
        public static bool IsTracked(int requestId) => requestId != 0;

        public static bool Contains(IReadOnlyList<int> ring, int requestId)
        {
            if (!IsTracked(requestId))
            {
                return false;
            }

            for (int i = 0; i < ring.Count; i++)
            {
                if (ring[i] == requestId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Writes <paramref name="requestId"/> at <paramref name="head"/>; returns the slot index written and the next head.</summary>
        public static int Push(int[] ring, int head, int requestId, out int nextHead)
        {
            int slot = ((head % ring.Length) + ring.Length) % ring.Length;
            ring[slot] = requestId;
            nextHead = (slot + 1) % ring.Length;
            return slot;
        }
    }
}
