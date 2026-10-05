// GameCore.Gameplay.Quest - the quest plugin's kernel half and its tracker (P1.4, catalog row 7).
//
// One target per quest with quest.status/stage/branch, quest.obj.<n>.count/done and a request ring. Routes:
//
//   quest.start         request id         -> QuestStarted, StageEntered, ...
//   quest.advance       stage, request id  -> StageEntered... | QuestCompleted, RewardGranted...
//   quest.setObjective  objective, count   -> ObjectiveUpdated, [StageEntered | QuestCompleted, RewardGranted...]
//   quest.fail          request id         -> QuestFailed
//   quest.complete      request id         -> QuestCompleted, RewardGranted...
//
// The decisions are the pure QuestRules. A RewardGranted event is the whole reward: the narrative outbox turns it into
// exactly one inventory.grant (or narrative.setFact) whose request id derives from the event, so a replayed outbox or a
// lost acknowledgement never grants twice. The quest stage runs after the world and inventory stages.
//
// QuestTracker is the host half: it reads level objectives (collect, reach, fact) from committed state and submits
// quest.setObjective for every changed objective, and quest.fail when an active quest's fail conditions hold.
//
// P1.7a:
//   * Edge objectives (talk, interact) are no longer collected by the tracker from a host cursor: the narrative delivery
//     records a quest.setObjective obligation for every edge objective a committed talk/interact signal advances, in the
//     committing step (A1), so a save taken after that step holds it. quest.setObjective takes an optional request id.
//   * Every request id is admitted through NarrativeObligations (ring + obligation claim) and an obligation is settled in
//     the step that commits it.
//   * Prerequisites: quest.start is refused (Ineligible) while a prerequisite quest is not completed. Fail closes
//     dependents: a quest.fail that commits also fails, in the same step, every inactive or active quest the failed
//     quest's dependents name or that lists it as a prerequisite (QuestRules.DependentsOf/CloseDependent). The fields are
//     QuestModel.Prerequisites/Dependents; the definition fields and the converter that fills them are P1.7b's.
//   * The tracker's quest.fail is retried every ResubmitFrames while the quest stays active (it was submitted once per
//     boot before, so a refused fail never retried), with request ids from NarrativeSubmitter.NextRequestId (A6).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.Quest
{
    /// <summary>A per-world read-only snapshot query; every call copies committed slots.</summary>
    public interface IQuestRuntimeQuery
    {
        bool TryRead(int questKey, out QuestState? state);
    }

    /// <summary>Runtime conversion adds terminal actions without changing the authoring/bake converter.</summary>
    public sealed class QuestRuntimeContentConverter : INarrativeContentConverter
    {
        public bool CanConvert(UnityEngine.ScriptableObject asset) => asset is QuestDefinition;

        public void Convert(UnityEngine.ScriptableObject asset, NarrativeConversion conversion)
        {
            var definition = (QuestDefinition)asset;
            QuestModel? model = QuestContentConverter.ToModel(definition, conversion);
            if (model == null) return;
            var complete = conversion.ActionSet(definition.CompletionActions);
            var fail = conversion.ActionSet(definition.FailActions);
            var runtime = new QuestModel(model.Key, model.Name, model.Stages, model.Objectives, model.Rewards,
                model.FailConditions, model.BranchNames, model.Prerequisites, model.Dependents, complete, fail);
            conversion.Models.AddQuest(runtime, definition.AuthoringId);
            conversion.Models.Alias(definition.DefinitionName, runtime.Key);
        }
    }

    /// <summary>Declarations of the quest plugin.</summary>
    public static class QuestDeclarations
    {
        public const string Stem = "quest";

        public static readonly StageId Stage = NarrativePluginSpec.StageOf(Stem);

        public static NarrativePluginSpec Spec(NarrativeModelSet models)
        {
            var spec = new NarrativePluginSpec(Stem, NarrativeCatalogNames.Quest, QuestIds.Owner)
                .Route(QuestIds.StartRoute, QuestIds.StartCommand, "start", 1)
                .Route(QuestIds.AdvanceRoute, QuestIds.AdvanceCommand, "advance", 2)
                .Route(QuestIds.SetObjectiveRoute, QuestIds.SetObjectiveCommand, "set-objective", 2, 1)
                .Route(QuestIds.FailRoute, QuestIds.FailCommand, "fail", 1)
                .Route(QuestIds.CompleteRoute, QuestIds.CompleteCommand, "complete", 1)
                .Slot(QuestIds.Status, 0, "status")
                .Slot(QuestIds.Stage, 0, "stage")
                .Slot(QuestIds.Branch, 0, "branch")
                .Slot(QuestIds.ReqHead, 0, "req-head");
            for (int i = 0; i < NarrativeKeys.RequestRingSize; i++)
            {
                spec.Slot(QuestIds.Req(i), 0, "req-" + i);
            }

            int objectives = Math.Max(1, models.MaxObjectives);
            for (int n = 0; n < objectives; n++)
            {
                spec.Slot(QuestIds.ObjectiveCount(n), 0, "obj-" + n + "-count");
                spec.Slot(QuestIds.ObjectiveDone(n), 0, "obj-" + n + "-done");
            }

            return spec.After(WorldDeclarations.Stage).After(NarrativePluginSpec.StageOf("inventory"));
        }

        /// <summary>The committed state of a quest target.</summary>
        public static QuestState ReadState(QuestModel quest, TargetId target, ICommittedSlotReader slots)
        {
            var state = new QuestState(quest.Objectives.Count);
            state.Status = slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.Status, 0);
            state.Stage = slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.Stage, 0);
            state.Branch = slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.Branch, 0);
            for (int n = 0; n < quest.Objectives.Count; n++)
            {
                state.Counts[n] = slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.ObjectiveCount(n), 0);
                state.Done[n] = slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.ObjectiveDone(n), 0);
            }

            return state;
        }
    }

    /// <summary>The quest module of one world.</summary>
    public sealed class QuestModule : INarrativeModule, INarrativeWorldAware, IQuestRuntimeQuery
    {
        private readonly Dictionary<TargetId, QuestModel> questsByTarget = new Dictionary<TargetId, QuestModel>();
        private NarrativeRuntime? runtime;
        private int objectiveSlots = 1;

        public string Name => "quest";

        public INarrativeContentConverter? Converter => new QuestRuntimeContentConverter();

        public NarrativeRuntime? Runtime => runtime;

        public QuestTracker? Tracker { get; private set; }

        public JournalPresenter? Journal { get; private set; }

        public ObjectiveMarker? Markers { get; private set; }

        /// <summary>The view the journal presenter pushes to (P1.5 replaces it).</summary>
        public IJournalView View { get; set; } = new NullJournalView();

        public int Commands { get; private set; }

        public int Refused { get; private set; }

        public int Malformed { get; private set; }

        public int RewardsGranted { get; private set; }

        /// <summary>quest.start commands refused because a prerequisite quest is not completed (P1.7a).</summary>
        public int PrerequisiteRefusals { get; private set; }

        /// <summary>Dependent quests a fail closed in its step (P1.7a).</summary>
        public int DependentsClosed { get; private set; }

        public void Declare(NarrativeComposition composition)
        {
            NarrativePluginSpec spec = QuestDeclarations.Spec(composition.Models);
            objectiveSlots = Math.Max(1, composition.Models.MaxObjectives);
            composition.AddPlugin(spec, new ManagedSystemRegistration<QuestCommandSystem>(spec.CommandSystem, spec.Stage, "GameplayQuestCommandSystem"));
            composition.AddRecipe(spec.CreateRecipe("quest"));
            foreach (QuestModel quest in composition.Models.Quests)
            {
                composition.AddSeed(composition.Index.TargetOf(NarrativeTargetKind.Quest, quest.Key), spec.Recipe("quest"), "quest:" + quest.Name);
            }
        }

        public void Attach(NarrativeRuntime attached, bool seedSlots)
        {
            runtime = attached ?? throw new ArgumentNullException(nameof(attached));
            foreach (QuestModel quest in attached.Models.Quests)
            {
                TargetId target = attached.Index.TargetOf(NarrativeTargetKind.Quest, quest.Key);
                if (target.IsDefault)
                {
                    continue;
                }

                questsByTarget[target] = quest;
                if (!seedSlots)
                {
                    continue;
                }

                attached.Seed(target, QuestIds.Owner, QuestIds.Status, QuestIds.Inactive);
                attached.Seed(target, QuestIds.Owner, QuestIds.Stage, 0);
                attached.Seed(target, QuestIds.Owner, QuestIds.Branch, 0);
                attached.Seed(target, QuestIds.Owner, QuestIds.ReqHead, 0);
                for (int i = 0; i < NarrativeKeys.RequestRingSize; i++)
                {
                    attached.Seed(target, QuestIds.Owner, QuestIds.Req(i), 0);
                }

                for (int n = 0; n < objectiveSlots; n++)
                {
                    attached.Seed(target, QuestIds.Owner, QuestIds.ObjectiveCount(n), 0);
                    attached.Seed(target, QuestIds.Owner, QuestIds.ObjectiveDone(n), 0);
                }
            }

            attached.System<QuestCommandSystem>().Module = this;
            Tracker = new QuestTracker(attached);
            attached.AddListener(Tracker);
        }

        public void OnWorld(NarrativeWorld world)
        {
            world.Runtime.RegisterQuery<IQuestRuntimeQuery>(this);
            Journal = new JournalPresenter(world.Runtime, View);
            world.Runtime.AddPresenter(Journal);
            Markers = new ObjectiveMarker(world.Runtime);
            world.Runtime.AddPresenter(Markers);
        }

        public bool TryRead(int questKey, out QuestState? state)
        {
            NarrativeRuntime? rt = runtime;
            state = null;
            if (rt == null || rt.Delivery.Owner.IsDisposed) return false;
            TargetId target = rt.Index.TargetOf(NarrativeTargetKind.Quest, questKey);
            if (!questsByTarget.TryGetValue(target, out QuestModel? quest)) return false;
            state = QuestDeclarations.ReadState(quest, target, rt.Slots);
            return true;
        }

        // ---- kernel half --------------------------------------------------------------------------------------

        internal void Run(EntityManager entityManager)
        {
            NarrativeRuntime? rt = runtime;
            WorldMessagePlane? plane = rt != null ? rt.Host.Messages : null;
            if (rt == null || plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(QuestIds.Owner);
            for (int i = 0; i < batch.Count; i++)
            {
                Execute(rt, plane, entityManager, batch[i]);
            }

            plane.ReleaseConsumed(QuestIds.Owner);
        }

        private void Execute(NarrativeRuntime rt, WorldMessagePlane plane, EntityManager em, StepMessage message)
        {
            byte[] payload = plane.PayloadOf(message);
            if (plane.Readers.TryRead<NarrativeCommand>(message.PayloadSchema, payload, out NarrativeCommand command, out string _)
                != PayloadDecodeOutcome.Decoded)
            {
                Malformed++;
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            TargetId target = message.Target;
            if (!questsByTarget.TryGetValue(target, out QuestModel? quest) || quest == null || !NarrativeSlots.TryEntity(rt.Registry, em, target, out Entity _))
            {
                Refuse(plane, message, DiagnosticCode.StaleHandle);
                return;
            }

            QuestState state = ReadLive(rt, em, quest, target);
            var produced = new List<QuestEvent>();
            int requestId = 0;
            QuestRefusal refusal;
            bool setObjective = message.Route.Equals(QuestIds.SetObjectiveRoute);
            requestId = setObjective ? NarrativeObligations.OptionalRequest(command, 2)
                : (message.Route.Equals(QuestIds.AdvanceRoute) ? command[1] : command[0]);
            int[]? ring = RequestRing.IsTracked(requestId) ? NarrativeSlots.ReadRing(rt.Registry, em, target, QuestIds.Owner, QuestIds.Req) : null;
            if (NarrativeObligations.Admit(rt.Tap, ring, requestId) != DiagnosticCode.None)
            {
                Refuse(plane, message, DiagnosticCode.IdempotencyConflict);
                return;
            }

            var closed = new List<KeyValuePair<TargetId, QuestState>>();
            if (setObjective)
            {
                int count = command[1];
                if (count == -1 && GameplayRequestIds.IsObligation(requestId)
                    && command[0] >= 0 && command[0] < quest.Objectives.Count && !quest.Objectives[command[0]].IsLevel)
                {
                    count = state.Counts[command[0]] == int.MaxValue ? int.MaxValue : state.Counts[command[0]] + 1;
                }

                refusal = QuestRules.SetObjective(quest, state, command[0], count, produced);
            }
            else
            {
                if (message.Route.Equals(QuestIds.StartRoute))
                {
                    refusal = QuestRules.Start(quest, state, key => StatusOf(rt, em, key), produced);
                }
                else if (message.Route.Equals(QuestIds.AdvanceRoute))
                {
                    refusal = QuestRules.Advance(quest, state, command[0], produced);
                }
                else if (message.Route.Equals(QuestIds.FailRoute))
                {
                    refusal = QuestRules.Fail(quest, state, produced);
                }
                else if (message.Route.Equals(QuestIds.CompleteRoute))
                {
                    refusal = QuestRules.Complete(quest, state, produced);
                }
                else
                {
                    Refuse(plane, message, DiagnosticCode.Ineligible);
                    return;
                }
            }

            if (refusal != QuestRefusal.None)
            {
                if (refusal == QuestRefusal.PrerequisitesUnmet)
                {
                    PrerequisiteRefusals++;
                }

                Refuse(plane, message, refusal == QuestRefusal.AlreadyStarted || refusal == QuestRefusal.NotActive || refusal == QuestRefusal.PrerequisitesUnmet
                    ? DiagnosticCode.Ineligible : DiagnosticCode.ResourceUnavailable);
                return;
            }

            var events = new StepEventBatch();
            if (message.Route.Equals(QuestIds.FailRoute))
            {
                // Fail closes dependents in the same step (P1.7a).
                List<int> dependents = QuestRules.DependentsOf(quest, rt.Models.Quests);
                for (int d = 0; d < dependents.Count; d++)
                {
                    TargetId dependentTarget = rt.Index.TargetOf(NarrativeTargetKind.Quest, dependents[d]);
                    if (dependentTarget.IsDefault || dependentTarget.Equals(target)
                        || !questsByTarget.TryGetValue(dependentTarget, out QuestModel? dependent) || dependent == null
                        || !NarrativeSlots.TryEntity(rt.Registry, em, dependentTarget, out Entity _))
                    {
                        continue;
                    }

                    QuestState dependentState = ReadLive(rt, em, dependent, dependentTarget);
                    var dependentEvents = new List<QuestEvent>();
                    if (QuestRules.CloseDependent(dependentState, dependentEvents))
                    {
                        events.Add(QuestIds.FailedEvent, dependentTarget, dependent.Key, dependentState.Stage, 0, 0, 0, 0);
                        NarrativeActions.AddDue(events, rt.Index.HubTarget, dependent.FailureActions, rt.ActorKey, 0, dependent.Key, false);
                        closed.Add(new KeyValuePair<TargetId, QuestState>(dependentTarget, dependentState));
                    }
                }
            }

            int rewards = 0;
            for (int i = 0; i < produced.Count; i++)
            {
                QuestEvent e = produced[i];
                switch (e.Kind)
                {
                    case QuestEventKind.Started:
                        events.Add(QuestIds.StartedEvent, target, quest.Key, e.A, 0, 0, 0, 0);
                        break;
                    case QuestEventKind.StageEntered:
                        events.Add(QuestIds.StageEnteredEvent, target, quest.Key, e.A, e.B, e.C, 0, 0);
                        break;
                    case QuestEventKind.ObjectiveUpdated:
                        events.Add(QuestIds.ObjectiveUpdatedEvent, target, quest.Key, e.A, e.B, e.C, 0, 0);
                        break;
                    case QuestEventKind.Completed:
                        NarrativeActions.AddDue(events, rt.Index.HubTarget, quest.CompletionActions, rt.ActorKey, 0, quest.Key, false);
                        events.Add(QuestIds.CompletedEvent, target, quest.Key, e.A, e.B, 0, 0, 0);
                        break;
                    case QuestEventKind.Failed:
                        NarrativeActions.AddDue(events, rt.Index.HubTarget, quest.FailureActions, rt.ActorKey, 0, quest.Key, false);
                        events.Add(QuestIds.FailedEvent, target, quest.Key, e.A, 0, 0, 0, 0);
                        break;
                    case QuestEventKind.RewardGranted:
                        events.Add(QuestIds.RewardGrantedEvent, target, quest.Key, e.A, e.B, e.C, e.D, 0);
                        rewards++;
                        break;
                }
            }

            if (events.Count == 0)
            {
                int objective = command[0];
                int count = objective >= 0 && objective < state.Counts.Length ? state.Counts[objective] : 0;
                int done = objective >= 0 && objective < state.Done.Length ? state.Done[objective] : 0;
                events.Add(QuestIds.ObjectiveUpdatedEvent, target, quest.Key, objective, count, done, 0, 0);
            }

            if (!events.CommitAll(plane, message, rt.Tap))
            {
                Refuse(plane, message, DiagnosticCode.BudgetExceeded);
                return;
            }

            Write(rt, em, target, quest, state);
            for (int c = 0; c < closed.Count; c++)
            {
                if (questsByTarget.TryGetValue(closed[c].Key, out QuestModel? dependent) && dependent != null)
                {
                    Write(rt, em, closed[c].Key, dependent, closed[c].Value);
                    DependentsClosed++;
                }
            }

            NarrativeSlots.PushRing(rt.Registry, em, target, QuestIds.Owner, QuestIds.ReqHead, QuestIds.Req, requestId);
            NarrativeObligations.Settle(rt.Tap, requestId);
            Commands++;
            RewardsGranted += rewards;
        }

        /// <summary>The live quest.status of a quest key (in-step; an unknown key reads as inactive).</summary>
        private int StatusOf(NarrativeRuntime rt, EntityManager em, int questKey)
        {
            TargetId target = rt.Index.TargetOf(NarrativeTargetKind.Quest, questKey);
            return target.IsDefault ? QuestRules.Inactive : NarrativeSlots.Read(rt.Registry, em, target, QuestIds.Owner, QuestIds.Status, QuestRules.Inactive);
        }

        private static QuestState ReadLive(NarrativeRuntime rt, EntityManager em, QuestModel quest, TargetId target)
        {
            var state = new QuestState(quest.Objectives.Count);
            state.Status = NarrativeSlots.Read(rt.Registry, em, target, QuestIds.Owner, QuestIds.Status, 0);
            state.Stage = NarrativeSlots.Read(rt.Registry, em, target, QuestIds.Owner, QuestIds.Stage, 0);
            state.Branch = NarrativeSlots.Read(rt.Registry, em, target, QuestIds.Owner, QuestIds.Branch, 0);
            for (int n = 0; n < quest.Objectives.Count; n++)
            {
                state.Counts[n] = NarrativeSlots.Read(rt.Registry, em, target, QuestIds.Owner, QuestIds.ObjectiveCount(n), 0);
                state.Done[n] = NarrativeSlots.Read(rt.Registry, em, target, QuestIds.Owner, QuestIds.ObjectiveDone(n), 0);
            }

            return state;
        }

        private static void Write(NarrativeRuntime rt, EntityManager em, TargetId target, QuestModel quest, QuestState state)
        {
            NarrativeSlots.Write(rt.Registry, em, target, QuestIds.Owner, QuestIds.Status, state.Status);
            NarrativeSlots.Write(rt.Registry, em, target, QuestIds.Owner, QuestIds.Stage, state.Stage);
            NarrativeSlots.Write(rt.Registry, em, target, QuestIds.Owner, QuestIds.Branch, state.Branch);
            for (int n = 0; n < quest.Objectives.Count; n++)
            {
                NarrativeSlots.Write(rt.Registry, em, target, QuestIds.Owner, QuestIds.ObjectiveCount(n), state.Counts[n]);
                NarrativeSlots.Write(rt.Registry, em, target, QuestIds.Owner, QuestIds.ObjectiveDone(n), state.Done[n]);
            }
        }

        private void Refuse(WorldMessagePlane plane, StepMessage message, DiagnosticCode code)
        {
            Refused++;
            plane.Reject(message, code, plane.ExecutingStep);
        }
    }

    /// <summary>The host half: objective signals, level objectives and fail conditions to quest commands.</summary>
    public sealed class QuestTracker : INarrativeEventListener
    {
        /// <summary>Frames before an unchanged level update that did not commit is submitted again.</summary>
        public const int ResubmitFrames = 30;

        private readonly NarrativeRuntime runtime;
        private readonly List<ObjectiveSignal> signals = new List<ObjectiveSignal>();
        private readonly Dictionary<long, int> lastSubmitted = new Dictionary<long, int>();
        private readonly Dictionary<long, int> lastFrame = new Dictionary<long, int>();
        private readonly Dictionary<int, int> failFrame = new Dictionary<int, int>();

        public QuestTracker(NarrativeRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public int Signals { get; private set; }

        public int Updates { get; private set; }

        public int Fails { get; private set; }

        /// <summary>
        /// Talk and interact signals become quest.setObjective obligations in the committing step (NarrativeDelivery,
        /// P1.7a); the tracker only follows level objectives and fail conditions.
        /// </summary>
        public void OnEvent(CommittedEvent committed)
        {
        }

        public void AfterEvents(int frame)
        {
            foreach (QuestModel quest in runtime.Models.Quests)
            {
                TargetId target = runtime.Index.TargetOf(NarrativeTargetKind.Quest, quest.Key);
                if (target.IsDefault)
                {
                    continue;
                }

                QuestState state = QuestDeclarations.ReadState(quest, target, runtime.Slots);
                if (state.Status != QuestRules.Active)
                {
                    failFrame.Remove(quest.Key);
                    continue;
                }

                if (quest.FailConditions != null
                    && ConditionRules.Evaluate(quest.FailConditions, runtime.State, runtime.Models, new ConditionContext(runtime.ActorKey, 0)).Passed)
                {
                    // Submitted again every ResubmitFrames while the quest stays active (a refused fail retries, P1.7a).
                    if (!failFrame.TryGetValue(quest.Key, out int when) || frame - when >= ResubmitFrames)
                    {
                        failFrame[quest.Key] = frame;
                        runtime.Submitter.Submit(QuestIds.FailRoute, target, QuestIds.FailCommand,
                            NarrativeCommands.QuestFail(runtime.Submitter.NextRequestId()));
                        Fails++;
                    }

                    continue;
                }

                List<ObjectiveUpdate> updates = QuestTracking.Pending(quest, state, runtime.State, runtime.ActorKey, signals);
                for (int i = 0; i < updates.Count; i++)
                {
                    ObjectiveUpdate update = updates[i];
                    long slot = ((long)quest.Key << 32) | (uint)update.Objective;
                    if (!quest.Objectives[update.Objective].IsLevel)
                    {
                        continue;
                    }

                    if (lastSubmitted.TryGetValue(slot, out int earlier) && earlier == update.Count
                        && lastFrame.TryGetValue(slot, out int when) && frame - when < ResubmitFrames)
                    {
                        continue;
                    }

                    lastSubmitted[slot] = update.Count;
                    lastFrame[slot] = frame;
                    runtime.Submitter.Submit(QuestIds.SetObjectiveRoute, target, QuestIds.SetObjectiveCommand,
                        NarrativeCommands.QuestSetObjective(update.Objective, update.Count));
                    Updates++;
                }
            }

            signals.Clear();
        }

        /// <summary>Edge-objective signals recorded by the narrative delivery in-step (P1.7a).</summary>
        public int EdgeSignals => runtime.Delivery.Signals;
    }

    /// <summary>The quest command stage.</summary>
    [DisableAutoCreation]
    public partial class QuestCommandSystem : SystemBase
    {
        public QuestModule? Module { get; set; }

        protected override void OnUpdate()
        {
            QuestModule? module = Module;
            if (module != null)
            {
                module.Run(EntityManager);
            }
        }
    }
}
