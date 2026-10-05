// GameCore.Gameplay.Logic.Editor - logic authoring operations (P1.4, catalog row 9; Studio 03 s4/s5).
//
//   logic.addRule   create a RuleDefinition (trigger, shared or inline conditions/actions, limits) on a content set
//   logic.explain   explain a rule: its trigger, gates and conditions evaluated over the content's initial state
//                   (with a running world, IExplainSource answers from the live trace instead)
//   logic.test      decide a rule over a given state ("fact.bell_rung=1; item.old_coin=3; fired=0; now=1000")
//   logic.whyNot    why a gated subject (interactable, trigger, portal) refuses or a rule skips over a state: the failed
//                   condition, the inputs it read, and the rules / action sets that would change what it reads (P1.7b)
//   authoring.migrateRefs  run every package's IAuthoringRefMigration (P1.7b, B1): legacy string and pseudo-category
//                   references become typed [AuthorRef]s; dry run unless apply
//
// LogicConditionExplainer is the IConditionExplainer the interaction and world tools reach through the type cache.
//
// The checks are the pure rules of GameCore.Rules.Gameplay.Logic over the converted models, so a tool, the bake and the
// running logic stage agree by construction.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
            [AuthorArg(Required = false, Doc = "Trigger filter of the trigger's kind (fact, item, vendor, quest, graph, region, rule, world item); empty = any.")] ScriptableObject? triggerSubject = null,
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

        [AuthorOperation("logic.explain", ReadOnly = true, Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live,
            Validator = typeof(LogicValidator), Requires = NarrativeKinds.Rule,
            Doc = "Explains a rule over the content's initial state: trigger, gates, every condition and its inputs, and the actions it would run.")]
        public static string Explain(RuleDefinition rule)
        {
            return Test(rule, string.Empty);
        }

        [AuthorOperation("logic.test", ReadOnly = true, Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live,
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

        [AuthorOperation("logic.whyNot", ReadOnly = true, Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live,
            Validator = typeof(LogicValidator), Requires = NarrativeKinds.ContentSet,
            Doc = "Why does it not happen? For a gated subject (interactable, trigger, portal) or a rule: its condition over a state, the first failed condition, the inputs read, and the rules and action sets that would change what it reads.")]
        public static WhyNotReport WhyNot(
            GameplayContentSet contentSet,
            [AuthorArg(Doc = "The subject: an interactable, trigger or portal definition, or a rule.")] ScriptableObject subject,
            [AuthorArg(Required = false, Doc = "State terms as logic.test (empty: the content's initial state).")] string state = "")
        {
            if (contentSet == null || contentSet.World == null)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.ContentSetMissingWorld + ": a content set of a world is required");
            }

            if (subject == null)
            {
                throw new ArgumentException(AuthoringHardeningCodes.UnresolvedReference + ": a subject is required");
            }

            var definitions = new List<ScriptableObject>(contentSet.Definitions);
            if (subject is RuleDefinition)
            {
                definitions.Add(subject);
            }

            NarrativeModelSet models = NarrativeAuthoring.Models(contentSet.World.AuthoringId, definitions.ToArray());
            StateSnapshot snapshot = NarrativeAuthoring.ParseState(state ?? string.Empty, models, out RuleState ruleState);
            ConditionSetModel? set;
            string reference;
            if (subject is RuleDefinition rule)
            {
                reference = rule.AuthoringId;
                if (!models.TryResolve(rule.AuthoringId, out int ruleKey) || !models.TryGetRule(ruleKey, out RuleModel? model) || model == null)
                {
                    throw new ArgumentException(NarrativeDiagnosticCodes.RuleUnknown + ": " + rule.name + " did not convert");
                }

                RuleDecision decision = RuleRules.Decide(model, ruleState, snapshot.NowMs, snapshot, models, new ConditionContext(0, 0));
                set = model.Conditions;
                ConditionResult? result = decision.Conditions;
                ConditionExplanation explanation = new ConditionExplanation(reference, model.Name, true, decision.Fire,
                    result != null ? result.FailedIndex : -1,
                    decision.Fire ? string.Empty : (result != null && !result.Passed ? result.FailedCondition : RuleDecision.ReasonName(decision.Reason)),
                    result != null ? result.Inputs : Array.Empty<string>());
                return new WhyNotReport(subject.name, explanation, Fixes(set, explanation, models, snapshot, ruleState));
            }

            if (!(subject is IConditionGated gated))
            {
                throw new ArgumentException(AuthoringHardeningCodes.WrongReferenceCategory + ": " + subject.name + " is not an interactable, trigger, portal or rule");
            }

            reference = gated.ConditionRef;
            ConditionExplanation gate = LogicConditionExplainer.Explain(models, reference, snapshot, out set);
            return new WhyNotReport(subject.name, gate, Fixes(set, gate, models, snapshot, ruleState));
        }

        /// <summary>The rules and action sets that change the input of the failed condition (fact and item conditions).</summary>
        private static IReadOnlyList<string> Fixes(ConditionSetModel? set, ConditionExplanation explanation, NarrativeModelSet models, StateSnapshot snapshot, RuleState ruleState)
        {
            var fixes = new List<string>();
            if (set == null || explanation.Passed || explanation.FailedIndex < 0 || explanation.FailedIndex >= set.Conditions.Count)
            {
                return fixes;
            }

            ConditionModel failed = set.Conditions[explanation.FailedIndex];
            foreach (RuleModel rule in models.Rules)
            {
                if (rule.Actions != null && Changes(rule.Actions, failed))
                {
                    RuleDecision decision = RuleRules.Decide(rule, ruleState, snapshot.NowMs, snapshot, models, new ConditionContext(0, 0));
                    string why = decision.Fire ? "would fire" : (decision.Conditions != null && !decision.Conditions.Passed
                        ? "skips: " + decision.Conditions.FailedCondition : "skips (" + RuleDecision.ReasonName(decision.Reason) + ")");
                    fixes.Add("rule " + rule.Name + " on " + rule.Trigger.Kind + " " + why);
                }
            }

            foreach (ActionSetModel actions in models.ActionSets)
            {
                if (Changes(actions, failed))
                {
                    fixes.Add("action set " + actions.Name);
                }
            }

            return fixes;
        }

        private static bool Changes(ActionSetModel actions, ConditionModel condition)
        {
            for (int i = 0; i < actions.Actions.Count; i++)
            {
                ActionModel action = actions.Actions[i];
                bool fact = condition.Kind == ConditionKind.Fact && (action.Kind == ActionKind.SetFact || action.Kind == ActionKind.AddFact);
                bool item = condition.Kind == ConditionKind.ItemCount && (action.Kind == ActionKind.Grant || action.Kind == ActionKind.Consume || action.Kind == ActionKind.Buy);
                if ((fact || item) && action.Key == condition.Key)
                {
                    return true;
                }
            }

            return false;
        }

        [AuthorOperation("authoring.migrateRefs", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Doc = "Finds legacy string and pseudo-category references (P1.3/P1.4 content) and, with apply, rewrites them as typed references; lists what it changed and what names nothing.")]
        public static MigrationSummary MigrateRefs(
            [AuthorArg(Required = false, Doc = "Asset folders to scan (empty: Assets).")] string[]? folders = null,
            [AuthorArg(Required = false, Doc = "Rewrite (true) or only report (false).")] bool apply = false)
        {
            IReadOnlyList<string> scope = folders ?? Array.Empty<string>();
            var types = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IAuthoringRefMigration>())
            {
                if (!type.IsAbstract && !type.IsInterface && type.GetConstructor(Type.EmptyTypes) != null)
                {
                    types.Add(type);
                }
            }

            types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            var reports = new List<AuthoringMigrationReport>();
            for (int i = 0; i < types.Count; i++)
            {
                var migration = (IAuthoringRefMigration)Activator.CreateInstance(types[i]);
                reports.Add(migration.Migrate(scope, apply));
            }

            return new MigrationSummary(apply, reports);
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
        AuthoringHardeningCodes.BuyActionInvalid,
        AuthoringHardeningCodes.RestoreStaminaInvalid,
        AuthoringHardeningCodes.LegacyReference,
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

            var diagnostics = NarrativeAuthoring.Diagnose(definition);
            string legacy = LogicRefMigration.LegacyFields(definition);
            if (legacy.Length > 0)
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.LegacyReference,
                    definition is IAuthoredObject authored ? authored.AuthoringId : definition.name,
                    definition.name + " still stores untyped references (" + legacy + "); run authoring.migrateRefs"));
            }

            return diagnostics;
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

    /// <summary>logic.whyNot's result.</summary>
    public sealed class WhyNotReport
    {
        public WhyNotReport(string subject, ConditionExplanation condition, IReadOnlyList<string> fixes)
        {
            Subject = subject ?? string.Empty;
            Condition = condition;
            Fixes = fixes ?? Array.Empty<string>();
        }

        public string Subject { get; }

        /// <summary>The evaluation of the subject's condition (or the rule's decision).</summary>
        public ConditionExplanation Condition { get; }

        public bool Passed => Condition.Passed;

        public string FailedCondition => Condition.FailedCondition;

        /// <summary>Rules and action sets that would change the failed condition's input.</summary>
        public IReadOnlyList<string> Fixes { get; }

        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append(Subject).Append(": ").Append(Condition).Append('\n');
            for (int i = 0; i < Condition.Inputs.Count; i++)
            {
                text.Append("  read ").Append(Condition.Inputs[i]).Append('\n');
            }

            for (int i = 0; i < Fixes.Count; i++)
            {
                text.Append("  changed by ").Append(Fixes[i]).Append('\n');
            }

            return text.ToString();
        }
    }

    /// <summary>authoring.migrateRefs' result: one report per package migration.</summary>
    public sealed class MigrationSummary
    {
        public MigrationSummary(bool applied, IReadOnlyList<AuthoringMigrationReport> reports)
        {
            Applied = applied;
            Reports = reports ?? Array.Empty<AuthoringMigrationReport>();
            int changed = 0;
            int unresolved = 0;
            for (int i = 0; i < Reports.Count; i++)
            {
                changed += Reports[i].Changed.Count;
                unresolved += Reports[i].Unresolved.Count;
            }

            ChangedCount = changed;
            UnresolvedCount = unresolved;
        }

        public bool Applied { get; }

        public int ChangedCount { get; }

        public int UnresolvedCount { get; }

        public IReadOnlyList<AuthoringMigrationReport> Reports { get; }
    }

    /// <summary>The logic package's IConditionExplainer: condition references evaluated over a content set's models.</summary>
    public sealed class LogicConditionExplainer : IConditionExplainer
    {
        /// <summary>Evaluates over the first content set (by asset path) whose models know the reference.</summary>
        public ConditionExplanation Explain(string conditionRef, string state)
        {
            if (string.IsNullOrEmpty(conditionRef))
            {
                return new ConditionExplanation(string.Empty, string.Empty, true, true, -1, string.Empty, Array.Empty<string>());
            }

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(GameplayContentSet));
            Array.Sort(guids, StringComparer.Ordinal);
            ConditionExplanation? unknown = null;
            for (int i = 0; i < guids.Length; i++)
            {
                GameplayContentSet? set = AssetDatabase.LoadAssetAtPath<GameplayContentSet>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (set == null || set.World == null)
                {
                    continue;
                }

                NarrativeModelSet models = NarrativeAuthoring.Models(set.World.AuthoringId, new List<ScriptableObject>(set.Definitions).ToArray());
                StateSnapshot snapshot = NarrativeAuthoring.ParseState(state ?? string.Empty, models, out RuleState _);
                ConditionExplanation explanation = Explain(models, conditionRef, snapshot, out ConditionSetModel? _);
                if (explanation.Known)
                {
                    return explanation;
                }

                unknown = unknown ?? explanation;
            }

            return unknown ?? new ConditionExplanation(conditionRef, string.Empty, false, false, -1, "no content set declares it", Array.Empty<string>());
        }

        /// <summary>Evaluates <paramref name="conditionRef"/> (a condition set reference or a fact shorthand) over a state.</summary>
        public static ConditionExplanation Explain(NarrativeModelSet models, string conditionRef, StateSnapshot state, out ConditionSetModel? set)
        {
            set = null;
            if (string.IsNullOrEmpty(conditionRef))
            {
                return new ConditionExplanation(string.Empty, string.Empty, true, true, -1, string.Empty, Array.Empty<string>());
            }

            if (conditionRef.StartsWith(NarrativeConditionEvaluator.FactPrefix, StringComparison.Ordinal))
            {
                if (!NarrativeConditionEvaluator.TryParseFact(conditionRef.Substring(NarrativeConditionEvaluator.FactPrefix.Length), models, out set, out string problem) || set == null)
                {
                    return new ConditionExplanation(conditionRef, string.Empty, false, false, -1, problem, Array.Empty<string>());
                }
            }
            else if (!models.TryResolve(conditionRef, out int key) || !models.TryGet(key, out set) || set == null)
            {
                return new ConditionExplanation(conditionRef, string.Empty, false, false, -1,
                    NarrativeDiagnosticCodes.ConditionSetUnknown + ": no condition set '" + conditionRef + "'", Array.Empty<string>());
            }

            ConditionResult result = ConditionRules.Evaluate(set, state, models, new ConditionContext(0, 0));
            return new ConditionExplanation(conditionRef, set.Name, true, result.Passed, result.FailedIndex, result.FailedCondition, result.Inputs);
        }
    }

    /// <summary>
    /// authoring.migrateRefs for narrative definitions: the P1.4 single subject / target / trigger-subject references
    /// (pseudo-category narrative.subject) of conditions, actions, rules, quest objectives and rewards. The definitions
    /// already move them into the typed fields when they load; this re-saves (marks dirty) every asset whose file still
    /// stores the old fields, and reports references whose kind matches no typed field.
    /// </summary>
    public sealed class LogicRefMigration : IAuthoringRefMigration
    {
        private static readonly Regex LegacyLine = new Regex(@"^\s*-?\s*(subject|target|triggerSubject): \{fileID: (-?\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        public string MigrationId => "logic.narrativeSubjects";

        public AuthoringMigrationReport Migrate(IReadOnlyList<string> folders, bool apply)
        {
            var report = new AuthoringMigrationReport(MigrationId);
            string[] scope = folders != null && folders.Count > 0 ? new List<string>(folders).ToArray() : new[] { "Assets" };
            var types = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<INarrativeDefinition>())
            {
                if (!type.IsAbstract && typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    types.Add(type);
                }
            }

            types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int t = 0; t < types.Count; t++)
            {
                string[] guids = AssetDatabase.FindAssets("t:" + types[t].Name, scope);
                Array.Sort(guids, StringComparer.Ordinal);
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!seen.Add(path))
                    {
                        continue;
                    }

                    ScriptableObject? asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (asset == null)
                    {
                        continue;
                    }

                    string unresolved = LegacyFields(asset);
                    if (unresolved.Length > 0)
                    {
                        report.AddUnresolved(path + ": " + unresolved + " name an asset of no matching kind");
                    }

                    int stored = StoredLegacyCount(path);
                    if (stored > 0 && unresolved.Length == 0)
                    {
                        report.AddChanged(path + ": " + stored + " untyped subject/target reference(s) -> typed fields");
                        if (apply)
                        {
                            EditorUtility.SetDirty(asset);
                        }
                    }
                }
            }

            return report;
        }

        /// <summary>The legacy reference fields of <paramref name="definition"/> that still hold an object after loading, comma-separated.</summary>
        public static string LegacyFields(ScriptableObject definition)
        {
            if (definition == null)
            {
                return string.Empty;
            }

            var names = new List<string>();
            using (var serialized = new SerializedObject(definition))
            {
                SerializedProperty property = serialized.GetIterator();
                bool enter = true;
                while (property.Next(enter))
                {
                    enter = property.propertyType != SerializedPropertyType.String;
                    if (property.propertyType == SerializedPropertyType.ObjectReference
                        && property.name.StartsWith("legacy", StringComparison.Ordinal)
                        && property.objectReferenceValue != null)
                    {
                        names.Add(property.propertyPath);
                    }
                }
            }

            return string.Join(", ", names);
        }

        /// <summary>How many old-format subject/target/triggerSubject references the asset file still stores.</summary>
        public static int StoredLegacyCount(string assetPath)
        {
            string full = System.IO.Path.GetFullPath(assetPath);
            if (!System.IO.File.Exists(full))
            {
                return 0;
            }

            string text = System.IO.File.ReadAllText(full);
            int count = 0;
            foreach (Match match in LegacyLine.Matches(text))
            {
                if (match.Groups[2].Value != "0")
                {
                    count++;
                }
            }

            return count;
        }
    }
}
