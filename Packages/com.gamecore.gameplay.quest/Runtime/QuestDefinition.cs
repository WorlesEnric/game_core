// GameCore.Gameplay.Quest - QuestDefinition (its own file: Unity resolves a ScriptableObject script by file name).
// P1.7b (05 row 7): prerequisites (a quest starts only after its prerequisites completed; a failed prerequisite closes
// its dependents) and completion / failure consequences. Studio's quest.simulate reports both; the quest kernel's
// gating and consequence runs are P1.7a's wiring (see PACKET.md, "needs from P1.7a").
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
        Doc = "A quest: prerequisites, stages of objectives (with alternative branches), rewards granted exactly once, optional fail conditions, and the actions run on completion or failure.")]
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

        [AuthorRef(Category = NarrativeKinds.Quest, Required = false, Doc = "Quests that must be completed before this one can start; when one of them fails, this quest closes (fails without starting).")]
        [SerializeField] private List<QuestDefinition> prerequisites = new List<QuestDefinition>();

        [AuthorRef(Category = NarrativeKinds.ActionSet, Required = false, Doc = "Actions run (once, through the outbox) when the quest completes, after its rewards.")]
        [SerializeField] private ActionSetDefinition? completionActions;

        [AuthorRef(Category = NarrativeKinds.ActionSet, Required = false, Doc = "Actions run (once, through the outbox) when the quest fails.")]
        [SerializeField] private ActionSetDefinition? failActions;

        public override string NarrativeKind => NarrativeKinds.Quest;

        public string Title => title.Length > 0 ? title : name;

        public IReadOnlyList<QuestStageEntry> Stages => stages;

        public IReadOnlyList<ObjectiveDefinition> Objectives => objectives;

        public IReadOnlyList<QuestRewardEntry> Rewards => rewards;

        public ConditionSetDefinition? FailConditions => failConditions;

        public IReadOnlyList<string> BranchNames => branchNames;

        public IReadOnlyList<QuestDefinition> Prerequisites => prerequisites;

        public ActionSetDefinition? CompletionActions => completionActions;

        public ActionSetDefinition? FailActions => failActions;

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

        public void SetPrerequisites(IEnumerable<QuestDefinition>? quests)
        {
            prerequisites = quests != null ? new List<QuestDefinition>(quests) : new List<QuestDefinition>();
        }

        /// <summary>quest.setConsequence: the actions run on completion and on failure (null: none).</summary>
        public void SetConsequences(ActionSetDefinition? onComplete, ActionSetDefinition? onFail)
        {
            completionActions = onComplete;
            failActions = onFail;
        }

        /// <summary>quest.setBranch: names branch <paramref name="branch"/> (1..n), growing the list with empty names.</summary>
        public void SetBranchName(int branch, string branchName)
        {
            if (branch < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(branch));
            }

            while (branchNames.Count < branch)
            {
                branchNames.Add(string.Empty);
            }

            branchNames[branch - 1] = branchName ?? string.Empty;
        }
    }
}
