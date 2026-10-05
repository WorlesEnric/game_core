// GameCore.Gameplay.Quest.Editor - quest authoring operations (P1.4, catalog row 7; Studio 03 s4/s5).
//
//   quest.addStage      append a stage (title, description, optional next stage)
//   quest.addObjective  add an objective to a stage (talk/collect/reach/interact/fact, count, branch)
//   quest.linkReward    add a reward (item or fact) granted exactly once on completion, optionally on one branch only
//   quest.simulate      play a path ("talk:Maren; fact:odd_paid=1; collect:bell_clapper=1; reach:Drowned Belfry")
//                       through the pure quest rules and print every stage, objective and reward event; a failed
//                       quest lists the dependents it closes (P1.7b)
//   quest.setBranch     put an objective on a branch and name the branch (P1.7b)
//   quest.setConsequence the action sets run on completion and on failure (P1.7b)
//   quest.inspectRuntime a quest's status, stage, branch and objectives over a state (P1.7b; the live slot reader is
//                       P1.7a's seam)
//   quest.setPrerequisites the quests that must complete first (P1.7b)
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Logic.Editor;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using UnityEditor;
using UnityEngine;

namespace GameCore.Gameplay.Quest.Editor
{
    /// <summary>The quest.* authoring operations.</summary>
    public static class QuestTools
    {
        [AuthorOperation("quest.addStage", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(QuestValidator), Requires = NarrativeKinds.Quest,
            Doc = "Appends a stage to a quest; returns its index.")]
        public static int AddStage(
            QuestDefinition quest,
            [AuthorArg(Doc = "Stage title.")] string title,
            [AuthorArg(Required = false, Doc = "Stage description.")] string description = "",
            [AuthorArg(Required = false, Doc = "Next stage index when satisfied (-1 = the following one).")] int next = -1)
        {
            Require(quest);
            if (next < -1)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.QuestStageOutOfRange + ": next stage " + next);
            }

