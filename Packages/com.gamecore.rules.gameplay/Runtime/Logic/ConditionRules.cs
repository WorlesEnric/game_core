// GameCore.Rules.Gameplay.Logic - pure condition evaluation (P1.4, catalog row 9).
//
// A condition set is a list of comparisons over committed gameplay state, combined with All or Any. The state is read
// through IConditionState, so the same evaluation runs inside a kernel stage (over live slots), in the host after a pump
// (over committed slots), in Studio tools (over a snapshot) and in plain dotnet tests. Every evaluation reports the
// first failed condition and every input it read, which is what the explain trace records ("why didn't the gate open").
#nullable enable
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Rules.Gameplay.Logic
{
    /// <summary>A comparison operator.</summary>
    public enum CompareOp
    {
        Equal = 0,
        NotEqual = 1,
        Less = 2,
        LessOrEqual = 3,
        Greater = 4,
        GreaterOrEqual = 5,
    }

    /// <summary>Comparison helpers.</summary>
    public static class Compare
    {
        public static bool Test(int left, CompareOp op, int right)
        {
            switch (op)
            {
                case CompareOp.Equal: return left == right;
                case CompareOp.NotEqual: return left != right;
                case CompareOp.Less: return left < right;
                case CompareOp.LessOrEqual: return left <= right;
                case CompareOp.Greater: return left > right;
                case CompareOp.GreaterOrEqual: return left >= right;
                default: return false;
            }
        }

        public static string Symbol(CompareOp op)
        {
            switch (op)
            {
                case CompareOp.Equal: return "==";
                case CompareOp.NotEqual: return "!=";
                case CompareOp.Less: return "<";
                case CompareOp.LessOrEqual: return "<=";
                case CompareOp.Greater: return ">";
                case CompareOp.GreaterOrEqual: return ">=";
                default: return "?";
            }
        }

        /// <summary>Parses ==, !=, &lt;, &lt;=, &gt;, &gt;= (and = as ==).</summary>
        public static bool TryParse(string text, out CompareOp op)
        {
            switch (text)
            {
                case "==":
                case "=":
                    op = CompareOp.Equal;
                    return true;
                case "!=":
                    op = CompareOp.NotEqual;
                    return true;
                case "<":
                    op = CompareOp.Less;
                    return true;
                case "<=":
                    op = CompareOp.LessOrEqual;
                    return true;
                case ">":
                    op = CompareOp.Greater;
                    return true;
                case ">=":
                    op = CompareOp.GreaterOrEqual;
                    return true;
                default:
                    op = CompareOp.Equal;
                    return false;
            }
        }
    }

    /// <summary>What a condition reads.</summary>
    public enum ConditionKind
    {
        /// <summary>A fact's value. Key = fact key.</summary>
        Fact = 0,

        /// <summary>How many of an item an inventory holds. Key = item key, Key2 = inventory key (0 = the actor's).</summary>
        ItemCount = 1,

        /// <summary>An inventory's currency. Key2 = inventory key (0 = the actor's).</summary>
        Currency = 2,

        /// <summary>A quest's status (0 inactive, 1 active, 2 completed, 3 failed). Key = quest key.</summary>
        QuestStatus = 3,

        /// <summary>A quest's current stage. Key = quest key.</summary>
        QuestStage = 4,

        /// <summary>The branch a quest took. Key = quest key.</summary>
        QuestBranch = 5,

        /// <summary>Whether a quest objective is done (0/1). Key = quest key, Key2 = objective index.</summary>
        ObjectiveDone = 6,

        /// <summary>The region an entity is in (its region key). Key2 = entity key (0 = the actor); compare with a region key.</summary>
        Region = 7,

        /// <summary>World domain time in milliseconds.</summary>
        TimeMs = 8,

        /// <summary>Whether a dialogue node was visited (0/1). Key = graph key, Key2 = node index.</summary>
        NodeVisited = 9,

        /// <summary>A named slot of an entity. Key = entity key (0 = the subject), Key2 = slot reference (resolved by the state).</summary>
        Slot = 10,

        /// <summary>How many times a rule fired. Key = rule key.</summary>
        RuleFired = 11,

        /// <summary>Whether a nested condition set holds (1/0). Key = condition set key.</summary>
        ConditionSet = 12,

        /// <summary>Always 1 (a placeholder that holds).</summary>
        Always = 13,
    }

    /// <summary>The quest fields a condition can read.</summary>
    public enum QuestField
    {
        Status = 0,
        Stage = 1,
        Branch = 2,
    }

    /// <summary>One comparison: <c>read(Kind, Key, Key2) Op Value</c>.</summary>
    public readonly struct ConditionModel
    {
        public ConditionModel(ConditionKind kind, int key, int key2, CompareOp op, int value, string label)
        {
            Kind = kind;
            Key = key;
            Key2 = key2;
            Op = op;
            Value = value;
            Label = label ?? string.Empty;
        }

        public ConditionKind Kind { get; }

        public int Key { get; }

        public int Key2 { get; }

        public CompareOp Op { get; }

        public int Value { get; }

        /// <summary>Human-readable name of what is read, e.g. <c>fact gate_open</c>.</summary>
        public string Label { get; }

        public string Describe() =>
            (Label.Length > 0 ? Label : Kind.ToString()) + " " + Compare.Symbol(Op) + " " + Value.ToString(CultureInfo.InvariantCulture);

        public override string ToString() => Describe();
    }

    /// <summary>How the conditions of a set combine.</summary>
    public enum ConditionMode
    {
        All = 0,
        Any = 1,
    }

    /// <summary>A reusable condition set (ConditionSetDefinition baked to keys).</summary>
    public sealed class ConditionSetModel
    {
        public ConditionSetModel(int key, string name, ConditionMode mode, IReadOnlyList<ConditionModel> conditions)
        {
            Key = key;
            Name = name ?? string.Empty;
            Mode = mode;
            Conditions = conditions ?? System.Array.Empty<ConditionModel>();
        }

        public int Key { get; }

        public string Name { get; }

        public ConditionMode Mode { get; }

        public IReadOnlyList<ConditionModel> Conditions { get; }
    }

    /// <summary>The gameplay state conditions read. Unknown things read as 0.</summary>
    public interface IConditionState
    {
        int Fact(int factKey);

        /// <summary>Count of an item in an inventory; inventory key 0 means the actor's inventory.</summary>
        int ItemCount(int inventoryKey, int itemKey, int actorKey);

        int Currency(int inventoryKey, int actorKey);

        int Quest(int questKey, QuestField field);

        int ObjectiveDone(int questKey, int objective);

        /// <summary>The region key of an entity (0 when unknown).</summary>
        int RegionOf(int entityKey);

        int NodeVisited(int graphKey, int node);

        /// <summary>A named slot of an entity (slot reference resolved by the implementation).</summary>
        int Slot(int entityKey, int slotRef);

        int RuleFired(int ruleKey);

        int NowMs { get; }
    }

    /// <summary>Resolves nested condition sets.</summary>
    public interface IConditionSetLookup
    {
        bool TryGet(int key, out ConditionSetModel? set);
    }

    /// <summary>The actor and subject of one evaluation.</summary>
    public readonly struct ConditionContext
    {
        public ConditionContext(int actorKey, int subjectKey)
        {
            ActorKey = actorKey;
            SubjectKey = subjectKey;
        }

        public int ActorKey { get; }

        public int SubjectKey { get; }
    }

    /// <summary>The outcome of one evaluation: pass/fail, the first failed condition and the inputs read.</summary>
    public sealed class ConditionResult
    {
        public ConditionResult(bool passed, int failedIndex, string failedCondition, IReadOnlyList<string> inputs)
        {
            Passed = passed;
            FailedIndex = failedIndex;
            FailedCondition = failedCondition ?? string.Empty;
            Inputs = inputs ?? System.Array.Empty<string>();
        }

        public bool Passed { get; }

        /// <summary>Index (in the evaluated set) of the first failed condition, or -1.</summary>
        public int FailedIndex { get; }

        public string FailedCondition { get; }

        public IReadOnlyList<string> Inputs { get; }

        public static ConditionResult Pass { get; } = new ConditionResult(true, -1, string.Empty, System.Array.Empty<string>());

        public override string ToString() => Passed ? "pass" : "fail: " + FailedCondition;
    }

    /// <summary>Pure condition evaluation.</summary>
    public static class ConditionRules
    {
        /// <summary>Deepest nesting of condition sets; deeper (or cyclic) nesting fails.</summary>
        public const int MaxDepth = 8;

        /// <summary>Evaluates a set. A null or empty set holds.</summary>
        public static ConditionResult Evaluate(ConditionSetModel? set, IConditionState state, IConditionSetLookup? lookup, in ConditionContext context)
        {
            if (set == null || set.Conditions.Count == 0)
            {
                return ConditionResult.Pass;
            }

            var inputs = new List<string>();
            int failed = EvaluateSet(set, state, lookup, context, 0, inputs, out string failedText);
            return failed < 0
                ? new ConditionResult(true, -1, string.Empty, inputs)
                : new ConditionResult(false, failed, failedText, inputs);
        }

        /// <summary>Evaluates one condition: true when it holds; <paramref name="left"/> is the value it read.</summary>
        public static bool Holds(ConditionModel condition, IConditionState state, IConditionSetLookup? lookup, in ConditionContext context, out int left)
        {
            left = Read(condition, state, lookup, context, 0, null);
            return Compare.Test(left, condition.Op, condition.Value);
        }

        /// <summary>The value a condition reads (its left-hand side).</summary>
        public static int Read(ConditionModel condition, IConditionState state, IConditionSetLookup? lookup, in ConditionContext context) =>
            Read(condition, state, lookup, context, 0, null);

        private static int EvaluateSet(
            ConditionSetModel set,
            IConditionState state,
            IConditionSetLookup? lookup,
            in ConditionContext context,
            int depth,
            List<string>? inputs,
            out string failedText)
        {
            failedText = string.Empty;
            int firstFailed = -1;
            string firstFailedText = string.Empty;
            for (int i = 0; i < set.Conditions.Count; i++)
            {
                ConditionModel condition = set.Conditions[i];
                int left = Read(condition, state, lookup, context, depth, inputs);
                bool holds = Compare.Test(left, condition.Op, condition.Value);
                if (inputs != null && inputs.Count < 64)
                {
                    inputs.Add(condition.Describe() + " : read " + left.ToString(CultureInfo.InvariantCulture) + (holds ? " (holds)" : " (fails)"));
                }

                if (set.Mode == ConditionMode.Any && holds)
                {
                    return -1;
                }

                if (!holds && firstFailed < 0)
                {
                    firstFailed = i;
                    firstFailedText = condition.Describe() + " (read " + left.ToString(CultureInfo.InvariantCulture) + ")";
                    if (set.Mode == ConditionMode.All)
                    {
                        failedText = firstFailedText;
                        return firstFailed;
                    }
                }
            }

            if (set.Mode == ConditionMode.Any)
            {
                failedText = set.Conditions.Count == 1 ? firstFailedText : "none of " + set.Conditions.Count.ToString(CultureInfo.InvariantCulture) + " conditions held; first: " + firstFailedText;
                return firstFailed < 0 ? 0 : firstFailed;
            }

            return -1;
        }

        private static int Read(
            ConditionModel condition,
            IConditionState state,
            IConditionSetLookup? lookup,
            in ConditionContext context,
            int depth,
            List<string>? inputs)
        {
            switch (condition.Kind)
            {
                case ConditionKind.Fact:
                    return state.Fact(condition.Key);
                case ConditionKind.ItemCount:
                    return state.ItemCount(condition.Key2, condition.Key, context.ActorKey);
                case ConditionKind.Currency:
                    return state.Currency(condition.Key2, context.ActorKey);
                case ConditionKind.QuestStatus:
                    return state.Quest(condition.Key, QuestField.Status);
                case ConditionKind.QuestStage:
                    return state.Quest(condition.Key, QuestField.Stage);
                case ConditionKind.QuestBranch:
                    return state.Quest(condition.Key, QuestField.Branch);
                case ConditionKind.ObjectiveDone:
                    return state.ObjectiveDone(condition.Key, condition.Key2);
                case ConditionKind.Region:
                    return state.RegionOf(condition.Key2 != 0 ? condition.Key2 : context.ActorKey);
                case ConditionKind.TimeMs:
                    return state.NowMs;
                case ConditionKind.NodeVisited:
                    return state.NodeVisited(condition.Key, condition.Key2);
                case ConditionKind.Slot:
                    return state.Slot(condition.Key != 0 ? condition.Key : context.SubjectKey, condition.Key2);
                case ConditionKind.RuleFired:
                    return state.RuleFired(condition.Key);
                case ConditionKind.ConditionSet:
                    if (depth + 1 > MaxDepth || lookup == null || !lookup.TryGet(condition.Key, out ConditionSetModel? nested) || nested == null)
                    {
                        return 0;
                    }

                    if (nested.Conditions.Count == 0)
                    {
                        return 1;
                    }

                    return EvaluateSet(nested, state, lookup, context, depth + 1, inputs, out string _) < 0 ? 1 : 0;
                case ConditionKind.Always:
                    return 1;
                default:
                    return 0;
            }
        }

        /// <summary>True when following nested condition sets from <paramref name="set"/> never revisits a set and stays within MaxDepth.</summary>
        public static bool IsAcyclic(ConditionSetModel set, IConditionSetLookup lookup)
        {
            var path = new HashSet<int>();
            return Walk(set, lookup, path, 0);
        }

        private static bool Walk(ConditionSetModel set, IConditionSetLookup lookup, HashSet<int> path, int depth)
        {
            if (depth > MaxDepth || !path.Add(set.Key))
            {
                return false;
            }

            for (int i = 0; i < set.Conditions.Count; i++)
            {
                ConditionModel condition = set.Conditions[i];
                if (condition.Kind != ConditionKind.ConditionSet)
                {
                    continue;
                }

                if (!lookup.TryGet(condition.Key, out ConditionSetModel? nested) || nested == null)
                {
                    continue;
                }

                if (!Walk(nested, lookup, path, depth + 1))
                {
                    return false;
                }
            }

            path.Remove(set.Key);
            return true;
        }
    }

    /// <summary>A dictionary-backed condition state (Studio tools, tests, simulations).</summary>
    public sealed class StateSnapshot : IConditionState
    {
        private readonly Dictionary<int, int> facts = new Dictionary<int, int>();
        private readonly Dictionary<long, int> items = new Dictionary<long, int>();
        private readonly Dictionary<int, int> currency = new Dictionary<int, int>();
        private readonly Dictionary<long, int> quests = new Dictionary<long, int>();
        private readonly Dictionary<long, int> objectives = new Dictionary<long, int>();
        private readonly Dictionary<int, int> regions = new Dictionary<int, int>();
        private readonly Dictionary<long, int> visited = new Dictionary<long, int>();
        private readonly Dictionary<long, int> slots = new Dictionary<long, int>();
        private readonly Dictionary<int, int> fired = new Dictionary<int, int>();

        /// <summary>The inventory key that inventory key 0 ("the actor's") resolves to.</summary>
        public int ActorInventoryKey { get; set; }

        public int NowMs { get; set; }

        public StateSnapshot SetFact(int factKey, int value)
        {
            facts[factKey] = value;
            return this;
        }

        public StateSnapshot SetItem(int inventoryKey, int itemKey, int count)
        {
            items[Pair(inventoryKey, itemKey)] = count;
            return this;
        }

        public StateSnapshot SetCurrency(int inventoryKey, int value)
        {
            currency[inventoryKey] = value;
            return this;
        }

        public StateSnapshot SetQuest(int questKey, QuestField field, int value)
        {
            quests[Pair(questKey, (int)field)] = value;
            return this;
        }

        public StateSnapshot SetObjectiveDone(int questKey, int objective, bool done)
        {
            objectives[Pair(questKey, objective)] = done ? 1 : 0;
            return this;
        }

        public StateSnapshot SetRegion(int entityKey, int regionKey)
        {
            regions[entityKey] = regionKey;
            return this;
        }

        public StateSnapshot SetVisited(int graphKey, int node, bool value)
        {
            visited[Pair(graphKey, node)] = value ? 1 : 0;
            return this;
        }

        public StateSnapshot SetSlot(int entityKey, int slotRef, int value)
        {
            slots[Pair(entityKey, slotRef)] = value;
            return this;
        }

        public StateSnapshot SetRuleFired(int ruleKey, int value)
        {
            fired[ruleKey] = value;
            return this;
        }

        public int Fact(int factKey) => facts.TryGetValue(factKey, out int value) ? value : 0;

        public int ItemCount(int inventoryKey, int itemKey, int actorKey) =>
            items.TryGetValue(Pair(inventoryKey == 0 ? ActorInventoryKey : inventoryKey, itemKey), out int value) ? value : 0;

        public int Currency(int inventoryKey, int actorKey) =>
            currency.TryGetValue(inventoryKey == 0 ? ActorInventoryKey : inventoryKey, out int value) ? value : 0;

        public int Quest(int questKey, QuestField field) => quests.TryGetValue(Pair(questKey, (int)field), out int value) ? value : 0;

        public int ObjectiveDone(int questKey, int objective) => objectives.TryGetValue(Pair(questKey, objective), out int value) ? value : 0;

        public int RegionOf(int entityKey) => regions.TryGetValue(entityKey, out int value) ? value : 0;

        public int NodeVisited(int graphKey, int node) => visited.TryGetValue(Pair(graphKey, node), out int value) ? value : 0;

        public int Slot(int entityKey, int slotRef) => slots.TryGetValue(Pair(entityKey, slotRef), out int value) ? value : 0;

        public int RuleFired(int ruleKey) => fired.TryGetValue(ruleKey, out int value) ? value : 0;

        private static long Pair(int a, int b) => ((long)a << 32) | (uint)b;
    }

    /// <summary>A dictionary-backed condition set lookup.</summary>
    public sealed class ConditionSetTable : IConditionSetLookup
    {
        private readonly Dictionary<int, ConditionSetModel> sets = new Dictionary<int, ConditionSetModel>();

        public int Count => sets.Count;

        public ConditionSetTable Add(ConditionSetModel set)
        {
            if (set != null)
            {
                sets[set.Key] = set;
            }

            return this;
        }

        public bool TryGet(int key, out ConditionSetModel? set)
        {
            if (sets.TryGetValue(key, out ConditionSetModel found))
            {
                set = found;
                return true;
            }

            set = null;
            return false;
        }
    }

    /// <summary>
    /// A state that overrides some facts on top of another state: the dialogue rules use it so a branch right after an
    /// action node sees the facts that node set in the same step.
    /// </summary>
    public sealed class FactOverlayState : IConditionState
    {
        private readonly IConditionState inner;
        private readonly IReadOnlyDictionary<int, int> overrides;

        public FactOverlayState(IConditionState inner, IReadOnlyDictionary<int, int> overrides)
        {
            this.inner = inner;
            this.overrides = overrides;
        }

        public int NowMs => inner.NowMs;

        public int Fact(int factKey) => overrides.TryGetValue(factKey, out int value) ? value : inner.Fact(factKey);

        public int ItemCount(int inventoryKey, int itemKey, int actorKey) => inner.ItemCount(inventoryKey, itemKey, actorKey);

        public int Currency(int inventoryKey, int actorKey) => inner.Currency(inventoryKey, actorKey);

        public int Quest(int questKey, QuestField field) => inner.Quest(questKey, field);

        public int ObjectiveDone(int questKey, int objective) => inner.ObjectiveDone(questKey, objective);

        public int RegionOf(int entityKey) => inner.RegionOf(entityKey);

        public int NodeVisited(int graphKey, int node) => inner.NodeVisited(graphKey, node);

        public int Slot(int entityKey, int slotRef) => inner.Slot(entityKey, slotRef);

        public int RuleFired(int ruleKey) => inner.RuleFired(ruleKey);
    }
}
