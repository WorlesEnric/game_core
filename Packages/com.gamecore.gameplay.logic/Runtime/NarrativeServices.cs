// GameCore.Gameplay.Logic - the logic package's services: the explain trace, the condition evaluator and the action
// runner the interaction package (P1.3) and the dialogue package call (P1.4).
//
//   ExplainTrace                  IExplainSource: the last 256 evaluations (rule fires and skips from the logic stage,
//                                 direct evaluations from the evaluator) with the first failed condition and every
//                                 input read - what Studio's inspect.explain shows
//   NarrativeConditionEvaluator   IConditionEvaluator over committed slots: a condition set (authoring id or name) or a
//                                 fact shorthand narrative.fact.<name>[op N]
//   NarrativeActionRunner         IActionRunner: an action set (submitted as logic.runActions, delivered once each by
//                                 the outbox) or the built-in inventory.pickup
//
// The P1.4 brief's signatures are kept; the same simple names exist in GameCore.Gameplay.Contracts (P1.3), so they are
// always named here through the Seams alias.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Rules.Gameplay.Inventory;
using GameCore.Rules.Gameplay.Logic;
using Seams = GameCore.Gameplay.Contracts.Narrative;

namespace GameCore.Gameplay.Logic
{
    /// <summary>The explain trace: a ring of the last 256 evaluations.</summary>
    public sealed class ExplainTrace : IExplainSource
    {
        public const int Capacity = 256;

        private readonly BoundedRing<ExplainRecord> ring = new BoundedRing<ExplainRecord>(Capacity);
        private readonly NarrativeModelSet models;

        public ExplainTrace(NarrativeModelSet models)
        {
            this.models = models;
        }

        public int Count => ring.Count;

        public long Added => ring.Added;

        public void Record(ExplainRecord record)
        {
            if (record != null)
            {
                ring.Add(record);
            }
        }

        public IReadOnlyList<ExplainRecord> Recent(int max) => ring.Recent(max);

        public bool TryExplain(string ruleRef, out ExplainRecord? record)
        {
            string name = ruleRef ?? string.Empty;
            if (models.TryResolve(name, out int key))
            {
                name = models.NameOf(key);
            }

            for (int i = 0; i < ring.Count; i++)
            {
                ExplainRecord candidate = ring.At(i);
                if (string.Equals(candidate.RuleRef, ruleRef, StringComparison.Ordinal)
                    || string.Equals(candidate.RuleName, ruleRef, StringComparison.Ordinal)
                    || string.Equals(candidate.RuleRef, name, StringComparison.Ordinal))
                {
                    record = candidate;
                    return true;
                }
            }

            record = null;
            return false;
        }

        /// <summary>The record of one rule decision.</summary>
        public static ExplainRecord Of(RuleModel rule, string ruleRef, long step, RuleDecision decision)
        {
            ConditionResult? conditions = decision.Conditions;
            return new ExplainRecord(
                ruleRef,
                rule.Name,
                step,
                decision.Fire,
                RuleDecision.ReasonName(decision.Reason),
                conditions != null ? conditions.FailedIndex : -1,
                conditions != null ? conditions.FailedCondition : GateText(rule, decision),
                conditions != null ? conditions.Inputs : Array.Empty<string>());
        }

        private static string GateText(RuleModel rule, RuleDecision decision)
        {
            switch (decision.Reason)
            {
                case SkipReason.Once: return "the rule fires once and already fired";
                case SkipReason.MaxFires: return "the rule reached " + rule.MaxFires.ToString(CultureInfo.InvariantCulture) + " fires";
                case SkipReason.Cooldown: return "cooling down until " + decision.Next.CooldownUntilMs.ToString(CultureInfo.InvariantCulture) + " ms";
                default: return string.Empty;
            }
        }
    }

    /// <summary>Evaluates condition references over committed state.</summary>
    public sealed class NarrativeConditionEvaluator : Seams.IConditionEvaluator
    {
        public const string FactPrefix = "narrative.fact.";

        private readonly NarrativeRuntime runtime;

