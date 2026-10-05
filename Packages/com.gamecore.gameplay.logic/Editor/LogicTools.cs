// GameCore.Gameplay.Logic.Editor - logic authoring operations (P1.4, catalog row 9; Studio 03 s4/s5).
//
//   logic.addRule   create a RuleDefinition (trigger, shared or inline conditions/actions, limits) on a content set
//   logic.explain   explain a rule: its trigger, gates and conditions evaluated over the content's initial state
//                   (with a running world, IExplainSource answers from the live trace instead)
//   logic.test      decide a rule over a given state ("fact.bell_rung=1; item.old_coin=3; fired=0; now=1000")
//
// The checks are the pure rules of GameCore.Rules.Gameplay.Logic over the converted models, so a tool, the bake and the
// running logic stage agree by construction.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Rules.Gameplay.Logic;
using UnityEditor;
using UnityEngine;

namespace GameCore.Gameplay.Logic.Editor
{
    /// <summary>The logic.* authoring operations.</summary>
    public static class LogicTools
    {
        [AuthorOperation("logic.addRule", Tier = ToolTier.Mechanism, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(LogicValidator), Requires = NarrativeKinds.ContentSet,
            Doc = "Creates a rule: when the trigger happens and the conditions hold, the actions run (through the outbox, exactly once).")]
        public static RuleDefinition AddRule(
            GameplayContentSet contentSet,
            [AuthorArg(Doc = "Rule name (asset name).")] string name,
            [AuthorArg(Doc = "The event that triggers the rule.")] TriggerKind trigger,
            [AuthorArg(Category = "narrative.subject", Required = false, Doc = "Trigger filter (fact, item, quest, graph, rule...); empty = any.")] ScriptableObject? triggerSubject = null,
            [AuthorArg(Required = false, Doc = "Trigger value filter (e.g. the fact value); ignored when matchAnyValue.")] int triggerValue = 0,
            [AuthorArg(Required = false, Doc = "Match any trigger value.")] bool matchAnyValue = true,
            [AuthorArg(Category = NarrativeKinds.ConditionSet, Required = false, Doc = "Shared conditions.")] ConditionSetDefinition? conditions = null,
            [AuthorArg(Category = NarrativeKinds.ActionSet, Required = false, Doc = "Shared actions.")] ActionSetDefinition? actions = null,
            [AuthorArg(Required = false, Doc = "Fire at most once.")] bool once = false,
            [AuthorArg(Unit = "ms", Min = 0, Required = false, Doc = "Minimum time between fires.")] int cooldownMs = 0,
            [AuthorArg(Required = false, Doc = "Entity authoring id filter for region/interaction triggers.")] string triggerEntityId = "",
            [AuthorArg(Required = false, Doc = "Asset path; defaults to <content set dir>/Rules/<name>.asset.")] string assetPath = "")
        {
            if (contentSet == null && string.IsNullOrEmpty(assetPath))
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.ContentSetMissingWorld + ": a content set is required");
            }