            Undo.RecordObject(quest, "quest.addStage");
            int index = quest.AddStage(new QuestStageEntry { title = title ?? string.Empty, description = description ?? string.Empty, next = next });
            EditorUtility.SetDirty(quest);
            return index;
        }

        [AuthorOperation("quest.addObjective", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(QuestValidator), Requires = NarrativeKinds.Quest,
            Doc = "Adds an objective to a stage: talk (graph), collect (item), reach (region), interact (entity id) or fact (fact value).")]
        public static int AddObjective(
            QuestDefinition quest,
            [AuthorArg(Doc = "Stage index.")] int stage,
            [AuthorArg(Doc = "Objective kind.")] ObjectiveKind kind,
            [AuthorArg(Required = false, Doc = "The dialogue graph (talk), item (collect), region (reach) or fact (fact).")] ScriptableObject? target = null,
            [AuthorArg(Required = false, Doc = "Entity authoring id (interact).")] string targetEntityId = "",
            [AuthorArg(Required = false, Min = 1, Doc = "Count needed.")] int required = 1,
            [AuthorArg(Required = false, Min = 0, Doc = "0 = always needed; n = part of branch n.")] int branch = 0,
            [AuthorArg(Required = false, Doc = "Journal text.")] string text = "")
        {
            Require(quest);
            if (stage < 0 || stage >= quest.Stages.Count)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.QuestStageOutOfRange + ": quest " + quest.name + " has no stage " + stage);
            }

            if (quest.Objectives.Count >= NarrativeLimits.MaxObjectives)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.QuestTooManyObjectives + ": a quest has at most " + NarrativeLimits.MaxObjectives + " objectives");
            }

            if (kind == ObjectiveKind.Interact ? NarrativeRefs.EntityKey(targetEntityId) == 0 : NarrativeRefs.KeyOf(target) == 0)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.QuestObjectiveTarget + ": a " + kind + " objective needs a target");
            }

            if (branch < 0 || required < 1)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.QuestBadBranch + ": branch >= 0 and required >= 1");
            }

            Undo.RecordObject(quest, "quest.addObjective");
            int index = quest.AddObjective(new ObjectiveDefinition
            {
                stage = stage,
                kind = kind,
                target = target,
                targetEntityId = targetEntityId ?? string.Empty,
                required = required,
                branch = branch,
                text = text ?? string.Empty,
            });
            EditorUtility.SetDirty(quest);
            return index;
        }

        [AuthorOperation("quest.linkReward", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(QuestValidator), Requires = NarrativeKinds.Quest,
            Doc = "Adds a completion reward (an item count or a fact value), granted exactly once through the outbox.")]
        public static int LinkReward(
            QuestDefinition quest,
            [AuthorArg(Doc = "Item or fact.")] RewardKind kind,
            [AuthorArg(Doc = "The item (item rewards) or the fact (fact rewards).")] ScriptableObject target,
            [AuthorArg(Required = false, Doc = "Count or value.")] int value = 1,
            [AuthorArg(Required = false, Min = 0, Doc = "0 = always; n = only on branch n.")] int branch = 0)
        {
            Require(quest);
            bool fact = target is IFactDefinition;
            if (target == null || NarrativeRefs.KeyOf(target) == 0 || fact != (kind == RewardKind.Fact))
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.QuestObjectiveTarget + ": a " + kind + " reward needs a matching " + (kind == RewardKind.Fact ? "fact" : "item"));
            }

            Undo.RecordObject(quest, "quest.linkReward");
            int index = quest.AddReward(new QuestRewardEntry { kind = kind, target = target, value = value, branch = branch });
            EditorUtility.SetDirty(quest);
            return index;
        }

        [AuthorOperation("quest.simulate", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live,
            Validator = typeof(QuestValidator), Requires = NarrativeKinds.Quest,
            Doc = "Plays a path through the quest rules: start; advance:n; fail; fact:<name>=v; talk:<graph>; collect:<item>=n; reach:<region>; interact:<entity id>.")]
        public static string Simulate(
            QuestDefinition quest,
            [AuthorArg(Doc = "Steps separated by ';'.")] string path)
        {
            SimulationResult result = SimulateResult(quest, path);
            if (result.State.Status != QuestRules.Failed)
            {
                return result.Text;
            }

            IReadOnlyList<QuestDefinition> closed = ClosedBy(quest);
            var lines = new List<string> { result.Text };
            for (int i = 0; i < closed.Count; i++)
            {
                lines.Add(AuthoringHardeningCodes.QuestClosedByPrerequisite + ": closes " + closed[i].Title + " (prerequisite " + quest.Title + " failed)");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Every quest that fails closed when <paramref name="quest"/> fails: the quests naming it as a prerequisite,
        /// transitively, in name order (the "failure closes dependents" semantics of 05 row 7).
        /// </summary>
        public static IReadOnlyList<QuestDefinition> ClosedBy(QuestDefinition quest)
        {
            var result = new List<QuestDefinition>();
            if (quest == null)
            {
                return result;
            }

            IReadOnlyList<QuestDefinition> all = AllQuests();
            var closed = new HashSet<QuestDefinition> { quest };
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = 0; i < all.Count; i++)
                {
                    QuestDefinition candidate = all[i];
                    if (closed.Contains(candidate))
                    {
                        continue;
                    }

                    for (int p = 0; p < candidate.Prerequisites.Count; p++)
                    {
                        if (candidate.Prerequisites[p] != null && closed.Contains(candidate.Prerequisites[p]))
                        {
                            closed.Add(candidate);
                            result.Add(candidate);
                            grew = true;
                            break;
                        }
                    }
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }

        [AuthorOperation("quest.setPrerequisites", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(QuestValidator), Requires = NarrativeKinds.Quest,
            Doc = "Sets the quests that must be completed before this one starts; a failed prerequisite closes this quest.")]
        public static void SetPrerequisites(
            QuestDefinition quest,
            [AuthorArg(Category = NarrativeKinds.Quest, Required = false, Doc = "The prerequisite quests (empty: none).")] QuestDefinition[]? prerequisites = null)
        {
            Require(quest);
            var list = new List<QuestDefinition>();
            QuestDefinition[] given = prerequisites ?? Array.Empty<QuestDefinition>();
            for (int i = 0; i < given.Length; i++)
            {
                if (given[i] != null && !list.Contains(given[i]))
                {
                    list.Add(given[i]);
                }
            }

            if (ReachesThrough(list, quest))
            {
                throw new ArgumentException(AuthoringHardeningCodes.QuestPrerequisiteCycle + ": " + quest.name + " would become its own prerequisite");
            }

            Undo.RecordObject(quest, "quest.setPrerequisites");
            quest.SetPrerequisites(list);
            EditorUtility.SetDirty(quest);
        }

        [AuthorOperation("quest.setBranch", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(QuestValidator), Requires = NarrativeKinds.Quest,
            Doc = "Puts an objective on a branch (0 = always needed, n = an alternative way through its stage) and optionally names branch n.")]
        public static void SetBranch(
            QuestDefinition quest,
            [AuthorArg(Min = 0, Doc = "Objective index.")] int objective,
            [AuthorArg(Min = 0, Doc = "Branch number (0 = always needed).")] int branch,
            [AuthorArg(Required = false, Doc = "Name of branch n (journal, simulate); empty keeps the current name.")] string branchName = "")
        {
            Require(quest);
            if (objective < 0 || objective >= quest.Objectives.Count)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.QuestObjectiveTarget + ": quest " + quest.name + " has no objective " + objective);
            }

            // Branches are unnamed until the first name is given; once named, an objective's branch names one of them
            // (or the branch named by this call).
            bool legal = branch == 0
                || (branch > 0 && (!string.IsNullOrEmpty(branchName) || quest.BranchNames.Count == 0 || branch <= quest.BranchNames.Count));
            if (!legal)
            {
                throw new ArgumentException(AuthoringHardeningCodes.QuestBranchOutOfRange + ": branch " + branch + " is not one of the quest's " + quest.BranchNames.Count + " named branches");
            }

            Undo.RecordObject(quest, "quest.setBranch");
            quest.Objectives[objective].branch = branch;
            if (branch > 0 && !string.IsNullOrEmpty(branchName))
            {
                quest.SetBranchName(branch, branchName);
            }

            EditorUtility.SetDirty(quest);
        }

        [AuthorOperation("quest.setConsequence", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(QuestValidator), Requires = NarrativeKinds.Quest,
            Doc = "Sets the action sets run once when the quest completes (after its rewards) and when it fails; null clears one.")]
        public static void SetConsequence(
            QuestDefinition quest,
            [AuthorArg(Category = NarrativeKinds.ActionSet, Required = false, Doc = "Run on completion.")] ActionSetDefinition? onComplete = null,
            [AuthorArg(Category = NarrativeKinds.ActionSet, Required = false, Doc = "Run on failure.")] ActionSetDefinition? onFail = null)
        {
            Require(quest);
            Undo.RecordObject(quest, "quest.setConsequence");
            quest.SetConsequences(onComplete, onFail);
            EditorUtility.SetDirty(quest);
            NarrativeAuthoring.ThrowIfInvalid(quest);
        }

        [AuthorOperation("quest.inspectRuntime", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live,
            Validator = typeof(QuestValidator), Requires = NarrativeKinds.Quest,
            Doc = "Reports a quest's status, stage, branch, objectives and prerequisites over a state (logic.test terms; empty: the initial state).")]
        public static QuestInspection InspectRuntime(
            QuestDefinition quest,
            [AuthorArg(Required = false, Doc = "State terms, e.g. quest.<quest>.status=1; quest.<quest>.stage=2; fact.<name>=v.")] string state = "")
        {
            Require(quest);
            NarrativeModelSet models = NarrativeAuthoring.Models(string.Empty, quest);
            if (!models.TryResolve(quest.AuthoringId, out int key) || !models.TryGetQuest(key, out QuestModel? model) || model == null)
            {
                throw new ArgumentException(models.Problems.Count > 0 ? models.Problems[0] : NarrativeDiagnosticCodes.QuestUnknown + ": the quest did not convert");
            }

            StateSnapshot snapshot = NarrativeAuthoring.ParseState(state ?? string.Empty, models, out RuleState _);
            int status = snapshot.Quest(key, QuestField.Status);
            int stage = snapshot.Quest(key, QuestField.Stage);
            int branch = snapshot.Quest(key, QuestField.Branch);
            var objectives = new List<string>();
            for (int i = 0; i < model.Objectives.Count; i++)
            {
                ObjectiveModel objective = model.Objectives[i];
                bool done = snapshot.ObjectiveDone(key, i) != 0;
                objectives.Add("#" + i + " stage " + objective.Stage + (objective.Branch > 0 ? " branch " + objective.Branch : string.Empty) + " "
                    + objective.Kind + " " + objective.TargetLabel + " x" + objective.Required + (done ? " done" : (objective.Stage == stage && status == QuestRules.Active ? " open" : string.Empty)));
            }

            var prerequisites = new List<string>();
            for (int i = 0; i < quest.Prerequisites.Count; i++)
            {
                QuestDefinition? prerequisite = quest.Prerequisites[i];
                if (prerequisite != null)
                {
                    prerequisites.Add(prerequisite.Title);
                }
            }

            var closes = new List<string>();
            IReadOnlyList<QuestDefinition> closed = ClosedBy(quest);
            for (int i = 0; i < closed.Count; i++)
            {
                closes.Add(closed[i].Title);
            }

            return new QuestInspection(quest.Title, StatusName(status), stage, branch,
                branch > 0 && branch <= quest.BranchNames.Count ? quest.BranchNames[branch - 1] : string.Empty, objectives, prerequisites, closes,
                state == null || state.Trim().Length == 0 ? "initial" : "state");
        }

        private static string StatusName(int status)
        {
            switch (status)
            {
                case QuestRules.Inactive: return "inactive";
                case QuestRules.Active: return "active";
                case QuestRules.Completed: return "completed";
                case QuestRules.Failed: return "failed";
                default: return status.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static IReadOnlyList<QuestDefinition> AllQuests()
        {
            var quests = new List<QuestDefinition>();
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(QuestDefinition));
            Array.Sort(guids, StringComparer.Ordinal);
            for (int i = 0; i < guids.Length; i++)
            {
                QuestDefinition? loaded = AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (loaded != null)
                {
                    quests.Add(loaded);
                }
            }

            return quests;
        }

        /// <summary>True when following prerequisites from <paramref name="start"/> reaches <paramref name="target"/>.</summary>
        internal static bool ReachesThrough(IReadOnlyList<QuestDefinition> start, QuestDefinition target)
        {
            var seen = new HashSet<QuestDefinition>();
            var stack = new Stack<QuestDefinition>();
            for (int i = 0; i < start.Count; i++)
            {
                if (start[i] != null)
                {
                    stack.Push(start[i]);
                }
            }

            while (stack.Count > 0)
            {
                QuestDefinition next = stack.Pop();
                if (next == target)
                {
                    return true;
                }

                if (!seen.Add(next))
                {
                    continue;
                }

                for (int i = 0; i < next.Prerequisites.Count; i++)
                {
                    if (next.Prerequisites[i] != null)
                    {
                        stack.Push(next.Prerequisites[i]);
                    }
                }
            }

            return false;
        }

        /// <summary>The structured result of quest.simulate (EditMode tests assert on it).</summary>
        public static SimulationResult SimulateResult(QuestDefinition quest, string path)
        {
            Require(quest);
            NarrativeModelSet models = NarrativeAuthoring.Models(string.Empty, quest);
            if (!models.TryResolve(quest.AuthoringId, out int key) || !models.TryGetQuest(key, out QuestModel? model) || model == null)
            {
                throw new ArgumentException(models.Problems.Count > 0 ? models.Problems[0] : NarrativeDiagnosticCodes.QuestUnknown + ": the quest did not convert");
            }

            StateSnapshot initial = NarrativeAuthoring.ParseState(string.Empty, models, out RuleState _);
            return QuestSimulator.Simulate(model, ParsePath(path, model, models), initial);
        }

        /// <summary>Parses a simulate path.</summary>
        public static List<SimulationStep> ParsePath(string path, QuestModel quest, NarrativeModelSet models)
        {
            var steps = new List<SimulationStep>();
            string[] terms = (path ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < terms.Length; i++)
            {
                string term = terms[i].Trim();
                if (term.Length == 0)
                {
                    continue;
                }

                int colon = term.IndexOf(':');
                string verb = (colon < 0 ? term : term.Substring(0, colon)).Trim().ToLowerInvariant();
                string rest = colon < 0 ? string.Empty : term.Substring(colon + 1).Trim();
                int value = 1;
                int eq = rest.LastIndexOf('=');
                if (eq > 0)
                {
                    if (!int.TryParse(rest.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    {
                        throw new ArgumentException(NarrativeDiagnosticCodes.ConditionInvalid + ": '" + term + "' has no integer value");
                    }

                    rest = rest.Substring(0, eq).Trim();
                }

                switch (verb)
                {
                    case "start":
                        steps.Add(new SimulationStep(SimulationStepKind.Start, 0, 0, "start"));
                        break;
                    case "fail":
                        steps.Add(new SimulationStep(SimulationStepKind.Fail, 0, 0, "fail"));
                        break;
                    case "advance":
                        if (!int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int stage))
                        {
                            stage = -1;
                        }

                        steps.Add(new SimulationStep(SimulationStepKind.Advance, 0, stage, "advance " + stage));
                        break;
                    case "fact":
                        if (!models.TryGetFactByName(rest, out FactModel? fact) || fact == null)
                        {
                            throw new ArgumentException(NarrativeDiagnosticCodes.FactUnknown + ": the quest references no fact '" + rest + "'");
                        }

                        steps.Add(new SimulationStep(SimulationStepKind.Fact, fact.Key, value, "fact " + rest + "=" + value));
                        break;
                    case "talk":
                        steps.Add(new SimulationStep(SimulationStepKind.Talk, Resolve(rest, ObjectiveKind.Talk, quest, models), value, "talk " + rest));
                        break;
                    case "collect":
                        steps.Add(new SimulationStep(SimulationStepKind.Collect, Resolve(rest, ObjectiveKind.Collect, quest, models), value, "collect " + rest + "=" + value));
                        break;
                    case "reach":
                        steps.Add(new SimulationStep(SimulationStepKind.Reach, Resolve(rest, ObjectiveKind.Reach, quest, models), value, "reach " + rest));
                        break;
                    case "interact":
                        steps.Add(new SimulationStep(SimulationStepKind.Interact, Resolve(rest, ObjectiveKind.Interact, quest, models), value, "interact " + rest));
                        break;
                    default:
                        throw new ArgumentException(NarrativeDiagnosticCodes.ConditionInvalid + ": unknown simulate step '" + verb + "'");
                }
            }

            return steps;
        }

        /// <summary>A path target: a content name or authoring id, an entity/region authoring id, or an objective's target label.</summary>
        private static int Resolve(string reference, ObjectiveKind kind, QuestModel quest, NarrativeModelSet models)
        {
            for (int i = 0; i < quest.Objectives.Count; i++)
            {
                ObjectiveModel objective = quest.Objectives[i];
                if (objective.Kind == kind && string.Equals(objective.TargetLabel, reference, StringComparison.OrdinalIgnoreCase))
                {
                    return objective.TargetKey;
                }
            }

            if (models.TryResolve(reference, out int key))
            {
                return key;
            }

            if (NarrativeRefs.EntityKey(reference) != 0)
            {
                return NarrativeRefs.EntityKey(reference);
            }

            throw new ArgumentException(NarrativeDiagnosticCodes.QuestObjectiveTarget + ": nothing named '" + reference + "' for a " + kind + " step");
        }

        private static void Require(QuestDefinition quest)
        {
            if (quest == null)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.QuestUnknown + ": a quest is required");
            }
        }
    }

    /// <summary>quest.inspectRuntime's result.</summary>
    public sealed class QuestInspection
    {
        public QuestInspection(string quest, string status, int stage, int branch, string branchName, IReadOnlyList<string> objectives,
            IReadOnlyList<string> prerequisites, IReadOnlyList<string> closesOnFailure, string source)
        {
            Quest = quest;
            Status = status;
            Stage = stage;
            Branch = branch;
            BranchName = branchName;
            Objectives = objectives;
            Prerequisites = prerequisites;
            ClosesOnFailure = closesOnFailure;
            Source = source;
        }

        public string Quest { get; }

        public string Status { get; }

        public int Stage { get; }

        public int Branch { get; }

        public string BranchName { get; }

        public IReadOnlyList<string> Objectives { get; }

        public IReadOnlyList<string> Prerequisites { get; }

        /// <summary>The dependents that close when this quest fails.</summary>
        public IReadOnlyList<string> ClosesOnFailure { get; }

        /// <summary><c>initial</c> or <c>state</c> (a live world reader is P1.7a's seam).</summary>
        public string Source { get; }
    }

    /// <summary>Validation of quests.</summary>
    [AuthorValidator("quest.validator", Codes = new[]
    {
        NarrativeDiagnosticCodes.QuestNoStages,
        NarrativeDiagnosticCodes.QuestBadBranch,
        NarrativeDiagnosticCodes.QuestTooManyObjectives,
        NarrativeDiagnosticCodes.QuestObjectiveTarget,
        NarrativeDiagnosticCodes.ConditionInvalid,
        AuthoringHardeningCodes.QuestPrerequisiteCycle,
        AuthoringHardeningCodes.QuestBranchOutOfRange,
        AuthoringHardeningCodes.LegacyReference,
    })]
    public static class QuestValidator
    {
        public static IReadOnlyList<GameplayDiagnostic> Validate(ScriptableObject definition)
        {
            var diagnostics = new List<GameplayDiagnostic>(LogicValidator.Validate(definition));
            if (definition is QuestDefinition quest)
            {
                if (QuestTools.ReachesThrough(quest.Prerequisites, quest))
                {
                    diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.QuestPrerequisiteCycle, quest.AuthoringId, quest.name + " is its own prerequisite"));
                }

                for (int i = 0; i < quest.Objectives.Count; i++)
                {
                    ObjectiveDefinition objective = quest.Objectives[i];
                    if (objective.branch > 0 && quest.BranchNames.Count > 0 && objective.branch > quest.BranchNames.Count)
                    {
                        diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.QuestBranchOutOfRange, quest.AuthoringId,
                            quest.name + " objective " + i + " is on branch " + objective.branch + " of " + quest.BranchNames.Count));
                    }

                    if (objective.LegacyTarget != null)
                    {
                        diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.LegacyReference, quest.AuthoringId,
                            quest.name + " objective " + i + " still stores an untyped target; run authoring.migrateRefs"));
                    }
                }

                for (int i = 0; i < quest.Rewards.Count; i++)
                {
                    if (quest.Rewards[i].LegacyTarget != null)
                    {
                        diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.LegacyReference, quest.AuthoringId,
                            quest.name + " reward " + i + " still stores an untyped target; run authoring.migrateRefs"));
                    }
                }
            }

            return diagnostics;
        }
    }

    /// <summary>The quest plugin's catalog registrations (P1.3's catalog contribution seam).</summary>
    public sealed class QuestCatalogContributor : IGameplayCatalogContributor
    {
        public GameplayCatalogContribution Contribution =>
            new GameplayCatalogContribution("com.gamecore.gameplay.quest", NarrativeCatalogNames.Quest.Schemas, NarrativeCatalogNames.Quest.Entries);
    }
}
