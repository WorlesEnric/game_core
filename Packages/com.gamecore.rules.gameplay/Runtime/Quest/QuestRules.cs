// GameCore.Rules.Gameplay.Quest - pure quest rules (P1.4, catalog row 7).
//
// A quest is an ordered list of stages; each stage has objectives (talk, collect, reach, interact, fact) with a target
// and a required count. An objective with branch 0 is needed on every path; objectives with branch b > 0 form the
// alternative b ("pay Odd" = branch 1, "persuade Odd" = branch 2). A stage completes when all its branch-0 objectives
// are done and, if it has branched objectives, every objective of at least one branch is done (the lowest such branch
// is recorded in quest.branch). The last stage completing completes the quest and grants its rewards, each reward as
// one RewardGranted event that the outbox delivers exactly once.
//
// Objective counts are level-triggered for fact/collect/reach (recomputed from committed state) and edge-triggered for
// talk/interact (counted per matching event); QuestTracking computes the objective updates both the host tracker and
// the quest.simulate tool submit, so the simulation and the running game agree by construction.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Rules.Gameplay.Logic;

namespace GameCore.Rules.Gameplay.Quest
{
    /// <summary>What an objective tracks.</summary>
    public enum ObjectiveKind
    {
        /// <summary>A conversation ended. Target = dialogue graph key. Edge-triggered.</summary>
        Talk = 0,

        /// <summary>The player holds items. Target = item key. Level-triggered (count = held).</summary>
        Collect = 1,

        /// <summary>The player is in a region. Target = region key. Level-triggered (latched once done).</summary>
        Reach = 2,

        /// <summary>The player used an interactable. Target = subject key. Edge-triggered.</summary>
        Interact = 3,

        /// <summary>A fact holds a value. Target = fact key; done when the fact's value reaches the count. Level-triggered.</summary>
        Fact = 4,
    }

    /// <summary>One objective of a quest stage.</summary>
    public sealed class ObjectiveModel
    {
        public ObjectiveModel(int index, int stage, ObjectiveKind kind, int targetKey, int required, int branch, string text, string targetLabel)
        {
            Index = index;
            Stage = stage;
            Kind = kind;
            TargetKey = targetKey;
            Required = required < 1 ? 1 : required;
            Branch = branch < 0 ? 0 : branch;
            Text = text ?? string.Empty;
            TargetLabel = targetLabel ?? string.Empty;
        }

        /// <summary>Index within the quest (the n of quest.obj.n.*).</summary>
        public int Index { get; }

        public int Stage { get; }

        public ObjectiveKind Kind { get; }

        public int TargetKey { get; }

        public int Required { get; }

        /// <summary>0 = needed on every path; b &gt; 0 = part of alternative b.</summary>
        public int Branch { get; }

        public string Text { get; }

        public string TargetLabel { get; }

        public bool IsLevel => Kind == ObjectiveKind.Collect || Kind == ObjectiveKind.Reach || Kind == ObjectiveKind.Fact;

        public string Describe() =>
            Kind + " " + (TargetLabel.Length > 0 ? TargetLabel : TargetKey.ToString(CultureInfo.InvariantCulture))
            + (Required > 1 ? " x" + Required.ToString(CultureInfo.InvariantCulture) : string.Empty)
            + (Branch > 0 ? " [branch " + Branch.ToString(CultureInfo.InvariantCulture) + "]" : string.Empty);
    }

    /// <summary>One stage of a quest.</summary>
    public sealed class StageModel
    {
        public StageModel(int index, string title, string description, IReadOnlyList<int> objectives, int next)
        {
            Index = index;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            Objectives = objectives ?? Array.Empty<int>();
            Next = next;
        }

        public int Index { get; }

        public string Title { get; }

        public string Description { get; }

        /// <summary>Indices (into the quest's objectives) of this stage's objectives.</summary>
        public IReadOnlyList<int> Objectives { get; }

