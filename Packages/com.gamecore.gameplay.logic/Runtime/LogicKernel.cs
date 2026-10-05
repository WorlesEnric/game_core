// GameCore.Gameplay.Logic - the logic plugin's kernel half (P1.4, catalog row 9).
//
//   logic.evaluate   (rule target)  actor, subject, trigger kind, event values  -> RuleFired + ActionDue... | RuleSkipped
//   logic.runActions (hub target)   action set, actor, subject                 -> ActionDue... + ActionsRun
//
// The logic stage runs after the world, inventory, quest and dialogue stages (optional-after edges), so it decides over
// the state those stages wrote this step. A rule's decision reads only its own slots (logic.fired, logic.cooldownMs,
// logic.counter) and committed gameplay state through the pure rules; every action that only the outbox can deliver is
// committed as one ActionDue event, which the narrative delivery turns into exactly one destination command. The
// explain trace records every decision with the first failed condition and the inputs read.
//
// P1.7a (A1): trigger routing moved into the committing step. The narrative delivery (the world's step tap) sees every
// committed trigger event and records one logic.evaluate obligation per selected rule, in priority order; TryTrigger is
// the shared decoder. Both routes take an optional trailing request id (the obligation's): evaluate 5(+1), runActions
// 3(+1); an obligation is claimed before the decision and settled in the step that commits it. Rule time (cooldowns,
// time conditions) is GameplayClock time through NarrativeState.NowMs (A5).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.Logic
{
    /// <summary>Declarations of the logic plugin.</summary>
    public static class LogicDeclarations
    {
        public const string Stem = "logic";

        public static readonly StageId Stage = NarrativePluginSpec.StageOf(Stem);

        /// <summary>The schema of P1.3's successful interaction event (InteractionSlots.SucceededEvent).</summary>
        public static readonly SchemaRef InteractionSucceededEvent = InteractionSlots.SucceededEvent;

        /// <summary>A fresh spec of the logic plugin (slots do not depend on content).</summary>
        public static NarrativePluginSpec Spec()
        {
            return new NarrativePluginSpec(Stem, NarrativeCatalogNames.Logic, LogicIds.Owner)
                .Route(LogicIds.EvaluateRoute, LogicIds.EvaluateCommand, "evaluate", 5, 1)
                .Route(LogicIds.RunActionsRoute, LogicIds.RunActionsCommand, "run-actions", 3, 1)
                .Slot(LogicIds.Fired, 0, "fired")
                .Slot(LogicIds.CooldownMs, 0, "cooldown-ms")
                .Slot(LogicIds.Counter, 0, "counter")
                .Slot(LogicIds.Invocations, 0, "invocations")
                .After(WorldDeclarations.Stage)
                .After(NarrativePluginSpec.StageOf("inventory"))
                .After(NarrativePluginSpec.StageOf("quest"))
                .After(NarrativePluginSpec.StageOf("dialogue"));
        }
    }

    /// <summary>Commits ActionDue events for the actions of a set (shared by logic, dialogue and inventory stages).</summary>
    public static class NarrativeActions
    {
        /// <summary>Adds one ActionDue per action; fact actions are skipped when <paramref name="skipFacts"/> (applied in-step by the caller).</summary>
        public static int AddDue(StepEventBatch batch, TargetId hub, ActionSetModel? set, int actor, int subject, int source, bool skipFacts)
        {
            if (set == null)
            {
                return 0;
            }

            int added = 0;
            for (int i = 0; i < set.Actions.Count; i++)
            {
                if (skipFacts && ActionRules.IsFactAction(set.Actions[i].Kind))
                {
                    continue;
                }

                batch.Add(LogicIds.ActionDueEvent, hub, set.Key, i, (int)set.Actions[i].Kind, actor, subject, source);
                added++;
            }

            return added;
        }
    }

    /// <summary>The logic module of one world: declarations, attach, the in-step command handling and the trigger router.</summary>
    public sealed class LogicModule : INarrativeModule, INarrativeEventListener
    {
        private readonly Dictionary<TargetId, RuleModel> rulesByTarget = new Dictionary<TargetId, RuleModel>();
        private readonly Dictionary<int, TargetId> targetsByRule = new Dictionary<int, TargetId>();
        private NarrativeRuntime? runtime;

        public string Name => "logic";

        public INarrativeContentConverter? Converter => new LogicContentConverter();

        public NarrativeRuntime? Runtime => runtime;

        public int Fired { get; private set; }

        public int Skipped { get; private set; }

        public int Runs { get; private set; }

        public int Refused { get; private set; }

        public int Malformed { get; private set; }

        /// <summary>Rule evaluations recorded from trigger events (by the narrative delivery, in-step; P1.7a).</summary>
        public int Triggered => runtime != null ? runtime.Delivery.Triggered : 0;

        /// <summary>Evaluations or runs refused because their obligation was already applied.</summary>
        public int AlreadyApplied { get; private set; }

        public void Declare(NarrativeComposition composition)
        {
            NarrativePluginSpec spec = LogicDeclarations.Spec();
            composition.AddPlugin(spec, new ManagedSystemRegistration<LogicCommandSystem>(spec.CommandSystem, spec.Stage, "GameplayLogicCommandSystem"));
            composition.AddRecipe(spec.CreateRecipe("hub"));
            composition.AddRecipe(spec.CreateRecipe("rule"));
            composition.AddSeed(composition.Index.HubTarget, spec.Recipe("hub"), "logic-hub");
            for (int i = 0; i < composition.Index.Targets.Count; i++)
            {
                NarrativeTarget target = composition.Index.Targets[i];
                if (target.Kind == NarrativeTargetKind.Rule)
                {
                    composition.AddSeed(target.Target, spec.Recipe("rule"), "rule:" + target.Name);
                }
            }
        }

        public void Attach(NarrativeRuntime attached, bool seedSlots)
        {
            runtime = attached ?? throw new ArgumentNullException(nameof(attached));
            foreach (RuleModel rule in attached.Models.Rules)
            {
                TargetId target = attached.Index.TargetOf(NarrativeTargetKind.Rule, rule.Key);
                if (target.IsDefault)
                {
                    continue;
                }

                rulesByTarget[target] = rule;
                targetsByRule[rule.Key] = target;
                if (seedSlots)
                {
                    attached.Seed(target, LogicIds.Owner, LogicIds.Fired, 0);
                    attached.Seed(target, LogicIds.Owner, LogicIds.CooldownMs, 0);
                    attached.Seed(target, LogicIds.Owner, LogicIds.Counter, 0);
                }
            }

            if (seedSlots)
            {
                attached.Seed(attached.Index.HubTarget, LogicIds.Owner, LogicIds.Invocations, 0);
            }

            attached.System<LogicCommandSystem>().Module = this;
            attached.AddListener(this);
        }

        public bool TryRuleTarget(int ruleKey, out TargetId target) => targetsByRule.TryGetValue(ruleKey, out target);

        // ---- host half -------------------------------------------------------------------------------------------

        /// <summary>Trigger events become logic.evaluate obligations in the committing step (NarrativeDelivery); nothing to do here.</summary>
        public void OnEvent(CommittedEvent committed)
        {
        }

        public void AfterEvents(int frame)
        {
        }

        /// <summary>The trigger kind, key and value of a committed event (false for events no rule can trigger on).</summary>
        public static bool TryTrigger(NarrativeRuntime rt, CommittedEvent committed, out TriggerKind kind, out int key, out int value, out int actor, out int subject) =>
            TryTrigger(rt, committed.Schema, committed.Payload, out kind, out key, out value, out actor, out subject);

        /// <summary>The trigger kind, key and value of an event by schema and payload (in-step, P1.7a).</summary>
        public static bool TryTrigger(NarrativeRuntime rt, SchemaRef schema, FrozenPayload? payload, out TriggerKind kind, out int key, out int value, out int actor, out int subject)
        {
            kind = TriggerKind.Manual;
            key = 0;
            value = 0;
            actor = rt.ActorKey;
            subject = 0;
            if (payload == null)
            {
                return false;
            }

            if (schema.Equals(WorldDeclarations.RegionEnteredEvent))
            {
                if (!WorldEvent.TryDecode(payload, out WorldEvent world))
                {
                    return false;
                }

                kind = TriggerKind.RegionEntered;
                key = world.B;
                value = rt.Index.EntityKeyOf(world.Target);
                actor = value;
                return true;
            }

            if (schema.Equals(LogicDeclarations.InteractionSucceededEvent))
            {
                if (payload.Length < 16)
                {
                    return false;
                }

                var reader = new GameplayPayloadReader(payload.Bytes);
                kind = TriggerKind.Interacted;
                key = rt.Index.EntityKeyOf(new TargetId(reader.Id()));
                subject = key;
                if (payload.Length >= 20)
                {
                    int interactor = reader.Int32();
                    if (interactor != 0)
                    {
                        actor = interactor;
                    }
                }

                return key != 0;
            }

            if (!NarrativeEvent.TryDecode(payload, out NarrativeEvent e))
            {
                return false;
            }

            if (schema.Equals(DialogueIds.FactSetEvent))
            {
                kind = TriggerKind.FactSet;
                key = e.A;
                value = e.B;
            }
            else if (schema.Equals(InventoryIds.ItemGrantedEvent))
            {
                kind = TriggerKind.ItemGranted;
                key = e.A;
                value = e.B;
            }
            else if (schema.Equals(InventoryIds.ItemConsumedEvent))
            {
                kind = TriggerKind.ItemConsumed;
                key = e.A;
                value = e.B;
            }
            else if (schema.Equals(InventoryIds.TradeDoneEvent))
            {
                kind = TriggerKind.TradeDone;
                key = e.A;
                value = e.B;
            }
            else if (schema.Equals(InventoryIds.ItemPickedUpEvent))
            {
                kind = TriggerKind.ItemPickedUp;
                key = e.A;
                value = e.B;
            }
            else if (schema.Equals(QuestIds.StartedEvent))
            {
                kind = TriggerKind.QuestStarted;
                key = e.A;
            }
            else if (schema.Equals(QuestIds.StageEnteredEvent))
            {
                kind = TriggerKind.StageEntered;
                key = e.A;
                value = e.B;
            }
            else if (schema.Equals(QuestIds.CompletedEvent))
            {
                kind = TriggerKind.QuestCompleted;
                key = e.A;
                value = e.C;
            }
            else if (schema.Equals(QuestIds.FailedEvent))
            {
                kind = TriggerKind.QuestFailed;
                key = e.A;
            }
            else if (schema.Equals(DialogueIds.EndedEvent))
            {
                kind = TriggerKind.DialogueEnded;
                key = e.A;
                value = e.B;
                subject = e.C;
            }
            else if (schema.Equals(DialogueIds.ChoiceMadeEvent))
            {
                kind = TriggerKind.ChoiceMade;
                key = e.A;
                value = e.C;
            }
            else if (schema.Equals(LogicIds.RuleFiredEvent))
            {
                kind = TriggerKind.RuleFired;
                key = e.A;
                value = e.B;
            }
            else if (schema.Equals(LogicIds.ActionsRunEvent) && e.C != 0)
            {
                kind = TriggerKind.Interacted;
                key = e.C;
                subject = e.C;
                if (e.B != 0)
                {
                    actor = e.B;
                }
            }
            else
            {
                return false;
            }

            return true;
        }

        // ---- kernel half: the logic stage ----------------------------------------------------------------------

        internal void Run(EntityManager entityManager)
        {
            NarrativeRuntime? rt = runtime;
            WorldMessagePlane? plane = rt != null ? rt.Host.Messages : null;
            if (rt == null || plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(LogicIds.Owner);
            for (int i = 0; i < batch.Count; i++)
            {
                Execute(rt, plane, entityManager, batch[i]);
            }

            plane.ReleaseConsumed(LogicIds.Owner);
        }

        private void Execute(NarrativeRuntime rt, WorldMessagePlane plane, EntityManager entityManager, StepMessage message)
        {
            byte[] payload = plane.PayloadOf(message);
            if (plane.Readers.TryRead<NarrativeCommand>(message.PayloadSchema, payload, out NarrativeCommand command, out string _)
                != PayloadDecodeOutcome.Decoded)
            {
                Malformed++;
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            if (message.Route.Equals(LogicIds.EvaluateRoute))
            {
                Evaluate(rt, plane, entityManager, message, command);
            }
            else if (message.Route.Equals(LogicIds.RunActionsRoute))
            {
                RunActions(rt, plane, entityManager, message, command);
            }
            else
            {
                Refuse(plane, message, DiagnosticCode.Ineligible);
            }
        }

        private void Evaluate(NarrativeRuntime rt, WorldMessagePlane plane, EntityManager entityManager, StepMessage message, NarrativeCommand command)
        {
            if (!rulesByTarget.TryGetValue(message.Target, out RuleModel? rule) || rule == null
                || !NarrativeSlots.TryEntity(rt.Registry, entityManager, message.Target, out Entity _))
            {
                Refuse(plane, message, DiagnosticCode.StaleHandle);
                return;
            }

            TargetId target = message.Target;
            int requestId = NarrativeObligations.OptionalRequest(command, 5);
            if (NarrativeObligations.Admit(rt.Tap, null, requestId) != DiagnosticCode.None)
            {
                AlreadyApplied++;
                Refuse(plane, message, DiagnosticCode.IdempotencyConflict);
                return;
            }

            var state = new RuleState(
                NarrativeSlots.Read(rt.Registry, entityManager, target, LogicIds.Owner, LogicIds.Fired, 0),
                NarrativeSlots.Read(rt.Registry, entityManager, target, LogicIds.Owner, LogicIds.CooldownMs, 0),
                NarrativeSlots.Read(rt.Registry, entityManager, target, LogicIds.Owner, LogicIds.Counter, 0));
            int actor = command[0];
            int subject = command[1];
            var context = new ConditionContext(actor, subject);
            RuleDecision decision = RuleRules.Decide(rule, state, rt.State.NowMs, rt.State, rt.Models, context);
            var events = new StepEventBatch();
            if (decision.Fire)
            {
                NarrativeActions.AddDue(events, rt.Index.HubTarget, rule.Actions, actor, subject, LogicIds.SourceRule, false);
                events.Add(LogicIds.RuleFiredEvent, target, rule.Key, decision.Next.Fired, actor, subject, command[2], command[3]);
            }
            else
            {
                int failed = decision.Conditions != null ? decision.Conditions.FailedIndex : -1;
                events.Add(LogicIds.RuleSkippedEvent, target, rule.Key, (int)decision.Reason, failed, actor, 0, 0);
            }

            if (!events.CommitAll(plane, message, rt.Tap))
            {
                Refuse(plane, message, DiagnosticCode.BudgetExceeded);
                return;
            }

            NarrativeObligations.Settle(rt.Tap, requestId);

            NarrativeSlots.Write(rt.Registry, entityManager, target, LogicIds.Owner, LogicIds.Fired, decision.Next.Fired);
            NarrativeSlots.Write(rt.Registry, entityManager, target, LogicIds.Owner, LogicIds.CooldownMs, decision.Next.CooldownUntilMs);
            NarrativeSlots.Write(rt.Registry, entityManager, target, LogicIds.Owner, LogicIds.Counter, decision.Next.Counter);
            NarrativeTarget? record = rt.Index.FindTarget(target);
            rt.Explain.Record(ExplainTrace.Of(rule, record != null ? record.AuthoringId : rule.Name, (long)message.Step.Value, decision));
            if (decision.Fire)
            {
                Fired++;
            }
            else
            {
                Skipped++;
            }
        }

        private void RunActions(NarrativeRuntime rt, WorldMessagePlane plane, EntityManager entityManager, StepMessage message, NarrativeCommand command)
        {
            if (!message.Target.Equals(rt.Index.HubTarget) || !rt.Models.TryGet(command[0], out ActionSetModel? set) || set == null)
            {
                Refuse(plane, message, DiagnosticCode.Ineligible);
                return;
            }

            int requestId = NarrativeObligations.OptionalRequest(command, 3);
            if (NarrativeObligations.Admit(rt.Tap, null, requestId) != DiagnosticCode.None)
            {
                AlreadyApplied++;
                Refuse(plane, message, DiagnosticCode.IdempotencyConflict);
                return;
            }

            int invocations = NarrativeSlots.Read(rt.Registry, entityManager, message.Target, LogicIds.Owner, LogicIds.Invocations, 0) + 1;
            var events = new StepEventBatch();
            NarrativeActions.AddDue(events, rt.Index.HubTarget, set, command[1], command[2], LogicIds.SourceRun, false);
            events.Add(LogicIds.ActionsRunEvent, message.Target, set.Key, command[1], command[2], invocations, 0, 0);
            if (!events.CommitAll(plane, message, rt.Tap))
            {
                Refuse(plane, message, DiagnosticCode.BudgetExceeded);
                return;
            }

            NarrativeObligations.Settle(rt.Tap, requestId);

            NarrativeSlots.Write(rt.Registry, entityManager, message.Target, LogicIds.Owner, LogicIds.Invocations, invocations);
            Runs++;
        }

        private void Refuse(WorldMessagePlane plane, StepMessage message, DiagnosticCode code)
        {
            Refused++;
            plane.Reject(message, code, plane.ExecutingStep);
        }
    }

    /// <summary>The logic command stage (runs after the gameplay stages).</summary>
    [DisableAutoCreation]
    public partial class LogicCommandSystem : SystemBase
    {
        /// <summary>This world's module; set after boot. Until then the stage is idle.</summary>
        public LogicModule? Module { get; set; }

        protected override void OnUpdate()
        {
            LogicModule? module = Module;
            if (module != null)
            {
                module.Run(EntityManager);
            }
        }
    }
}
