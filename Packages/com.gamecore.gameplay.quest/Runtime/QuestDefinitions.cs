// GameCore.Gameplay.Quest - quest authoring and its conversion to the pure models (P1.4, catalog row 7).
//
//   QuestDefinition       stages (title, description, next), objectives, rewards, fail conditions, branch names
//   ObjectiveDefinition   one objective of a stage: talk (graph), collect (item), reach (region), interact (entity),
//                         fact (fact value); objectives with a branch number form alternative ways through a stage
//
// A stage is satisfied when every branch-0 objective is done and, if the stage has branch objectives, every objective
// of one branch is done; the first satisfying branch is latched as the quest branch. Rewards with a branch number are
// granted only on that branch - the pay-or-persuade quest of Hollowmere uses this.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using UnityEngine;

namespace GameCore.Gameplay.Quest
{
    /// <summary>One stage of a quest.</summary>
    [Serializable]
    public sealed class QuestStageEntry
    {
        [AuthorField(Doc = "Stage title (journal).")]
        public string title = string.Empty;

        [AuthorField(Doc = "Stage description (journal).")]
        [TextArea(1, 4)]
        public string description = string.Empty;

        [AuthorField(Doc = "Next stage index when satisfied (-1 = the following stage; past the last stage completes the quest).")]
        public int next = -1;
    }

    /// <summary>One objective of a quest stage.</summary>
    [Serializable]
    public sealed class ObjectiveDefinition
    {
        [AuthorField(Min = 0, Doc = "Stage index the objective belongs to.")]
        public int stage;

        [AuthorField(Doc = "Talk, collect, reach, interact or fact.")]
        public ObjectiveKind kind = ObjectiveKind.Fact;

        [AuthorRef(Category = "narrative.subject", Required = false, Doc = "Dialogue graph (talk), item (collect), region (reach) or fact (fact).")]
        public ScriptableObject? target;

        [AuthorField(Type = "authoringId", Doc = "Entity authoring id (interact objectives).")]
        public string targetEntityId = string.Empty;

        [AuthorField(Min = 1, Doc = "Count needed (collect: items held; fact: the value reached; talk/interact: times).")]
        public int required = 1;

        [AuthorField(Min = 0, Doc = "0 = always needed; n > 0 = part of alternative branch n.")]
        public int branch;

        [AuthorField(Doc = "Journal text.")]
        public string text = string.Empty;
    }

    /// <summary>One reward of a quest, granted exactly once (through the outbox) when the quest completes.</summary>
    [Serializable]
    public sealed class QuestRewardEntry
    {
        [AuthorField(Doc = "Item or fact.")]
        public RewardKind kind = RewardKind.Item;

        [AuthorRef(Category = "narrative.subject", Doc = "The item granted or the fact set.")]
        public ScriptableObject? target;

        [AuthorField(Doc = "Count (items) or value (facts).")]
        public int value = 1;

        [AuthorField(Min = 0, Doc = "0 = always; n > 0 = only when the quest completed on branch n.")]
        public int branch;
    }

    /// <summary>The quest package's converter.</summary>
    public sealed class QuestContentConverter : INarrativeContentConverter
    {
        public bool CanConvert(ScriptableObject asset) => asset is QuestDefinition;

        public void Convert(ScriptableObject asset, NarrativeConversion conversion)
        {
            if (asset is QuestDefinition quest)
            {
                QuestModel? model = ToModel(quest, conversion);
                if (model != null)
                {
                    conversion.Models.AddQuest(model, quest.AuthoringId);
                    conversion.Models.Alias(quest.DefinitionName, model.Key);
                }
            }
        }

