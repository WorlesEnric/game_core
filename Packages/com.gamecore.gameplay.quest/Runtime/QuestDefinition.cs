// GameCore.Gameplay.Quest - QuestDefinition (its own file: Unity resolves a ScriptableObject script by file name).
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
    /// <summary>A quest.</summary>
    [Authorable(NarrativeKinds.Quest, DisplayName = "Quest", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A quest: stages of objectives (with alternative branches), rewards granted exactly once, optional fail conditions.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Quest", fileName = "Quest")]
    public sealed class QuestDefinition : NarrativeDefinitionAsset
    {
        [AuthorField(Doc = "Journal title.")]
        [SerializeField] private string title = string.Empty;

        [AuthorField(Doc = "The stages, in order.")]
        [SerializeField] private List<QuestStageEntry> stages = new List<QuestStageEntry>();

        [AuthorField(Doc = "The objectives of every stage.")]
        [SerializeField] private List<ObjectiveDefinition> objectives = new List<ObjectiveDefinition>();

        [AuthorField(Doc = "Rewards on completion.")]
        [SerializeField] private List<QuestRewardEntry> rewards = new List<QuestRewardEntry>();

        [AuthorRef(Category = NarrativeKinds.ConditionSet, Required = false, Doc = "While active, the quest fails as soon as these hold.")]
        [SerializeField] private ConditionSetDefinition? failConditions;

        [AuthorField(Doc = "Names of branches 1..n (journal, simulate).")]
        [SerializeField] private List<string> branchNames = new List<string>();

        public override string NarrativeKind => NarrativeKinds.Quest;

        public string Title => title.Length > 0 ? title : name;

        public IReadOnlyList<QuestStageEntry> Stages => stages;

        public IReadOnlyList<ObjectiveDefinition> Objectives => objectives;

        public IReadOnlyList<QuestRewardEntry> Rewards => rewards;

        public ConditionSetDefinition? FailConditions => failConditions;

        public IReadOnlyList<string> BranchNames => branchNames;

        public void Configure(string journalTitle, ConditionSetDefinition? fail, IEnumerable<string>? branches)
        {
            title = journalTitle ?? string.Empty;
            failConditions = fail;
            branchNames = branches != null ? new List<string>(branches) : new List<string>();
        }

        public int AddStage(QuestStageEntry stage)
        {
            stages.Add(stage ?? throw new ArgumentNullException(nameof(stage)));
            return stages.Count - 1;
        }

        public int AddObjective(ObjectiveDefinition objective)
        {
            objectives.Add(objective ?? throw new ArgumentNullException(nameof(objective)));
            return objectives.Count - 1;
        }

        public int AddReward(QuestRewardEntry reward)
        {
            rewards.Add(reward ?? throw new ArgumentNullException(nameof(reward)));
            return rewards.Count - 1;
        }
    }
}