            if (trigger == TriggerKind.Manual && actions == null)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.RuleMissingTrigger + ": a manual rule without actions does nothing");
            }

            if (cooldownMs < 0)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.ActionInvalid + ": the cooldown is not negative");
            }

            RuleDefinition rule = NarrativeAuthoring.CreateAsset<RuleDefinition>(contentSet, name, "Rules", assetPath, "logic.addRule");
            rule.ConfigureTrigger(trigger, triggerSubject, triggerEntityId ?? string.Empty, matchAnyValue, triggerValue);
            rule.SetConditions(conditions, null);
            rule.SetActions(actions, null);
            rule.ConfigureLimits(once, cooldownMs, 0, 0);
            EditorUtility.SetDirty(rule);
            NarrativeAuthoring.ThrowIfInvalid(rule);
            return rule;
        }

        [AuthorOperation("logic.explain", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live,
            Validator = typeof(LogicValidator), Requires = NarrativeKinds.Rule,
            Doc = "Explains a rule over the content's initial state: trigger, gates, every condition and its inputs, and the actions it would run.")]
        public static string Explain(RuleDefinition rule)
        {
            return Test(rule, string.Empty);
        }

        [AuthorOperation("logic.test", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live,
            Validator = typeof(LogicValidator), Requires = NarrativeKinds.Rule,
            Doc = "Decides a rule over a test state and reports fire/skip, the first failed condition and the inputs read.")]
        public static string Test(
            RuleDefinition rule,
            [AuthorArg(Doc = "State terms: fact.<name>=v; item.<item>=n; currency=n; quest.<quest>.status|stage|branch=v; visited.<graph>.<node>=0|1; now=ms; fired=n; cooldown=ms; counter=n.")] string state)
        {
            if (rule == null)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.RuleUnknown + ": a rule is required");
            }

            NarrativeModelSet models = NarrativeAuthoring.Models(string.Empty, rule);
            if (!models.TryResolve(rule.AuthoringId, out int key) || !models.TryGetRule(key, out RuleModel? model) || model == null)
            {
                string problem = models.Problems.Count > 0 ? models.Problems[0] : NarrativeDiagnosticCodes.RuleUnknown + ": the rule did not convert";
                throw new ArgumentException(problem);
            }

            StateSnapshot snapshot = NarrativeAuthoring.ParseState(state, models, out RuleState ruleState);
            RuleDecision decision = RuleRules.Decide(model, ruleState, snapshot.NowMs, snapshot, models, new ConditionContext(0, 0));
            return Describe(model, decision, models);
        }

        /// <summary>The explanation text of one decision (also used by Studio's inspect.explain over a live record).</summary>
        public static string Describe(RuleModel rule, RuleDecision decision, NarrativeModelSet models)
        {
            var text = new StringBuilder();
            text.Append("rule ").Append(rule.Name).Append(": ").Append(decision.Fire ? "FIRES" : "skips (" + RuleDecision.ReasonName(decision.Reason) + ")").Append('\n');
            text.Append("trigger ").Append(rule.Trigger.Kind.ToString());
            if (rule.Trigger.FilterKey != 0)
            {
                text.Append(" of ").Append(models.NameOf(rule.Trigger.FilterKey));
            }

            if (rule.Trigger.FilterValue != TriggerModel.AnyValue)
            {
                text.Append(" = ").Append(rule.Trigger.FilterValue.ToString(CultureInfo.InvariantCulture));
            }

            text.Append('\n');
            text.Append("limits once=").Append(rule.Once ? "yes" : "no")
                .Append(" cooldown=").Append(rule.CooldownMs.ToString(CultureInfo.InvariantCulture)).Append("ms")
                .Append(" maxFires=").Append(rule.MaxFires.ToString(CultureInfo.InvariantCulture)).Append('\n');
            ConditionResult? conditions = decision.Conditions;
            if (conditions != null)
            {
                if (!conditions.Passed)
                {
                    text.Append("failed condition #").Append(conditions.FailedIndex.ToString(CultureInfo.InvariantCulture)).Append(": ")
                        .Append(conditions.FailedCondition).Append('\n');
                }

                for (int i = 0; i < conditions.Inputs.Count; i++)
                {
                    text.Append("  read ").Append(conditions.Inputs[i]).Append('\n');
                }
            }

            if (rule.Actions != null)
            {
                for (int i = 0; i < rule.Actions.Actions.Count; i++)
                {
                    text.Append(decision.Fire ? "  run " : "  would run ").Append(rule.Actions.Actions[i].Describe()).Append('\n');
                }
            }

            return text.ToString();
        }
    }

    /// <summary>Validation of logic definitions (conditions, actions, rules) and of content sets.</summary>
    [AuthorValidator("logic.validator", Codes = new[]
    {
        NarrativeDiagnosticCodes.ContentSetMissingWorld,
        NarrativeDiagnosticCodes.ContentSetDuplicate,
        NarrativeDiagnosticCodes.ContentUnknownKind,
        NarrativeDiagnosticCodes.ContentKeyCollision,
        NarrativeDiagnosticCodes.ContentMissingReference,
        NarrativeDiagnosticCodes.FactUnknown,
        NarrativeDiagnosticCodes.FactInvalidName,
        NarrativeDiagnosticCodes.FactDuplicate,
        NarrativeDiagnosticCodes.ConditionInvalid,
        NarrativeDiagnosticCodes.ConditionCycle,
        NarrativeDiagnosticCodes.ActionInvalid,
        NarrativeDiagnosticCodes.RuleMissingTrigger,
    })]
    public static class LogicValidator
    {
        /// <summary>Problems of a narrative definition (and everything it references), or of a whole content set.</summary>
        public static IReadOnlyList<GameplayDiagnostic> Validate(ScriptableObject definition)
        {
            if (definition is GameplayContentSet set)
            {
                return ValidateSet(set);
            }

            return NarrativeAuthoring.Diagnose(definition);
        }

        public static IReadOnlyList<GameplayDiagnostic> ValidateSet(GameplayContentSet set)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (set == null)
            {
                return diagnostics;
            }

            string path = AssetDatabase.GetAssetPath(set);
            if (set.World == null)
            {
                diagnostics.Add(new GameplayDiagnostic(NarrativeDiagnosticCodes.ContentSetMissingWorld, path, set.name + " names no world"));
                return diagnostics;
            }

            NarrativeBake.Plan(set, path, set.World.AuthoringId, diagnostics);
            return diagnostics;
        }
    }
}