        /// <summary>The stage after this one; -1 = the next index (or completion after the last stage).</summary>
        public int Next { get; }
    }

    /// <summary>What a reward grants.</summary>
    public enum RewardKind
    {
        Item = 1,
        Fact = 2,
    }

    /// <summary>One reward of a quest.</summary>
    public sealed class RewardModel
    {
        public RewardModel(RewardKind kind, int key, int value, int branch, string label)
        {
            Kind = kind;
            Key = key;
            Value = value;
            Branch = branch < 0 ? 0 : branch;
            Label = label ?? string.Empty;
        }

        public RewardKind Kind { get; }

        /// <summary>Item key or fact key.</summary>
        public int Key { get; }

        /// <summary>Item count or fact value.</summary>
        public int Value { get; }

        /// <summary>0 = granted on every path; b = only when the quest took branch b.</summary>
        public int Branch { get; }

        public string Label { get; }

        public string Describe() =>
            (Kind == RewardKind.Item ? "item " : "fact ") + (Label.Length > 0 ? Label : Key.ToString(CultureInfo.InvariantCulture))
            + (Kind == RewardKind.Item ? " x" : " = ") + Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A quest definition baked to a model.</summary>
    public sealed class QuestModel
    {
        public QuestModel(
            int key,
            string name,
            IReadOnlyList<StageModel> stages,
            IReadOnlyList<ObjectiveModel> objectives,
            IReadOnlyList<RewardModel> rewards,
            ConditionSetModel? failConditions,
            IReadOnlyList<string>? branchNames)
            : this(key, name, stages, objectives, rewards, failConditions, branchNames, null, null)
        {
        }

        /// <summary>A quest with prerequisite and dependent quest keys (P1.7a; the converter fills them from the definition).</summary>
        public QuestModel(
            int key,
            string name,
            IReadOnlyList<StageModel> stages,
            IReadOnlyList<ObjectiveModel> objectives,
            IReadOnlyList<RewardModel> rewards,
            ConditionSetModel? failConditions,
            IReadOnlyList<string>? branchNames,
            IReadOnlyList<int>? prerequisites,
            IReadOnlyList<int>? dependents)
        {
            Key = key;
            Name = name ?? string.Empty;
            Stages = stages ?? Array.Empty<StageModel>();
            Objectives = objectives ?? Array.Empty<ObjectiveModel>();
            Rewards = rewards ?? Array.Empty<RewardModel>();
            FailConditions = failConditions;
            BranchNames = branchNames ?? Array.Empty<string>();
            Prerequisites = prerequisites ?? Array.Empty<int>();
            Dependents = dependents ?? Array.Empty<int>();
        }

        public int Key { get; }

        public string Name { get; }

        public IReadOnlyList<StageModel> Stages { get; }

        public IReadOnlyList<ObjectiveModel> Objectives { get; }

        public IReadOnlyList<RewardModel> Rewards { get; }

        /// <summary>When these hold while the quest is active, the tracker fails it (null = never).</summary>
        public ConditionSetModel? FailConditions { get; }

        /// <summary>Display names of branches 1..n (index 0 = branch 1).</summary>
        public IReadOnlyList<string> BranchNames { get; }

        /// <summary>Quest keys that must be completed before this quest may start (P1.7a).</summary>
        public IReadOnlyList<int> Prerequisites { get; }

        /// <summary>Quest keys closed (failed) when this quest fails, besides every quest that lists it as a prerequisite (P1.7a).</summary>
        public IReadOnlyList<int> Dependents { get; }

        public string BranchName(int branch) =>
            branch >= 1 && branch <= BranchNames.Count ? BranchNames[branch - 1] : "branch " + branch.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Why a quest command was refused.</summary>
    public enum QuestRefusal
    {
        None = 0,
        NotActive = 1,
        AlreadyStarted = 2,
        StageOutOfRange = 3,
        ObjectiveOutOfRange = 4,

        /// <summary>A prerequisite quest is not completed (P1.7a).</summary>
        PrerequisitesUnmet = 5,
    }

