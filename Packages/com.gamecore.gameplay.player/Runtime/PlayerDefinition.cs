// GameCore.Gameplay.Player - PlayerDefinition and InputProfile (P1.3, catalog row 3).
//
// The player is an authored entity like any other (AuthoredEntity + EntityDefinition, so the bake, the streamer and
// Studio know it); what makes it the player is a PlayerDefinition whose `entity` is that entity's definition, and the
// world's focus entity being that placed entity. The PlayerDefinition holds the tuning the kernel rules use
// (converted once to integers: mm/s, ms, stamina units), the locomotion shape the presentation's CharacterController
// uses, the camera rig and the input profile.
#nullable enable
using System;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameCore.Gameplay.Player
{
    /// <summary>The authored player: movement, stamina, locomotion shape, camera and input.</summary>
    [Authorable("player.definition", DisplayName = "Player", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "The player: which entity definition is the player, its speeds, stamina, CharacterController shape and camera rig.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Player Definition", fileName = "PlayerDefinition")]
    public sealed class PlayerDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "entity.definition", Doc = "The entity definition of the placed player entity (its view prefab).")]
        [SerializeField] private EntityDefinition? entity;

        [AuthorRef(Category = "player.inputProfile", Required = false, Doc = "Input actions and sensitivities.")]
        [SerializeField] private InputProfile? input;

        [AuthorRef(Category = "asset.prefab", Required = false, Doc = "Player rig prefab: the CharacterController PlayerLocomotion resolves movement with.")]
        [SerializeField] private GameObject? rig;

        [AuthorField(Unit = "m/s", Min = 0.1, Max = 20, Doc = "Walking speed.")]
        [SerializeField] private float walkSpeed = 2.5f;

        [AuthorField(Unit = "m/s", Min = 0.1, Max = 30, Doc = "Running speed (costs stamina).")]
        [SerializeField] private float runSpeed = 5.5f;

        [AuthorField(Unit = "m", Min = 0, Max = 5, Doc = "Jump height.")]
        [SerializeField] private float jumpHeight = 1.0f;

        [AuthorField(Unit = "m/s2", Min = 1, Max = 50, Doc = "Gravity applied by the locomotion.")]
        [SerializeField] private float gravity = 18f;

        [AuthorField(Unit = "ms", Min = 5, Max = 100, Doc = "Logical duration of one step (stamina accounting).")]
        [SerializeField] private int stepMilliseconds = 20;

        [AuthorField(Unit = "ms", Min = 20, Max = 500, Doc = "Longest frame one move may cover (anti-teleport clamp window).")]
        [SerializeField] private int moveWindowMilliseconds = 100;

        [AuthorField(Min = 1, Max = 100000, Doc = "Maximum stamina.")]
        [SerializeField] private int staminaMax = 1000;

        [AuthorField(Unit = "1/s", Min = 0, Max = 100000, Doc = "Stamina drained per second of running.")]
        [SerializeField] private int staminaDrainPerSecond = 200;

        [AuthorField(Unit = "1/s", Min = 0, Max = 100000, Doc = "Stamina regenerated per second of rest.")]
        [SerializeField] private int staminaRegenPerSecond = 150;

        [AuthorField(Unit = "ms", Min = 0, Max = 10000, Doc = "Rest needed before stamina regenerates.")]
        [SerializeField] private int staminaRegenDelayMilliseconds = 800;

        [AuthorField(Min = 0, Max = 100000, Doc = "Stamina one jump costs.")]
        [SerializeField] private int jumpCost = 150;

        [AuthorField(Unit = "m", Min = 0.5, Max = 4, Doc = "CharacterController height.")]
        [SerializeField] private float height = 1.8f;

        [AuthorField(Unit = "m", Min = 0.1, Max = 1.5, Doc = "CharacterController radius.")]
        [SerializeField] private float radius = 0.35f;

        [AuthorField(Unit = "m", Min = 0, Max = 1, Doc = "Highest step the locomotion climbs.")]
        [SerializeField] private float stepOffset = 0.35f;

        [AuthorField(Unit = "deg", Min = 0, Max = 89, Doc = "Steepest walkable slope.")]
        [SerializeField] private float slopeLimit = 45f;

        [AuthorField(Unit = "m", Min = 0.5, Max = 10, Doc = "Interaction focus range.")]
        [SerializeField] private float interactRange = 2.5f;

        [AuthorField(Unit = "deg", Min = 5, Max = 180, Doc = "Half angle of the focus cone.")]
        [SerializeField] private float focusHalfAngle = 60f;

        [AuthorField(Unit = "m", Min = 0.2, Max = 3, Doc = "Stride length between footsteps.")]
        [SerializeField] private float strideLength = 0.75f;

        [AuthorField(Unit = "m", Min = 1, Max = 20, Doc = "Camera distance behind the player.")]
        [SerializeField] private float cameraDistance = 5f;

        [AuthorField(Unit = "m", Min = 0, Max = 5, Doc = "Camera pivot height above the player.")]
        [SerializeField] private float cameraHeight = 1.6f;

        [AuthorField(Unit = "deg", Min = -89, Max = 0, Doc = "Lowest camera pitch.")]
        [SerializeField] private float cameraPitchMin = -30f;

        [AuthorField(Unit = "deg", Min = 0, Max = 89, Doc = "Highest camera pitch.")]
        [SerializeField] private float cameraPitchMax = 70f;

        [AuthorField(Unit = "m", Min = 0.05, Max = 1, Doc = "Camera collision probe radius.")]
        [SerializeField] private float cameraCollisionRadius = 0.25f;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public EntityDefinition? Entity => entity;

        public InputProfile? Input => input;

        public GameObject? Rig => rig;

        public float WalkSpeed => walkSpeed;

        public float RunSpeed => runSpeed;

        public float JumpHeight => jumpHeight;

        public float Gravity => gravity;

        public float Height => height;

        public float Radius => radius;

        public float StepOffset => stepOffset;

        public float SlopeLimit => slopeLimit;

        public float InteractRange => interactRange;

        public float FocusHalfAngle => focusHalfAngle;

        public float StrideLength => strideLength;

        public float CameraDistance => cameraDistance;

        public float CameraHeight => cameraHeight;

        public float CameraPitchMin => cameraPitchMin;

        public float CameraPitchMax => cameraPitchMax;

        public float CameraCollisionRadius => cameraCollisionRadius;

        public int StepMilliseconds => stepMilliseconds;

        /// <summary>The integer tuning the kernel rules use.</summary>
        public PlayerTuning ToTuning() =>
            new PlayerTuning(
                GameplayUnits.ToMillimetres(walkSpeed),
                GameplayUnits.ToMillimetres(runSpeed),
                stepMilliseconds,
                moveWindowMilliseconds,
                GameplayUnits.ToMillimetres(Math.Max(stepOffset, jumpHeight) + 0.1),
                staminaMax,
                staminaDrainPerSecond,
                staminaRegenPerSecond,
                staminaRegenDelayMilliseconds,
                jumpCost);

        /// <summary>The integer focus tuning (range, cone, close range 0.8 m, 20 % hysteresis).</summary>
        public FocusTuning ToFocusTuning() =>
            new FocusTuning(
                GameplayUnits.ToMillimetres(interactRange),
                GameplayUnits.DegreesToMilliradians(focusHalfAngle),
                800,
                20);

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void SetEntity(EntityDefinition? value) => entity = value;

        public void SetInput(InputProfile? value) => input = value;

        public void SetRig(GameObject? value) => rig = value;

        /// <summary>player.tuneMovement: speeds, jump and stamina in one call.</summary>
        public void TuneMovement(float walk, float run, float jump, int maxStamina, int drainPerSecond, int regenPerSecond)
        {
            walkSpeed = walk;
            runSpeed = run;
            jumpHeight = jump;
            staminaMax = maxStamina;
            staminaDrainPerSecond = drainPerSecond;
            staminaRegenPerSecond = regenPerSecond;
        }

        /// <summary>player.setCamera: the orbit rig in one call.</summary>
        public void SetCamera(float distance, float pivotHeight, float pitchMin, float pitchMax)
        {
            cameraDistance = distance;
            cameraHeight = pivotHeight;
            cameraPitchMin = pitchMin;
            cameraPitchMax = pitchMax;
        }

        public void SetContentStamp(string stamp) => contentStamp = stamp ?? string.Empty;

        private void Reset() => EnsureAuthoringId();

        private void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureAuthoringId();
            }
        }
    }
}