        public static QuestModel? ToModel(QuestDefinition quest, NarrativeConversion conversion)
        {
            int key = NarrativeRefs.KeyOf(quest);
            if (key == 0)
            {
                conversion.Problem(quest, NarrativeDiagnosticCodes.ContentKeyCollision + ": quest " + quest.name + " has no authoring id");
                return null;
            }

            if (quest.Stages.Count == 0)
            {
                conversion.Problem(quest, NarrativeDiagnosticCodes.QuestNoStages + ": quest " + quest.name + " has no stages");
                return null;
            }

            if (quest.Objectives.Count > NarrativeLimits.MaxObjectives)
            {
                conversion.Problem(quest, NarrativeDiagnosticCodes.QuestTooManyObjectives + ": quest " + quest.name + " has more than " + NarrativeLimits.MaxObjectives + " objectives");
                return null;
            }

            var objectives = new List<ObjectiveModel>();
            var perStage = new List<List<int>>();
            for (int s = 0; s < quest.Stages.Count; s++)
            {
                perStage.Add(new List<int>());
            }

            for (int i = 0; i < quest.Objectives.Count; i++)
            {
                ObjectiveDefinition objective = quest.Objectives[i];
                if (objective.stage < 0 || objective.stage >= quest.Stages.Count)
                {
                    conversion.Problem(quest, NarrativeDiagnosticCodes.QuestObjectiveTarget + ": objective " + i + " names stage " + objective.stage);
                    continue;
                }

                if (objective.branch < 0 || (objective.branch > 0 && quest.BranchNames.Count > 0 && objective.branch > quest.BranchNames.Count))
                {
                    conversion.Problem(quest, NarrativeDiagnosticCodes.QuestBadBranch + ": objective " + i + " names branch " + objective.branch);
                }

                int target = objective.kind == ObjectiveKind.Interact ? NarrativeRefs.EntityKey(objective.targetEntityId) : NarrativeRefs.KeyOf(objective.target);
                if (target == 0)
                {
                    conversion.Problem(quest, NarrativeDiagnosticCodes.QuestObjectiveTarget + ": objective " + i + " (" + objective.kind + ") has no target");
                }

                if (objective.target != null && objective.target is INarrativeDefinition)
                {
                    conversion.Convert(objective.target);
                }

                string label = objective.kind == ObjectiveKind.Interact ? objective.targetEntityId : NarrativeRefs.LabelOf(objective.target);
                objectives.Add(new ObjectiveModel(objectives.Count, objective.stage, objective.kind, target, Math.Max(1, objective.required), objective.branch,
                    objective.text, label));
                perStage[objective.stage].Add(objectives.Count - 1);
            }

            var stages = new List<StageModel>();
            for (int s = 0; s < quest.Stages.Count; s++)
            {
                QuestStageEntry stage = quest.Stages[s];
                if (stage.next < -1 || stage.next > quest.Stages.Count)
                {
                    conversion.Problem(quest, NarrativeDiagnosticCodes.QuestObjectiveTarget + ": stage " + s + " continues at " + stage.next);
                }

                stages.Add(new StageModel(s, stage.title, stage.description, perStage[s], stage.next));
            }

            var rewards = new List<RewardModel>();
            for (int r = 0; r < quest.Rewards.Count; r++)
            {
                QuestRewardEntry reward = quest.Rewards[r];
                int target = NarrativeRefs.KeyOf(reward.target);
                if (target == 0)
                {
                    conversion.Problem(quest, NarrativeDiagnosticCodes.QuestObjectiveTarget + ": reward " + r + " has no item or fact");
                    continue;
                }

                if (reward.target is INarrativeDefinition)
                {
                    conversion.Convert(reward.target!);
                }

                rewards.Add(new RewardModel(reward.kind, target, reward.value, reward.branch, NarrativeRefs.LabelOf(reward.target)));
            }

            ConditionSetModel? fail = conversion.ConditionSet(quest.FailConditions);
            return new QuestModel(key, quest.DefinitionName, stages, objectives, rewards, fail, quest.BranchNames);
        }
    }

    /// <summary>Fixed limits of the quest plugin's slots.</summary>
    public static class NarrativeLimits
    {
        /// <summary>Objectives per quest (slots quest.obj.0..n-1.count/done).</summary>
        public const int MaxObjectives = 32;
    }
}
