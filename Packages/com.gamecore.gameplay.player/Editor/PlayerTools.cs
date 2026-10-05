// GameCore.Gameplay.Player.Editor - player authoring operations and the player validator (P1.3, Studio 03 s4/s5).
//
//   player.setSpawn       move the placed player entity to a location (m) and heading (deg) inside its region
//   player.tuneMovement   speeds, jump height and stamina of a PlayerDefinition
//   player.setCamera      the orbit camera rig of a PlayerDefinition
//
// Like the P1.1 tools, every tool validates before it changes anything, records Undo, marks what it edited dirty and
// refuses with an ArgumentException whose message starts with the GP-* code.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameCore.Gameplay.Player.Editor
{
    /// <summary>The player.* authoring operations.</summary>
    public static class PlayerTools
    {
        [AuthorOperation("player.setSpawn", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(PlayerValidator), Requires = "world.region", RequiresOnTarget = "entity.instance",
            Doc = "Moves the placed player entity to a location inside its region; the bake makes it the player's spawn pose.")]
        public static void SetSpawn(
            AuthoredEntity player,
            [AuthorArg(Unit = "m", Doc = "Spawn location (world space).")] Vector3 location,
            [AuthorArg(Unit = "deg", Required = false, Doc = "Spawn heading around +Y.")] float yaw = 0f)
        {
            if (player == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.PlayerNotInWorld + ": a placed player entity is required");
            }

            IAuthoredRegion? region = player.Region;
            if (region == null || !region.ContainsPoint(location.x, location.y, location.z))
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.PlayerSpawnOutsideRegion + ": the spawn location lies outside the player's region");
            }

            Undo.RecordObject(player.transform, "player.setSpawn");
            player.transform.SetPositionAndRotation(location, Quaternion.Euler(0f, yaw, 0f));
            EditorUtility.SetDirty(player);
            if (player.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            }
        }

        [AuthorOperation("player.tuneMovement", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(PlayerValidator), Requires = "player.definition",
            Doc = "Sets the player's walk/run speeds, jump height and stamina budget.")]
        public static void TuneMovement(
            PlayerDefinition definition,
            [AuthorArg(Unit = "m/s", Min = 0.1, Max = 20, Doc = "Walking speed.")] float walkSpeed,
            [AuthorArg(Unit = "m/s", Min = 0.1, Max = 30, Doc = "Running speed (>= walking speed).")] float runSpeed,
            [AuthorArg(Unit = "m", Min = 0, Max = 5, Required = false, Doc = "Jump height.")] float jumpHeight = 1f,
            [AuthorArg(Min = 1, Max = 100000, Required = false, Doc = "Maximum stamina.")] int staminaMax = 1000,
            [AuthorArg(Unit = "1/s", Min = 0, Max = 100000, Required = false, Doc = "Stamina drained per second running.")] int drainPerSecond = 200,
            [AuthorArg(Unit = "1/s", Min = 0, Max = 100000, Required = false, Doc = "Stamina regenerated per second resting.")] int regenPerSecond = 150)
        {
            if (definition == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.PlayerMissingDefinition + ": a player definition is required");
            }

            if (walkSpeed < 0.1f || walkSpeed > 20f || runSpeed < walkSpeed || runSpeed > 30f || jumpHeight < 0f || jumpHeight > 5f
                || staminaMax < 1 || staminaMax > 100000 || drainPerSecond < 0 || drainPerSecond > 100000 || regenPerSecond < 0 || regenPerSecond > 100000)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.PlayerTuningOutOfRange
                    + ": walk 0.1..20 m/s, run walk..30 m/s, jump 0..5 m, stamina 1..100000, drain/regen 0..100000 per second");
            }

            Undo.RecordObject(definition, "player.tuneMovement");
            definition.TuneMovement(walkSpeed, runSpeed, jumpHeight, staminaMax, drainPerSecond, regenPerSecond);
            EditorUtility.SetDirty(definition);
        }

        [AuthorOperation("player.setCamera", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(PlayerValidator), Requires = "player.definition",
            Doc = "Sets the third-person orbit camera: distance, pivot height and pitch limits.")]
        public static void SetCamera(
            PlayerDefinition definition,
            [AuthorArg(Unit = "m", Min = 1, Max = 20, Doc = "Distance behind the player.")] float distance,
            [AuthorArg(Unit = "m", Min = 0, Max = 5, Required = false, Doc = "Pivot height above the player.")] float pivotHeight = 1.6f,
            [AuthorArg(Unit = "deg", Min = -89, Max = 0, Required = false, Doc = "Lowest pitch.")] float pitchMin = -30f,
            [AuthorArg(Unit = "deg", Min = 0, Max = 89, Required = false, Doc = "Highest pitch.")] float pitchMax = 70f)
        {
            if (definition == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.PlayerMissingDefinition + ": a player definition is required");
            }

            if (distance < 1f || distance > 20f || pivotHeight < 0f || pivotHeight > 5f || pitchMin < -89f || pitchMin > 0f || pitchMax < 0f || pitchMax > 89f)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.PlayerCameraOutOfRange
                    + ": distance 1..20 m, pivot 0..5 m, pitch min -89..0 deg, pitch max 0..89 deg");
            }

            Undo.RecordObject(definition, "player.setCamera");
            definition.SetCamera(distance, pivotHeight, pitchMin, pitchMax);
            EditorUtility.SetDirty(definition);
        }
    }

    /// <summary>Validates player definitions.</summary>
    [AuthorValidator("player.validator", Codes = new[]
    {
        PlayerNpcInteractionCodes.PlayerMissingDefinition,
        PlayerNpcInteractionCodes.PlayerTuningOutOfRange,
        PlayerNpcInteractionCodes.PlayerCameraOutOfRange,
        PlayerNpcInteractionCodes.PlayerMissingInputActions,
    })]
    public static class PlayerValidator
    {
        public static IReadOnlyList<GameplayDiagnostic> Validate(PlayerDefinition definition)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (definition == null)
            {
                return diagnostics;
            }

            if (definition.Entity == null)
            {
                diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.PlayerMissingDefinition, definition.AuthoringId,
                    definition.name + " names no entity definition for the player"));
            }

            if (definition.WalkSpeed < 0.1f || definition.RunSpeed < definition.WalkSpeed || definition.StepMilliseconds < 5)
            {
                diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.PlayerTuningOutOfRange, definition.AuthoringId,
                    definition.name + " has out-of-range movement tuning"));
            }

            if (definition.CameraDistance < 1f || definition.CameraPitchMin > definition.CameraPitchMax)
            {
                diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.PlayerCameraOutOfRange, definition.AuthoringId,
                    definition.name + " has an invalid camera rig"));
            }

            if (definition.Input == null || definition.Input.Actions == null || definition.Input.Actions.FindActionMap(definition.Input.Map, false) == null)
            {
                diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.PlayerMissingInputActions, definition.AuthoringId,
                    definition.name + " has no input actions map"));
            }

            return diagnostics;
        }
    }
}