        public NarrativeConditionEvaluator(NarrativeRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public int Evaluations { get; private set; }

        public bool Evaluate(string conditionSetRef, in EvaluationContext ctx, out string failedCondition)
        {
            Evaluations++;
            if (string.IsNullOrEmpty(conditionSetRef))
            {
                failedCondition = string.Empty;
                return true;
            }

            var context = new ConditionContext(ctx.ActorKey != 0 ? ctx.ActorKey : runtime.ActorKey, ctx.SubjectKey);
            ConditionSetModel? set;
            if (conditionSetRef.StartsWith(FactPrefix, StringComparison.Ordinal))
            {
                if (!TryParseFact(conditionSetRef.Substring(FactPrefix.Length), runtime.Models, out set, out string problem) || set == null)
                {
                    failedCondition = problem;
                    return false;
                }
            }
            else if (!runtime.Models.TryResolve(conditionSetRef, out int key) || !runtime.Models.TryGet(key, out set) || set == null)
            {
                failedCondition = NarrativeDiagnosticCodes.ConditionSetUnknown + ": no baked condition set '" + conditionSetRef + "'";
                return false;
            }

            ConditionResult result = ConditionRules.Evaluate(set, runtime.State, runtime.Models, context);
            runtime.Explain.Record(new ExplainRecord(
                conditionSetRef,
                set.Name,
                0L,
                result.Passed,
                result.Passed ? "evaluated" : "condition",
                result.FailedIndex,
                result.FailedCondition,
                result.Inputs));
            failedCondition = result.FailedCondition;
            return result.Passed;
        }

        /// <summary>Parses <c>name</c>, <c>name&gt;=N</c>, <c>name==N</c>, <c>name!=N</c>, <c>name&lt;N</c>... into a one-condition set.</summary>
        public static bool TryParseFact(string text, NarrativeModelSet models, out ConditionSetModel? set, out string problem)
        {
            set = null;
            int split = text.IndexOfAny(new[] { '=', '!', '<', '>' });
            string name = split < 0 ? text : text.Substring(0, split);
            CompareOp op = CompareOp.NotEqual;
            int value = 0;
            if (split >= 0)
            {
                string rest = text.Substring(split);
                int digits = 0;
                while (digits < rest.Length && (rest[digits] == '=' || rest[digits] == '!' || rest[digits] == '<' || rest[digits] == '>'))
                {
                    digits++;
                }

                if (!Compare.TryParse(rest.Substring(0, digits), out op)
                    || !int.TryParse(rest.Substring(digits), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    problem = NarrativeDiagnosticCodes.ConditionInvalid + ": '" + text + "' is not <fact>[op value]";
                    return false;
                }
            }

            if (!models.TryGetFactByName(name, out FactModel? fact) || fact == null)
            {
                problem = NarrativeDiagnosticCodes.FactUnknown + ": the bake declares no fact '" + name + "'";
                return false;
            }

            set = new ConditionSetModel(
                NarrativeKeys.NameKey("gameplay.fact-condition." + text),
                FactPrefix + text,
                ConditionMode.All,
                new[] { new ConditionModel(ConditionKind.Fact, fact.Key, 0, op, value, "fact " + fact.Name) });
            problem = string.Empty;
            return true;
        }
    }

    /// <summary>Runs action references: action sets through the logic stage, and the built-in inventory.pickup.</summary>
    public sealed class NarrativeActionRunner : Seams.IActionRunner
    {
        /// <summary>The built-in action that picks up the subject world item into the player's inventory.</summary>
        public const string Pickup = "inventory.pickup";

        private readonly NarrativeRuntime runtime;
        private int pickups;

        public NarrativeActionRunner(NarrativeRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public int Runs { get; private set; }

        public int Refused { get; private set; }

        public bool TryRun(string actionSetRef, in EvaluationContext ctx)
        {
            int actor = ctx.ActorKey != 0 ? ctx.ActorKey : runtime.ActorKey;
            if (string.Equals(actionSetRef, Pickup, StringComparison.Ordinal))
            {
                return RunPickup(ctx, actor);
            }

            if (!runtime.Models.TryResolve(actionSetRef ?? string.Empty, out int key) || !runtime.Models.TryGet(key, out ActionSetModel? set) || set == null)
            {
                Refused++;
                return false;
            }

            CommandAdmissionReceipt receipt = runtime.Submitter.Submit(
                LogicIds.RunActionsRoute,
                runtime.Index.HubTarget,
                LogicIds.RunActionsCommand,
                NarrativeCommands.RunActions(set.Key, actor, ctx.SubjectKey));
            Count(receipt.Admitted);
            return receipt.Admitted;
        }

        private bool RunPickup(in EvaluationContext ctx, int actor)
        {
            int worldItem = ctx.SubjectKey;
            if (!runtime.Models.TryGetWorldItem(worldItem, out WorldItemModel? _)
                && ctx.SubjectAuthoringId.Length > 0 && runtime.Models.TryResolve(ctx.SubjectAuthoringId, out int resolved))
            {
                worldItem = resolved;
            }

            if (!runtime.Models.TryGetWorldItem(worldItem, out WorldItemModel? item) || item == null
                || !runtime.Index.TryInventoryTarget(0, out TargetId inventory))
            {
                Refused++;
                return false;
            }

            pickups++;
            int requestId = NarrativeKeys.NameKey("gameplay.pickup." + runtime.Index.WorldId + "." + runtime.Host.World + "." + pickups.ToString(CultureInfo.InvariantCulture));
            CommandAdmissionReceipt receipt = runtime.Submitter.Submit(
                InventoryIds.PickupRoute,
                inventory,
                InventoryIds.PickupCommand,
                NarrativeCommands.Pickup(item.Key, requestId));
            Count(receipt.Admitted);
            return receipt.Admitted;
        }

        private void Count(bool admitted)
        {
            if (admitted)
            {
                Runs++;
            }
            else
            {
                Refused++;
            }
        }
    }

    /// <summary>
    /// Interaction until P1.3's interact.use is merged: evaluate a subject's condition, then run its action (the shape of
    /// P1.3's flow, where the interaction system evaluates in-step and runs the action after the committed use).
    /// </summary>
    public static class NarrativeInteractions
    {
        public static bool Use(NarrativeWorld world, string subjectAuthoringId, string conditionRef, string actionRef, out string failed)
        {
            EvaluationContext ctx = EvaluationContext.ForSubject(subjectAuthoringId);
            if (!world.Conditions.Evaluate(conditionRef, ctx, out failed))
            {
                return false;
            }

            return world.Actions.TryRun(actionRef, ctx);
        }
    }
}
