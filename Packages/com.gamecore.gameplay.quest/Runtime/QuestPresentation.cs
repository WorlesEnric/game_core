// GameCore.Gameplay.Quest - quest presentation over committed slots (P1.4).
//
//   JournalPresenter  every quest that has started, its current stage and its objectives, pushed to an IJournalView
//                     whenever any committed quest slot changes (P1.5 renders it)
//   ObjectiveMarker   where the current objectives point: a region (reach), an entity (interact) or a conversation
//                     (talk), for the world-space marker and the compass P1.5 draws
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;

namespace GameCore.Gameplay.Quest
{
    /// <summary>The committed journal.</summary>
    public static class JournalViews
    {
        public static JournalViewModel Read(NarrativeRuntime runtime, ICommittedSlotReader slots, int revision)
        {
            var quests = new List<QuestView>();
            foreach (QuestModel quest in runtime.Models.Quests)
            {
                TargetId target = runtime.Index.TargetOf(NarrativeTargetKind.Quest, quest.Key);
                if (target.IsDefault)
                {
                    continue;
                }

                QuestState state = QuestDeclarations.ReadState(quest, target, slots);
                if (state.Status == QuestRules.Inactive)
                {
                    continue;
                }

                var objectives = new List<ObjectiveView>();
                StageModel? stage = state.Stage >= 0 && state.Stage < quest.Stages.Count ? quest.Stages[state.Stage] : null;
                if (stage != null)
                {
                    for (int i = 0; i < stage.Objectives.Count; i++)
                    {
                        ObjectiveModel objective = quest.Objectives[stage.Objectives[i]];
                        objectives.Add(new ObjectiveView(objective.Index, objective.Text.Length > 0 ? objective.Text : objective.Describe(),
                            state.Counts[objective.Index], objective.Required, state.Done[objective.Index] != 0, objective.Branch));
                    }
                }

                quests.Add(new QuestView(quest.Key, quest.Name, state.Status, state.Stage,
                    stage != null ? stage.Title : string.Empty, stage != null ? stage.Description : string.Empty, state.Branch, objectives));
            }

            return new JournalViewModel(quests, revision);
        }

        /// <summary>A fingerprint of every committed quest slot (changes whenever the journal would).</summary>
        public static int Fingerprint(NarrativeRuntime runtime, ICommittedSlotReader slots)
        {
            unchecked
            {
                int hash = 17;
                foreach (QuestModel quest in runtime.Models.Quests)
                {
                    TargetId target = runtime.Index.TargetOf(NarrativeTargetKind.Quest, quest.Key);
                    if (target.IsDefault)
                    {
                        continue;
                    }

                    hash = (hash * 31) + slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.Status, 0);
                    hash = (hash * 31) + slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.Stage, 0);
                    hash = (hash * 31) + slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.Branch, 0);
                    for (int n = 0; n < quest.Objectives.Count; n++)
                    {
                        hash = (hash * 31) + slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.ObjectiveCount(n), 0);
                        hash = (hash * 31) + slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.ObjectiveDone(n), 0);
                    }
                }

                return hash;
            }
        }
    }

    /// <summary>Pushes the committed journal to an IJournalView when it changes.</summary>
    public sealed class JournalPresenter : IPresentationBinder
    {
        private readonly NarrativeRuntime runtime;
        private int fingerprint;
        private bool first = true;

        public JournalPresenter(NarrativeRuntime runtime, IJournalView view)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            View = view ?? new NullJournalView();
        }

        public IJournalView View { get; set; }

        public string BinderName => "quest.journal";

        public bool IsActive => true;

        public int Revision { get; private set; }

        public JournalViewModel? Last { get; private set; }

        public int Present(ICommittedSlotReader slots)
        {
            int next = JournalViews.Fingerprint(runtime, slots);
            if (!first && next == fingerprint)
            {
                return 0;
            }

            first = false;
            fingerprint = next;
            Revision++;
            Last = JournalViews.Read(runtime, slots, Revision);
            View.Show(Last);
            return 1;
        }
    }

    /// <summary>Where one current objective points.</summary>
    public sealed class ObjectiveMarkerInfo
    {
        public ObjectiveMarkerInfo(int questKey, int objective, ObjectiveKind kind, string text, string regionId, string entityId, int graphKey)
        {
            QuestKey = questKey;
            Objective = objective;
            Kind = kind;
            Text = text;
            RegionId = regionId;
            EntityId = entityId;
            GraphKey = graphKey;
        }

        public int QuestKey { get; }

        public int Objective { get; }

        public ObjectiveKind Kind { get; }

        public string Text { get; }

        /// <summary>Region authoring id (reach objectives).</summary>
        public string RegionId { get; }

        /// <summary>Entity authoring id (interact objectives).</summary>
        public string EntityId { get; }

        /// <summary>Dialogue graph key (talk objectives).</summary>
        public int GraphKey { get; }
    }

    /// <summary>The current, not yet done objectives of every active quest that point somewhere.</summary>
    public sealed class ObjectiveMarker : IPresentationBinder
    {
        private readonly NarrativeRuntime runtime;
        private readonly List<ObjectiveMarkerInfo> markers = new List<ObjectiveMarkerInfo>();
        private int fingerprint;
        private bool first = true;

        public ObjectiveMarker(NarrativeRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public string BinderName => "quest.markers";

        public bool IsActive => true;

        public IReadOnlyList<ObjectiveMarkerInfo> Markers => markers;

        public int Revision { get; private set; }

        public int Present(ICommittedSlotReader slots)
        {
            int next = JournalViews.Fingerprint(runtime, slots);
            if (!first && next == fingerprint)
            {
                return 0;
            }

            first = false;
            fingerprint = next;
            markers.Clear();
            foreach (QuestModel quest in runtime.Models.Quests)
            {
                TargetId target = runtime.Index.TargetOf(NarrativeTargetKind.Quest, quest.Key);
                if (target.IsDefault)
                {
                    continue;
                }

                QuestState state = QuestDeclarations.ReadState(quest, target, slots);
                if (state.Status != QuestRules.Active || state.Stage < 0 || state.Stage >= quest.Stages.Count)
                {
                    continue;
                }

                StageModel stage = quest.Stages[state.Stage];
                for (int i = 0; i < stage.Objectives.Count; i++)
                {
                    ObjectiveModel objective = quest.Objectives[stage.Objectives[i]];
                    if (state.Done[objective.Index] != 0)
                    {
                        continue;
                    }

                    switch (objective.Kind)
                    {
                        case ObjectiveKind.Reach:
                            markers.Add(new ObjectiveMarkerInfo(quest.Key, objective.Index, objective.Kind, objective.Text,
                                runtime.Index.RegionAuthoringIdOf(objective.TargetKey), string.Empty, 0));
                            break;
                        case ObjectiveKind.Interact:
                            markers.Add(new ObjectiveMarkerInfo(quest.Key, objective.Index, objective.Kind, objective.Text,
                                string.Empty, runtime.Index.EntityAuthoringIdOf(objective.TargetKey), 0));
                            break;
                        case ObjectiveKind.Talk:
                            markers.Add(new ObjectiveMarkerInfo(quest.Key, objective.Index, objective.Kind, objective.Text,
                                string.Empty, string.Empty, objective.TargetKey));
                            break;
                    }
                }
            }

            Revision++;
            return 1;
        }
    }
}
