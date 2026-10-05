// GameCore.Gameplay.Player - ThirdPersonCamera: an orbit camera with collision, no Cinemachine (P1.3).
//
// A presentation binder: after the pump it orbits a camera around the committed player pose (pivot = pose + height),
// driven by the Look input the adapter sampled this frame, and pulls the camera in when a sphere cast from the pivot hits
// geometry. Its heading feeds the adapter (moves are camera-relative). Headless (no graphics device) or without a camera
// it reports inactive and touches nothing; the heading then stays at its initial value.
#nullable enable
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.Player
{
    /// <summary>Orbit camera rig settings in one value (from PlayerDefinition, or defaults).</summary>
    public readonly struct OrbitSettings
    {
        public OrbitSettings(float distance, float height, float pitchMin, float pitchMax, float collisionRadius, float sensitivity, bool invertY)
        {
            Distance = distance;
            Height = height;
            PitchMin = pitchMin;
            PitchMax = pitchMax;
            CollisionRadius = collisionRadius;
            Sensitivity = sensitivity;
            InvertY = invertY;
        }

        public float Distance { get; }

        public float Height { get; }

        public float PitchMin { get; }

        public float PitchMax { get; }

        public float CollisionRadius { get; }

        public float Sensitivity { get; }

        public bool InvertY { get; }

        public static OrbitSettings Default => new OrbitSettings(5f, 1.6f, -30f, 70f, 0.25f, 0.15f, false);

        public static OrbitSettings From(PlayerDefinition? definition)
        {
            if (definition == null)
            {
                return Default;
            }

            InputProfile? input = definition.Input;
            return new OrbitSettings(
                definition.CameraDistance,
                definition.CameraHeight,
                definition.CameraPitchMin,
                definition.CameraPitchMax,
                definition.CameraCollisionRadius,
                input != null ? input.LookSensitivity : 0.15f,
                input != null && input.InvertY);
        }

        /// <summary>The orbit position for a pivot, heading and pitch (degrees) at a distance, without collision.</summary>
        public static Vector3 OrbitPosition(Vector3 pivot, float yawDegrees, float pitchDegrees, float distance)
        {
            Quaternion rotation = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
            return pivot - rotation * Vector3.forward * distance;
        }
    }

    /// <summary>The player's third-person orbit camera.</summary>
    public sealed class ThirdPersonCamera : IPresentationBinder
    {
        private readonly Camera? camera;
        private readonly TargetId player;
        private readonly PlayerInputAdapter? input;
        private readonly RaycastHit[] hits = new RaycastHit[8];
        private float yaw;
        private float pitch = 15f;

        public ThirdPersonCamera(Camera? camera, TargetId player, PlayerInputAdapter? input, OrbitSettings settings, float initialYawDegrees)
        {
            this.camera = camera;
            this.player = player;
            this.input = input;
            Settings = settings;
            yaw = initialYawDegrees;
            IsActive = camera != null && !BinderEnvironment.IsHeadless;
        }

        public string BinderName => "gameplay.third-person-camera";

        public bool IsActive { get; }

        public OrbitSettings Settings { get; set; }

        /// <summary>Camera heading in degrees around +Y (the adapter's move frame).</summary>
        public float Yaw => yaw;

        public float Pitch => pitch;

        /// <summary>Distance the camera ended at after collision this frame.</summary>
        public float LastDistance { get; private set; }

        public int Present(ICommittedSlotReader slots)
        {
            if (!IsActive || camera == null
                || !slots.TryRead(player, GameplaySlots.WorldOwner, GameplaySlots.PosX, out int x)
                || !slots.TryRead(player, GameplaySlots.WorldOwner, GameplaySlots.PosZ, out int z))
            {
                return 0;
            }

            int y = slots.TryRead(player, PlayerSlots.Owner, PlayerSlots.PosY, out int py) ? py : 0;
            if (input != null)
            {
                Vector2 look = input.LastIntent.Look;
                yaw = Mathf.Repeat(yaw + look.x * Settings.Sensitivity, 360f);
                pitch = Mathf.Clamp(pitch + (Settings.InvertY ? look.y : -look.y) * Settings.Sensitivity, Settings.PitchMin, Settings.PitchMax);
            }

            var pivot = new Vector3((float)GameplayUnits.ToMetres(x), (float)GameplayUnits.ToMetres(y) + Settings.Height, (float)GameplayUnits.ToMetres(z));
            Vector3 desired = OrbitSettings.OrbitPosition(pivot, yaw, pitch, Settings.Distance);
            Vector3 direction = desired - pivot;
            float distance = Settings.Distance;
            int count = Physics.SphereCastNonAlloc(pivot, Settings.CollisionRadius, direction.normalized, hits, Settings.Distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = hits[i].collider;
                if (hit == null || hit is CharacterController
                    || (hit.GetComponentInParent<EntityViewTag>() is EntityViewTag tag && tag.Target.Equals(player)))
                {
                    continue;
                }

                distance = Mathf.Min(distance, Mathf.Max(0.3f, hits[i].distance));
            }

            LastDistance = distance;
            Transform transform = camera.transform;
            transform.position = pivot + direction.normalized * distance;
            transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
            return 1;
        }
    }
}