    /// <summary>The mutable state of one quest (its slots).</summary>
    public sealed class QuestState
    {
        public QuestState(int objectiveCount)
        {
            int n = objectiveCount < 0 ? 0 : objectiveCount;
            Counts = new int[n];
            Done = new int[n];
        }

        /// <summary>quest.status: 0 inactive, 1 active, 2 completed, 3 failed.</summary>
        public int Status { get; set; }

        public int Stage { get; set; }

        public int Branch { get; set; }

        public int[] Counts { get; }

        public int[] Done { get; }

        public QuestState Clone()
        {
            var copy = new QuestState(Counts.Length) { Status = Status, Stage = Stage, Branch = Branch };
            Array.Copy(Counts, copy.Counts, Counts.Length);
            Array.Copy(Done, copy.Done, Done.Length);
            return copy;
        }
    }

    /// <summary>What one quest operation produced (each becomes one committed event).</summary>
    public enum QuestEventKind
    {
        /// <summary>A = first stage.</summary>
        Started = 0,

        /// <summary>A = stage, B = previous stage, C = branch.</summary>
        StageEntered = 1,

        /// <summary>A = objective, B = count, C = done.</summary>
        ObjectiveUpdated = 2,

        /// <summary>A = last stage, B = branch.</summary>
        Completed = 3,

        /// <summary>A = stage.</summary>
        Failed = 4,

        /// <summary>A = reward index, B = kind, C = item or fact key, D = count or value.</summary>
        RewardGranted = 5,
    }

