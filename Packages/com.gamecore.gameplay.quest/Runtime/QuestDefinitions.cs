// GameCore.Gameplay.Quest - quest authoring and its conversion to the pure models (P1.4, catalog row 7).
//
//   QuestDefinition       stages (title, description, next), objectives, rewards, fail conditions, branch names
//   ObjectiveDefinition   one objective of a stage: talk (graph), collect (item), reach (region), interact (entity),
//                         fact (fact value); objectives with a branch number form alternative ways through a stage
//
// P1.7b: an objective's and a reward's target is one typed [AuthorRef] per kind (graph, item, region, fact); the P1.4
// single `target` (pseudo-category narrative.subject) is a hidden legacy field migrated on load and by
// authoring.migrateRefs, and the `target` accessor reads the field the kind uses.
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
using GameCore.Gameplay.World;
using UnityEngine;
using UnityEngine.Serialization;

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
    public sealed class ObjectiveDefinition : ISerializationCallbackReceiver
    {
        [AuthorField(Min = 0, Doc = "Stage index the objective belongs to.")]
        public int stage;

        [AuthorField(Doc = "Talk, collect, reach, interact or fact.")]
        public ObjectiveKind kind = ObjectiveKind.Fact;

        [AuthorRef(Category = NarrativeKinds.Graph, Required = false, Doc = "The dialogue graph (talk objectives).")]
        public ScriptableObject? graph;

        [AuthorRef(Category = NarrativeKinds.Item, Required = false, Doc = "The item (collect objectives).")]
        public ScriptableObject? item;

        [AuthorRef(Category = NarrativeSubjects.Region, Required = false, Doc = "The region (reach objectives).")]
        public RegionDefinition? region;

        [AuthorRef(Category = NarrativeKinds.Fact, Required = false, Doc = "The fact (fact objectives).")]
        public ScriptableObject? fact;

        [AuthorRef(Category = AuthorRefCategories.EntityInstance, Required = false, Doc = "Entity authoring id (interact objectives).")]
        public string targetEntityId = string.Empty;

        [AuthorField(Min = 1, Doc = "Count needed (collect: items held; fact: the value reached; talk/interact: times).")]
        public int required = 1;

        [AuthorField(Min = 0, Doc = "0 = always needed; n > 0 = part of alternative branch n.")]
        public int branch;

        [AuthorField(Doc = "Journal text.")]
        public string text = string.Empty;

        // Legacy P1.4 storage (pseudo-category narrative.subject); see the file header.
        [SerializeField, HideInInspector, FormerlySerializedAs("target")] private ScriptableObject? legacyTarget;

        /// <summary>The objective's target: the typed field the kind uses (the legacy field until migrated). Setting it stores it by its own kind.</summary>
        public ScriptableObject? target
        {
            get
            {
                ScriptableObject? typed = kind switch
                {
                    ObjectiveKind.Talk => graph,
                    ObjectiveKind.Collect => item,
                    ObjectiveKind.Reach => region,
                    ObjectiveKind.Fact => fact,
                    _ => null,
                };
                return typed != null ? typed : legacyTarget;
            }

            set => Assign(value);
        }

        public ScriptableObject? LegacyTarget => legacyTarget;

        public bool MigrateLegacy()
        {
            if (ReferenceEquals(legacyTarget, null) || NarrativeSubjects.KindOf(legacyTarget).Length == 0)
            {
                return false;
            }

            ScriptableObject moved = legacyTarget!;
            Assign(moved);
            return true;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() => MigrateLegacy();

        private void Assign(ScriptableObject? next)
        {
            graph = null;
            item = null;
            region = null;
            fact = null;
            legacyTarget = null;
            switch (NarrativeSubjects.KindOf(next))
            {
                case NarrativeKinds.Graph: graph = next; break;
                case NarrativeKinds.Item: item = next; break;
                case NarrativeSubjects.Region: region = (RegionDefinition)next!; break;
                case NarrativeKinds.Fact: fact = next; break;
                default: legacyTarget = next; break;
            }
        }
    }

    /// <summary>One reward of a quest, granted exactly once (through the outbox) when the quest completes.</summary>
    [Serializable]
    public sealed class QuestRewardEntry : ISerializationCallbackReceiver
    {
        [AuthorField(Doc = "Item or fact.")]
        public RewardKind kind = RewardKind.Item;

        [AuthorRef(Category = NarrativeKinds.Item, Required = false, Doc = "The item granted (item rewards).")]
        public ScriptableObject? item;

        [AuthorRef(Category = NarrativeKinds.Fact, Required = false, Doc = "The fact set (fact rewards).")]
        public ScriptableObject? fact;

        // Legacy P1.4 storage (pseudo-category narrative.subject); see the file header.
        [SerializeField, HideInInspector, FormerlySerializedAs("target")] private ScriptableObject? legacyTarget;

        /// <summary>The reward's target: the item or the fact by kind (the legacy field until migrated). Setting it stores it by its own kind.</summary>
        public ScriptableObject? target
        {
            get
            {
                ScriptableObject? typed = kind == RewardKind.Fact ? fact : item;
                return typed != null ? typed : legacyTarget;
            }

            set => Assign(value);
        }

        public ScriptableObject? LegacyTarget => legacyTarget;

        public bool MigrateLegacy()
        {
            if (ReferenceEquals(legacyTarget, null) || NarrativeSubjects.KindOf(legacyTarget).Length == 0)
            {
                return false;
            }

            ScriptableObject moved = legacyTarget!;
            Assign(moved);
            return true;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() => MigrateLegacy();

        private void Assign(ScriptableObject? next)
        {
            item = null;
            fact = null;
            legacyTarget = null;
            switch (NarrativeSubjects.KindOf(next))
            {
                case NarrativeKinds.Item: item = next; break;
                case NarrativeKinds.Fact: fact = next; break;
                default: legacyTarget = next; break;
            }
        }

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

            // P1.7b -> P1.7a: prerequisite quest keys (each prerequisite is converted too); the kernel derives the
            // dependents a failure closes from every quest's prerequisites (QuestRules.DependentsOf).
            var prerequisites = new List<int>();
            for (int p = 0; p < quest.Prerequisites.Count; p++)
            {
                QuestDefinition? prerequisite = quest.Prerequisites[p];
                int prerequisiteKey = NarrativeRefs.KeyOf(prerequisite);
                if (prerequisite == null || prerequisiteKey == 0 || prerequisite == quest)
                {
                    conversion.Problem(quest, AuthoringHardeningCodes.QuestPrerequisiteCycle + ": prerequisite " + p + " of quest " + quest.name + " is empty or the quest itself");
                    continue;
                }

                conversion.Convert(prerequisite);
                if (!prerequisites.Contains(prerequisiteKey))
                {
                    prerequisites.Add(prerequisiteKey);
                }
            }

            return new QuestModel(key, quest.DefinitionName, stages, objectives, rewards, fail, quest.BranchNames, prerequisites, null);
        }
    }

    /// <summary>Fixed limits of the quest plugin's slots.</summary>
    public static class NarrativeLimits
    {
        /// <summary>Objectives per quest (slots quest.obj.0..n-1.count/done).</summary>
        public const int MaxObjectives = 32;
    }
}
