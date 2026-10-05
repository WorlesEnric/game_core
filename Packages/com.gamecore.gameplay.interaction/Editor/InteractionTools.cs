// GameCore.Gameplay.Interaction.Editor - interaction authoring operations and the interaction validator (P1.3).
//
//   interaction.addDoor        place a door/gate interactable at a location in the active region scene
//   interaction.addExaminable  place an examinable interactable at a location
//   interaction.addTrigger     place a trigger volume at a location
//   interaction.setStates      set an interactable's initial state and per-state prompts
//   interaction.linkCondition  set an interactable's (or trigger's) condition and action references
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
            Doc = "Links an interactable to a condition ref (unlocks when true, refuses when false) and an action ref (run after success).")]
        public static void LinkCondition(
            InteractableDefinition definition,
            [AuthorArg(Category = "logic.condition", Required = false, Doc = "Condition ref, e.g. narrative.fact.gate_open (empty: always allowed).")] string conditionRef,
            [AuthorArg(Category = "logic.action", Required = false, Doc = "Action ref (empty: no action).")] string actionRef = "")
        {
            if (definition == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.InteractableMissingEntityDefinition + ": an interactable definition is required");
            }

            Undo.RecordObject(definition, "interaction.linkCondition");
            definition.Link(conditionRef ?? string.Empty, actionRef ?? string.Empty);
            EditorUtility.SetDirty(definition);
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

    /// <summary>Validates interaction rosters and definitions.</summary>
    [AuthorValidator("interaction.validator", Codes = new[]
    {
        PlayerNpcInteractionCodes.InteractableMissingEntityDefinition,
        PlayerNpcInteractionCodes.InteractableDuplicateEntityDefinition,
        PlayerNpcInteractionCodes.InteractableIllegalState,
        PlayerNpcInteractionCodes.InteractableMissingPrompt,
        PlayerNpcInteractionCodes.InteractableLockWithoutCondition,
        PlayerNpcInteractionCodes.TriggerSizeOutOfRange,
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

            return diagnostics;
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
