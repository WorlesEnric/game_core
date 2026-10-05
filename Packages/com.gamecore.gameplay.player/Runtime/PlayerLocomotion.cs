// GameCore.Gameplay.Player - PlayerLocomotion: CharacterController collision resolution and the player view (P1.3).
//
// Two halves with different rules:
//   * the resolver (IPlayerMotionResolver) owns a "player rig" GameObject with a CharacterController (slopes, steps,
//     collision only). Each frame, before the pump, it is teleported to the COMMITTED pose and moved by the requested
//     horizontal displacement plus the COMMITTED vertical speed (player.verticalSpeed; P1.7a A6: gravity and jump
//     take-off are integrated by the kernel, not here); where it ends, and whether it found ground, is fed back to
//     PlayerInputAdapter as the next player.move. It is input resolution, not presentation: it runs whenever the
//     application is playing - headless batchmode included, because region scenes and their colliders load there too -
//     and never in edit mode (the adapter then falls back to the kinematic resolver). The rig is never the authority:
//     the kernel clamps what the rig proposes.
//   * the binder (IPresentationBinder) puts the player's entity view at the committed pose (world.pos, P1.7a A4) and
//     emits footsteps by stride from the committed travel. It is inactive headless (batchmode without graphics).
#nullable enable
using System;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.Player
{
    /// <summary>The player's CharacterController resolver and view binder.</summary>
    public sealed class PlayerLocomotion : IPlayerMotionResolver, IPresentationBinder, IDisposable
    {
        private readonly PrefabViewBinder? views;
        private readonly TargetId player;
        private readonly string playerAuthoringId;
        private readonly PlayerDefinition? definition;
        private GameObject? rig;
        private CharacterController? controller;
        private GameObject? ignoredView;
        private bool hasLast;
        private Vector3 lastPosition;
        private int lastStamina;
        private float strideTravel;
        private int foot;

        public PlayerLocomotion(PrefabViewBinder? views, TargetId player, string playerAuthoringId, PlayerDefinition? definition, GameObject? rigPrefab)
        {
            this.views = views;
            this.player = player;
            this.playerAuthoringId = playerAuthoringId ?? string.Empty;
            this.definition = definition;
            if (Application.isPlaying)
            {
                CreateRig(rigPrefab);
            }
        }

        public string BinderName => "gameplay.player-locomotion";

        /// <summary>The binder half presents (footsteps from committed travel; the view only when one exists); never headless.</summary>
        public bool IsActive => !BinderEnvironment.IsHeadless;

        /// <summary>The resolver half runs whenever the rig exists (play mode, headless included).</summary>
        public bool IsResolving => controller != null && controller.enabled;

        public IFootstepSink Footsteps { get; set; } = new NullFootstepSink();

        public GameObject? Rig => rig;

        public bool IsGrounded => controller != null && controller.isGrounded;

        public int Resolutions { get; private set; }

        /// <summary>Frames resolved without ground under the committed pose (horizontal only).</summary>
        public int UngroundedResolutions { get; private set; }

        /// <summary>How far below the committed pose ground is searched for (m).</summary>
        public float GroundProbeDistance { get; set; } = 20f;

        public int FootstepCount { get; private set; }

        public Vector3 Resolve(Vector3 from, Vector3 horizontal, float verticalSpeed, float deltaTime, out bool airborne)
        {
            airborne = false;
            CharacterController? cc = controller;
            if (cc == null || rig == null)
            {
                return new Vector3(from.x + horizontal.x, from.y, from.z + horizontal.z);
            }

            IgnoreViewColliders();
            rig.transform.position = from;
            Physics.SyncTransforms();

            // No ground under the committed pose (the region scene is still loading, or the pose is off the map): move
            // horizontally only and report grounded, never fall into the void.
            if (!Physics.Raycast(from + Vector3.up * 0.5f, Vector3.down, out RaycastHit _, GroundProbeDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                UngroundedResolutions++;
                return new Vector3(from.x + horizontal.x, from.y, from.z + horizontal.z);
            }

            // A resting or falling player is pushed down at least 1 m/s so the controller keeps its ground contact on
            // slopes and steps; a rising player moves up by the committed speed.
            float vertical = verticalSpeed > 0f ? verticalSpeed : Mathf.Min(verticalSpeed, -1f);
            cc.Move(new Vector3(horizontal.x, vertical * deltaTime, horizontal.z));
            airborne = !cc.isGrounded;
            Resolutions++;
            return rig.transform.position;
        }

        public int Present(ICommittedSlotReader slots)
        {
            if (!slots.TryRead(player, GameplaySlots.WorldOwner, GameplaySlots.PosX, out int x)
                || !slots.TryRead(player, GameplaySlots.WorldOwner, GameplaySlots.PosZ, out int z))
            {
                return 0;
            }

            int y = slots.TryRead(player, GameplaySlots.WorldOwner, GameplaySlots.PosY, out int py) ? py : 0;
            int yaw = slots.TryRead(player, GameplaySlots.WorldOwner, GameplaySlots.Yaw, out int pyaw) ? pyaw : 0;
            int stamina = slots.TryRead(player, PlayerSlots.Owner, PlayerSlots.Stamina, out int ps) ? ps : 0;
            var committed = new Vector3((float)GameplayUnits.ToMetres(x), (float)GameplayUnits.ToMetres(y), (float)GameplayUnits.ToMetres(z));
            EmitFootsteps(committed, stamina);
            if (views == null || !views.IsActive || !views.TryGetView(player, out GameObject? view) || view == null)
            {
                return 0;
            }

            view.transform.localPosition = committed;
            view.transform.localRotation = Quaternion.Euler(0f, (float)GameplayUnits.MilliradiansToDegrees(yaw), 0f);
            return 1;
        }

        public void Dispose()
        {
            if (rig != null)
            {
                ViewObjects.Destroy(rig);
            }

            rig = null;
            controller = null;
        }

        private void CreateRig(GameObject? rigPrefab)
        {
            rig = rigPrefab != null ? UnityEngine.Object.Instantiate(rigPrefab) : new GameObject();
            rig.name = "[Player Rig]";
            if (!rig.TryGetComponent(out CharacterController cc))
            {
                cc = rig.AddComponent<CharacterController>();
            }

            float height = definition != null ? definition.Height : 1.8f;
            float radius = definition != null ? definition.Radius : 0.35f;
            cc.height = height;
            cc.radius = Mathf.Min(radius, height * 0.5f);
            cc.stepOffset = Mathf.Min(definition != null ? definition.StepOffset : 0.35f, height - cc.radius);
            cc.slopeLimit = definition != null ? definition.SlopeLimit : 45f;
            cc.skinWidth = Mathf.Max(0.01f, cc.radius * 0.1f);
            cc.center = new Vector3(0f, height * 0.5f + cc.skinWidth, 0f);
            controller = cc;
        }

        /// <summary>The rig and the player's own view overlap by design; their colliders must not push each other.</summary>
        private void IgnoreViewColliders()
        {
            if (views == null || controller == null || !views.TryGetView(player, out GameObject? view) || view == null || view == ignoredView)
            {
                return;
            }

            Collider[] colliders = view.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Physics.IgnoreCollision(controller, colliders[i], true);
            }

            ignoredView = view;
        }

        private void EmitFootsteps(Vector3 committed, int stamina)
        {
            if (!hasLast)
            {
                hasLast = true;
                lastPosition = committed;
                lastStamina = stamina;
                return;
            }

            Vector3 delta = committed - lastPosition;
            bool running = stamina < lastStamina;
            lastPosition = committed;
            lastStamina = stamina;
            delta.y = 0f;
            float travelled = delta.magnitude;
            if (travelled > 5f)
            {
                // A portal travel or an adoption, not a stride.
                strideTravel = 0f;
                return;
            }

            strideTravel += travelled;
            float stride = definition != null ? definition.StrideLength : 0.75f;
            if (strideTravel < stride)
            {
                return;
            }

            strideTravel -= stride;
            foot = 1 - foot;
            FootstepCount++;
            Footsteps.OnFootstep(new FootstepEvent(
                playerAuthoringId,
                foot,
                GameplayUnits.ToMillimetres(committed.x),
                GameplayUnits.ToMillimetres(committed.y),
                GameplayUnits.ToMillimetres(committed.z),
                running));
        }
    }
}
