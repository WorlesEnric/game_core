// GameCore.Gameplay.Quest.Editor - quest authoring operations (P1.4, catalog row 7; Studio 03 s4/s5).
//
//   quest.addStage      append a stage (title, description, optional next stage)
//   quest.addObjective  add an objective to a stage (talk/collect/reach/interact/fact, count, branch)
//   quest.linkReward    add a reward (item or fact) granted exactly once on completion, optionally on one branch only
//   quest.simulate      play a path ("talk:Maren; fact:odd_paid=1; collect:bell_clapper=1; reach:Drowned Belfry")
//                       through the pure quest rules and print every stage, objective and reward event
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
            [AuthorArg(Category = "narrative.subject", Required = false, Doc = "Graph, item, region or fact.")] ScriptableObject? target = null,
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
            [AuthorArg(Category = "narrative.subject", Doc = "The item or the fact.")] ScriptableObject target,
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
            return SimulateResult(quest, path).Text;
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

    /// <summary>Validation of quests.</summary>
    [AuthorValidator("quest.validator", Codes = new[]
    {
        NarrativeDiagnosticCodes.QuestNoStages,
        NarrativeDiagnosticCodes.QuestBadBranch,
        NarrativeDiagnosticCodes.QuestTooManyObjectives,
        NarrativeDiagnosticCodes.QuestObjectiveTarget,
        NarrativeDiagnosticCodes.ConditionInvalid,
    })]
    public static class QuestValidator
    {
        public static IReadOnlyList<GameplayDiagnostic> Validate(ScriptableObject definition) => LogicValidator.Validate(definition);
    }

    /// <summary>The quest plugin's catalog registrations (P1.3's catalog contribution seam).</summary>
    public sealed class QuestCatalogContributor : IGameplayCatalogContributor
    {
        public GameplayCatalogContribution Contribution =>
            new GameplayCatalogContribution("com.gamecore.gameplay.quest", NarrativeCatalogNames.Quest.Schemas, NarrativeCatalogNames.Quest.Entries);
    }
}
