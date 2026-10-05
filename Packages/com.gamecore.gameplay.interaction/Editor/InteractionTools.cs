// GameCore.Gameplay.Interaction.Editor - interaction authoring operations and the interaction validator (P1.3).
//
//   interaction.addDoor        place a door/gate interactable at a location in the active region scene
//   interaction.addExaminable  place an examinable interactable at a location
//   interaction.addTrigger     place a trigger volume at a location
//   interaction.setStates      set an interactable's initial state and per-state prompts
//   interaction.linkCondition  set an interactable's condition: a logic.conditionSet, or a narrative.fact compared with a
//                              value (P1.7b: typed references; the string overload resolves P1.3-style strings)
//   interaction.setActions     set what a committed use runs: a logic.actionSet or a built-in action (P1.7b)
//   interaction.explain        evaluate an interactable's condition over a state and name the failed condition (P1.7b)
//
// InteractionRefMigration (authoring.migrateRefs, P1.7b) moves the P1.3 string condition/action refs of interactables
// and triggers into the typed references.
//
// Every tool validates before it changes anything, records Undo, marks what it edited dirty and refuses with an
// ArgumentException whose message starts with the GP-* code.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Entities.Editor;
using GameCore.Rules.Gameplay.Interaction;
using GameCore.Rules.Gameplay.Logic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Gameplay.Interaction.Editor
{
    /// <summary>The interaction.* authoring operations.</summary>
    public static class InteractionTools
    {
        [AuthorOperation("interaction.addDoor", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(InteractionValidator), Requires = "world.region", RequiresOnTarget = "interaction.interactable",
            Doc = "Places a door or gate interactable (its definition's entity) at a location (m) and heading (deg).")]
        public static AuthoredEntity AddDoor(
            InteractableDefinition door,
            [AuthorArg(Unit = "m", Doc = "World position.")] Vector3 location,
            [AuthorArg(Unit = "deg", Required = false, Doc = "Heading around +Y.")] float yaw = 0f,
            [AuthorArg(Required = false, Doc = "Object name.")] string name = "")
        {
            return AddIn(SceneManager.GetActiveScene(), door, location, yaw, name, true);
        }

        [AuthorOperation("interaction.addExaminable", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(InteractionValidator), Requires = "world.region", RequiresOnTarget = "interaction.interactable",
            Doc = "Places an examinable (or point/switch) interactable at a location (m) and heading (deg).")]
        public static AuthoredEntity AddExaminable(
            InteractableDefinition examinable,
            [AuthorArg(Unit = "m", Doc = "World position.")] Vector3 location,
            [AuthorArg(Unit = "deg", Required = false, Doc = "Heading around +Y.")] float yaw = 0f,
            [AuthorArg(Required = false, Doc = "Object name.")] string name = "")
        {
            return AddIn(SceneManager.GetActiveScene(), examinable, location, yaw, name, false);
        }

        /// <summary>Places an interactable into an explicit scene (authoring scripts and tests).</summary>
        public static AuthoredEntity AddIn(Scene scene, InteractableDefinition definition, Vector3 location, float yaw, string name, bool door)
        {
            if (definition == null || definition.Entity == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.InteractableMissingEntityDefinition + ": an interactable definition with an entity definition is required");
            }

            bool isDoor = definition.Kind == InteractableKind.Door || definition.Kind == InteractableKind.Gate;
            if (door != isDoor)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.InteractableIllegalState + ": " + definition.name + " is a "
                    + definition.Kind + (door ? ", not a door or gate" : "; use interaction.addDoor"));
            }

            return Place(scene, definition.Entity, location, yaw, string.IsNullOrEmpty(name) ? definition.name : name);
        }

        [AuthorOperation("interaction.addTrigger", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(InteractionValidator), Requires = "world.region", RequiresOnTarget = "interaction.trigger",
            Doc = "Places a trigger volume (its definition's entity) at a location (m) and heading (deg).")]
        public static AuthoredEntity AddTrigger(
            TriggerDefinition trigger,
            [AuthorArg(Unit = "m", Doc = "World position (bottom centre of the box).")] Vector3 location,
            [AuthorArg(Unit = "deg", Required = false, Doc = "Heading around +Y.")] float yaw = 0f,
            [AuthorArg(Required = false, Doc = "Object name.")] string name = "")
        {
            if (trigger == null || trigger.Entity == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.InteractableMissingEntityDefinition + ": a trigger definition with an entity definition is required");
            }

            if (trigger.Size.x < 0.1f || trigger.Size.y < 0.1f || trigger.Size.z < 0.1f || trigger.Size.x > 100f || trigger.Size.y > 100f || trigger.Size.z > 100f)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.TriggerSizeOutOfRange + ": trigger sizes lie within 0.1..100 m");
            }

            return Place(SceneManager.GetActiveScene(), trigger.Entity, location, yaw, string.IsNullOrEmpty(name) ? trigger.name : name);
        }

        [AuthorOperation("interaction.setStates", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(InteractionValidator), Requires = "interaction.interactable",
            Doc = "Sets an interactable's initial state and its prompt per state (\"state=text\" entries).")]
        public static void SetStates(
            InteractableDefinition definition,
            [AuthorArg(Doc = "Initial state name (idle, closed, open, locked, used, broken, off, on).")] string initialState,
            [AuthorArg(Required = false, Doc = "Prompts as \"state=text\".")] string[]? prompts = null)
        {
            if (definition == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.InteractableMissingEntityDefinition + ": an interactable definition is required");
            }

            if (!InteractableStates.TryParse(initialState, out int state) || !InteractionRules.IsLegal(definition.Kind, state))
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.InteractableIllegalState + ": '" + initialState + "' is not a state of a " + definition.Kind);
            }

            var parsed = new List<StatePrompt>();
            string[] entries = prompts ?? Array.Empty<string>();
            for (int i = 0; i < entries.Length; i++)
            {
                string entry = entries[i] ?? string.Empty;
                int split = entry.IndexOf('=');
                string name = split > 0 ? entry.Substring(0, split).Trim() : string.Empty;
                if (!InteractableStates.TryParse(name, out int promptState) || !InteractionRules.IsLegal(definition.Kind, promptState))
                {
                    throw new ArgumentException(PlayerNpcInteractionCodes.InteractableIllegalState + ": prompt '" + entry + "' does not name a state of a " + definition.Kind);
                }

                string text = entry.Substring(split + 1).Trim();
                if (text.Length == 0)
                {
                    throw new ArgumentException(PlayerNpcInteractionCodes.InteractableMissingPrompt + ": prompt '" + entry + "' has no text");
                }

                parsed.Add(new StatePrompt(name, text));
            }

            Undo.RecordObject(definition, "interaction.setStates");
            definition.SetStates(initialState, parsed);
            EditorUtility.SetDirty(definition);
        }

        [AuthorOperation("interaction.linkCondition", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(InteractionValidator), Requires = "interaction.interactable",
            Doc = "Sets an interactable's condition (unlocks a lock when it holds, refuses a use when it fails): a condition set, or a fact compared with a value; neither = always allowed.")]
        public static void LinkCondition(
            InteractableDefinition definition,
            [AuthorArg(Category = "logic.conditionSet", Required = false, Doc = "Condition set (wins over the fact).")] ScriptableObject? condition,
            [AuthorArg(Category = "narrative.fact", Required = false, Doc = "Fact condition (used when no condition set is given).")] ScriptableObject? fact = null,
            [AuthorArg(Required = false, Doc = "Comparison of the fact condition (default: the fact is not 0).")] CompareOp op = CompareOp.NotEqual,
            [AuthorArg(Required = false, Doc = "Value the fact is compared with.")] int value = 0)
        {
            if (definition == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.InteractableMissingEntityDefinition + ": an interactable definition is required");
            }

            RequireCategory(condition, "logic.conditionSet");
            RequireCategory(fact, "narrative.fact");
            if (condition != null && fact != null)
            {
                throw new ArgumentException(AuthoringHardeningCodes.AmbiguousCondition + ": give a condition set or a fact condition, not both");
            }

            Undo.RecordObject(definition, "interaction.linkCondition");
            definition.SetCondition(condition, fact, op, value);
            EditorUtility.SetDirty(definition);
        }

        /// <summary>
        /// P1.3-style string form (authoring scripts and tests): the condition string is resolved to a condition set (by
        /// authoring id or name) or to a fact condition (<c>narrative.fact.&lt;name&gt;[op N]</c> naming a fact asset); the
        /// action string to an action set or a built-in action. A string that names nothing is stored as a legacy string
        /// (the validators report it as GP-REF-001).
        /// </summary>
        public static void LinkCondition(InteractableDefinition definition, string conditionRef, string actionRef = "")
        {
            if (definition == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.InteractableMissingEntityDefinition + ": an interactable definition is required");
            }

            InteractionRefs.ResolvedCondition condition = InteractionRefs.ResolveCondition(conditionRef ?? string.Empty, null);
            InteractionRefs.ResolvedAction action = InteractionRefs.ResolveAction(actionRef ?? string.Empty, null);
            Undo.RecordObject(definition, "interaction.linkCondition");
            definition.Link(condition.Resolved ? string.Empty : conditionRef ?? string.Empty, action.Resolved ? string.Empty : actionRef ?? string.Empty);
            if (condition.Resolved)
            {
                definition.SetCondition(condition.Set, condition.Fact, condition.Op, condition.Value);
            }

            if (action.Resolved)
            {
                definition.SetActions(action.Set, action.BuiltIn);
            }

            EditorUtility.SetDirty(definition);
        }

        [AuthorOperation("interaction.setActions", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(InteractionValidator), Requires = "interaction.interactable",
            Doc = "Sets what a committed successful use runs: an action set, or a built-in action (inventory.pickup); neither = nothing.")]
        public static void SetActions(
            InteractableDefinition definition,
            [AuthorArg(Category = "logic.actionSet", Required = false, Doc = "Action set (wins over the built-in action).")] ScriptableObject? actions,
            [AuthorArg(Required = false, Doc = "Built-in action: inventory.pickup (empty: none).")] string builtIn = "")
        {
            if (definition == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.InteractableMissingEntityDefinition + ": an interactable definition is required");
            }

            RequireCategory(actions, "logic.actionSet");
            if (!string.IsNullOrEmpty(builtIn) && !Contains(ConditionRefs.BuiltInActions, builtIn))
            {
                throw new ArgumentException(AuthoringHardeningCodes.InteractableUnknownBuiltInAction + ": '" + builtIn + "' is not a built-in action ("
                    + string.Join(", ", ConditionRefs.BuiltInActions) + ")");
            }

            Undo.RecordObject(definition, "interaction.setActions");
            definition.SetActions(actions, actions != null ? string.Empty : builtIn ?? string.Empty);
            EditorUtility.SetDirty(definition);
        }

        [AuthorOperation("interaction.explain", ReadOnly = true, Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live,
            Validator = typeof(InteractionValidator), Requires = "interaction.interactable",
            Doc = "Evaluates an interactable's condition over a state (logic.test terms; empty: the content's initial state) and names the first failed condition and the inputs it read.")]
        public static ConditionExplanation Explain(
            InteractableDefinition definition,
            [AuthorArg(Required = false, Doc = "State terms: fact.<name>=v; item.<item>=n; quest.<quest>.status=v ... (empty: initial state).")] string state = "")
        {
            if (definition == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.InteractableMissingEntityDefinition + ": an interactable definition is required");
            }

            return InteractionRefs.Explain(definition.ConditionRef, state ?? string.Empty);
        }

        private static void RequireCategory(ScriptableObject? value, string typeId)
        {
            if (value != null && !AuthoredAssetLookup.IsNullOrOfType(value, typeId))
            {
                throw new ArgumentException(AuthoringHardeningCodes.WrongReferenceCategory + ": " + value.name + " is not a " + typeId);
            }
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static AuthoredEntity Place(Scene scene, EntityDefinition entityDefinition, Vector3 location, float yaw, string name)
        {
            AuthoredEntity entity = EntityTools.PlaceIn(scene, entityDefinition, location, yaw, name);
            IAuthoredRegion? region = entity.Region;
            if (region == null || !region.ContainsPoint(location.x, location.y, location.z))
            {
                Undo.DestroyObjectImmediate(entity.gameObject);
                throw new ArgumentException(PlayerNpcInteractionCodes.InteractableNotPlaced + ": the location lies outside the scene's region");
            }

            return entity;
        }
    }

    /// <summary>Resolution of interaction condition/action strings and condition explanations (P1.7b).</summary>
    public static class InteractionRefs
    {
        /// <summary>A condition string resolved to typed references.</summary>
        public readonly struct ResolvedCondition
        {
            public ResolvedCondition(bool resolved, ScriptableObject? set, ScriptableObject? fact, CompareOp op, int value)
            {
                Resolved = resolved;
                Set = set;
                Fact = fact;
                Op = op;
                Value = value;
            }

            public bool Resolved { get; }

            public ScriptableObject? Set { get; }

            public ScriptableObject? Fact { get; }

            public CompareOp Op { get; }

            public int Value { get; }
        }

        /// <summary>An action string resolved to typed references.</summary>
        public readonly struct ResolvedAction
        {
            public ResolvedAction(bool resolved, ScriptableObject? set, string builtIn)
            {
                Resolved = resolved;
                Set = set;
                BuiltIn = builtIn;
            }

            public bool Resolved { get; }

            public ScriptableObject? Set { get; }

            public string BuiltIn { get; }
        }

        /// <summary>
        /// Resolves a P1.3 condition string: empty resolves to nothing (always allowed); <c>narrative.fact.&lt;name&gt;[op N]</c>
        /// to the fact asset of that name; anything else to a condition set by authoring id or name. Unresolved when the
        /// text names nothing (or more than one asset).
        /// </summary>
        public static ResolvedCondition ResolveCondition(string text, IReadOnlyList<string>? folders)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new ResolvedCondition(true, null, null, CompareOp.NotEqual, 0);
            }

            if (ConditionRefs.TryParseFactShorthand(text, out string factName, out CompareOp op, out int value))
            {
                IReadOnlyList<ScriptableObject> facts = AuthoredAssetLookup.AssetsOfType("narrative.fact", folders);
                ScriptableObject? found = null;
                for (int i = 0; i < facts.Count; i++)
                {
                    if (facts[i] is GameCore.Gameplay.Contracts.Narrative.IFactDefinition fact && string.Equals(fact.FactName, factName, StringComparison.Ordinal))
                    {
                        if (found != null)
                        {
                            return new ResolvedCondition(false, null, null, op, value);
                        }

                        found = facts[i];
                    }
                }

                return found != null ? new ResolvedCondition(true, null, found, op, value) : new ResolvedCondition(false, null, null, op, value);
            }

            ScriptableObject? set = AuthoredAssetLookup.Resolve(text, AuthoredAssetLookup.AssetsOfType("logic.conditionSet", folders), out bool _);
            return set != null ? new ResolvedCondition(true, set, null, CompareOp.NotEqual, 0) : new ResolvedCondition(false, null, null, CompareOp.NotEqual, 0);
        }

        /// <summary>Resolves a P1.3 action string: empty, a built-in action, or an action set by authoring id or name.</summary>
        public static ResolvedAction ResolveAction(string text, IReadOnlyList<string>? folders)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new ResolvedAction(true, null, string.Empty);
            }

            for (int i = 0; i < ConditionRefs.BuiltInActions.Count; i++)
            {
                if (string.Equals(ConditionRefs.BuiltInActions[i], text, StringComparison.Ordinal))
                {
                    return new ResolvedAction(true, null, text);
                }
            }

            ScriptableObject? set = AuthoredAssetLookup.Resolve(text, AuthoredAssetLookup.AssetsOfType("logic.actionSet", folders), out bool _);
            return set != null ? new ResolvedAction(true, set, string.Empty) : new ResolvedAction(false, null, string.Empty);
        }

        /// <summary>
        /// Evaluates a condition reference through the logic package's <see cref="IConditionExplainer"/> (found through the
        /// type cache; interaction cannot depend on logic). Without one, an empty reference holds and any other is unknown.
        /// </summary>
        public static ConditionExplanation Explain(string conditionRef, string state)
        {
            IConditionExplainer? explainer = Explainer();
            if (explainer != null)
            {
                return explainer.Explain(conditionRef ?? string.Empty, state ?? string.Empty);
            }

            bool empty = string.IsNullOrEmpty(conditionRef);
            return new ConditionExplanation(conditionRef ?? string.Empty, string.Empty, empty, empty, -1,
                empty ? string.Empty : "no condition explainer is installed (com.gamecore.gameplay.logic)", Array.Empty<string>());
        }

        /// <summary>The installed condition explainer, or null.</summary>
        public static IConditionExplainer? Explainer()
        {
            var types = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IConditionExplainer>())
            {
                if (!type.IsAbstract && !type.IsInterface && type.GetConstructor(Type.EmptyTypes) != null)
                {
                    types.Add(type);
                }
            }

            types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            return types.Count > 0 ? (IConditionExplainer)Activator.CreateInstance(types[0]) : null;
        }
    }

    /// <summary>authoring.migrateRefs for interactables and triggers: P1.3 condition/action strings become typed references.</summary>
    public sealed class InteractionRefMigration : IAuthoringRefMigration
    {
        public string MigrationId => "interaction.conditionActionRefs";

        public AuthoringMigrationReport Migrate(IReadOnlyList<string> folders, bool apply)
        {
            var report = new AuthoringMigrationReport(MigrationId);
            IReadOnlyList<InteractableDefinition> interactables = AuthoredAssetLookup.AssetsOf<InteractableDefinition>(folders);
            for (int i = 0; i < interactables.Count; i++)
            {
                InteractableDefinition definition = interactables[i];
                Migrate(definition, definition.LegacyConditionRef, definition.LegacyActionRef, report, apply,
                    c => definition.SetCondition(c.Set, c.Fact, c.Op, c.Value), a => definition.SetActions(a.Set, a.BuiltIn));
            }

            IReadOnlyList<TriggerDefinition> triggers = AuthoredAssetLookup.AssetsOf<TriggerDefinition>(folders);
            for (int i = 0; i < triggers.Count; i++)
            {
                TriggerDefinition definition = triggers[i];
                Migrate(definition, definition.LegacyConditionRef, definition.LegacyActionRef, report, apply,
                    c => definition.SetCondition(c.Set, c.Fact, c.Op, c.Value), a => definition.SetActions(a.Set, a.BuiltIn));
            }

            return report;
        }

        private static void Migrate(ScriptableObject definition, string conditionText, string actionText, AuthoringMigrationReport report, bool apply,
            Action<InteractionRefs.ResolvedCondition> setCondition, Action<InteractionRefs.ResolvedAction> setActions)
        {
            string where = AuthoredAssetLookup.Describe(definition);
            if (conditionText.Length > 0)
            {
                InteractionRefs.ResolvedCondition condition = InteractionRefs.ResolveCondition(conditionText, null);
                if (!condition.Resolved)
                {
                    report.AddUnresolved(where + ": conditionRef '" + conditionText + "' names no condition set or fact");
                }
                else
                {
                    report.AddChanged(where + ": conditionRef '" + conditionText + "' -> "
                        + (condition.Set != null ? "condition " + AuthoredAssetLookup.Describe(condition.Set)
                            : "conditionFact " + AuthoredAssetLookup.Describe(condition.Fact!) + " " + Compare.Symbol(condition.Op) + " " + condition.Value));
                    if (apply)
                    {
                        Undo.RecordObject(definition, "authoring.migrateRefs");
                        setCondition(condition);
                        EditorUtility.SetDirty(definition);
                    }
                }
            }

            if (actionText.Length > 0)
            {
                InteractionRefs.ResolvedAction action = InteractionRefs.ResolveAction(actionText, null);
                if (!action.Resolved)
                {
                    report.AddUnresolved(where + ": actionRef '" + actionText + "' names no action set or built-in action");
                }
                else
                {
                    report.AddChanged(where + ": actionRef '" + actionText + "' -> "
                        + (action.Set != null ? "actions " + AuthoredAssetLookup.Describe(action.Set) : "builtInAction " + action.BuiltIn));
                    if (apply)
                    {
                        Undo.RecordObject(definition, "authoring.migrateRefs");
                        setActions(action);
                        EditorUtility.SetDirty(definition);
                    }
                }
            }
        }
    }

    /// <summary>Validates interaction rosters and definitions.</summary>
    [AuthorValidator("interaction.validator", Codes = new[]
    {
        PlayerNpcInteractionCodes.InteractableMissingEntityDefinition,
        PlayerNpcInteractionCodes.InteractableDuplicateEntityDefinition,
        PlayerNpcInteractionCodes.InteractableIllegalState,
        PlayerNpcInteractionCodes.InteractableMissingPrompt,
        PlayerNpcInteractionCodes.InteractableLockWithoutCondition,
        PlayerNpcInteractionCodes.TriggerSizeOutOfRange,
        AuthoringHardeningCodes.LegacyReference,
        AuthoringHardeningCodes.WrongReferenceCategory,
        AuthoringHardeningCodes.AmbiguousCondition,
        AuthoringHardeningCodes.InteractableUnknownBuiltInAction,
    })]
    public static class InteractionValidator
    {
        public static IReadOnlyList<GameplayDiagnostic> Validate(InteractionRoster roster)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (roster == null)
            {
                return diagnostics;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < roster.Interactables.Count; i++)
            {
                InteractableDefinition definition = roster.Interactables[i];
                if (definition == null)
                {
                    diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.InteractableMissingEntityDefinition, roster.AuthoringId, roster.name + " lists a missing interactable"));
                    continue;
                }

                if (definition.Entity != null && !seen.Add(definition.Entity.AuthoringId))
                {
                    diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.InteractableDuplicateEntityDefinition, definition.AuthoringId,
                        definition.name + " shares its entity definition with another definition of " + roster.name));
                }

                diagnostics.AddRange(Validate(definition));
            }

            for (int i = 0; i < roster.Triggers.Count; i++)
            {
                TriggerDefinition trigger = roster.Triggers[i];
                if (trigger == null || trigger.Entity == null)
                {
                    diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.InteractableMissingEntityDefinition, roster.AuthoringId, roster.name + " lists a trigger without an entity definition"));
                    continue;
                }

                if (!seen.Add(trigger.Entity.AuthoringId))
                {
                    diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.InteractableDuplicateEntityDefinition, trigger.AuthoringId,
                        trigger.name + " shares its entity definition with another definition of " + roster.name));
                }

                Vector3 size = trigger.Size;
                if (size.x < 0.1f || size.y < 0.1f || size.z < 0.1f || size.x > 100f || size.y > 100f || size.z > 100f)
                {
                    diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.TriggerSizeOutOfRange, trigger.AuthoringId, trigger.name + " has a size outside 0.1..100 m"));
                }

                CheckReferences(trigger, trigger.AuthoringId, trigger.Condition, trigger.ConditionFact, trigger.Actions, trigger.BuiltInAction,
                    trigger.LegacyConditionRef, trigger.LegacyActionRef, diagnostics);
            }

            return diagnostics;
        }

        public static IReadOnlyList<GameplayDiagnostic> Validate(InteractableDefinition definition)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (definition == null)
            {
                return diagnostics;
            }

            if (definition.Entity == null)
            {
                diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.InteractableMissingEntityDefinition, definition.AuthoringId, definition.name + " names no entity definition"));
            }

            if (!string.IsNullOrEmpty(definition.InitialStateName)
                && (!InteractableStates.TryParse(definition.InitialStateName, out int initial) || !InteractionRules.IsLegal(definition.Kind, initial)))
            {
                diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.InteractableIllegalState, definition.AuthoringId,
                    definition.name + " starts in '" + definition.InitialStateName + "', which a " + definition.Kind + " cannot be in"));
            }

            for (int i = 0; i < definition.Prompts.Count; i++)
            {
                StatePrompt prompt = definition.Prompts[i];
                if (prompt == null || string.IsNullOrEmpty(prompt.text))
                {
                    diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.InteractableMissingPrompt, definition.AuthoringId, definition.name + " has an empty prompt"));
                }
            }

            if (definition.InitialState == InteractableStates.Locked && string.IsNullOrEmpty(definition.ConditionRef))
            {
                diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.InteractableLockWithoutCondition, definition.AuthoringId,
                    definition.name + " starts locked but no condition ref can unlock it"));
            }

            CheckReferences(definition, definition.AuthoringId, definition.Condition, definition.ConditionFact, definition.Actions, definition.BuiltInAction,
                definition.LegacyConditionRef, definition.LegacyActionRef, diagnostics);
            return diagnostics;
        }

        private static void CheckReferences(UnityEngine.Object owner, string id, ScriptableObject? condition, ScriptableObject? fact, ScriptableObject? actions,
            string builtIn, string legacyCondition, string legacyAction, List<GameplayDiagnostic> diagnostics)
        {
            if (!AuthoredAssetLookup.IsNullOrOfType(condition, "logic.conditionSet"))
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.WrongReferenceCategory, id, owner.name + "'s condition is not a logic.conditionSet"));
            }

            if (!AuthoredAssetLookup.IsNullOrOfType(fact, "narrative.fact"))
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.WrongReferenceCategory, id, owner.name + "'s condition fact is not a narrative.fact"));
            }

            if (!AuthoredAssetLookup.IsNullOrOfType(actions, "logic.actionSet"))
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.WrongReferenceCategory, id, owner.name + "'s actions are not a logic.actionSet"));
            }

            if (condition != null && fact != null)
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.AmbiguousCondition, id, owner.name + " names both a condition set and a fact condition (the set wins)"));
            }

            bool knownBuiltIn = builtIn.Length == 0;
            for (int i = 0; i < ConditionRefs.BuiltInActions.Count && !knownBuiltIn; i++)
            {
                knownBuiltIn = string.Equals(ConditionRefs.BuiltInActions[i], builtIn, StringComparison.Ordinal);
            }

            if (!knownBuiltIn)
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.InteractableUnknownBuiltInAction, id, owner.name + " runs unknown built-in action '" + builtIn + "'"));
            }

            if (condition == null && fact == null && legacyCondition.Length > 0)
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.LegacyReference, id,
                    owner.name + " names its condition by the string '" + legacyCondition + "'; run authoring.migrateRefs"));
            }

            if (actions == null && builtIn.Length == 0 && legacyAction.Length > 0)
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.LegacyReference, id,
                    owner.name + " names its action by the string '" + legacyAction + "'; run authoring.migrateRefs"));
            }
        }
    }

    /// <summary>Contributes the interaction plugin, its command system, layout and schemas to the bake's catalog description.</summary>
    public sealed class InteractionCatalogContributor : GameCore.Gameplay.Compile.IGameplayCatalogContributor
    {
        public GameCore.Gameplay.Compile.GameplayCatalogContribution Contribution =>
            new GameCore.Gameplay.Compile.GameplayCatalogContribution(
                "com.gamecore.gameplay.interaction", InteractionDeclarations.CatalogSchemas, InteractionDeclarations.CatalogEntries);
    }
}