    /// <summary>One produced quest event.</summary>
    public readonly struct QuestEvent
    {
        public QuestEvent(QuestEventKind kind, int a, int b, int c, int d)
        {
            Kind = kind;
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public QuestEventKind Kind { get; }

        public int A { get; }

        public int B { get; }

        public int C { get; }

        public int D { get; }

        public override string ToString() =>
            Kind + "(" + A.ToString(CultureInfo.InvariantCulture) + "," + B.ToString(CultureInfo.InvariantCulture) + ","
            + C.ToString(CultureInfo.InvariantCulture) + "," + D.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Pure quest transitions. Every operation mutates the state only when accepted.</summary>
    public static class QuestRules
    {
        public const int Inactive = 0;
        public const int Active = 1;
        public const int Completed = 2;
        public const int Failed = 3;

        public static QuestRefusal Start(QuestModel quest, QuestState state, List<QuestEvent> events)
        {
            if (state.Status != Inactive)
            {
                return QuestRefusal.AlreadyStarted;
            }

            if (quest.Stages.Count == 0)
            {
                return QuestRefusal.StageOutOfRange;
            }

            state.Status = Active;
            state.Stage = 0;
            state.Branch = 0;
            Array.Clear(state.Counts, 0, state.Counts.Length);
            Array.Clear(state.Done, 0, state.Done.Length);
            events.Add(new QuestEvent(QuestEventKind.Started, 0, 0, 0, 0));
            events.Add(new QuestEvent(QuestEventKind.StageEntered, 0, -1, 0, 0));
            CheckStage(quest, state, events);
            return QuestRefusal.None;
        }

        /// <summary>
        /// True when every prerequisite of <paramref name="quest"/> is completed; <paramref name="statusOf"/> returns a quest
        /// key's quest.status (an unknown key reads as inactive, so it blocks). <paramref name="unmet"/> is the first one
        /// that is not (0 when all are).
        /// </summary>
        public static bool PrerequisitesMet(QuestModel quest, Func<int, int> statusOf, out int unmet)
        {
            unmet = 0;
            for (int i = 0; i < quest.Prerequisites.Count; i++)
            {
                int key = quest.Prerequisites[i];
                if (statusOf(key) != Completed)
                {
                    unmet = key;
                    return false;
                }
            }

            return true;
        }

        /// <summary><see cref="Start"/> refused with PrerequisitesUnmet when a prerequisite quest is not completed (P1.7a).</summary>
        public static QuestRefusal Start(QuestModel quest, QuestState state, Func<int, int> statusOf, List<QuestEvent> events)
        {
            if (state.Status == Inactive && !PrerequisitesMet(quest, statusOf, out int _))
            {
                return QuestRefusal.PrerequisitesUnmet;
            }

            return Start(quest, state, events);
        }

        /// <summary>
        /// The quests a failure of <paramref name="failed"/> closes, in key order without duplicates: its declared
        /// dependents and every quest that lists it as a prerequisite (P1.7a). The failed quest itself is never included.
        /// </summary>
        public static List<int> DependentsOf(QuestModel failed, IEnumerable<QuestModel> quests)
        {
            var keys = new SortedSet<int>();
            for (int i = 0; i < failed.Dependents.Count; i++)
            {
                keys.Add(failed.Dependents[i]);
            }

            foreach (QuestModel quest in quests)
            {
                for (int i = 0; i < quest.Prerequisites.Count; i++)
                {
                    if (quest.Prerequisites[i] == failed.Key)
                    {
                        keys.Add(quest.Key);
                        break;
                    }
                }
            }

            keys.Remove(failed.Key);
            return new List<int>(keys);
        }

        /// <summary>
        /// Closes a dependent of a failed quest (P1.7a): an inactive or active quest becomes failed (a Failed event at its
        /// stage); a completed or already failed quest is left alone. True when it changed.
        /// </summary>
        public static bool CloseDependent(QuestState state, List<QuestEvent> events)
        {
            if (state.Status != Inactive && state.Status != Active)
            {
                return false;
            }

            state.Status = Failed;
            events.Add(new QuestEvent(QuestEventKind.Failed, state.Stage, 0, 0, 0));
            return true;
        }

        /// <summary>Sets an objective of the current stage to <paramref name="count"/>; done latches once the count reaches the requirement.</summary>
        public static QuestRefusal SetObjective(QuestModel quest, QuestState state, int objective, int count, List<QuestEvent> events)
        {
            if (state.Status != Active)
            {
                return QuestRefusal.NotActive;
            }

            if (objective < 0 || objective >= quest.Objectives.Count || objective >= state.Counts.Length)
            {
                return QuestRefusal.ObjectiveOutOfRange;
            }

            ObjectiveModel model = quest.Objectives[objective];
            if (model.Stage != state.Stage)
            {
                return QuestRefusal.StageOutOfRange;
            }

            int value = count < 0 ? 0 : count;
            int done = state.Done[objective] != 0 || value >= model.Required ? 1 : 0;
            if (value != state.Counts[objective] || done != state.Done[objective])
            {
                state.Counts[objective] = value;
                state.Done[objective] = done;
                events.Add(new QuestEvent(QuestEventKind.ObjectiveUpdated, objective, value, done, 0));
            }

            CheckStage(quest, state, events);
            return QuestRefusal.None;
        }

        /// <summary>Forces the quest to <paramref name="stage"/> (-1 = the current stage's next); past the last stage completes it.</summary>
        public static QuestRefusal Advance(QuestModel quest, QuestState state, int stage, List<QuestEvent> events)
        {
            if (state.Status != Active)
            {
                return QuestRefusal.NotActive;
            }

            int target = stage < 0 ? NextOf(quest, state.Stage) : stage;
            if (target < 0 || target > quest.Stages.Count)
            {
                return QuestRefusal.StageOutOfRange;
            }

            if (target >= quest.Stages.Count)
            {
                CompleteNow(quest, state, events);
                return QuestRefusal.None;
            }

            EnterStage(quest, state, target, events);
            CheckStage(quest, state, events);
            return QuestRefusal.None;
        }

        public static QuestRefusal Complete(QuestModel quest, QuestState state, List<QuestEvent> events)
        {
            if (state.Status != Active)
            {
                return QuestRefusal.NotActive;
            }

            CompleteNow(quest, state, events);
            return QuestRefusal.None;
        }

        public static QuestRefusal Fail(QuestModel quest, QuestState state, List<QuestEvent> events)
        {
            if (state.Status != Active)
            {
                return QuestRefusal.NotActive;
            }

            state.Status = Failed;
            events.Add(new QuestEvent(QuestEventKind.Failed, state.Stage, 0, 0, 0));
            return QuestRefusal.None;
        }

        /// <summary>True when the stage's objectives are satisfied; <paramref name="branch"/> is the branch that satisfied it (0 = none needed).</summary>
        public static bool StageSatisfied(QuestModel quest, QuestState state, int stage, out int branch)
        {
            branch = 0;
            if (stage < 0 || stage >= quest.Stages.Count)
            {
                return false;
            }

            StageModel model = quest.Stages[stage];
            var branches = new SortedDictionary<int, bool>();
            for (int i = 0; i < model.Objectives.Count; i++)
            {
                int index = model.Objectives[i];
                if (index < 0 || index >= quest.Objectives.Count)
                {
                    continue;
                }

                ObjectiveModel objective = quest.Objectives[index];
                bool done = index < state.Done.Length && state.Done[index] != 0;
                if (objective.Branch == 0)
                {
                    if (!done)
                    {
                        return false;
                    }

                    continue;
                }

                branches[objective.Branch] = (!branches.TryGetValue(objective.Branch, out bool all) || all) && done;
            }

            if (branches.Count == 0)
            {
                return true;
            }

            foreach (KeyValuePair<int, bool> pair in branches)
            {
                if (pair.Value)
                {
                    branch = pair.Key;
                    return true;
                }
            }

            return false;
        }

        public static int NextOf(QuestModel quest, int stage) =>
            stage >= 0 && stage < quest.Stages.Count && quest.Stages[stage].Next >= 0 ? quest.Stages[stage].Next : stage + 1;

        private static void CheckStage(QuestModel quest, QuestState state, List<QuestEvent> events)
        {
            for (int guard = 0; guard <= quest.Stages.Count && state.Status == Active; guard++)
            {
                if (!StageSatisfied(quest, state, state.Stage, out int branch))
                {
                    return;
                }

                if (branch > 0 && state.Branch == 0)
                {
                    state.Branch = branch;
                }

                int next = NextOf(quest, state.Stage);
                if (next >= quest.Stages.Count || next < 0)
                {
                    CompleteNow(quest, state, events);
                    return;
                }

                EnterStage(quest, state, next, events);
            }
        }

        private static void EnterStage(QuestModel quest, QuestState state, int stage, List<QuestEvent> events)
        {
            int previous = state.Stage;
            state.Stage = stage;
            StageModel model = quest.Stages[stage];
            for (int i = 0; i < model.Objectives.Count; i++)
            {
                int index = model.Objectives[i];
                if (index >= 0 && index < state.Counts.Length)
                {
                    state.Counts[index] = 0;
                    state.Done[index] = 0;
                }
            }

            events.Add(new QuestEvent(QuestEventKind.StageEntered, stage, previous, state.Branch, 0));
        }

        private static void CompleteNow(QuestModel quest, QuestState state, List<QuestEvent> events)
        {
            state.Status = Completed;
            events.Add(new QuestEvent(QuestEventKind.Completed, state.Stage, state.Branch, 0, 0));
            for (int i = 0; i < quest.Rewards.Count; i++)
            {
                RewardModel reward = quest.Rewards[i];
                if (reward.Branch != 0 && reward.Branch != state.Branch)
                {
                    continue;
                }

                events.Add(new QuestEvent(QuestEventKind.RewardGranted, i, (int)reward.Kind, reward.Key, reward.Value));
            }
        }
    }

    /// <summary>An edge signal for talk/interact objectives.</summary>
    public readonly struct ObjectiveSignal
    {
        public ObjectiveSignal(ObjectiveKind kind, int targetKey)
        {
            Kind = kind;
            TargetKey = targetKey;
        }

        public ObjectiveKind Kind { get; }

        public int TargetKey { get; }
    }

    /// <summary>One objective update the tracker submits (quest.setObjective).</summary>
    public readonly struct ObjectiveUpdate
    {
        public ObjectiveUpdate(int objective, int count)
        {
            Objective = objective;
            Count = count;
        }

        public int Objective { get; }

        public int Count { get; }
    }

    /// <summary>The objective updates of the current stage, given committed state and the edge signals since the last pass.</summary>
    public static class QuestTracking
    {
        /// <summary>The count a level objective reads from <paramref name="world"/> (the actor's inventory and region).</summary>
        public static int LevelCount(ObjectiveModel objective, IConditionState world, int actorKey)
        {
            switch (objective.Kind)
            {
                case ObjectiveKind.Fact:
                    return world.Fact(objective.TargetKey);
                case ObjectiveKind.Collect:
                    return world.ItemCount(0, objective.TargetKey, actorKey);
                case ObjectiveKind.Reach:
                    return world.RegionOf(actorKey) == objective.TargetKey && objective.TargetKey != 0 ? 1 : 0;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// The updates an active quest needs: level objectives whose committed count differs, and edge objectives matched
        /// by a signal (count + matches). Done objectives are latched and skipped.
        /// </summary>
        public static List<ObjectiveUpdate> Pending(
            QuestModel quest,
            QuestState state,
            IConditionState world,
            int actorKey,
            IReadOnlyList<ObjectiveSignal> signals)
        {
            var updates = new List<ObjectiveUpdate>();
            if (state.Status != QuestRules.Active || state.Stage < 0 || state.Stage >= quest.Stages.Count)
            {
                return updates;
            }

            StageModel stage = quest.Stages[state.Stage];
            for (int i = 0; i < stage.Objectives.Count; i++)
            {
                int index = stage.Objectives[i];
                if (index < 0 || index >= quest.Objectives.Count || index >= state.Counts.Length || state.Done[index] != 0)
                {
                    continue;
                }

                ObjectiveModel objective = quest.Objectives[index];
                int current = state.Counts[index];
                int next = current;
                if (objective.IsLevel)
                {
                    next = LevelCount(objective, world, actorKey);
                }
                else
                {
                    for (int s = 0; s < signals.Count; s++)
                    {
                        if (signals[s].Kind == objective.Kind && signals[s].TargetKey == objective.TargetKey)
                        {
                            next++;
                        }
                    }
                }

                if (next < 0)
                {
                    next = 0;
                }

                if (next != current)
                {
                    updates.Add(new ObjectiveUpdate(index, next));
                }
            }

            return updates;
        }
    }

    /// <summary>One step of a simulated play path.</summary>
    public enum SimulationStepKind
    {
        /// <summary>Set a fact. Key = fact key, Value = value.</summary>
        Fact = 0,

        /// <summary>A conversation with a graph ended. Key = graph key.</summary>
        Talk = 1,

        /// <summary>The player now holds Value of an item. Key = item key.</summary>
        Collect = 2,

        /// <summary>The player entered a region. Key = region key.</summary>
        Reach = 3,

        /// <summary>The player used an interactable. Key = subject key.</summary>
        Interact = 4,

        /// <summary>quest.start.</summary>
        Start = 5,

        /// <summary>quest.advance to Value (-1 = next).</summary>
        Advance = 6,

        /// <summary>quest.fail.</summary>
        Fail = 7,
    }

    /// <summary>One step of a simulated path.</summary>
    public readonly struct SimulationStep
    {
        public SimulationStep(SimulationStepKind kind, int key, int value, string label)
        {
            Kind = kind;
            Key = key;
            Value = value;
            Label = label ?? string.Empty;
        }

        public SimulationStepKind Kind { get; }

        public int Key { get; }

        public int Value { get; }

        public string Label { get; }

        public string Describe() =>
            Kind + (Label.Length > 0 ? " " + Label : (Key != 0 ? " " + Key.ToString(CultureInfo.InvariantCulture) : string.Empty))
            + (Kind == SimulationStepKind.Fact || Kind == SimulationStepKind.Collect || Kind == SimulationStepKind.Advance
                ? " = " + Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
    }

    /// <summary>The outcome of quest.simulate.</summary>
    public sealed class SimulationResult
    {
        public SimulationResult(QuestState state, IReadOnlyList<QuestEvent> events, IReadOnlyList<string> trace, IReadOnlyList<RewardModel> rewards)
        {
            State = state;
            Events = events;
            Trace = trace;
            Rewards = rewards;
        }

        public QuestState State { get; }

        public bool Completed => State.Status == QuestRules.Completed;

        public IReadOnlyList<QuestEvent> Events { get; }

        public IReadOnlyList<string> Trace { get; }

        /// <summary>Rewards granted (each exactly once).</summary>
        public IReadOnlyList<RewardModel> Rewards { get; }

        public string Text => string.Join("\n", Trace);
    }

    /// <summary>Plays a path against a quest with the tracker's own rules (quest.simulate).</summary>
    public static class QuestSimulator
    {
        /// <summary>
        /// Starts the quest (unless the path starts it), applies every step to a state snapshot and pumps the tracker
        /// after each step until nothing changes. The trace names every stage, objective and reward event.
        /// </summary>
        public static SimulationResult Simulate(QuestModel quest, IReadOnlyList<SimulationStep> path, StateSnapshot? initial)
        {
            StateSnapshot world = initial ?? new StateSnapshot();
            const int actor = 0;
            var state = new QuestState(quest.Objectives.Count);
            var events = new List<QuestEvent>();
            var trace = new List<string>();
            var rewards = new List<RewardModel>();
            bool startsItself = path.Count > 0 && path[0].Kind == SimulationStepKind.Start;
            if (!startsItself)
            {
                Apply(quest, state, world, actor, Array.Empty<ObjectiveSignal>(), events, trace, rewards, () => QuestRules.Start(quest, state, events), "start");
            }

            for (int i = 0; i < path.Count; i++)
            {
                SimulationStep step = path[i];
                trace.Add("> " + step.Describe());
                var signals = new List<ObjectiveSignal>();
                Func<QuestRefusal>? command = null;
                switch (step.Kind)
                {
                    case SimulationStepKind.Fact:
                        world.SetFact(step.Key, step.Value);
                        break;
                    case SimulationStepKind.Collect:
                        world.SetItem(world.ActorInventoryKey, step.Key, step.Value);
                        break;
                    case SimulationStepKind.Reach:
                        world.SetRegion(actor, step.Key);
                        break;
                    case SimulationStepKind.Talk:
                        signals.Add(new ObjectiveSignal(ObjectiveKind.Talk, step.Key));
                        break;
                    case SimulationStepKind.Interact:
                        signals.Add(new ObjectiveSignal(ObjectiveKind.Interact, step.Key));
                        break;
                    case SimulationStepKind.Start:
                        command = () => QuestRules.Start(quest, state, events);
                        break;
                    case SimulationStepKind.Advance:
                        command = () => QuestRules.Advance(quest, state, step.Value, events);
                        break;
                    case SimulationStepKind.Fail:
                        command = () => QuestRules.Fail(quest, state, events);
                        break;
                }

                Apply(quest, state, world, actor, signals, events, trace, rewards, command, step.Kind.ToString());
            }

            trace.Add("= " + StatusName(state.Status) + " at stage " + state.Stage.ToString(CultureInfo.InvariantCulture)
                + (state.Branch > 0 ? " via " + quest.BranchName(state.Branch) : string.Empty));
            return new SimulationResult(state, events, trace, rewards);
        }

        public static string StatusName(int status)
        {
            switch (status)
            {
                case QuestRules.Inactive: return "inactive";
                case QuestRules.Active: return "active";
                case QuestRules.Completed: return "completed";
                case QuestRules.Failed: return "failed";
                default: return "status " + status.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static void Apply(
            QuestModel quest,
            QuestState state,
            StateSnapshot world,
            int actor,
            IReadOnlyList<ObjectiveSignal> signals,
            List<QuestEvent> events,
            List<string> trace,
            List<RewardModel> rewards,
            Func<QuestRefusal>? command,
            string what)
        {
            int before = events.Count;
            if (command != null)
            {
                QuestRefusal refusal = command();
                if (refusal != QuestRefusal.None)
                {
                    trace.Add("  refused " + what + ": " + refusal);
                }
            }

            IReadOnlyList<ObjectiveSignal> pendingSignals = signals;
            for (int pass = 0; pass < 16; pass++)
            {
                List<ObjectiveUpdate> updates = QuestTracking.Pending(quest, state, world, actor, pendingSignals);
                pendingSignals = Array.Empty<ObjectiveSignal>();
                if (updates.Count == 0)
                {
                    break;
                }

                for (int u = 0; u < updates.Count; u++)
                {
                    QuestRules.SetObjective(quest, state, updates[u].Objective, updates[u].Count, events);
                }
            }

            for (int i = before; i < events.Count; i++)
            {
                trace.Add("  " + Describe(quest, events[i]));
                if (events[i].Kind == QuestEventKind.RewardGranted && events[i].A >= 0 && events[i].A < quest.Rewards.Count)
                {
                    rewards.Add(quest.Rewards[events[i].A]);
                }
            }
        }

        /// <summary>A readable line for one quest event.</summary>
        public static string Describe(QuestModel quest, QuestEvent e)
        {
            switch (e.Kind)
            {
                case QuestEventKind.Started:
                    return "QuestStarted " + quest.Name;
                case QuestEventKind.StageEntered:
                    return "StageEntered " + e.A.ToString(CultureInfo.InvariantCulture)
                        + (e.A >= 0 && e.A < quest.Stages.Count ? " \"" + quest.Stages[e.A].Title + "\"" : string.Empty);
                case QuestEventKind.ObjectiveUpdated:
                    return "ObjectiveUpdated " + e.A.ToString(CultureInfo.InvariantCulture)
                        + (e.A >= 0 && e.A < quest.Objectives.Count ? " (" + quest.Objectives[e.A].Describe() + ")" : string.Empty)
                        + " count=" + e.B.ToString(CultureInfo.InvariantCulture) + (e.C != 0 ? " done" : string.Empty);
                case QuestEventKind.Completed:
                    return "QuestCompleted " + quest.Name + (e.B > 0 ? " via " + quest.BranchName(e.B) : string.Empty);
                case QuestEventKind.Failed:
                    return "QuestFailed " + quest.Name + " at stage " + e.A.ToString(CultureInfo.InvariantCulture);
                case QuestEventKind.RewardGranted:
                    return "RewardGranted " + (e.A >= 0 && e.A < quest.Rewards.Count ? quest.Rewards[e.A].Describe() : e.ToString());
                default:
                    return e.ToString();
            }
        }

        /// <summary>A readable summary of a quest (journal export, quest.simulate headers).</summary>
        public static string Outline(QuestModel quest)
        {
            var builder = new StringBuilder(quest.Name);
            for (int s = 0; s < quest.Stages.Count; s++)
            {
                StageModel stage = quest.Stages[s];
                builder.Append("\n  stage ").Append(s).Append(": ").Append(stage.Title);
                for (int i = 0; i < stage.Objectives.Count; i++)
                {
                    int index = stage.Objectives[i];
                    if (index >= 0 && index < quest.Objectives.Count)
                    {
                        builder.Append("\n    - ").Append(quest.Objectives[index].Describe());
                    }
                }
            }

            for (int r = 0; r < quest.Rewards.Count; r++)
            {
                builder.Append("\n  reward: ").Append(quest.Rewards[r].Describe());
            }

            return builder.ToString();
        }
    }
}
